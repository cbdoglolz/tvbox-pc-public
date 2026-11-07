using TVBoxForWindows.Core;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Engine;

public record PlaybackCandidate(Site Site, Vod Vod, VodFlag Flag);
public record DiscoveryStatus(string SourceKey, string SourceName, string Message, bool Available, long? LatencyMs = null, bool NoMatch = false);

public static class SourceDiscovery
{
    public const int ConcurrentSites = 10;
    public static async Task RunAsync(CatalogItem title, string seedKey, string seedId, string preferredKey,
        string episodeName, Action<PlaybackCandidate> found, Action<DiscoveryStatus> status, CancellationToken ct,
        string onlySiteKey = null, string excludeSiteKey = null, bool ignoreCache = false, string preference = null, bool adOnly = false, bool internationalOnly = false)
    {
        // A running generation must not change rules midway when the settings combo changes.
        var quality = (preference ?? Setting.SourcePreference) == "quality";
        var health = ignoreCache ? new Dictionary<string, SourceHealth>() : await Task.Run(() => SourceLearning.Health(title, episodeName, VodConfigService.Cid), ct).ConfigureAwait(false);
        var sites = SiteRegistry.Active.Where(s => (s.IsSearchable || s.Type == InternationalSources.SiteType) && !s.IsHidden)
            .Where(s => (onlySiteKey == null || s.Key == onlySiteKey) && s.Key != excludeSiteKey && (!adOnly || SourceAdPolicy.Get(title, s.Key) == AdMark.Ads) && (!internationalOnly || s.Type == InternationalSources.SiteType))
            .OrderByDescending(s => s.Key == (string.IsNullOrEmpty(seedKey) ? preferredKey : seedKey)).ToList();
        var initial = sites.Where(s => ignoreCache || s.Key == seedKey || SourceLearning.Eligible(health, s.Key, DateTimeOffset.UtcNow)).ToList();
        var verified = 0;
        var contacted = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
        using var workers = new SemaphoreSlim(ConcurrentSites);
        var regularSites = sites.Where(s => adOnly || onlySiteKey != null || SourceAdPolicy.Get(title, s.Key) != AdMark.Ads).ToList();
        var regular = initial.Intersect(regularSites).ToList();
        await RunSites(regular.OrderBy(s => SourceAdPolicy.Rank(title, s.Key))).ConfigureAwait(false);
        // Previously working ad sources cannot make the warm cache exclude all unmarked alternatives.
        if (!ct.IsCancellationRequested && verified == 0)
            await RunSites(regularSites.Except(regular).Where(s => !health.TryGetValue(s.Key, out var h) || h.Good || DateTimeOffset.UtcNow - h.CheckedAt >= TimeSpan.FromMinutes(30))).ConfigureAwait(false);
        if (!ct.IsCancellationRequested && verified == 0) await RunSites(sites.Except(regularSites)).ConfigureAwait(false);
        foreach (var cached in sites.Where(s => !contacted.ContainsKey(s.Key)))
            App.Post(() => { if (!ct.IsCancellationRequested) status(new(cached.Key, cached.Name, "已采用本集缓存；刷新可重新检测", false)); });

        async Task RunSites(IEnumerable<Site> selected) => await Task.WhenAll(selected.Select(async site =>
        {
            try { await workers.WaitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            try
            {
                if (!SiteRegistry.Enabled(site.Key)) { App.Post(() => { if (!ct.IsCancellationRequested) status(new(site.Key, site.Name, "已停用", false)); }); return; }
                contacted.TryAdd(site.Key, 0);
                if (site.Type == InternationalSources.SiteType) {
                    var candidates = await InternationalSources.FindAsync(site, title, episodeName, quality, ct).ConfigureAwait(false);
                    foreach (var candidate in candidates) { Interlocked.Increment(ref verified); App.Post(() => { if (!ct.IsCancellationRequested && SiteRegistry.Enabled(site.Key)) found(candidate); }); }
                    Notify(new(site.Key, site.Name, candidates.Count > 0 ? "媒体检测通过" : "未解析到可播放直链，未缓存为永久失败", candidates.Count > 0, candidates.FirstOrDefault()?.Flag.ProbeLatencyMs));
                    return;
                }
                var details = new List<Vod>();
                var names = CatalogIdentity.PlaybackNames(title).ToList();
                if (site.Key == seedKey && !string.IsNullOrEmpty(seedId))
                {
                    var seed = await SiteService.DetailContent(site, seedId).WaitAsync(ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(seed.GetMsg())) throw new IOException(seed.GetMsg());
                    details.AddRange(seed.List.Take(1));
                }
                var sawMatch = false; var mediaFailure = "";
                var detailIds = new HashSet<string>();
                foreach (var searchName in new[] { "" }.Concat(CatalogIdentity.PlaybackSearchNames(title)))
                {
                    ct.ThrowIfCancellationRequested();
                    if (searchName.Length > 0)
                    {
                        var query = System.Text.RegularExpressions.Regex.IsMatch(searchName, "[\u4e00-\u9fff]") ? searchName.Replace(" ", "") : searchName;
                        var result = await SiteService.SearchContent(site, query, false).WaitAsync(ct).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(result.GetMsg())) throw new IOException(result.GetMsg());
                        foreach (var possible in result.List.Where(v => Matches(title, v, names)).Take(4))
                        {
                            if (!detailIds.Add(possible.Id)) continue;
                            var response = await SiteService.DetailContent(site, possible.Id).WaitAsync(ct).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(response.GetMsg())) throw new IOException(response.GetMsg());
                            var detail = response.List.FirstOrDefault();
                            if (detail != null && Matches(title, detail, names)) details.Add(detail);
                        }
                    }
                    foreach (var match in details)
                    {
                        sawMatch = true;
                        foreach (var flag in match.GetFlags().OrderByDescending(f => f.Flag.Contains("m3u8", StringComparison.OrdinalIgnoreCase)))
                        {
                            var episode = FindEpisode(flag, title, episodeName);
                            if (episode == null) continue;
                            var direct = Sniffer.IsVideoFormat(episode.Url);
                            if (!direct && site.Key != seedKey && site.Type is not (3 or 4)) continue;
                            if (direct)
                            {
                                var timer = System.Diagnostics.Stopwatch.StartNew(); bool playable;
                                try { playable = await MediaProbe.CheckAsync(episode.Url, site.Header, ct, (url, height, bitrate) =>
                                    { episode.Url = url; flag.ProbeHeight = height; flag.ProbeBitrate = bitrate; }, quality, message => mediaFailure = message).ConfigureAwait(false); }
                                catch (Exception) when (!ct.IsCancellationRequested) { playable = false; mediaFailure = "媒体检测超时/连接失败，可刷新重试"; }
                                finally { flag.ProbeLatencyMs = timer.ElapsedMilliseconds; }
                                if (!playable) continue;
                            }
                            flag.IsMediaVerified = direct; flag.SourceSite = site; flag.SourceVodId = match.Id;
                            flag.OriginalFlag = flag.Flag; flag.Flag = $"{site.Name} · {flag.Flag}";
                            ct.ThrowIfCancellationRequested(); Interlocked.Increment(ref verified);
                            App.Post(() => { if (!ct.IsCancellationRequested && SiteRegistry.Enabled(site.Key)) found(new(site, match, flag)); });
                            Notify(new(site.Key, site.Name, direct ? "媒体检测通过" : "交由播放器解析验证", direct, flag.ProbeLatencyMs));
                            return;
                        }
                    }
                    details.Clear();
                }
                // Only an actual completed content search with no title match is negative-cached.
                // A matched show's missing episode or failed URL may be a new upload, expiring link or network failure.
                Notify(new(site.Key, site.Name, sawMatch ? mediaFailure.Length > 0 ? "作品与集数已匹配；" + mediaFailure : "本集暂未检测到可播线路，可刷新重试" : "没有匹配的影片", false, NoMatch: !sawMatch));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception error) { Notify(new(site.Key, site.Name, error is OperationCanceledException ? "连接超时（未加入失败过滤）" : "连接暂不可用（未加入失败过滤）", false)); Logger.D("SourceDiscovery", site.Key + ": " + error.GetType().Name); }
            finally { workers.Release(); }
            void Notify(DiscoveryStatus update)
            {
                if (ct.IsCancellationRequested) return;
                if (update.Available || update.NoMatch) SourceLearning.Remember(title, episodeName, VodConfigService.Cid, site.Key, update.Available);
                App.Post(() => { if (!ct.IsCancellationRequested) status(update); });
            }
        })).ConfigureAwait(false);
    }

    public static Episode FindEpisode(VodFlag flag, CatalogItem title, string name)
    {
        if (title.Kind == "movie") return flag.Episodes.FirstOrDefault();
        var season = title.SelectedSeason ?? CatalogIdentity.Season(title);
        if (string.IsNullOrEmpty(name)) return flag.Episodes.FirstOrDefault(e => EpisodeIdentity.Parse(e.Name).Season < 0 || season <= 0 || EpisodeIdentity.Parse(e.Name).Season == season);
        var identity = EpisodeIdentity.Parse(name);
        return flag.Episodes.FirstOrDefault(e => e.Name == name && (season <= 0 || EpisodeIdentity.Parse(e.Name).Season < 0 || EpisodeIdentity.Parse(e.Name).Season == season))
            ?? flag.Episodes.FirstOrDefault(e => identity.Matches(EpisodeIdentity.Parse(e.Name), season > 0 ? season : -1));
    }
    static bool Matches(CatalogItem title, Vod vod, List<string> names) => names.Any(name => TitleMatchRules.Matches(name, title.Year, title.Kind, vod.CleanName, vod.Year, vod.TypeName))
        || title.Kind == "tv" && CatalogIdentity.Season(title) == 1 && CatalogIdentity.Season(new CatalogItem { Title = vod.CleanName }) == 0
            && new[] { title.Title, title.OriginalTitle }.Concat(title.Aliases).Where(n => !string.IsNullOrEmpty(n))
                .Any(name => TitleMatchRules.Matches(CatalogIdentity.BaseTitle(name), title.Year, title.Kind, vod.CleanName, vod.Year, vod.TypeName));
}
