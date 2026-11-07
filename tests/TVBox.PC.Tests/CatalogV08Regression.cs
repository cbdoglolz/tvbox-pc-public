using System.Net;
using System.Text.Json.Nodes;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;
using TVBoxForWindows.Player;

internal static class CatalogV08Regression
{
    public static async Task RunAsync(Action<bool, string> require)
    {
        var path = Path.Combine(AppPaths.Root, "daily-fixture.sqlite"); var store = new MetadataStore(path);
        var daily = new DailyDiscovery(store); var day = new DateOnly(2026, 11, 7);
        var pool = Enumerable.Range(1, 90).Select(i => new CatalogItem { Title = "发现" + i, Id = i.ToString(), Kind = "movie", Year = "2026", Poster = "https://fixture.test/" + i }).ToList();
        var old = new HashSet<string>();
        for (var d = 10; d >= 1; d--) foreach (var work in daily.Choose("douban", day.AddDays(-d), pool).Items) old.Add(DailyDiscovery.Identity(work));
        var picks = daily.Choose("douban", day, pool);
        require(picks.Items.Count == 5 && picks.Items.Select(DailyDiscovery.Identity).Distinct().Count() == 5, "v08 daily set contains exactly five distinct films");
        require(picks.Items.All(w => !old.Contains(DailyDiscovery.Identity(w))), "v08 daily picks avoid all previous ten days");
        require(new DailyDiscovery(new MetadataStore(path)).Choose("douban", day, pool.AsEnumerable().Reverse()).Items.Select(w => w.Id).SequenceEqual(picks.Items.Select(w => w.Id)), "v08 persisted daily order survives a cold database reopen and changed candidate order");
        require(daily.Choose("netflix", day, pool).Provider == "netflix", "v08 platforms retain separate fixed daily sets");
        require(daily.Choose("tmdb", day, pool.Take(4)) == null && daily.Read("tmdb", day) == null, "v08 incomplete set is never persisted");
        var tiny = new DailyDiscovery(new MetadataStore(Path.Combine(AppPaths.Root, "tiny.sqlite")));
        tiny.Choose("douban", day.AddDays(-1), pool.Take(5));
        require(tiny.Choose("douban", day, pool.Take(5)).ReusedRecent, "v08 insufficient fresh pool explicitly reports recent repeats");
        var health = new Dictionary<string, SourceHealth> { ["good"] = new(true, DateTimeOffset.UtcNow), ["bad"] = new(false, DateTimeOffset.UtcNow) };
        require(SourceLearning.Eligible(health, "good", DateTimeOffset.UtcNow) && !SourceLearning.Eligible(health, "bad", DateTimeOffset.UtcNow) && !SourceLearning.Eligible(health, "new", DateTimeOffset.UtcNow), "v08 warm source selection tests only prior good providers");
        require(SourceLearning.Eligible(health, "bad", DateTimeOffset.UtcNow.AddDays(8)), "v08 stale source health expires after seven days");
        var movie = new CatalogItem { Title = "缓存测试", Kind = "movie", Year = "2026" };
        SourceLearning.Remember(movie, "", 7, "good", true);
        require(SourceLearning.Health(movie, "正片", 7)["good"].Good, "v08 film health survives episode-label changes");
        require(SourceLearning.EpisodeIdentity("tv", "第2集") == SourceLearning.EpisodeIdentity("tv", "S01E02"), "v08 same episode labels share one playback statistics identity");
        require(SourceLearning.Health(movie, "正片", 8).Count == 0, "v08 separate source configurations cannot share health records");
        SourceLearning.Record("fixture", "测试站", "attempt", "speed"); SourceLearning.Record("fixture", "测试站", "failure", "speed");
        SourceLearning.Record("fixture", "测试站", "win", "quality", 1500, 1080);
        var stats = SourceLearning.Statistics().Single(r => r.Key == "fixture");
        require(stats.Attempts == 1 && stats.Failures == 1 && stats.QualityWins == 1 && stats.SpeedWins == 0 && stats.TotalStartMs == 1500 && stats.MaximumHeight == 1080, "v08 durable statistics separate attempts failures preference and real resolution");
        SourceLearning.ObserveHeight("fixture", 2160);
        require(SourceLearning.Statistics().Single(r => r.Key == "fixture") is { MaximumHeight: 2160, QualityWins: 1, Samples: 1 }, "v08 delayed resolution updates statistics without adding another playback win");
        Setting.Incognito = true; SourceLearning.Record("private", "不应保存", "win", "speed"); Setting.Incognito = false;
        require(!SourceLearning.Statistics().Any(r => r.Key == "private"), "v08 incognito playback does not write performance statistics");
        var dir = Path.Combine(AppPaths.Cache, "lru-fixture"); Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "old"), new byte[100]); File.SetLastWriteTimeUtc(Path.Combine(dir, "old"), DateTime.UtcNow.AddDays(-2));
        File.WriteAllBytes(Path.Combine(dir, "active"), new byte[20]);
        var cleanup = CacheMaintenance.Clean(dir, 60, DateTime.UtcNow);
        require(cleanup.Deleted == 1 && cleanup.After == 20 && File.Exists(Path.Combine(dir, "active")), "v08 cache cleanup removes old file while preserving active writes");
        require(SourceLearning.Statistics().Any(r => r.Key == "fixture") && daily.Read("douban", day).Items.Count == 5, "v08 cache trim preserves source statistics and daily history");
        require(DanmakuSearch.BaseUrl("https://fixture.test/token") == "https://fixture.test/token/api/v2/" && DanmakuSearch.BaseUrl("https://fixture.test/token/api/v2/") == "https://fixture.test/token/api/v2/", "v08 compatible endpoint preserves token path and does not double API prefix");
        require(DanmakuSearch.BaseUrl("https://fixture.test/api/v1/token/") == "https://fixture.test/api/v1/token/", "v08 version-one token endpoint stays intact");
        var engine = new DanmakuEngine();
        await engine.LoadAsync("""{"comments":[{"p":"1.25,5,16711680,0","m":"顶部测试"},{"p":"0.5,1,16777215,0","m":"滚动测试"}]}""");
        require(engine.Items.Count == 2 && engine.Items[0].TimeMs == 500 && engine.Items[1].Mode == 5 && engine.Items[1].Color == 16711680, "v08 Dandan comments p/m parsed and sorted with correct time mode color");
        await engine.LoadAsync("<i><d p=\"2,4,25,65280\">底部弹幕</d></i>");
        require(engine.Items.Single().Mode == 4 && engine.Items[0].Text == "底部弹幕", "v08 Bilibili XML uses bottom mode and original text");
        await engine.LoadAsync("<!DOCTYPE i [<!ENTITY x SYSTEM 'file:///private'>]><i><d p=\"1\">&x;</d></i>");
        require(engine.Items.Count == 0, "v08 external XML entities rejected");
        require(DanmakuSearch.EpisodeNumber("S02E07") == 7 && DanmakuSearch.EpisodeNumber("第12集") == 12, "v08 season number cannot replace danmaku episode number");
        var episodes = JsonNode.Parse("""[{"episodeId":1,"episodeTitle":"第1集"},{"episodeId":2,"episodeTitle":"第2集"},{"episodeId":3,"episodeTitle":"第2集预告"}]""") as JsonArray;
        require(DanmakuSearch.SelectEpisode(episodes, "02", "tv")?["episodeId"]?.ToString() == "2", "v08 auto danmaku matches exact episode and excludes previews");
        require(DanmakuSearch.SelectEpisode(episodes, "第7集", "tv") == null && DanmakuSearch.SelectEpisode(episodes, "正片", "movie") == null, "v08 missing episode and ambiguous movie never attach first search result");
        var handler = new FixtureHandler(); var service = new DanmakuSearch(new HttpClient(handler));
        var result = await service.SearchAsync(new CatalogItem { Title = "测试剧", Kind = "tv" }, "第2集", "https://fixture.test/token/api/v2", default);
        await engine.LoadAsync(result.Text);
        require(handler.Urls.Last().Contains("comment/2") && engine.Items.Count == 1, "v08 automatic search-to-episode-to-comment chain completes without manual URL entry");
        handler.Ambiguous = true;
        require((await service.SearchAsync(new CatalogItem { Title = "测试剧", Kind = "tv" }, "第2集", "https://fixture.test", default)).Text.Length == 0, "v08 ambiguous same-title episode stops automatic attachment");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var canceled = false; try { await service.SearchAsync(movie, "", "", cancellation.Token); } catch (OperationCanceledException) { canceled = true; }
        require(canceled, "v08 canceled auto search cannot continue a previous playback request");
    }
    sealed class FixtureHandler : HttpMessageHandler
    {
        public bool Ambiguous; public List<string> Urls = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Urls.Add(request.RequestUri.AbsoluteUri);
            var text = request.RequestUri.AbsolutePath.Contains("comment/") ? """{"comments":[{"p":"1,1,16777215,0","m":"自动加载"}]}""" :
                Ambiguous ? """{"animes":[{"animeTitle":"测试剧","episodes":[{"episodeId":2,"episodeTitle":"第2集"}]},{"animeTitle":"测试剧","episodes":[{"episodeId":22,"episodeTitle":"第2集"}]}]}""" :
                """{"animes":[{"animeTitle":"测试剧","episodes":[{"episodeId":1,"episodeTitle":"第1集"},{"episodeId":2,"episodeTitle":"第2集"}]}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) });
        }
    }
}
