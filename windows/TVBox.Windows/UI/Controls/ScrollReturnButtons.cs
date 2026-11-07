using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TVBoxForWindows.UI.Controls;

public sealed class ScrollReturnButtons : UserControl
{
    readonly Button _up = new() { Content = "↑", Width = 34, Height = 34, Padding = new(0) };
    readonly Button _down = new() { Content = "↓", Width = 34, Height = 34, Padding = new(0) };
    ScrollViewer _scroll;
    double _returnOffset;
    Action _jumping;
    public ScrollReturnButtons()
    {
        HorizontalAlignment = HorizontalAlignment.Right; VerticalAlignment = VerticalAlignment.Top; Margin = new(0, 8, 18, 0);
        var panel = new StackPanel { Spacing = 5 }; panel.Children.Add(_up); panel.Children.Add(_down); Content = panel;
        ToolTipService.SetToolTip(_up, "回到顶部，保留瀑布流与当前位置"); ToolTipService.SetToolTip(_down, "回到刚才浏览的位置");
        _up.Click += (_, _) => { if (_scroll == null) return; _returnOffset = _scroll.VerticalOffset; _jumping?.Invoke(); _scroll.ChangeView(null, 0, null, true); Refresh(); };
        _down.Click += (_, _) => { if (_scroll == null) return; var offset = _returnOffset; _returnOffset = 0; _jumping?.Invoke(); _scroll.ChangeView(null, offset, null, true); Refresh(); }; Refresh();
    }
    public void Attach(ScrollViewer viewer, Action jumping = null) { if (ReferenceEquals(_scroll, viewer)) return; if (_scroll != null) _scroll.ViewChanged -= OnView; _scroll = viewer; _returnOffset = 0; _jumping = jumping; if (_scroll != null) _scroll.ViewChanged += OnView; Refresh(); }
    void OnView(object sender, ScrollViewerViewChangedEventArgs e) { if (_returnOffset > 0 && _scroll.VerticalOffset >= _returnOffset - 8) _returnOffset = 0; Refresh(); }
    void Refresh() { _up.Visibility = _scroll?.VerticalOffset > 200 ? Visibility.Visible : Visibility.Collapsed; _down.Visibility = _returnOffset > 0 && _scroll != null && _scroll.VerticalOffset < _returnOffset - 8 ? Visibility.Visible : Visibility.Collapsed; }
}
