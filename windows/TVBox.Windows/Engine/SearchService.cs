using TVBoxForWindows.Models;

namespace TVBoxForWindows.Engine;

/// <summary>聚合搜索（移植自 SiteViewModel.searchContent(List&lt;Site&gt;)）：并行搜索全部可搜站点，逐站回调。</summary>
public class SearchService
{
    /// <summary>并行搜索所有可搜站点；每站完成即回调（UI 线程）。keyword 自动繁→简。
    /// 最多三站并行，单站超时用 site.RequestTimeout；每站报告结果或错误。</summary>
    public static async Task SearchAll(string keyword, bool quick, Action<Models.Site, List<Models.Vod>> onSiteResult, CancellationToken ct,
        Action<SearchStatus> onStatus = null)
    {
        keyword = Core.Trans.T2S(keyword);
        var sites = Core.SiteRegistry.Active.Where(s => !s.IsHidden && (quick ? s.IsQuickSearch : s.IsSearchable)).ToList();
        if (sites.Count == 0) return;
        using var gate = new SemaphoreSlim(10);
        var tasks = sites.Select(site => SearchOne(site, keyword, quick, onSiteResult, gate, ct, onStatus)).ToList();
        try { await Task.WhenAll(tasks); } catch { }
    }

    /// <summary>单站搜索：限流 + 超时 + 取消，异常一律吞掉（等价 Android 每站独立任务失败不影响整体）。</summary>
    static async Task SearchOne(Site site, string keyword, bool quick, Action<Site, List<Vod>> onSiteResult, SemaphoreSlim gate, CancellationToken ct,
        Action<SearchStatus> onStatus)
    {
        try { await gate.WaitAsync(ct); }
        catch { return; }
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (ct.IsCancellationRequested || !Core.SiteRegistry.Enabled(site.Key)) return;
            var result = await SiteService.SearchContent(site, keyword, quick).WaitAsync(TimeSpan.FromMilliseconds(site.RequestTimeout), ct);
            if (ct.IsCancellationRequested || !Core.SiteRegistry.Enabled(site.Key)) return;
            var error = result == null ? "没有返回数据" : result.GetMsg();
            App.Post(() =>
            {
                if (ct.IsCancellationRequested || !Core.SiteRegistry.Enabled(site.Key)) return;
                if (result?.List.Count > 0) onSiteResult?.Invoke(site, result.List);
                onStatus?.Invoke(new(site.Name, result?.List.Count ?? 0, error, started.ElapsedMilliseconds));
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error)
        {
            App.Post(() =>
            {
                if (!ct.IsCancellationRequested)
                    onStatus?.Invoke(new(site.Name, 0, error is TimeoutException ? "连接超时" : error.Message, started.ElapsedMilliseconds));
            });
        }
        finally { gate.Release(); }
    }
}

public record SearchStatus(string Name, int Count, string Error, long ElapsedMs);
