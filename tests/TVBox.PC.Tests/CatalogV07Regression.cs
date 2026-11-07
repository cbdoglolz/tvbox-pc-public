using System.Text.Json;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;

internal static class CatalogV07Regression
{
    public static async Task RunAsync(Action<bool, string> require)
    {
        var raw = """{"id":77,"title":"中文片名","overview":"中文剧情","release_date":"2024-01-02","credits":{"cast":[{"id":12,"name":"演员甲","character":"角色乙","profile_path":"/person.jpg"}]}}""";
        using var doc = JsonDocument.Parse(raw);
        var film = CatalogMetadataParser.TmdbItem(doc.RootElement, "movie");
        require(film.Cast.Single().RoleLabel == "演员甲 饰演 角色乙", "v07 actor name and actual character stay paired");
        require(film.Cast[0].Id == "12" && film.Cast[0].Portrait.EndsWith("/person.jpg"), "v07 actor identity and profile photo preserved");
        var target = new CatalogItem(); target.ApplyMetadata(film);
        require(target.Cast.Count == 1 && target.DetailsSchema == 8, "v07 enrichment and restored card preserve actor metadata schema");
        require(LocalizedSynopsis.Chinese("English overview", "中文剧情") == "中文剧情", "v07 Chinese overview chosen over English fallback");
        require(LocalizedSynopsis.Chinese("English only") == "", "v07 untranslated synopsis not labeled Chinese");
        var credits = """{"cast":[{"id":7,"media_type":"movie","title":"甲","release_date":"2020-01-01"},{"id":7,"media_type":"movie","title":"甲","release_date":"2020-01-01"},{"id":8,"media_type":"tv","name":"乙","first_air_date":"2021-01-01"},{"id":9,"media_type":"person","name":"同名人物"}]}""";
        require(PersonCatalog.ParseCredits(credits, "movie").Count == 1, "v07 person filmography deduplicates repeated cast credits");
        require(PersonCatalog.ParseCredits(credits, "tv").Single().Title == "乙", "v07 person filmography separates TV from movie and non-media nodes");
        var q = new CatalogQuery { PersonId = 12, PersonName = "演员甲", Provider = "tmdb", Category = "all", YearFrom = 2020, Countries = new() { "US" } };
        var other = q.Copy(); other.PersonId = 13;
        require(q.CacheKey() != other.CacheKey(), "v07 different people cannot share a query cache");
        require(q.ClearFilters().PersonId == 12 && q.ClearFilters().Countries.Count == 0, "v07 clearing facets preserves person collection identity");
        var store = new MetadataStore(Path.Combine(AppPaths.Root, "v07-query.sqlite"));
        var coordinator = new CatalogQueryCoordinator(store: store);
        var state = new CatalogQueryState { Query = q, Items = new() { film }, Candidates = new() { film }, Offset = 412, ScannedPages = 3 };
        coordinator.Save(state);
        require(ReferenceEquals(coordinator.Restore(q), state), "v07 provider round-trip uses warm state without disk deserialization");
        require(new CatalogQueryCoordinator(store: store).Restore(q).Offset == 412, "v07 cold restart restores exact offset from durable cache");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) _ = new CatalogQueryCoordinator(store: store).Restore(q);
        var cold = watch.Elapsed.TotalMilliseconds; watch.Restart();
        for (var i = 0; i < 100; i++) _ = coordinator.Restore(q);
        Console.WriteLine($"PERF: 100 query restores, cold={cold:0.00}ms warm={watch.Elapsed.TotalMilliseconds:0.00}ms (fixture, excludes UI and network)");
        store.WriteBatch(("batch", "a", (object)q, null), ("batch", "b", (object)film, null));
        require(store.Read<CatalogQuery>("batch", "a").Value.PersonId == 12 && store.Read<CatalogItem>("batch", "b").Value.Cast.Count == 1, "v07 atomic multi-object cache writes round-trip their actual types");
        var http = new MetadataHttp(new HttpClient(new PersonHandler(credits)), () => new string('a', 32));
        var provider = new PersonCatalog(http, store);
        var pq = new CatalogQuery { PersonId = 12, Provider = "tmdb", Category = "all", PageSize = 1 };
        var page = await provider.FetchAsync(pq, "movie", 1, CancellationToken.None);
        require(page.Items.Count == 1 && !page.HasMore && page.Total == 1, "v07 person collection paging reports real deduplicated totals");
    }
    sealed class PersonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
