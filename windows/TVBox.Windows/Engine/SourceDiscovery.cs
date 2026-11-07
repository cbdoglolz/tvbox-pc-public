using TVBoxForWindows.Core;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Engine;

public record PlaybackCandidate(Site Site, Vod Vod, VodFlag Flag);
public record DiscoveryStatus(string SourceKey, string SourceName, string Message, bool Available, long? LatencyMs = null, bool NoMatch = false);

public static class SourceDiscovery
{
    public static async Task RunAsync(CatalogItem title, string seedKey, string seedId, string preferredKey,
        string episodeName, Action<PlaybackCandidate> found, Action<DiscoveryStatus> status, CancellationToken ct, string onlySiteKey = null, string excludeSiteKey = null, bool ignoreCache = false)
    {
        var health = ignoreCache ? new Dictionary<string, SourceHealth>() : await Task.Run(() => SourceLearning.Health(title, episodeName, VodConfigService.Cid), ct).ConfigureAwait(false);
        var sites = VodConfigService.Instance.Sites.Where(s => s.IsSearchable && !s.IsHidden)
            .Where(s => (onlySiteKey == null || s.Key == onlySiteKey) && s.Key != excludeSiteKey)
            .OrderByDescending(s => s.Key == (string.IsNullOrEmpty(seedKey) ? preferredKey : seedKey)).ToList();
        foreach (var skipped in sites.Where(s => !SourceLearning.Eligible(health, s.Key, DateTimeOffset.UtcNow)).ToList())
        {
            sites.Remove(skipped); App.Post(() => { if (!ct.IsCancellationRequested) status(new(skipped.Key, skipped.Name, "已跳过历史失败/未验证来源；点击刷新可重新检测", false)); });
        }
        using var searches = new SemaphoreSlim(3);
        using var probes = new SemaphoreSlim(1);
        await Task.WhenAll(sites.Select(async site =>
        {
            try { await searches.WaitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            try
            {
                Vod match = null;
                if (site.Key == seedKey && !string.IsNullOrEmpty(seedId))
                    match = (await SiteService.DetailContent(site, seedId).WaitAsync(ct).ConfigureAwait(false)).List.FirstOrDefault();
                else
                {
                    var names = CatalogIdentity.PlaybackNames(title).ToList();
                    foreach (var searchName in CatalogIdentity.PlaybackSearchNames(title))
                    {
                        var query = System.Text.RegularExpressions.Regex.IsMatch(searchName, "[\u4e00-\u9fff]") ? searchName.Replace(" ", "") : searchName;
                        var result = await SiteService.SearchContent(site, query, false).WaitAsync(ct);
                        if (!string.IsNullOrEmpty(result.GetMsg())) throw new IOException(result.GetMsg());
                        var matches = result.List.Where(v => names.Any(name => TitleMatchRules.Matches(name, title.Year, title.Kind, v.CleanName, v.Year, v.TypeName))).ToList();
                        foreach (var possible in matches.Take(3))
                        {
                            var detail = (await SiteService.DetailContent(site, possible.Id).WaitAsync(ct).ConfigureAwait(false)).List.FirstOrDefault();
                            if (detail != null && names.Any(name => TitleMatchRules.Matches(name, title.Year, title.Kind, detail.CleanName, detail.Year, detail.TypeName))) { match = detail; break; }
                        }
                        if (match != null) break;
                    }
                }
                if (match == null) { Notify(new(site.Key, site.Name, "没有匹配的影片", false, NoMatch: true)); return; }
                var flags = match.GetFlags().OrderByDescending(f => f.Flag.Contains("m3u8", StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var flag in flags)
                {
                    var episode = string.IsNullOrEmpty(episodeName) ? flag.Episodes.FirstOrDefault() : flag.Find(episodeName, true);
                    if (episode == null && title.Kind == "movie") episode = flag.Episodes.FirstOrDefault();
                    if (episode == null) continue;
                    // Raw MacCMS web-player pages are not playable media. Prefer direct HLS/media variants.
                    var direct = Sniffer.IsVideoFormat(episode.Url);
                    if (!direct && site.Key != seedKey && site.Type is not (3 or 4)) continue;
                    if (direct)
                    {
                        bool playable;
                        await probes.WaitAsync(ct).ConfigureAwait(false);
                        var timer = System.Diagnostics.Stopwatch.StartNew();
                        try { playable = await MediaProbe.CheckAsync(episode.Url, site.Header, ct, (url, height, bitrate) =>
                        { episode.Url = url; flag.ProbeHeight = height; flag.ProbeBitrate = bitrate; }, Setting.SourcePreference == "quality").ConfigureAwait(false); }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { playable = false; }
                        catch (Exception) when (!ct.IsCancellationRequested) { playable = false; }
                        finally { flag.ProbeLatencyMs = timer.ElapsedMilliseconds; probes.Release(); }
                        if (!playable) continue;
                    }
                    flag.IsMediaVerified = direct;
                    flag.SourceSite = site;
                    flag.SourceVodId = match.Id;
                    flag.OriginalFlag = flag.Flag;
                    flag.Flag = $"{site.Name} · {flag.Flag}";
                    ct.ThrowIfCancellationRequested();
                    App.Post(() => { if (!ct.IsCancellationRequested) found(new(site, match, flag)); });
                    Notify(new(site.Key, site.Name, direct ? "媒体检测通过" : "交由播放器解析验证", direct, flag.ProbeLatencyMs));
                    return;
                }
                Notify(new(site.Key, site.Name, string.IsNullOrEmpty(episodeName) ? "未找到可用视频线路" : $"没有可播放的{episodeName}", false));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception error) { Notify(new(site.Key, site.Name, error is OperationCanceledException ? "连接超时" : error.Message, false)); }
            finally { searches.Release(); }
            void Notify(DiscoveryStatus update)
            {
                if (ct.IsCancellationRequested) return;
                // Resolvable spider URLs remain unknown until the player actually renders a frame.
                if (update.Message != "交由播放器解析验证") SourceLearning.Remember(title, episodeName, VodConfigService.Cid, site.Key, update.Available);
                App.Post(() => { if (!ct.IsCancellationRequested) status(update); });
            }
        }));
    }
}
