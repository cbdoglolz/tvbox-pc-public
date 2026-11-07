using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;
using TVBoxForWindows.Net;
using System.Text;
using System.Collections.Concurrent;

internal static class CatalogV087Regression
{
    public static async Task RunAsync(Action<bool, string> require)
    {
        require(InternationalSources.PreferredTitle(new CatalogItem { Title = "肖申克的救赎", OriginalTitle = "The Shawshank Redemption", Aliases = new() { "Gaqceva shoushenkidan" } }) == "The Shawshank Redemption", "v087 a Latin-letter foreign alias cannot override the actual original English title");
        var historyBefore = Setting.GetString("search_history");
        SearchHistory.Clear(); for (var i = 0; i < 12; i++) SearchHistory.Add("query " + i);
        require(SearchHistory.Items.Count == 10 && SearchHistory.Items[0] == "query 11" && !SearchHistory.Items.Contains("query 0"), "v087 recent search keeps the latest ten entries");
        SearchHistory.Add("query 5"); SearchHistory.Remove("query 9");
        require(SearchHistory.Items[0] == "query 5" && SearchHistory.Items.Count == 9 && !SearchHistory.Items.Contains("query 9"), "v087 search deduplication and individual removal survive setting reload");
        Setting.Load(); require(SearchHistory.Items.Count == 9, "v087 recent search survives restarting storage"); SearchHistory.Clear(); require(SearchHistory.Items.Count == 0, "v087 clearing search does not retain old suggestions"); Setting.Put("search_history", historyBefore);
        var work = new CatalogItem { Id = "870001", Provider = "tmdb", Kind = "tv", Title = "回归剧集", Year = "2025", SelectedSeason = 1 };
        var history = new History { Key = work.WorkKey, Cid = 987, CatalogSnapshot = JsonUtil.Serialize(work), VodName = work.Title, EpisodeSeason = 1, EpisodeNumber = 2, Position = 65000, Duration = 1800000 };
        Stores.SaveHistory(history); ViewingState.Observe(work, history);
        require(ViewingState.Get(work, 1, 2).Position == 65000 && ViewingState.Get(work, 1, 3).Position == 0, "v087 per-episode progress is independent of another episode");
        require(ViewingState.Get(work).Status == WatchStatus.Watching && ViewingState.IsContinue(history), "v087 a rendered episode becomes watching and remains resumable");
        ViewingState.Hide(new[] { history }); require(!ViewingState.IsContinue(history) && Stores.FindHistory(987, work.WorkKey).Position == 65000, "v087 removing Continue preserves history and position");
        ViewingState.Unhide(history); require(ViewingState.IsContinue(history), "v087 explicitly reopening a title restores Continue visibility");
        ViewingState.Set(work, WatchStatus.Completed, 1, 2); ViewingState.Observe(work, history);
        require(ViewingState.Get(work, 1, 2).Status == WatchStatus.Completed, "v087 manual episode completion is not erased by periodic progress saves");
        ViewingState.Set(work, WatchStatus.Completed); require(!ViewingState.IsContinue(history), "v087 a manually completed work is excluded from Continue");
        var s2 = work.Copy(); s2.SelectedSeason = 2; require(ViewingState.Get(s2, 2, 2).Position == 0, "v087 another season does not inherit the watched episode position");
        Stores.DeleteHistories(987);
        var company = new CatalogQuery { Provider = "tmdb", Category = "all", CompanyId = 32, CompanyName = "出品方", Keyword = "animation" };
        var request = CatalogRequestBuilder.Tmdb(company, "movie", 1);
        require(request.Route == "discover/movie" && request.Parameters["with_companies"] == "32", "v087 keyword search within a production company retains the production filter");
        var other = company.Copy(); other.CompanyId = 33; require(company.CacheKey() != other.CacheKey(), "v087 company caches cannot mix different production studios");
        company.CompanyIsNetwork = true; require(CatalogRequestBuilder.Tmdb(company, "tv", 1).Parameters["with_networks"] == "32", "v087 television network clicks use the network identifier");
        require(company.ClearFilters().CompanyId == 32, "v087 clearing ordinary facets keeps the selected company scope");
        var buffering = new BufferingHealth(); buffering.Observe(true, 1000); require(!buffering.Observe(false, 2000), "v087 one short buffer does not produce repeated-source warnings");
        buffering.Observe(true, 3000); buffering.Observe(false, 4000); buffering.Observe(true, 5000);
        require(buffering.Observe(false, 6000) && buffering.Count == 3 && buffering.TotalMs == 3000, "v087 three buffers in one minute produce a source health warning");
        buffering.Reset(); buffering.Observe(true, 1000); require(buffering.Observe(false, 10000) && !buffering.Observe(false, 10010), "v087 a long stall warns once and duplicate completions are ignored");
        await SourcePolicy(require, work);
        var ts = new byte[4096]; ts[0] = ts[188] = ts[376] = 0x47;
        HttpUtil.Responses["https://slow.example/master.m3u8"] = Encoding.UTF8.GetBytes("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1500000,RESOLUTION=1920x1080\nmedia.m3u8\n");
        HttpUtil.Responses["https://slow.example/media.m3u8"] = Encoding.UTF8.GetBytes("#EXTM3U\n#EXTINF:2,\nsegment.ts\n"); HttpUtil.Responses["https://slow.example/segment.ts"] = ts;
        ProbeHandler.BeforeSend = (request, ct) => request.RequestUri.Host == "slow.example" ? Task.Delay(2200, ct) : Task.CompletedTask;
        try { require(await MediaProbe.CheckAsync("https://slow.example/master.m3u8", new(), default), "v087 a valid multi-hop media probe exceeding six seconds is not falsely filtered"); }
        finally { ProbeHandler.BeforeSend = null; }
        await InternationalParsing(require, work, ts);
    }
    static async Task SourcePolicy(Action<bool, string> require, CatalogItem work)
    {
        var saved = VodConfigService.Instance.Sites; var first = new Site { Key = "v087_good", Name = "无广告站", Api = "https://good.example" }; var ads = new Site { Key = "v087_ads", Name = "有广告站", Api = "https://ads.example" };
        try {
            VodConfigService.Instance.Sites = new() { first, ads };
            SiteRegistry.Set(first.Key, new() { Enabled = false, Group = "回归分组" }); require(!SiteRegistry.Active.Any(s => s.Key == first.Key), "v087 disabled sites disappear from the same effective list used by search and discovery");
            SiteRegistry.Set(first.Key, new() { Enabled = true, Group = "回归分组" }); Setting.Load(); require(SiteRegistry.Enabled(first.Key) && SiteRegistry.Preference(first.Key).Group == "回归分组", "v087 site enable and grouping persist across storage reload");
            SiteRegistry.Set(first.Key, new() { Removed = true }); require(!SiteRegistry.Active.Any(s => s.Key == first.Key) && SiteRegistry.All.Any(s => s.Key == first.Key), "v087 removal is a reversible local override, not deletion of the subscription identity"); SiteRegistry.Set(first.Key, new());
            SourceAdPolicy.Set(work, ads, AdMark.Ads); SourceAdPolicy.Set(work, first, AdMark.Clean);
            require(SourceAdPolicy.Get(work, ads.Key) == AdMark.Ads && SourceAdPolicy.Get(withSeason(2), ads.Key) == AdMark.Unknown, "v087 ad marking applies to one work season without contaminating the next season");
            var contacted = new ConcurrentBag<string>();
            SiteService.Search = (site, _) => { contacted.Add(site.Key); return Task.FromResult(new Result { List = new() { new() { Id = site.Key, Name = work.Title, Year = "2025", TypeName = "日本剧" } } }); };
            SiteService.Detail = (site, _) => Task.FromResult(new Result { List = new() { new() { Id = site.Key, Name = work.Title, Year = "2025", TypeName = "日本剧", PlayFrom = "hls", PlayUrl = "第01集$https://policy.example/a.mp4" } } });
            var mp4 = new byte[64]; Encoding.ASCII.GetBytes("ftyp").CopyTo(mp4, 4); HttpUtil.Responses["https://policy.example/a.mp4"] = mp4;
            var found = new List<PlaybackCandidate>();
            await SourceDiscovery.RunAsync(work, null, null, null, "S1EP01", found.Add, _ => { }, default, ignoreCache: true);
            require(found.Any(f => f.Site.Key == first.Key) && !contacted.Contains(ads.Key), "v087 automatic source selection skips a marked ad site when an unmarked or clean site works");
            SiteRegistry.Set(first.Key, new() { Enabled = false }); contacted.Clear(); found.Clear();
            await SourceDiscovery.RunAsync(work, null, null, null, "第01集", found.Add, _ => { }, default, ignoreCache: true);
            require(found.Count == 1 && found[0].Site.Key == ads.Key && !contacted.Contains(first.Key), "v087 a sole playable ad source remains a fallback while disabled sites never participate");
            require(SourceAdPolicy.Statistics().Any(r => r.Name == ads.Name && r.Ads == 1), "v087 marking one season counts once rather than once per episode");
        } finally { VodConfigService.Instance.Sites = saved; SiteRegistry.Set(first.Key, new()); SiteRegistry.Set(ads.Key, new()); SourceAdPolicy.Set(work, ads, AdMark.Unknown); SourceAdPolicy.Set(work, first, AdMark.Unknown); }
        CatalogItem withSeason(int number) { var copy = work.Copy(); copy.SelectedSeason = number; return copy; }
    }
    static async Task InternationalParsing(Action<bool, string> require, CatalogItem work, byte[] ts)
    {
        HttpUtil.Responses["https://international.example/media.mkv"] = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 1, 2, 3 };
        HttpUtil.Responses["https://international.example/fake.mkv"] = Encoding.UTF8.GetBytes("<!DOCTYPE html><html>verification</html>");
        var output = "{\"streams\":[{\"name\":\"test\",\"url\":\"https://international.example/fake.mkv\"},{\"name\":\"test2\",\"url\":\"https://international.example/media.mkv\",\"headers\":{\"Referer\":\"https://international.example/\"},\"subtitles\":[{\"name\":\"English\",\"url\":\"https://international.example/sub.vtt\"}]}]}";
        var candidates = await InternationalSources.ProbeStreamsAsync(output, new Site { Key = "nuvio_4khdhub", Name = "4KHDHub" }, work, 2, false, default);
        require(candidates.Count == 1 && candidates[0].Flag.Episodes[0].Name == "第02集", "v087 international candidates reject HTML and retain the exact requested episode");
        require(candidates[0].Site.Header["Referer"] == "https://international.example/" && candidates[0].Flag.Episodes[0].Subtitles.Count == 1, "v087 international playback carries required Referer and external subtitles");
        require(candidates[0].Flag.ProbeHeight == 0, "v087 filename 4K claims do not become verified resolution");
        HttpUtil.Responses["https://international.example/compact.mkv"] = HttpUtil.Responses["https://international.example/media.mkv"];
        var sizes = "{\"streams\":[{\"name\":\"remux\",\"quality\":\"2160p\",\"title\":\"Movie Remux 53.36 GB\",\"url\":\"https://international.example/media.mkv\"},{\"name\":\"compact\",\"quality\":\"1080p\",\"title\":\"Movie 6.2 GB\",\"url\":\"https://international.example/compact.mkv\"}]}";
        var fast = await InternationalSources.ProbeStreamsAsync(sizes, new Site { Key = "nuvio_4khdhub", Name = "4KHDHub" }, work, 2, false, default);
        var sharp = await InternationalSources.ProbeStreamsAsync(sizes, new Site { Key = "nuvio_4khdhub", Name = "4KHDHub" }, work, 2, true, default);
        require(fast[0].Flag.OriginalFlag == "compact" && sharp[0].Flag.OriginalFlag == "remux", "v087 speed mode avoids a first-listed 53GB remux while quality mode retains the higher declared encode");
        var active = 0; var peak = 0;
        ProbeHandler.BeforeSend = async (request, ct) => { if (request.RequestUri.Host != "parallel.example") return; var now = Interlocked.Increment(ref active); int observed; do { observed = peak; if (now <= observed) break; } while (Interlocked.CompareExchange(ref peak, now, observed) != observed); try { await Task.Delay(80, ct); } finally { Interlocked.Decrement(ref active); } };
        try {
            var batch = Enumerable.Range(0, 10).Select(i => { var url = "https://parallel.example/" + i + ".mkv"; HttpUtil.Responses[url] = HttpUtil.Responses["https://international.example/media.mkv"]; return new { name = "encode" + i, url }; }).ToArray();
            var parallel = await InternationalSources.ProbeStreamsAsync(System.Text.Json.JsonSerializer.Serialize(new { streams = batch }), new Site { Key = "nuvio_4khdhub", Name = "4KHDHub" }, work, 2, false, default);
            require(peak > 1 && peak <= 4 && active == 0 && parallel.Count == 3, "v087 international variants probe concurrently with a four-request bound and cancel surplus work after three verified results");
        } finally { ProbeHandler.BeforeSend = null; }
        var failure = "";
        require(!await MediaProbe.CheckAsync("https://international.example/missing.mp4", new(), default, failure: reason => failure = reason) && failure.Contains("HTTP 404"), "v087 a matched but expired media URL reports its HTTP failure instead of pretending no title matched");
        require(PlaybackPresentation.SidebarTitle(new CatalogItem { Title = "剧集", Kind = "tv", SelectedSeason = 2 }, null, new Episode { Name = "EP03" }).Contains("第2季 第3集") && PlaybackPresentation.Progress(new History { CatalogKind = "tv", VodName = "剧集", EpisodeSeason = 2, EpisodeNumber = 3, Position = 65000 }).Contains("第2季 · 第3集"), "v087 sidebar and resume cards use explicit season and episode fields even without a season suffix in the title");
    }
}
