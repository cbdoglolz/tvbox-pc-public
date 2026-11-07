using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using TVBoxForWindows.Core;
using TVBoxForWindows.Models;
using TVBoxForWindows.Player;

internal static class CatalogV082Regression
{
    const string Comments = """{"comments":[{"p":"2,5,16777215,fixture","m":"fixture second"},{"p":"1,1,16777215,fixture","m":"fixture first"}]}""";
    static CatalogItem Work() => new() { Title = "赛博朋克：边缘行者 第二季", OriginalTitle = "Cyberpunk: Edgerunners Season 2", Year = "2026", Kind = "tv", Aliases = new() { "赛博朋克：边缘行者2" } };
    public static async Task RunAsync(Action<bool, string> require)
    {
        Setting.Put("danmaku_builtin_relay", true);
        var handler = new RelayHandler(); var search = new DanmakuSearch(new HttpClient(handler));
        var result = await search.SearchAsync(Work(), "第01集", "", default);
        var engine = new DanmakuEngine(); await engine.LoadAsync(result.Text);
        require(result.Provider == DanmakuSearch.RelayLabel && result.LibraryId == "194450001" && result.EpisodeTitle.Contains("第1话"), "v082 built-in relay identifies the precise second-season episode and attributes its source");
        require(engine.Items.Count == 2 && engine.Items[0].TimeMs == 1000 && engine.Items[1].Mode == 5, "v082 relay comments parse in chronological order with top and scrolling modes");
        require(handler.Paths.Count == 3 && handler.Paths.All(p => !p.Contains("search/episodes") && !p.Contains("bilibili")), "v082 supported search-anime to bangumi to comments flow avoids unsupported endpoint and redundant Bili calls");
        require(handler.Paths[0].Contains("search/anime") && Uri.UnescapeDataString(handler.Paths[0]).EndsWith("赛博朋克：边缘行者2"), "v082 verified Chinese numeric season alias is queried first");
        require(handler.UserAgents.All(x => x == "TVBoxPC/0.8.2"), "v082 public relay uses honest project user-agent without borrowed client credentials");

        var episodes = JsonNode.Parse("""[{"episodeId":9,"episodeTitle":"Edgerunners first","episodeNumber":"1"},{"episodeId":10,"episodeTitle":"Opening","episodeNumber":"1"}]""") as JsonArray;
        require(DanmakuSearch.SelectEpisode(episodes, "E01", "tv")?["episodeId"]?.ToString() == "9", "v082 explicit episode numbers work and English Edgerunners is not mistaken for ED credits");
        episodes.Add(JsonNode.Parse("""{"episodeId":11,"episodeTitle":"alternate episode one","episodeNumber":"1"}"""));
        require(DanmakuSearch.SelectEpisode(episodes, "E01", "tv") == null, "v082 ambiguous same-number episodes remain unbound");
        require(DanmakuSearch.SelectEpisode(episodes, "E02", "tv") == null, "v082 absent requested episode cannot reuse another episode pool");

        var custom = new RelayHandler { Legacy404 = true };
        var customResult = await new DanmakuSearch(new HttpClient(custom)).SearchAsync(Work(), "第1集", "https://fixture.test", default);
        require(custom.Paths.Count == 4 && customResult.LibraryId == "194450001" && customResult.Provider == "自定义聚合服务", "v082 custom server with unsupported legacy route falls back to its own catalog protocol");
        require(custom.Paths.All(p => p.StartsWith("https://fixture.test/")), "v082 explicit custom endpoint never silently sends titles to another service");

        var wrong = new RelayHandler { WrongSeason = true };
        var noMatch = await new DanmakuSearch(new HttpClient(wrong)).SearchAsync(Work(), "第1集", DanmakuSearch.BuiltInRelay, default);
        require(noMatch.Text.Length == 0 && wrong.Paths.All(p => !p.Contains("bangumi/") && !p.Contains("comment/")), "v082 first season search hit is rejected before any comments download");
        var wrongYear = new RelayHandler { WrongYear = true };
        noMatch = await new DanmakuSearch(new HttpClient(wrongYear)).SearchAsync(Work(), "第1集", DanmakuSearch.BuiltInRelay, default);
        require(noMatch.Text.Length == 0 && wrongYear.Paths.All(p => !p.Contains("comment/")), "v082 known mismatched production year is rejected");

        var fallback = new RelayHandler { RelayFailure = true, BiliMatch = true };
        var bili = await new DanmakuSearch(new HttpClient(fallback)).SearchAsync(Work(), "第1集", "", default);
        require(bili.Provider == "B站正片" && bili.LibraryId == "321" && bili.Text.Contains("fixture Bili"), "v082 relay outage automatically falls back to exact Bili season and episode");
        var emptyFallback = new RelayHandler { NoMatch = true, BiliMatch = true };
        bili = await new DanmakuSearch(new HttpClient(emptyFallback)).SearchAsync(Work(), "第1集", "", default);
        require(bili.Provider == "B站正片", "v082 reliable empty relay result also falls back to Bili");
        Setting.Put("danmaku_builtin_relay", false);
        var disabled = new RelayHandler { BiliMatch = true };
        await new DanmakuSearch(new HttpClient(disabled)).SearchAsync(Work(), "第1集", "", default);
        require(disabled.Paths.All(p => !p.Contains("152468.xyz")), "v082 disabled relay setting makes no public-relay requests");
        Setting.Put("danmaku_builtin_relay", true);
        var cancelled = new RelayHandler { CancelRelay = true };
        using var cts = new CancellationTokenSource(); cancelled.Cancel = cts;
        var didCancel = false;
        try { await new DanmakuSearch(new HttpClient(cancelled)).SearchAsync(Work(), "第1集", "", cts.Token); }
        catch (OperationCanceledException) { didCancel = true; }
        require(didCancel && cancelled.Paths.Count == 1, "v082 abandoned episode cancels its search without launching fallback requests");

        var cached = new RelayHandler(); var cacheSearch = new DanmakuSearch(new HttpClient(cached));
        await cacheSearch.SearchCachedAsync(Work(), "第1集-cache", "", default);
        var requests = cached.Paths.Count;
        result = await cacheSearch.SearchCachedAsync(Work(), "第1集-cache", "", default);
        require(cached.Paths.Count == requests && result.Message.Contains("缓存") && result.Provider == DanmakuSearch.RelayLabel && result.LibraryId == "194450001", "v082 positive disk cache avoids repeated searches while retaining attribution and exact library ID");
        await cacheSearch.SearchCachedAsync(Work(), "第1集-cache", "", default, forceRefresh: true);
        require(cached.Paths.Count == requests * 2, "v082 explicit retry bypasses the cached result");
        var misses = new RelayHandler { NoMatch = true }; var missSearch = new DanmakuSearch(new HttpClient(misses));
        await missSearch.SearchCachedAsync(Work(), "第2集-empty", "", default); requests = misses.Paths.Count;
        result = await missSearch.SearchCachedAsync(Work(), "第2集-empty", "", default);
        require(misses.Paths.Count == requests && result.Text.Length == 0 && result.Message.Contains("缓存"), "v082 valid no-match result is cached briefly across source retries");
        var workWithAlias = Work(); workWithAlias.Aliases.Add("新增可靠别名 第二季");
        await missSearch.SearchCachedAsync(workWithAlias, "第2集-empty", "", default);
        require(misses.Paths.Count > requests, "v082 newly hydrated aliases invalidate an earlier negative cache");
        var transient = new RelayHandler { RelayFailure = true }; var transientSearch = new DanmakuSearch(new HttpClient(transient));
        result = await transientSearch.SearchCachedAsync(Work(), "第3集-outage", "", default); requests = transient.Paths.Count;
        await transientSearch.SearchCachedAsync(Work(), "第3集-outage", "", default);
        require(result.Transient && transient.Paths.Count > requests, "v082 temporary relay outages are not persisted as missing coverage");
        var old = JsonSerializer.Deserialize<DanmakuSearchResult>("""{"Text":"x","Message":"old"}""");
        require(old.Provider == "" && old.LibraryId == "" && !old.Transient, "v082 older two-field result payload remains compatible with attribution fields");
    }
    sealed class RelayHandler : HttpMessageHandler
    {
        public bool Legacy404, WrongSeason, WrongYear, RelayFailure, BiliMatch, NoMatch, CancelRelay;
        public CancellationTokenSource Cancel;
        public List<string> Paths = new(), UserAgents = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri.AbsoluteUri; Paths.Add(url); UserAgents.Add(request.Headers.UserAgent.ToString());
            if (CancelRelay) { Cancel.Cancel(); ct.ThrowIfCancellationRequested(); }
            var relay = request.RequestUri.Host == "danmaku-api.152468.xyz";
            if ((relay && RelayFailure) || (Legacy404 && url.Contains("search/episodes"))) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            string body;
            if (url.Contains("finger/spi")) body = """{"data":{"b_3":"fixture"}}""";
            else if (url.Contains("bilibili.com/x/web-interface/search")) body = BiliMatch ? """{"code":0,"data":{"result":[{"title":"赛博朋克：边缘行者2","season_id":123}]}}""" : """{"code":0,"data":{"result":[]}}""";
            else if (url.Contains("pgc/view/web/season")) body = """{"result":{"title":"赛博朋克：边缘行者2","type":1,"publish":{"pub_time":"2026-10-20"},"episodes":[{"title":"1","cid":321}]}}""";
            else if (url.Contains("comment.bilibili.com")) body = """<i><d p="1,1,25,16777215">fixture Bili</d></i>""";
            else if (url.Contains("search/anime")) body = NoMatch ? """{"animes":[]}""" : JsonSerializer.Serialize(new { animes = new[] { new { animeId = 19445, animeTitle = WrongSeason ? "赛博朋克：边缘行者" : "赛博朋克：边缘行者 2", startDate = WrongYear ? "2022-10-20" : "2026-10-20", type = "web" } } });
            else if (url.Contains("bangumi/")) body = """{"bangumi":{"animeTitle":"赛博朋克：边缘行者 2","episodes":[{"episodeId":194450001,"episodeTitle":"第1话 fixture","episodeNumber":"1"},{"episodeId":194450002,"episodeTitle":"第2话 fixture","episodeNumber":"2"},{"episodeId":194459901,"episodeTitle":"Opening","episodeNumber":"1"}]}}""";
            else if (url.Contains("comment/")) body = Comments;
            else throw new Exception("Unexpected fixture request: " + url);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
