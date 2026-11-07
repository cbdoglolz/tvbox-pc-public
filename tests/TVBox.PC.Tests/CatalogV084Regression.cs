using System.Text.Json;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;

internal static class CatalogV084Regression
{
    public static async Task RunAsync(Action<bool, string> require)
    {
        var dir = Path.Combine(AppPaths.Root, "v084"); Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "metadata.sqlite"); var store = new MetadataStore(dbPath);
        var raw = new CatalogItem { Id = "84", Title = "持久缓存", Poster = "https://fixture.test/en.jpg" };
        var calls = 0;
        var first = new CatalogArtworkResolver((w, _) => { calls++; w.Poster = "https://fixture.test/zh.jpg"; w.PosterLanguage = "zh"; w.ArtworkSchema = 2; return Task.FromResult(w); }, () => true, store);
        await first.ResolveAsync(raw, default);
        var second = new CatalogArtworkResolver((_, _) => throw new Exception("Should use disk"), () => true, new MetadataStore(dbPath));
        var cached = await second.ResolveAsync(raw, default);
        require(calls == 1 && cached.Poster.EndsWith("/zh.jpg") && cached.ArtworkResolved, "v084 a new resolver and database connection display the previously chosen Chinese poster without a metadata request");
        var changed = new CatalogItem { Id = "84", Title = raw.Title, Poster = "https://fixture.test/rebound.jpg", ExternalIds = new() { Tmdb = 999 } };
        store.Write("mapping", raw.WorkKey, new MetadataLink(changed.ExternalIds, false));
        var refreshes = 0;
        var rebound = new CatalogArtworkResolver((_, _) => { refreshes++; return Task.FromResult(changed); }, () => true, store);
        require((await rebound.ResolveAsync(raw, default)).Poster == changed.Poster && refreshes == 1, "v084 changed explicit mapping invalidates old preferred artwork");
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aF1UAAAAASUVORK5CYII=");
        await ImageByteCache.WriteAsync(dir, "https://fixture.test/image", png);
        require((await ImageByteCache.ReadAsync(dir, "https://fixture.test/image")).SequenceEqual(png), "v084 image bytes survive a fresh disk read without redownload");
        await ImageByteCache.WriteAsync(dir, "html", System.Text.Encoding.UTF8.GetBytes("<html>error</html>"));
        require(!File.Exists(ImageByteCache.PathFor(dir, "html")), "v084 HTTP error HTML cannot poison the image cache");
        File.WriteAllText(ImageByteCache.PathFor(dir, "corrupt"), "not an image");
        require(await ImageByteCache.ReadAsync(dir, "corrupt") == null && !File.Exists(ImageByteCache.PathFor(dir, "corrupt")), "v084 invalid legacy cache entry is discarded for a single network retry");
        File.SetLastWriteTimeUtc(ImageByteCache.PathFor(dir, "https://fixture.test/image"), DateTime.UtcNow.AddDays(-3));
        await ImageByteCache.ReadAsync(dir, "https://fixture.test/image");
        require(File.GetLastWriteTimeUtc(ImageByteCache.PathFor(dir, "https://fixture.test/image")) > DateTime.UtcNow.AddMinutes(-1), "v084 reused disk images become recent for automatic cache cleanup");
        using var doc = JsonDocument.Parse("""{"id":84,"name":"剧集","seasons":[{"season_number":0,"name":"特别篇","poster_path":"/special.jpg"},{"season_number":2,"name":"第二季","air_date":"2025-01-02","episode_count":8,"poster_path":"/s2.jpg"}],"production_companies":[{"id":7,"name":"Studio","logo_path":"/studio.png"}],"networks":[{"id":8,"name":"Network","logo_path":"/net.png"}],"belongs_to_collection":{"id":9,"name":"系列"},"images":{}}""");
        var series = CatalogMetadataParser.TmdbItem(doc.RootElement, "tv");
        require(series.Seasons.Count == 2 && series.Seasons[0].Label == "特别篇" && series.Seasons[1].Poster.EndsWith("/s2.jpg"), "v084 seasons retain specials and season-specific artwork");
        require(series.Companies.Count == 2 && series.Companies[1].Role == "播出平台" && series.CollectionId == 9 && series.PresentationSchema == 1, "v084 production logos networks and official collection identity survive metadata parsing");
        var copy = series.Copy(); copy.ApplyMetadata(new CatalogItem());
        require(copy.Companies.Count == 2 && copy.CollectionId == 9, "v084 lightweight catalogue rows cannot erase rich production metadata");
        var secondSeason = WorkMediaService.ForSeason(series, series.Seasons[1]); var special = WorkMediaService.ForSeason(series, series.Seasons[0]);
        require(secondSeason.WorkKey.EndsWith(":s2") && secondSeason.Year == "2025" && special.WorkKey.EndsWith(":s0") && !CatalogIdentity.SameWork(special, secondSeason), "v084 episode playback isolates season two and special episode progress");
        var episodes = WorkMediaService.ParseSeason("""{"episodes":[{"season_number":2,"episode_number":2,"name":"第二集","overview":"单集中文简介","still_path":"/e2.jpg","vote_average":8.5,"vote_count":12,"runtime":42,"air_date":"2025-01-09"},{"season_number":2,"episode_number":1,"vote_average":0,"vote_count":0},{"season_number":1,"episode_number":9},{"season_number":2,"episode_number":2}]}""", 2);
        require(episodes.Count == 2 && episodes[0].Number == 1 && episodes[1].Still.EndsWith("/e2.jpg") && episodes[1].Facts.Contains("TMDB 8.5"), "v084 single-episode cards deduplicate sort retain still synopsis and true TMDB score");
        require(episodes[0].Facts.Contains("TMDB -") && !episodes[1].Facts.Contains("IMDb"), "v084 unknown episode ratings remain a dash and are never relabeled IMDb");
        store.Write("season-media-v1", "84:2", episodes, TimeSpan.FromDays(-1));
        require(new WorkMediaService(store: new MetadataStore(dbPath)).CachedSeason(series, 2)?.Count == 2, "v084 saved season stills remain available for continue-watching preview after cache expiry");
        var cartoon = new CatalogItem { Provider = "tmdb", Id = "11", Kind = "movie", Genres = new() { "animation", "horror" } };
        var wrongTv = new CatalogItem { Id = "12", Kind = "tv", Genres = new() { "animation" } };
        var liveMovie = new CatalogItem { Id = "13", Kind = "movie", Genres = new() { "horror" } };
        require(!WorkRecommendations.Eligible(cartoon, wrongTv, 1) && !WorkRecommendations.Eligible(cartoon, liveMovie, 1) && WorkRecommendations.Eligible(cartoon, liveMovie, 2), "v084 animated movies outrank live-action horror and animation series until animation movie stages finish");
        var provider = new FixtureProvider(); var recommendations = new WorkRecommendations(provider, store);
        var state = await recommendations.MoreAsync(cartoon, new(), default);
        require(state.Items.Count == 1 && state.Tier == 0 && state.Page == 2 && provider.Queries.All(q => q.Category == "movie" && q.AllGenres && q.Genres.Contains("animation")), "v084 more recommendations cannot broaden types while the strict same-theme animation page has more results");
        var restored = new WorkRecommendations(provider, new MetadataStore(dbPath)).Restore(cartoon);
        require(restored.Items.Count == 1 && restored.Page == 2, "v084 recommendation pagination and priority stage persist across instances");
        var corrected = cartoon.Copy(); corrected.ExternalIds.Tmdb = 9999;
        require(recommendations.Restore(corrected).Items.Count == 0, "v084 a corrected metadata identity cannot reuse recommendations belonging to the old match");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); var didCancel = false;
        try { await recommendations.MoreAsync(cartoon, restored, cancelled.Token); } catch (OperationCanceledException) { didCancel = true; }
        require(didCancel && restored.Page == 2 && restored.Items.Count == 1, "v084 cancelled recommendation work cannot advance cached pagination or leak into a hidden page");
        var query = new CatalogQuery { Keyword = "吸血鬼猎人", Category = "all", SortDirection = "default" };
        var old = new CatalogItem { Id = "old", Title = "吸血鬼猎人D", Kind = "movie", Year = "1985", Popularity = 1 };
        var future = new CatalogItem { Id = "future", Title = "吸血鬼猎人D：复活", Kind = "tv", Year = "2099", Popularity = 100 };
        require(CatalogFilters.Apply(new[] { future, old }, query)[0] == old, "v084 broad title search prefers a released film over an upcoming related title");
        query.Keyword = future.Title; require(CatalogFilters.Apply(new[] { future, old }, query)[0] == future, "v084 an exact requested upcoming title is still shown as itself");
        query.Keyword = "吸血鬼猎人"; query.Sort = "year"; query.SortDirection = "desc";
        require(CatalogFilters.Apply(new[] { old, future }, query)[0] == future, "v084 explicit user year sorting overrides default search relevance");
        var history = new History { Position = 16000, Duration = 60000, EpisodeSeason = 2, EpisodeNumber = 3 };
        var card = new ContinueCard { History = history, Work = series };
        require(card.Episode == "S2 E3" && card.Percent > 26 && card.Percent < 27 && card.Timestamp == "剩余 00:44", "v084 continue-watching presents the actual saved episode progress and remaining timestamp");
        card.SetArtwork("still", "第三集"); require(card.Image == "still" && card.EpisodeTitle == "第三集" && history.Position == 16000, "v084 preview artwork never rewrites viewing progress");
        var wrongLink = new CatalogItem { Id = "wrong", Provider = "douban", Companies = series.Companies.ToList(), CollectionId = 9, Seasons = series.Seasons.ToList() };
        var unlinked = new MetadataDetailsService(store: store).Unlink(wrongLink);
        require(unlinked.Companies.Count == 0 && unlinked.CollectionId == null && unlinked.Seasons.Count == 0, "v084 unlinking incorrect TMDB metadata also removes wrong studios collections and seasons");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { ArgumentList = { "verify-artwork-restart" }, UseShellExecute = false });
        await process.WaitForExitAsync();
        require(process.ExitCode == 0, "v084 poster selection and image cache survive an actual process restart");
    }
    sealed class FixtureProvider : ICatalogQueryProvider
    {
        public List<CatalogQuery> Queries = new();
        public Task<CatalogItem> DetailsAsync(CatalogItem work, CancellationToken ct) => Task.FromResult(work);
        public Task<CatalogProviderPage> FetchAsync(CatalogQuery query, string kind, int page, string country, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Queries.Add(query); return Task.FromResult(new CatalogProviderPage(new() { new() { Id = "fixture" + page, Kind = kind, Genres = new() { "animation", "horror" } } }, true, null, "fixture")); }
    }
}
