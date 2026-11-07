using System.Collections.Concurrent;
using System.Text;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;
using TVBoxForWindows.Net;

internal static class CatalogRegression
{
    public static async Task RunAsync(Action<bool, string> require)
    {
        var resourceHistory = new History { Key = "provider@vod", VodName = "某剧 第二季", CatalogKind = "tv", CatalogYear = "2026", VodRemarks = "第03集", Position = 60000 };
        var subject = new CatalogItem { Id = "123", Title = "某剧第2季", Year = "2026", Kind = "tv" };
        require(CatalogHistory.Find(subject, new[] { resourceHistory }) == resourceHistory, "resource/search watch history appears on matching subject posters");
        var promoted = CatalogHistory.Promote(resourceHistory, subject);
        require(promoted.Key == "douban@123" && promoted.Position == 60000 && promoted.SourceKey == "provider" && promoted.SourceVodId == "vod" && resourceHistory.Key == "provider@vod", "subject resume preserves source, episode and position without mutating legacy history");
        subject.Year = "2025";
        require(CatalogHistory.Find(subject, new[] { resourceHistory }) == null, "same-title remake history is not attached to another year");
        subject.Year = "2026"; resourceHistory.CatalogId = "different";
        require(CatalogHistory.Find(subject, new[] { resourceHistory }) == null, "different known subject IDs never merge through title matching");
        require(PlaybackPresentation.Delay(999) == "999 ms" && PlaybackPresentation.Delay(1200).Contains("1.20"), "probe delay changes from milliseconds to seconds");
        require(PlaybackPresentation.DelayColor(999) != PlaybackPresentation.DelayColor(1000) && PlaybackPresentation.DelayColor(2999) != PlaybackPresentation.DelayColor(3000), "latency colors have explicit one-second and three-second boundaries");
        var progress = new History { VodName = "暗黑 第二季", CatalogKind = "tv", VodRemarks = "第03集", Position = 3723000 };
        require(PlaybackPresentation.Progress(progress).Contains("第二季") && PlaybackPresentation.Progress(progress).Contains("第3集") && PlaybackPresentation.Progress(progress).Contains("01:02:03"), "resume cards show season, episode and exact position");
        progress.VodRemarks = "S02E03";
        require(PlaybackPresentation.Progress(progress).Contains("第3集"), "SxxExx labels use the episode instead of the season number");
        progress.VodRemarks = "第123456789012345集";
        require(PlaybackPresentation.Progress(progress).Contains("123456789012345"), "untrusted large episode labels cannot overflow progress rendering");
        progress.CatalogKind = "movie"; progress.VodRemarks = "1080P";
        require(!PlaybackPresentation.Progress(progress).Contains("1080"), "movie quality is never displayed as episode number");
        var metric = new TVBoxForWindows.Player.MediaSnapshot(1920, 1080, "h264", "HLS", 24, 8000000, 60000);
        var rateMemory = new TVBoxForWindows.Player.MediaRateMemory();
        require(rateMemory.Observe(1, 0, 1200000, true) == (1200000, true) && rateMemory.Observe(1, 0, 0, false) == (1200000, true), "paused media retains the last observed bitrate and labels samples");
        require(rateMemory.Observe(2, 0, 0, false) == (0, false), "new source never inherits previous source bitrate");
        require(rateMemory.Observe(2, 8000000, 1000000, true) == (8000000, false), "declared codec bitrate takes precedence over packet-rate samples");
        require(metric.Summary.Contains("8.00 Mb/s") && metric.SizeLabel.Contains("估算"), "codec bitrate uses bits and stream size is explicitly estimated");
        require(new TVBoxForWindows.Player.MediaSnapshot(0, 0, "", "HLS", 0, 0, 0).SizeLabel == "流媒体未公布", "unknown media size is not invented");
        var low = new VodFlag { Flag = "720P", ProbeHeight = 720, ProbeLatencyMs = 100 };
        var high = new VodFlag { Flag = "1080P", ProbeHeight = 1080, ProbeLatencyMs = 1500 };
        require(PlaybackPresentation.OrderFlags(new[] { low, high }, false).First() == 0 && PlaybackPresentation.OrderFlags(new[] { low, high }, true).First() == 1, "speed and quality preferences genuinely change candidate order");
        require(PlaybackPresentation.QualityScore(new VodFlag { Flag = "1080资源 · m3u8", OriginalFlag = "m3u8", SourceSite = new Site() }) == 0, "provider brand names cannot masquerade as video resolution");
        var firstSession = CatalogSession.Get("regression", "tv");
        firstSession.Apply(new CatalogPage { Page = 1, Total = 100, Items = new() { new() { Id = "one" } } }, true);
        firstSession.Apply(new CatalogPage { Page = 2, Total = 100, Items = new() { new() { Id = "one" }, new() { Id = "two" } } }, false);
        firstSession.Offset = 880;
        var returnSession = CatalogSession.Get("regression", "tv");
        require(ReferenceEquals(firstSession, returnSession) && returnSession.Items.Count == 2 && returnSession.Page == 2 && returnSession.Offset == 880, "recreated pages recover all loaded cards, page and scroll offset without duplicate cards");
        require(CatalogSession.Get("regression", "movie").Items.Count == 0 && CatalogSession.Get("other", "tv").Items.Count == 0, "each provider and category has an isolated waterfall");
        firstSession.Apply(new CatalogPage { Page = 3, Total = 100 }, false);
        require(!firstSession.HasMore && firstSession.Items.Count == 2, "empty next page stops loading without removing previous cards");
        require(CatalogChannels.For("douban").Any(c => c.Key == "top250") && CatalogChannels.For("netflix").All(c => c.Tags.Contains("Netflix") && c.Key != "top250"), "Netflix stays filtered to its catalogue and does not claim an official Top250");
        var card = new CatalogItem(); var changes = 0; card.PropertyChanged += (_, _) => changes++;
        card.UpdateProgress(progress); card.UpdateProgress(null);
        require(!card.CanResume && changes == 4, "returning cards update and clear resume state without rebuilding the waterfall");
        var json = """
        {"total":3,"items":[
          {"id":"123","title":"近期电视剧","pic":{"large":"https://img2.doubanio.com/poster.jpg"},"rating":{"value":8.6},"card_subtitle":"2026 / 中国大陆 / 剧情"},
          {"id":"124","title":"未评分电影","rating":null,"pic":{"normal":"https://img2.doubanio.com/fallback.jpg"}},
          {"id":"125","title":" ","rating":{"value":9}},
          {"id":"123","title":"重复条目"}]}
        """;
        var page = CatalogFeedParser.Parse(json, "tv", 1);
        require(page.Items.Count == 2 && page.Items[0].Id == "123", "Douban cards use subject identity, deduplicate and ignore blank titles");
        require(page.Items[0].RatingLabel == "★ 8.6" && page.Items[0].Year == "2026", "real Douban rating and year retained");
        require(page.Items[1].RatingLabel == "暂无评分" && page.Items[1].Poster.EndsWith("fallback.jpg"), "missing rating never invented, normal poster fallback");
        require(CatalogFeedParser.Parse(json, "movie", 2).Items.All(i => i.Kind == "movie"), "movie and television feed identities separated");
        var invalidRejected = false;
        try { CatalogFeedParser.Parse("{}", "tv", 1); } catch (InvalidDataException) { invalidRejected = true; }
        require(invalidRejected, "invalid Douban payload cannot replace valid cache");
        var unsafePoster = CatalogFeedParser.Parse("{\"items\":[{\"id\":\"9\",\"title\":\"test\",\"pic\":{\"large\":\"file:///private\",\"normal\":\"file:///private\"}}]}", "movie", 1).Items[0];
        require(unsafePoster.Poster.Length == 0 && unsafePoster.PosterFallback.Length == 0, "remote catalogue cannot select local primary or fallback image files");
        require(TitleMatchRules.Matches("庆余年 第二季", "2024", "tv", "庆余年第2季", "2024", "国产剧"), "season and whitespace normalization");
        require(!TitleMatchRules.Matches("庆余年 第二季", "2024", "tv", "庆余年第一季", "2019", "国产剧"), "never play another season");
        require(!TitleMatchRules.Matches("星际穿越", "2014", "movie", "星际穿越[电影解说]", "2014", "电影解说"), "commentary is not the requested movie");
        require(!TitleMatchRules.Matches("同名作品", "2024", "movie", "同名作品", "1998", "剧情片"), "same-name remake year mismatch rejected");
        require(TitleMatchRules.Matches("剧情电影", "2026", "movie", "剧情电影", "2026", "剧情片"), "drama film is not misclassified as a television series");
        require(TitleMatchRules.ResumePosition("第01集", "第1集", "tv", 90000) == 90000, "same episode keeps progress across source naming variants");
        require(TitleMatchRules.ResumePosition("第01集", "第02集", "tv", 90000) == 0, "selecting another episode never reuses the old episode position");
        require(TitleMatchRules.GuessKind("剧情片") == "movie" && TitleMatchRules.GuessKind("国产剧") == "tv", "search classification distinguishes drama movies and television");
        require(TitleMatchRules.ResumePosition("720P", "1080P", "movie", 90000) == 90000, "movie quality labels are not treated as episode numbers");

        var ts = new byte[600]; ts[0] = ts[188] = ts[376] = 0x47;
        var collection = CatalogFeedParser.Parse("{\"total\":250,\"subject_collection_items\":[{\"id\":\"123\",\"title\":\"Top Film\",\"cover_url\":\"https://image.test/a.jpg\"}]}", "movie", 1);
        require(collection.Total == 250 && collection.Items[0].Poster.EndsWith("a.jpg"), "Top250 collection schema and poster are parsed");
        require(CatalogFeedParser.Parse("{\"items\":[{\"id\":\"123\",\"title\":\"Not a movie\",\"type\":\"playlist\"}]}", "movie", 1).Items.Count == 0, "recommendation playlist cards cannot be played as movies");
        HttpUtil.Responses["https://media.test/master.m3u8"] = Encoding.UTF8.GetBytes("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1000000,RESOLUTION=1280x720\nlow.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=4000000,RESOLUTION=1920x1080\nhigh.m3u8\n");
        HttpUtil.Responses["https://media.test/low.m3u8"] = ts; HttpUtil.Responses["https://media.test/high.m3u8"] = ts;
        var chosenHeight = 0;
        require(await MediaProbe.CheckAsync("https://media.test/master.m3u8", new(), default, (_, h, _) => chosenHeight = h, true) && chosenHeight == 1080, "quality preference probes and selects the actual higher-resolution HLS variant");
        require(await MediaProbe.CheckAsync("https://media.test/master.m3u8", new(), default, (_, h, _) => chosenHeight = h, false) && chosenHeight == 720, "speed preference selects the lower-bandwidth HLS variant");
        HttpUtil.Responses["https://media.test/good/index.m3u8"] = Encoding.UTF8.GetBytes("#EXTM3U\n#EXTINF:5,\nsegment.ts\n");
        HttpUtil.Responses["https://media.test/good/segment.ts"] = ts;
        HttpUtil.Responses["https://media.test/bad/index.m3u8"] = Encoding.UTF8.GetBytes("<html>200 OK but not a video</html>");
        require(await MediaProbe.CheckAsync("https://media.test/good/index.m3u8", new(), default), "HLS resolves relative segment and reads real TS bytes");
        require(!await MediaProbe.CheckAsync("https://media.test/bad/index.m3u8", new(), default), "HTTP 200 HTML does not pass media probing");
        require(!await MediaProbe.CheckAsync("https://media.test/missing.m3u8", new(), default), "404 media rejected");

        VodConfigService.Instance.Sites = new() { new Site { Key = "bad", Name = "坏源" }, new Site { Key = "good", Name = "好源" }, new Site { Key = "wrong", Name = "同名错版" } };
        Vod MakeVod(Site site) => new() { Id = site.Key, Name = "测试电影", Year = site.Key == "wrong" ? "1990" : "2026", TypeName = "剧情片", PlayFrom = "m3u8", PlayUrl = $"正片$https://media.test/{site.Key}/index.m3u8" };
        SiteService.Search = (site, _) => Task.FromResult(new Result { List = new() { MakeVod(site) } });
        SiteService.Detail = (site, _) => Task.FromResult(new Result { List = new() { MakeVod(site) } });
        var candidates = new ConcurrentBag<PlaybackCandidate>();
        var statuses = new ConcurrentBag<DiscoveryStatus>();
        await SourceDiscovery.RunAsync(new CatalogItem { Title = "测试电影", Year = "2026", Kind = "movie" }, "", "", "bad", "", candidates.Add, statuses.Add, default);
        require(candidates.Count == 1 && candidates.Single().Site.Key == "good", "bad source skipped and next actual media source selected");
        require(candidates.Single().Flag.SourceSite.Key == "good" && candidates.Single().Flag.OriginalFlag == "m3u8", "aggregated flag retains real site and original resolver flag");
        require(statuses.Count == 3 && statuses.Count(s => s.Available) == 1, "every source reports its real outcome");
        var contacted = new ConcurrentBag<string>();
        SiteService.Search = (site, _) => { contacted.Add(site.Key); return Task.FromResult(new Result { List = new() { MakeVod(site) } }); };
        await SourceDiscovery.RunAsync(new CatalogItem { Title = "测试电影", Year = "2026", Kind = "movie" }, "", "", "", "", _ => { }, _ => { }, default);
        require(contacted.Count == 1 && contacted.Single() == "good", "v08 repeated film discovery contacts only the cached playable provider");
        contacted = new();
        await SourceDiscovery.RunAsync(new CatalogItem { Title = "测试电影", Year = "2026", Kind = "movie" }, "", "", "", "", _ => { }, _ => { }, default, ignoreCache: true);
        require(contacted.Count == 3, "v08 explicit source refresh rechecks failed and unknown providers");
        candidates = new(); statuses = new();
        await SourceDiscovery.RunAsync(new CatalogItem { Title = "测试电影", Year = "2026", Kind = "movie" }, "good", "good", "good", "", candidates.Add, statuses.Add, default, onlySiteKey: "good");
        require(statuses.Count == 1 && candidates.Single().Site.Key == "good", "v05 preferred resume stage contacts only the saved source");
        candidates = new(); statuses = new();
        await SourceDiscovery.RunAsync(new CatalogItem { Title = "测试电影", Year = "2026", Kind = "movie" }, null, null, null, "", candidates.Add, statuses.Add, default, excludeSiteKey: "good");
        require(statuses.Count == 2 && statuses.All(s => s.SourceKey != "good"), "v05 fallback stage does not repeat the already tried preferred source");
        VodConfigService.Instance.Sites = new() { new Site { Key = "script", Name = "用户脚本源", Type = 3 } };
        SiteService.Detail = (_, _) => Task.FromResult(new Result { List = new() { new Vod { Id = "1", Name = "脚本影片", PlayFrom = "internal", PlayUrl = "正片$internal-id" } } });
        candidates = new(); statuses = new();
        await SourceDiscovery.RunAsync(new CatalogItem { Title = "脚本影片" }, "script", "1", "", "", candidates.Add, statuses.Add, default);
        require(candidates.Count == 1 && !candidates.Single().Flag.IsMediaVerified && !statuses.Single().Available, "explicit script sources reach the existing resolver without falsely claiming media verification");

        var active = 0; var maximum = 0;
        VodConfigService.Instance.Sites = Enumerable.Range(0, 32).Select(i => new Site { Key = i.ToString(), Name = i.ToString() }).ToList();
        SiteService.Search = async (_, _) =>
        {
            var concurrent = Interlocked.Increment(ref active);
            int old;
            do { old = maximum; if (old >= concurrent) break; } while (Interlocked.CompareExchange(ref maximum, concurrent, old) != old);
            await Task.Delay(5); Interlocked.Decrement(ref active); return new Result();
        };
        statuses = new();
        await SourceDiscovery.RunAsync(new CatalogItem { Title = "测试电影" }, "", "", "", "", _ => { }, statuses.Add, default);
        require(maximum == 10 && statuses.Count == 32, "32 sources use ten concurrent site workers with an enforced upper bound");
        using var cancellation = new CancellationTokenSource(20);
        SiteService.Search = async (_, _) => { await Task.Delay(300); return new Result(); };
        var lateCallbacks = 0;
        await SourceDiscovery.RunAsync(new CatalogItem { Title = "离开的影片" }, "", "", "", "", _ => Interlocked.Increment(ref lateCallbacks), _ => Interlocked.Increment(ref lateCallbacks), cancellation.Token);
        require(lateCallbacks == 0, "leaving playback cancels queued and late source callbacks");
    }
}
