using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.UI.Controls;

public sealed class SourceAdButton : Button
{
    string _renderKey;
    public CatalogItem Work { get; set; }
    public Site Site { get; set; }
    public SourceAdButton() { Padding = new(5, 4, 5, 4); Loaded += (_, _) => Refresh(); }
    public void Refresh()
    {
        var mark = SourceAdPolicy.Get(Work, Site?.Key); IsEnabled = Work != null && Site != null;
        var key = Work?.WorkKey + ":" + Site?.Key + ":" + mark; if (_renderKey == key) return; _renderKey = key;
        var work = Work?.Copy(); var site = Site;
        Content = new AdMarkGlyph { Mark = mark };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, SourceAdPolicy.Label(mark) + " · " + Site?.Name);
        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(mark == AdMark.Ads ? Windows.UI.Color.FromArgb(255, 246, 99, 110) : mark == AdMark.Clean ? Windows.UI.Color.FromArgb(255, 113, 221, 178) : Windows.UI.Color.FromArgb(255, 229, 180, 63));
        ToolTipService.SetToolTip(this, SourceAdPolicy.Label(mark) + " · 标记本片本季的“" + Site?.Name + "”来源；下一集自动选源优先避开有广告站点，不会删除广告画面");
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "手动标记本片本季的片内广告；自动选源会据此优化优先级。", IsEnabled = false });
        menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var value in Enum.GetValues<AdMark>()) { var item = new MenuFlyoutItem { Text = SourceAdPolicy.Label(value) }; item.Click += (_, _) => { SourceAdPolicy.Set(work, site, value); DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Refresh); App.Main.ShowNotice("已标记“" + Site?.Name + "”：" + SourceAdPolicy.Label(value) + "，应用到本片本季"); }; menu.Items.Add(item); }
        Flyout = menu;
    }
}
