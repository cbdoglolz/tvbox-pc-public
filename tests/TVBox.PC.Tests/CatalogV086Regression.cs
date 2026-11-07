using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;
using TVBoxForWindows.Net;
using TVBoxForWindows.Player;

internal static class CatalogV086Regression
{
    public static async Task RunAsync(Action<bool, string> require)
    {
        foreach (var label in new[] { "S1EP02", "S01E02", "Season 1 Episode 2", "第02集", "第二集", "02", "EP02", "2(1080P)" })
            require(EpisodeIdentity.Parse(label).Number == 2, "v086 shared episode parser: " + label);
        require(!EpisodeIdentity.Parse("S1EP02").Matches(EpisodeIdentity.Parse("S2EP02")) && EpisodeIdentity.Parse("1080P").Number == -1, "v086 seasons and resolution numbers cannot become an episode match");
        require(TitleMatchRules.ResumePosition("S1EP02", "S1EP03", "tv", 16000) == 0 && TitleMatchRules.ResumePosition("S1EP02", "第02集", "tv", 16000) == 16000, "v086 resume distinguishes episodes after the same season prefix");
        require(ResumeProgress.SourceSwitchPosition(0, 350000, 350000, null) == 350000, "v086 a transient decoder zero cannot discard verified source-switch progress");
        require(ResumeProgress.SourceSwitchPosition(350000, 350000, 350000, 0) == 0 && ResumeProgress.SourceSwitchPosition(10000, 10000, 10000, null) == 10000, "v086 an explicit seek to the beginning or a verified backward seek is preserved");
        var clock = new PlaybackTimeline(); clock.Observe(354000, 0, true, 1);
        require(clock.Observe(9000, 500, true, 1) == 354500 && clock.Observe(9500, 1000, true, 1) == 355000, "v086 an ad segment timestamp reset cannot send progress and resume back to the beginning");
        require(clock.Observe(370000, 1500, true, 1) == 355500 && clock.Corrections == 2, "v086 returning from an ad timestamp domain does not double-count the timeline offset");
        clock.BeginSeek(); clock.Observe(0, 2000, false, 1); clock.EndSeek(true);
        require(clock.Observe(10000, 2500, true, 1) == 10000, "v086 user backward seeking resets the discontinuity offset rather than being corrected away");
        clock.Reset(); clock.Observe(300000, 0, true, 2);
        require(clock.Observe(302000, 1000, true, 2) == 302000 && clock.Observe(900000, 600000, true, 2) == 900000 && clock.Corrections == 0, "v086 speed changes and long UI sampling gaps are not treated as timestamp faults");
        clock.Reset(); clock.Observe(354000, 0, true, 1); clock.Observe(9000, 500, true, 1); clock.BeginSeek(); clock.EndSeek(false);
        var failedSeekPosition = clock.Observe(9500, 1000, true, 1); clock.Reset();
        require(failedSeekPosition == 355000 && clock.Observe(10000, 1500, true, 1) == 10000, "v086 failed seek preserves the old timestamp mapping while a new source resets it");
        clock.Reset(); clock.Observe(354000, 0, true, 1); clock.Observe(9000, 500, true, 1);
        for (var second = 1; second <= 20; second++) clock.Observe(9000 + second * 1000, 500 + second * 1000, true, 1);
        clock.Observe(355000, 21000, true, 1);
        var targetAfterAd = clock.BeginSeek(380000); var displayAfterSeek = clock.Read(targetAfterAd); clock.EndSeek(true);
        require(displayAfterSeek == 380000 && targetAfterAd < 380000 && clock.BeginSeek(350000) == 350000, "v086 seek maps the correct timestamp segment on either side of an ad discontinuity");
        var snapshot = clock.Snapshot(); clock.Reset(); clock.Restore(snapshot);
        require(clock.BeginSeek(380000) == targetAfterAd, "v086 reopening the same stream preserves its observed seek mapping without applying it to a different stream");
        var series = new CatalogItem { Provider = "tmdb", Id = "860001", Title = "回归剧集", Year = "2026", Kind = "tv", SelectedSeason = 1 };
        var flag = new VodFlag { Episodes = new() { new() { Name = "第01集" }, new() { Name = "第02集" }, new() { Name = "S2EP02" } } };
        require(SourceDiscovery.FindEpisode(flag, series, "S1EP02")?.Name == "第02集", "v086 source episode matching uses the same identity as season cards");
        SourceLearning.Remember(series, "S1EP02", 86, "working", true);
        require(SourceLearning.Health(series, "第02集", 86).ContainsKey("working") && !SourceLearning.Health(series, "S1EP03", 86).ContainsKey("working"), "v086 source health survives label changes without poisoning another episode");
        series.SelectedSeason = 2;
        require(!SourceLearning.Health(series, "第02集", 86).ContainsKey("working"), "v086 source health is isolated across seasons");

        // A site's first same-title record may lack this episode while another valid version has it.
        VodConfigService.Instance.Sites = new() { new Site { Key = "v086", Name = "回归来源" } };
        series.SelectedSeason = 1;
        Vod Vod(string id) => new() { Id = id, Name = "回归剧集", Year = "2026", TypeName = "国产剧", PlayFrom = "m3u8", PlayUrl = id == "partial" ? "第01集$https://media.test/good/index.m3u8" : "第02集$https://media.test/good/index.m3u8" };
        SiteService.Search = (_, _) => Task.FromResult(new Result { List = new() { Vod("partial"), Vod("complete") } });
        SiteService.Detail = (_, id) => Task.FromResult(new Result { List = new() { Vod(id) } });
        var found = new ConcurrentBag<PlaybackCandidate>();
        await SourceDiscovery.RunAsync(series, null, null, null, "S1EP02", found.Add, _ => { }, default, ignoreCache: true);
        require(found.Count == 1 && found.Single().Vod.Id == "complete", "v086 discovery checks another matching version instead of caching the first version's missing episode");
        SiteService.Search = (_, _) => throw new HttpRequestException("transient fixture");
        await SourceDiscovery.RunAsync(series, null, null, null, "S1EP03", _ => { }, _ => { }, default, ignoreCache: true);
        require(!SourceLearning.Health(series, "第03集", 0).ContainsKey("v086"), "v086 network errors never enter the unavailable-episode cache");
        var recovery = new CatalogItem { Title = "缓存回退剧集", Year = "2026", Kind = "tv" };
        SourceLearning.Remember(recovery, "第02集", 0, "old", true);
        VodConfigService.Instance.Sites = new() { new Site { Key = "old", Name = "旧来源" }, new Site { Key = "new", Name = "新来源" } };
        var contacted = new ConcurrentBag<string>();
        SiteService.Search = (site, _) => { contacted.Add(site.Key); return Task.FromResult(new Result { List = new() { new Vod { Id = site.Key, Name = recovery.Title, Year = "2026", TypeName = "国产剧", PlayFrom = "m3u8", PlayUrl = "第02集$https://media.test/" + (site.Key == "old" ? "bad" : "good") + "/index.m3u8" } } }); };
        SiteService.Detail = async (site, _) => await SiteService.Search(site, "");
        found = new();
        await SourceDiscovery.RunAsync(recovery, null, null, null, "S1EP02", found.Add, _ => { }, default);
        require(found.Single().Site.Key == "new" && contacted.Contains("old") && contacted.Contains("new"), "v086 failed cached good source expands to new providers rather than becoming a permanent allow-list");

        // The source worker owns search, detail and media probe, so ten probes can be in flight too.
        var probing = 0; var maximum = 0;
        ProbeHandler.BeforeSend = async (_, ct) => { var n = Interlocked.Increment(ref probing); int old; do { old = maximum; if (old >= n) break; } while (Interlocked.CompareExchange(ref maximum, n, old) != old); await Task.Delay(20, ct); Interlocked.Decrement(ref probing); };
        VodConfigService.Instance.Sites = Enumerable.Range(0, 23).Select(i => new Site { Key = "probe" + i, Name = "Probe" + i }).ToList();
        SiteService.Search = (_, _) => Task.FromResult(new Result { List = new() { Vod("complete") } });
        SiteService.Detail = (_, _) => Task.FromResult(new Result { List = new() { Vod("complete") } });
        found = new();
        try { await SourceDiscovery.RunAsync(series, null, null, null, "S1EP02", found.Add, _ => { }, default, ignoreCache: true); }
        finally { ProbeHandler.BeforeSend = null; }
        require(maximum == 10 && found.Count == 23, "v086 ten concurrent site workers include actual media-byte probing with a bounded cap");

        var fallbackStore = new MetadataStore(Path.Combine(AppPaths.Root, "v086-search.sqlite"));
        using var http = new HttpClient(new FallbackHandler());
        var provider = new CatalogQueryProvider(new MetadataHttp(http, () => "0123456789abcdef0123456789abcdef"), fallbackStore);
        // Credential availability is deliberately scoped to the isolated test profile.
        TmdbCredentials.Save("0123456789abcdef0123456789abcdef");
        var query = new CatalogQuery { Provider = "douban", Category = "movie", Keyword = "回退", YearFrom = 2025, MinimumRating = 8, RatingProvider = "douban" };
        var fallback = await provider.FetchAsync(query, "movie", 1, "", default);
        require(fallback.Items.Count == 1 && fallback.Items[0].Provider == "tmdb" && fallback.ScopeNote.Contains("回退TMDB") && query.Provider == "douban" && query.YearFrom == 2025, "v086 Douban403 searches fallback to TMDB without changing the original filter state");
        require(!CatalogFilters.Matches(fallback.Items[0], query), "v086 fallback never substitutes TMDB score for a requested Douban minimum");

        await CheckUpdater(require);
        await CheckDanmaku(require);
    }

