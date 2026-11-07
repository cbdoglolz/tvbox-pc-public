using System.Net;
using System.Text.Json;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;
using TVBoxForWindows.Player;

internal static class CatalogV081Regression
{
    public static async Task RunAsync(Action<bool, string> require)
    {
        var years = CatalogYears.Choices(2026);
        require(years[1].Label == "2026" && years[2].Label == "2025" && years[37].Label == "1990", "v081 year picker starts with every year from current year through 1990");
        require(years[^1].Key == "custom" && Array.FindIndex(years, x => x.Key == "d2020") > 37, "v081 decades follow exact years and custom range is last");
        require(CatalogYears.Range("y2020", 2026) == (2020, 2020) && CatalogYears.Range("d2020", 2026) == (2020, 2026), "v081 year and decade with the same first year have distinct ranges");
        require(CatalogYears.Key(2025, 2025, 2026) == "y2025" && CatalogYears.Key(1994, 2003, 2026) == "custom", "v081 query restore shows the exact year or custom range without changing bounds");
        require(CatalogYears.Key(1800, 1999, 2026) == "custom", "v081 older saved before-2000 filter is not reinterpreted as before-1950");
        require(CatalogYears.Choices(2027)[1].Label == "2027", "v081 new calendar year is added automatically");
        var q = new CatalogQuery { RatingProvider = "imdb", MinimumRating = 7, MinimumVotes = 100, YearFrom = 2025, YearTo = 2025 };
        q.Validate(); require(q.RatingProvider == "douban" && q.MinimumRating == null && q.MinimumVotes == null && q.YearFrom == 2025, "v081 retired IMDb filter migrates without applying its threshold to another score or losing year");
        using var json = JsonDocument.Parse("""{"id":12,"title":"中文作品","poster_path":"/english.jpg","images":{"posters":[{"file_path":"/en.jpg","iso_639_1":"en","vote_count":9999},{"file_path":"/zh.jpg","iso_639_1":"zh","vote_count":4},{"file_path":"/no-text.jpg","iso_639_1":null,"vote_count":999}]}}""");
        var work = CatalogMetadataParser.TmdbItem(json.RootElement, "movie");
        require(work.Poster.EndsWith("/zh.jpg") && work.PosterLanguage == "zh", "v081 tagged Chinese poster outranks popular English and untagged artwork");
        using var fallback = JsonDocument.Parse("""{"poster_path":"/original.jpg","images":{"posters":[{"file_path":"/en.jpg","iso_639_1":"en"}]}}""");
        require(PosterPreference.ChooseTmdb(fallback.RootElement) == ("/original.jpg", ""), "v081 missing Chinese artwork preserves original-language fallback");
        using var invalid = JsonDocument.Parse("""{"poster_path":"/valid.jpg","images":{"posters":[{"file_path":"https://private.test/a","iso_639_1":"zh"},{"file_path":"/../secret","iso_639_1":"zh"}]}}""");
        require(PosterPreference.ChooseTmdb(invalid.RootElement).Path == "/valid.jpg", "v081 artwork metadata cannot inject arbitrary image hosts or traversal paths");
        var card = new CatalogItem { Poster = "https://fixture.test/old.jpg" }; var changed = new List<string>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName); card.ApplyMetadata(work);
        require(card.Poster == work.Poster && changed.Contains("Poster") && changed.Contains("PosterFallback"), "v081 metadata hydration really replaces an already-rendered poster and notifies its image binding");
        card.ApplyMetadata(new CatalogItem { Poster = "https://fixture.test/english-late.jpg" });
        require(card.Poster == work.Poster && card.PosterLanguage == "zh", "v081 later generic metadata cannot downgrade verified Chinese artwork to an English poster");
        var service = new MetadataDetailsService(store: new MetadataStore(Path.Combine(AppPaths.Root, "v081.sqlite")));
        var linked = service.Associate(new CatalogItem { Id = "5", Title = "中文作品", Kind = "movie", Poster = "https://fixture.test/douban.jpg" }, work);
        require(linked.Poster == work.Poster && linked.PosterFallback == "https://fixture.test/douban.jpg", "v081 verified Chinese TMDB poster hydrates Douban cards while retaining an image fallback");
        work.ExternalIds.Douban = "123"; var artworkProvider = new ArtworkProvider(work);
        var loader = new PosterMetadataLoader(new MetadataDetailsService(artworkProvider, new MetadataStore(Path.Combine(AppPaths.Root, "art-cache.sqlite"))), () => true);
        var refreshed = work.Copy(); refreshed.Poster = "https://fixture.test/english.jpg";
        await loader.LoadAsync(new[] { refreshed }, (c, full) => c.ApplyMetadata(full), default);
        var second = work.Copy(); second.Poster = "https://fixture.test/english-again.jpg";
        await loader.LoadAsync(new[] { second }, (c, full) => c.ApplyMetadata(full), default);
        require(artworkProvider.Details == 1 && second.Poster == work.Poster, "v081 refreshing a warm poster wall reapplies cached Chinese artwork instead of skipping hydration");
        using var stopped = new CancellationTokenSource(); stopped.Cancel(); var callbacks = 0;
        await loader.LoadAsync(new[] { second }, (_, _) => callbacks++, stopped.Token);
        require(callbacks == 0, "v081 warm artwork cache cannot update an abandoned page");
        var series = new CatalogItem { Title = "赛博朋克：边缘行者 第二季", OriginalTitle = "Cyberpunk: Edgerunners Season 2", Kind = "tv", Aliases = new() { "赛博朋克：边缘行者2" } };
        require(DanmakuSearch.SameTitle("赛博朋克：边缘行者2", series), "v081 second-season numeric alias matches its explicit season title");
        require(!DanmakuSearch.SameTitle("赛博朋克：边缘行者", series) && !DanmakuSearch.SameTitle("赛博朋克：边缘行者 第一季", series), "v081 unsuffixed and first-season pools cannot attach to season two");
        require(DanmakuSearch.SearchNames(series).Any(n => n.StartsWith("Cyberpunk")) && DanmakuSearch.SearchNames(series).All(n => n.Contains('2') || n.Contains("第二季")), "v081 automatic search includes alternate language names while retaining season identity");
        var handler = new AliasHandler(); var search = new DanmakuSearch(new HttpClient(handler));
        var result = await search.SearchAsync(new CatalogItem { Title = "测试 第二季", Kind = "tv", Aliases = new() { "测试2" } }, "第01集", "https://fixture.test", default);
        var engine = new DanmakuEngine(); await engine.LoadAsync(result.Text);
        require(handler.Searches == 2 && engine.Items.Count == 1, "v081 automatic search retries a reliable alias after the display title has no results");
    }
    sealed class ArtworkProvider(CatalogItem full) : ICatalogQueryProvider
    {
        public int Details;
        public Task<CatalogItem> DetailsAsync(CatalogItem work, CancellationToken ct) { Details++; return Task.FromResult(full.Copy()); }
        public Task<CatalogProviderPage> FetchAsync(CatalogQuery q, string kind, int page, string country, CancellationToken ct) => Task.FromResult(new CatalogProviderPage(new(), false, 0, ""));
    }
    sealed class AliasHandler : HttpMessageHandler
    {
        public int Searches;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var text = """{"comments":[{"p":"1,1,16777215,0","m":"第二季弹幕"}]}""";
            if (request.RequestUri.AbsolutePath.Contains("search/")) { Searches++; text = Searches == 1 ? "{\"animes\":[]}" : """{"animes":[{"animeTitle":"测试2","episodes":[{"episodeTitle":"第1集","episodeId":201}]}]}"""; }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) });
        }
    }
}
