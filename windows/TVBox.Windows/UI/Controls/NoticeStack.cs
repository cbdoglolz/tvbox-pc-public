using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace TVBoxForWindows.UI.Controls;
public sealed class NoticeStack : StackPanel
{
    readonly Dictionary<InfoBar, Microsoft.UI.Dispatching.DispatcherQueueTimer> _timers = new();
    public NoticeStack() { Spacing = 8; HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Center; MaxWidth = 660; }
    public void Show(string message, InfoBarSeverity severity)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var existing = Children.OfType<InfoBar>().FirstOrDefault(b => b.Message == message);
        if (existing != null) return;
        while (Children.Count >= 3) Remove((InfoBar)Children[0]);
        var bar = new InfoBar { Message = message, Severity = severity, IsOpen = true, IsClosable = true, RequestedTheme = ElementTheme.Dark,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(245,35,36,56)),
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White), HorizontalAlignment = HorizontalAlignment.Stretch };
        var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(3); timer.IsRepeating = false;
        timer.Tick += (_, _) => Remove(bar); bar.Closed += (_, _) => Remove(bar);
        _timers[bar] = timer; Children.Add(bar); timer.Start();
    }
    public void Clear() { foreach (var bar in Children.OfType<InfoBar>().ToList()) Remove(bar); }
    void Remove(InfoBar bar) { if (_timers.Remove(bar, out var timer)) timer.Stop(); Children.Remove(bar); }
}