    sealed class FallbackHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(request.RequestUri.Host == "m.douban.com" ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
        { RequestMessage = request, Content = new StringContent("{\"results\":[{\"id\":8601,\"title\":\"回退测试\",\"release_date\":\"2026-01-01\",\"vote_average\":9,\"vote_count\":100}],\"total_pages\":1}") });
    }

    static async Task CheckUpdater(Action<bool, string> require)
    {
        var root = Path.Combine(AppPaths.Root, "updater-fixtures"); Directory.CreateDirectory(root);
        byte[] Zip(bool unsafePath = false)
        {
            using var bytes = new MemoryStream();
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
                foreach (var name in new[] { "TVBox.exe", "libs/TVBox.dll", "libs/TVBox.runtimeconfig.json", "runtime/host/fxr/9/hostfxr.dll", "LICENSE" }.Concat(unsafePath ? new[] { "../prefs.json" } : Array.Empty<string>()))
                { using var writer = new StreamWriter(zip.CreateEntry("TVBox-x64-9.0.0/" + name).Open()); writer.Write("fixture-" + name); }
            return bytes.ToArray();
        }
        var package = Zip(); var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(package)).ToLowerInvariant();
        using var client = new HttpClient(new UpdateHandler(package, hash));
        var service = new ReleaseUpdate(client);
        var latest = await service.CheckAsync("0.8.6", default);
        require(latest.Version == "9.0.0" && latest.Sha256 == hash && await service.CheckAsync("9.0.0", default) == null, "v086 updater chooses a newer public stable x64 release and ignores the current version");
        var ready = await service.DownloadAsync(latest, root, null, default);
        require(File.Exists(Path.Combine(ready.Directory, "libs", "TVBox.dll")), "v086 update download checks all bytes and extracts the real wrapped portable layout");
        var rejected = false;
        try { await service.DownloadAsync(latest with { Sha256 = new string('0', 64) }, root, null, default); } catch (InvalidDataException) { rejected = true; }
        require(rejected, "v086 updater refuses a package whose SHA256 does not match");
        var malicious = Path.Combine(root, "bad.zip"); File.WriteAllBytes(malicious, Zip(true)); rejected = false;
        try { ReleaseUpdate.ExtractCleanPackage(malicious, Path.Combine(root, "escape")); } catch (InvalidDataException) { rejected = true; }
        require(rejected && !File.Exists(Path.Combine(root, "prefs.json")), "v086 update extraction rejects traversal before writing any destination files");
        require(!ReleaseUpdate.AllowedPackagePath("libs/prefs.json") && !ReleaseUpdate.AllowedPackagePath("cache/image.jpg") && !ReleaseUpdate.AllowedPackagePath("libs/account.credential") && !ReleaseUpdate.AllowedPackagePath("libs/x.dll:stream"), "v086 updater excludes user state, credential files and alternate streams");
        require(ReleaseUpdate.AllowedPackagePath("libs/Microsoft.Data.Sqlite.dll") && !ReleaseUpdate.AllowedPackagePath("libs/metadata-v4.sqlite-wal"), "v086 updater distinguishes required SQLite code libraries from private database files");
    }

    sealed class UpdateHandler(byte[] package, string hash) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var content = request.RequestUri.Host == "api.github.com" ? new StringContent(JsonSerializer.Serialize(new { draft = false, prerelease = false, tag_name = "v9.0.0", assets = new[] { new { name = "TVBox-x64-9.0.0.zip", size = package.Length, digest = "sha256:" + hash, browser_download_url = "https://github.com/" + ReleaseUpdate.Repository + "/releases/download/v9.0.0/TVBox-x64-9.0.0.zip" } } })) : (HttpContent)new ByteArrayContent(package);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = content });
        }
    }

    static async Task CheckDanmaku(Action<bool, string> require)
    {
        DanmakuService.Save(new() { new("first", "失败夹具", "https://unavailable.fixture/api/v2/"), new("official", "官方签名夹具", "https://api.dandanplay.net/api/v2/", true, "fixture-app", "fixture-private-secret") });
        require(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(AppPaths.Root, "services.credential"))).Contains("fixture-private-secret") && DanmakuService.All()[1].AppSecret == "fixture-private-secret", "v086 optional service credentials round-trip through Windows encryption without plaintext on disk");
        using var handler = new DanmakuHandler(); using var client = new HttpClient(handler);
        var result = await new DanmakuSearch(client).SearchAsync(new CatalogItem { Title = "肖申克的救赎", OriginalTitle = "The Shawshank Redemption", Kind = "movie", Year = "1994" }, "正片", "", default);
        require(handler.ValidSignatures == 3 && !handler.LeakedSecret, "v086 official danmaku requests sign path and timestamp without sending AppSecret or leaking auth to another service");
        var engine = new DanmakuEngine(); await engine.LoadAsync(result.Text);
        require(result.Provider == "官方签名夹具" && engine.Items.Count == 1, "v086 auto danmaku continues after a configured service fails and loads valid comments from the next service");
        DanmakuService.Save(new());
        Setting.Put("danmaku_builtin_relay", false);
        using var biliClient = new HttpClient(new BiliDatesHandler());
        var film = new CatalogItem { Title = "肖申克的救赎", Kind = "movie", Year = "1994" };
        var bili = new DanmakuSearch(biliClient);
        var comments = await bili.SearchAsync(film, "正片", "", default);
        require(comments.Text.Length > 0, "v086 Bilibili 2019 upload date must not reject the original 1994 film");
        film.Year = "1995";
        require((await bili.SearchAsync(film, "正片", "", default)).Text.Length == 0, "v086 Bilibili original release date still rejects a genuinely different movie year");
        Setting.Put("danmaku_builtin_relay", true);
    }
    sealed class DanmakuHandler : HttpMessageHandler
    {
        public int ValidSignatures; public bool LeakedSecret;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri.Host != "api.dandanplay.net")
            {
                LeakedSecret |= request.Headers.Contains("X-AppId") || request.Headers.Contains("X-AppSecret") || request.Headers.Contains("X-Signature");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { RequestMessage = request });
            }
            var timestamp = long.Parse(request.Headers.GetValues("X-Timestamp").Single());
            if (request.Headers.GetValues("X-Signature").Single() == DanmakuService.Signature("fixture-app", timestamp, request.RequestUri.AbsolutePath, "fixture-private-secret")) ValidSignatures++;
            LeakedSecret |= request.Headers.Contains("X-AppSecret");
            var path = request.RequestUri.AbsolutePath;
            var body = path.Contains("search/anime") ? "{\"animes\":[{\"animeId\":1,\"animeTitle\":\"肖申克的救赎\",\"type\":\"movie\",\"startDate\":\"1994-01-01\"}]}" : path.Contains("bangumi/") ? "{\"bangumi\":{\"animeTitle\":\"肖申克的救赎\",\"episodes\":[{\"episodeId\":10001,\"episodeTitle\":\"正片\"}]}}" : "{\"comments\":[{\"p\":\"1,1,16777215\",\"m\":\"测试弹幕\"}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(body) });
        }
    }
    sealed class BiliDatesHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri.AbsolutePath;
            var body = path.Contains("finger/") ? "{\"data\":{\"b_3\":\"fixture\"}}" : path.Contains("search/") ? "{\"code\":0,\"data\":{\"result\":[{\"title\":\"肖申克的救赎\",\"season_id\":28274,\"pubtime\":780249600}]}}" : path.Contains("season") ? "{\"result\":{\"title\":\"肖申克的救赎\",\"type\":2,\"publish\":{\"pub_time\":\"2019-09-21\"},\"episodes\":[{\"cid\":577974829,\"title\":\"正片\"}]}}" : "<i><d p=\"1,1,25,16777215\">测试</d></i>";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(body) });
        }
    }
}
