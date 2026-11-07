using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Player;

public record DanmakuSearchResult(string Text, string Message, string Provider = "", string LibraryId = "", string EpisodeTitle = "", bool Transient = false);
/// <summary>Title/episode matching, never attach arbitrary user-uploaded search videos.</summary>
public sealed class DanmakuSearch(HttpClient client = null)
{
    public const string BuiltInRelay = "https://danmaku-api.152468.xyz/api/v2/";
    public const string RelayLabel = "弹弹play聚合（uosc公共代理）";
    static readonly HttpClient DefaultClient = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(18) };
    static readonly SemaphoreSlim RelayGate = new(1);
    static DateTimeOffset _lastRelayRequest, _relayRetryAt;
    readonly HttpClient _client = client ?? DefaultClient;
    string _biliBuvid;
    public static event Action SettingsChanged;
    public static void NotifySettingsChanged() => SettingsChanged?.Invoke();
    public static string BaseUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("请输入兼容弹弹play的服务地址（可包含 /api/v2 或令牌路径）");
        var root = uri.AbsoluteUri.TrimEnd('/');
        return root.EndsWith("/api/v2", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(root, @"/api/v1/[^/]+$") ? root + "/" : root + "/api/v2/";
    }
    static string S(JsonNode n, string key) => n?[key]?.ToString() ?? "";
    static string TitleKey(string title, int season, bool knownAlias)
    {
        var key = TitleMatchRules.Normalize(WebUtility.HtmlDecode(Regex.Replace(title ?? "", "<[^>]+>", "")));
        if (season <= 0 || Regex.IsMatch(key, @"第\d+季")) return key;
        var suffix = Regex.Match(key, @"(?<=\D)0*(\d{1,2})$");
        if (suffix.Success && int.TryParse(suffix.Groups[1].Value, out var number) && number == season)
            return key[..suffix.Index] + "第" + season + "季";
        // A service's unsuffixed first season must never silently become season two.
        return knownAlias || season == 1 ? key + "第" + season + "季" : key;
    }
    public static bool SameTitle(string actual, CatalogItem work)
    {
        var season = CatalogIdentity.Season(work); var key = TitleKey(actual, season, false);
        return new[] { work.Title, work.OriginalTitle }.Concat(work.Aliases).Where(n => !string.IsNullOrWhiteSpace(n)).Any(n => TitleKey(n, season, true) == key);
    }
    public static IEnumerable<string> SearchNames(CatalogItem work)
    {
        var season = CatalogIdentity.Season(work);
        return new[] { work.Title, work.OriginalTitle }.Concat(work.Aliases).Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => season > 0 && !Regex.IsMatch(TitleMatchRules.Normalize(n), @"第\d+季|(?<=\D)" + season + "$") ? n + " 第" + season + "季" : n)
            .DistinctBy(n => TitleMatchRules.Normalize(n)).Take(4);
    }
    public static int EpisodeNumber(string label)
    {
        return EpisodeIdentity.Parse(label).Number;
    }
    public static JsonNode SelectEpisode(JsonArray episodes, string episode, string kind)
    {
        if (episodes == null) return null;
        var number = EpisodeNumber(episode);
        var choices = episodes.Where(n => !Regex.IsMatch(S(n, "episodeTitle") + " " + S(n, "long_title"), @"预告|花絮|解说|剪辑|(?:^|\s)(?:PV|OP|ED|Opening|Ending)(?:[\s\d.:：]|$)", RegexOptions.IgnoreCase)).ToList();
        if (kind == "movie") return choices.Count == 1 ? choices[0] : null;
        if (number < 0) number = 1;
        var matches = choices.Where(n => {
            if (int.TryParse(S(n, "episodeNumber"), out var explicitNumber)) return explicitNumber == number;
            var title = S(n, "episodeTitle"); if (title.Length == 0) title = S(n, "title");
            var no = Regex.Match(title, @"^(?:第\s*)?(\d+)(?:\s*[集话話]|\s*$|\s*[:：])");
            return no.Success && int.TryParse(no.Groups[1].Value, out var value) && value == number;
        }).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
    async Task<string> Get(string url, CancellationToken ct)
    {
        var relay = client == null && new Uri(url).Host == new Uri(BuiltInRelay).Host;
        if (!relay) return await GetCore(url, ct);
        await RelayGate.WaitAsync(ct);
        try {
            var delay = _lastRelayRequest.AddMilliseconds(400) - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            _lastRelayRequest = DateTimeOffset.UtcNow;
            return await GetCore(url, ct);
        } finally { RelayGate.Release(); }
    }
    async Task<string> GetCore(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", new Uri(url).Host == new Uri(BuiltInRelay).Host ? "TVBoxPC/0.8.2" : "Mozilla/5.0");
        if (request.RequestUri.Host == "api.dandanplay.net")
        {
            var account = DanmakuService.All().FirstOrDefault(p => p.Official && p.Enabled && p.AppId.Length > 0 && p.AppSecret.Length > 0);
            if (account == null) throw new IOException("弹弹play官方API未配置AppId/AppSecret");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            request.Headers.TryAddWithoutValidation("X-AppId", account.AppId);
            request.Headers.TryAddWithoutValidation("X-Timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
            request.Headers.TryAddWithoutValidation("X-Signature", DanmakuService.Signature(account.AppId, timestamp, request.RequestUri.AbsolutePath, account.AppSecret));
        }
        if (new Uri(url).Host.EndsWith("bilibili.com", StringComparison.OrdinalIgnoreCase)) {
            request.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com/");
            if (_biliBuvid?.Length > 0) request.Headers.TryAddWithoutValidation("Cookie", "buvid3=" + _biliBuvid);
        }
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 16 * 1024 * 1024) throw new IOException("弹幕数据过大");
        await using var stream = await response.Content.ReadAsStreamAsync(ct); using var bytes = new MemoryStream(); var buffer = new byte[16384];
        int read; while ((read = await stream.ReadAsync(buffer, ct)) > 0) { if (bytes.Length + read > 16 * 1024 * 1024) throw new IOException("弹幕数据过大"); bytes.Write(buffer, 0, read); }
        var payload = bytes.ToArray();
        // Bilibili's legacy XML may be raw DEFLATE without a Content-Encoding header.
        var prefix = Encoding.UTF8.GetString(payload, 0, Math.Min(payload.Length, 128)).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
        if (payload.Length > 0 && !prefix.StartsWith('<') && !prefix.StartsWith('{') && !prefix.StartsWith('[')) {
            using var zipped = new DeflateStream(new MemoryStream(payload), CompressionMode.Decompress);
            using var expanded = new MemoryStream();
            while ((read = await zipped.ReadAsync(buffer, ct)) > 0) { if (expanded.Length + read > 16 * 1024 * 1024) throw new IOException("弹幕解压数据过大"); expanded.Write(buffer, 0, read); }
            payload = expanded.ToArray();
        }
        return Encoding.UTF8.GetString(payload).TrimStart('\uFEFF');
    }
    public Task<string> LoadUrl(string url, CancellationToken ct) => Get(url, ct);
    public async Task<DanmakuSearchResult> SearchCachedAsync(CatalogItem work, string episode, string endpoint, CancellationToken ct, bool forceRefresh = false)
    {
        var identity = "v3|" + JsonUtil.Serialize(DanmakuService.All()) + "|" + Setting.GetBool("danmaku_bili", true) + "|" + Setting.GetBool("danmaku_builtin_relay", true) + "|" + work.Title + "|" + work.OriginalTitle + "|" + string.Join('|', work.Aliases.Order()) + "|" + work.Year + "|" + work.Kind + "|" + episode + "|" + endpoint;
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var path = Path.Combine(AppPaths.Cache, "danmaku", key + ".json");
        if (!forceRefresh && !Setting.Incognito && File.Exists(path)) {
            try { var saved = JsonUtil.Deserialize<DanmakuSearchResult>(await File.ReadAllTextAsync(path, ct)); if (saved?.Text != null && !saved.Transient && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < (saved.Text.Length > 0 ? TimeSpan.FromDays(1) : TimeSpan.FromMinutes(15))) return saved with { Message = saved.Message + "（缓存）" }; } catch (IOException) { } catch (System.Text.Json.JsonException) { }
        }
        if (forceRefresh) _relayRetryAt = DateTimeOffset.MinValue;
        var result = await SearchAsync(work, episode, endpoint, ct);
        if (!Setting.Incognito && result.Text.Length > 0) {
            var check = new DanmakuEngine(); await Task.Run(() => check.LoadAsync(result.Text, ct), ct);
            if (check.Items.Count > 0) { ct.ThrowIfCancellationRequested(); await Task.Run(() => DurableJsonFile.Write(path, JsonUtil.Serialize(result)), ct); }
        }
        else if (!Setting.Incognito && !result.Transient) { ct.ThrowIfCancellationRequested(); await Task.Run(() => DurableJsonFile.Write(path, JsonUtil.Serialize(result)), ct); }
        return result;
    }
    public async Task<DanmakuSearchResult> SearchAsync(CatalogItem work, string episode, string endpoint, CancellationToken ct)
    {
        var messages = new List<string>(); var transient = false;
        foreach (var service in DanmakuService.All().Where(p => p.Enabled))
        {
            try
            {
                var result = service.Official ? await SearchCatalogAsync(work, episode, service.Url, service.Name, ct) : await SearchLegacyAsync(work, episode, service.Url, ct);
                if (result.Text.Length > 0) return result with { Provider = service.Name };
                messages.Add(service.Name + "未找到可靠匹配");
            }
            catch (Exception) when (!ct.IsCancellationRequested) { transient = true; messages.Add(service.Name + "暂不可用，请检查地址、凭据或配额"); }
        }
        DanmakuSearchResult fallback;
        try { fallback = await SearchLegacyAsync(work, episode, endpoint, ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { fallback = new("", "默认弹幕服务暂不可用", Transient: true); }
        return fallback.Text.Length > 0 ? fallback : fallback with { Transient = transient || fallback.Transient, Message = string.Join("；", messages.Append(fallback.Message)) };
    }
    async Task<DanmakuSearchResult> SearchLegacyAsync(CatalogItem work, string episode, string endpoint, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(endpoint)) {
            var root = BaseUrl(endpoint);
            if (root == BuiltInRelay) return await SearchCatalogAsync(work, episode, root, RelayLabel, ct);
            try {
            foreach (var name in SearchNames(work)) {
                var data = JsonNode.Parse(await Get(root + "search/episodes?anime=" + Uri.EscapeDataString(name), ct));
                var candidates = (data?["animes"] as JsonArray ?? new()).Where(a => SameTitle(S(a, "animeTitle"), work)).ToList();
                var matches = candidates.Select(a => SelectEpisode(a?["episodes"] as JsonArray, episode, work.Kind)).Where(e => e != null).DistinctBy(e => S(e, "episodeId")).ToList();
                if (matches.Count > 1) return new("", "弹幕有多个同名版本，请在弹幕菜单手动指定");
                if (matches.Count == 0) continue;
                var id = S(matches[0], "episodeId"); if (id.Length == 0) continue;
                return new(await Get(root + "comment/" + Uri.EscapeDataString(id) + "?withRelated=true", ct), "聚合弹幕自动匹配完成", "自定义聚合服务", id, S(matches[0], "episodeTitle"));
            }
            return new("", "聚合服务未覆盖本片本季本集，已尝试作品别名");
            } catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented) {
                return await SearchCatalogAsync(work, episode, root, "自定义聚合服务", ct);
            }
        }
        var relayFailed = false;
        if (Setting.GetBool("danmaku_builtin_relay", true)) {
            if (client != null || DateTimeOffset.UtcNow >= _relayRetryAt) {
                try {
                    var relay = await SearchCatalogAsync(work, episode, BuiltInRelay, RelayLabel, ct);
                    if (relay.Text.Length > 0) return relay;
                } catch (Exception e) when (!ct.IsCancellationRequested) {
                    relayFailed = true;
                    if (client == null) _relayRetryAt = DateTimeOffset.UtcNow.AddMinutes(1);
                    Logger.D("DanmakuProvider", "公共代理暂不可用，回退B站：" + e.GetType().Name);
                }
            } else relayFailed = true;
        }
        var bili = Setting.GetBool("danmaku_bili", true) ? await SearchBilibiliAsync(work, episode, ct) : new DanmakuSearchResult("", "B站正片弹幕已关闭");
        return bili.Text.Length > 0 ? bili : bili with { Transient = relayFailed, Message = Setting.GetBool("danmaku_builtin_relay", true) ? (relayFailed ? "弹弹play公共代理暂不可用；" : "弹弹play未找到可靠匹配；") + bili.Message : bili.Message };
    }
    async Task<DanmakuSearchResult> SearchCatalogAsync(CatalogItem work, string episode, string root, string label, CancellationToken ct)
    {
        var names = SearchNames(work).OrderByDescending(n => System.Text.RegularExpressions.Regex.IsMatch(n, @"[\u4e00-\u9fff].*\d+$")).ToList();
        var season = CatalogIdentity.Season(work);
        if (season > 1) names.Add(Regex.Replace(work.Title, @"第\s*[一二三四五六七八九十\d]+\s*季|Season\s*\d+", "", RegexOptions.IgnoreCase).Trim() + " " + season);
        foreach (var name in names.DistinctBy(TitleMatchRules.Normalize)) {
            var data = JsonNode.Parse(await Get(root + "search/anime?keyword=" + Uri.EscapeDataString(name), ct));
            var candidates = (data?["animes"] as JsonArray ?? new()).Where(a => SameTitle(S(a, "animeTitle"), work))
                .Where(a => work.Year.Length != 4 || S(a, "startDate").Length < 4 || S(a, "startDate")[..4] == work.Year)
                .Where(a => S(a, "type").Length == 0 || (work.Kind == "movie") == (S(a, "type") == "movie")).DistinctBy(a => S(a, "animeId")).ToList();
            if (candidates.Count > 1) return new("", label + "存在多个同名版本，未自动绑定");
            if (candidates.Count == 0) continue;
            var animeId = S(candidates[0], "animeId"); if (animeId.Length == 0) continue;
            var detail = JsonNode.Parse(await Get(root + "bangumi/" + Uri.EscapeDataString(animeId), ct));
            var series = detail?["bangumi"] ?? detail;
            var titles = series?["titles"] as JsonArray;
            if (!SameTitle(S(series, "animeTitle"), work) && !(titles?.Any(n => SameTitle(S(n, "title"), work)) == true)) continue;
            var ep = SelectEpisode(series?["episodes"] as JsonArray, episode, work.Kind); var id = S(ep, "episodeId");
            if (id.Length == 0) continue;
            var comments = await Get(root + "comment/" + Uri.EscapeDataString(id) + "?withRelated=true", ct);
            var check = new DanmakuEngine(); await Task.Run(() => check.LoadAsync(comments, ct), ct);
            if (check.Items.Count == 0) continue;
            return new(comments, label + "自动匹配完成", label, id, S(ep, "episodeTitle"));
        }
        return new("", label + "未找到可靠的本片本季本集弹幕");
    }
    async Task<DanmakuSearchResult> SearchBilibiliAsync(CatalogItem work, string episode, CancellationToken ct)
    {
        var anonymous = JsonNode.Parse(await Get("https://api.bilibili.com/x/frontend/finger/spi", ct));
        _biliBuvid = S(anonymous?["data"], "b_3");
        foreach (var name in SearchNames(work))
        foreach (var category in new[] { "media_ft", "media_bangumi" }) {
            var search = JsonNode.Parse(await Get("https://api.bilibili.com/x/web-interface/search/type?search_type=" + category + "&keyword=" + Uri.EscapeDataString(name), ct));
            if (S(search, "code") != "0") throw new IOException("B站搜索暂不可用，可在设置中配置聚合服务");
            var matches = (search?["data"]?["result"] as JsonArray ?? new()).Where(a => SameTitle(S(a, "title"), work)).DistinctBy(a => S(a, "season_id")).ToList();
            if (matches.Count > 1) return new("", "B站存在多个同名版本，请手动指定弹幕");
            if (matches.Count == 0) continue;
            var detail = JsonNode.Parse(await Get("https://api.bilibili.com/pgc/view/web/season?season_id=" + Uri.EscapeDataString(S(matches[0], "season_id")), ct));
            var series = detail?["result"];
            if (!SameTitle(S(series, "title"), work)) return new("", "B站剧集名称不匹配，已跳过");
            if ((work.Kind == "movie") != (S(series, "type") == "2")) return new("", "B站正片类型不同，已跳过同名作品");
            // Search pubtime is the film's release date; season.publish.pub_time is its Bilibili upload date.
            // E.g. Shawshank is released in 1994 but its Bilibili season was published in 2019.
            if (long.TryParse(S(matches[0], "pubtime"), out var releaseTimestamp) && releaseTimestamp is >= -5364662400 and <= 4133980799
                && work.Year.Length == 4 && DateTimeOffset.FromUnixTimeSeconds(releaseTimestamp).Year.ToString() != work.Year)
                return new("", "B站原作上映年份不同，已跳过同名作品");
            var ep = SelectEpisode(series?["episodes"] as JsonArray, episode, work.Kind); var cid = S(ep, "cid");
            if (!long.TryParse(cid, out _)) return new("", "B站未找到匹配的正片集数");
            return new(await Get("https://comment.bilibili.com/" + cid + ".xml", ct), "B站正片弹幕自动匹配完成", "B站正片", cid, S(ep, "title"));
        }
        return new("", "B站弹幕未覆盖本片本季；已检索别名，可配置多平台聚合服务");
    }
}
