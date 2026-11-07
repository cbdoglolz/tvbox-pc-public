using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TVBoxForWindows.UI.Pages;

public sealed partial class PlayerPage
{
    long _mediaSummaryTick;
    bool _mediaDialogOpen;
    void RefreshMediaSummary()
    {
        if (_core == null || MediaInfoText == null) return;
        if (Environment.TickCount64 - _mediaSummaryTick < 1000) return;
        _mediaSummaryTick = Environment.TickCount64;
        MediaInfoText.Text = _core.ReadMediaSnapshot().Summary + " · 详情 ›";
    }
    async void OnMediaDetails(object sender, RoutedEventArgs e)
    {
        if (_mediaDialogOpen || _core == null) return;
        _mediaDialogOpen = true; _menuOpen++;
        try
        {
            var info = _core.ReadMediaSnapshot();
            var detail = $"影片：{_session?.History?.VodName}\n集数：{_session?.CurrentEpisode?.Name}\n来源：{_session?.CurrentSite?.Name}\n\n分辨率：{info.Width} × {info.Height}\n编码：{info.Codec}\n封装/传输：{info.Container}\n帧率：{info.Fps:0.##} fps\n音视频码率：{info.Rate}\n时长：{TimeSpan.FromMilliseconds(info.DurationMs):hh\\:mm\\:ss}\n大小：{info.SizeLabel}\n\n媒体探测耗时：{Models.PlaybackPresentation.Delay(_session?.CurrentFlag?.ProbeLatencyMs)}（不含排队，不是 Ping）\n绿色 <1秒，橙色 1–3秒，红色 ≥3秒。\n分辨率和编码来自当前解码器；码率未知时保持未知，下载速度不当作码率。";
            detail += $"\n\n本次流缓冲：{_bufferHealth.Count}次，累计{_bufferHealth.TotalMs / 1000d:0.0}秒（不含主动定位）。远程点播预缓冲3秒、预读上限30秒；无法消除来源持续传输不足。";
            await new ContentDialog { XamlRoot = XamlRoot, Title = "媒体信息", CloseButtonText = "关闭",
                Content = new ScrollViewer { MaxHeight = 500, Content = new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } } }.ShowAsync();
        }
        finally { _mediaDialogOpen = false; _menuOpen = Math.Max(0, _menuOpen - 1); if (!_closed) ShowControls(); }
    }
}
