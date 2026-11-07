using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;
using TVBoxForWindows.UI.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace TVBoxForWindows.UI.Pages;

public sealed partial class SitesPage : Page
{
    readonly StackPanel _rows = new() { Spacing = 10 };
    readonly ComboBox _groups = new() { PlaceholderText = "全部分组", MinWidth = 160 };
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    public SitesPage()
    {
        InitializeComponent();
        var body = new StackPanel { Spacing = 18, Padding = new(24) };
        body.Children.Add(new TextBlock { Text = "站点", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = "关闭的站点不参与搜索和自动选源；当前播放不受开关影响。设置随本地资料保留。", TextWrapping = TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(_groups); _groups.SelectionChanged += (_, _) => Render();
        void Action(string label, Action callback) { var b = new Button { Content = label }; b.Click += (_, _) => callback(); actions.Children.Add(b); }
        Action("全部开启", () => SetAll(true)); Action("全部关闭", () => SetAll(false)); Action("添加站点", async () => await AddSite());
        body.Children.Add(actions); body.Children.Add(_summary); body.Children.Add(_rows);
        body.Children.Add(new TextBlock { Text = "扩展与云端", FontSize = 22 });
        var clouds = new TextBlock { Text = "国际实验站点默认关闭，尚未通过本机播放验证，可能返回403或没有匹配资源；可自行启用测试。小雅 / G-Box / AList / WebDAV接入准备中。", TextWrapping = TextWrapping.Wrap }; body.Children.Add(clouds);
        Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Loaded += (_, _) => { var selected = _groups.SelectedItem; _groups.ItemsSource = new[] { "全部分组" }.Concat(SiteRegistry.All.Select(s => SiteRegistry.Preference(s.Key).Group).Distinct()).ToList(); _groups.SelectedItem = selected ?? "全部分组"; Render(); };
    }
    IEnumerable<Site> Visible => SiteRegistry.All.Where(s => !SiteRegistry.Preference(s.Key).Removed && (_groups.SelectedItem as string is not string group || group == "全部分组" || SiteRegistry.Preference(s.Key).Group == group));
    void SetAll(bool enabled) { foreach (var site in Visible.ToList()) { var p = SiteRegistry.Preference(site.Key); p.Enabled = enabled; SiteRegistry.Set(site.Key, p); } Render(); }
    void Render()
    {
        _rows.Children.Clear(); _summary.Text = $"当前配置 {SiteRegistry.All.Count(s => !SiteRegistry.Preference(s.Key).Removed)} 个站点，已开启 {SiteRegistry.Active.Count} 个";
        foreach (var site in Visible) {
            var pref = SiteRegistry.Preference(site.Key); var row = new StackPanel { Spacing = 8, Padding = new(12) };
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var enabled = new ToggleSwitch { Header = site.Name, IsOn = pref.Enabled, OnContent = "已启用", OffContent = "已停用" };
            enabled.Toggled += (_, _) => { var p = SiteRegistry.Preference(site.Key); p.Enabled = enabled.IsOn; SiteRegistry.Set(site.Key, p); _summary.Text = $"已开启 {SiteRegistry.Active.Count} 个站点"; }; controls.Children.Add(enabled);
            var group = new TextBox { Text = pref.Group, Header = "分组", Width = 130 }; group.LostFocus += (_, _) => { var p = SiteRegistry.Preference(site.Key); p.Group = string.IsNullOrWhiteSpace(group.Text) ? "默认点播" : group.Text.Trim(); SiteRegistry.Set(site.Key, p); }; controls.Children.Add(group);
            var copy = new Button { Content = new SymbolIcon(Symbol.Copy), VerticalAlignment = VerticalAlignment.Bottom }; ToolTipService.SetToolTip(copy, "复制接口网址"); copy.Click += (_, _) => { var data = new DataPackage(); data.SetText(site.Api); Clipboard.SetContent(data); App.Main.ShowNotice("已复制“" + site.Name + "”接口网址"); }; controls.Children.Add(copy);
            var open = new Button { Content = ExternalLinkIcon.Create(), VerticalAlignment = VerticalAlignment.Bottom }; ToolTipService.SetToolTip(open, "打开站点网址"); open.Click += async (_, _) => { try { if (Uri.TryCreate(site.Api, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") await Windows.System.Launcher.LaunchUriAsync(uri); } catch { App.Main.ShowNotice("无法打开站点"); } }; controls.Children.Add(open);
            var remove = new Button { Content = new SymbolIcon(Symbol.Delete), VerticalAlignment = VerticalAlignment.Bottom }; ToolTipService.SetToolTip(remove, "从本地站点列表移除"); remove.Click += async (_, _) => { var d = new ContentDialog { RequestedTheme = ActualTheme, XamlRoot = XamlRoot, Title = "移除站点？", Content = "仅移除“" + site.Name + "”的本地入口，收藏、历史与原订阅不删除。", PrimaryButtonText = "移除", CloseButtonText = "取消" }; if (await d.ShowAsync() == ContentDialogResult.Primary) { var p = SiteRegistry.Preference(site.Key); p.Removed = true; SiteRegistry.Set(site.Key, p); Render(); } }; controls.Children.Add(remove);
            row.Children.Add(controls); row.Children.Add(new TextBlock { Text = site.Api, TextWrapping = TextWrapping.Wrap, FontSize = 12, IsTextSelectionEnabled = true });
            _rows.Children.Add(new Border { Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["DiscoveryPanel"], Child = row });
        }
        var removed = SiteRegistry.All.Where(s => SiteRegistry.Preference(s.Key).Removed).ToList();
        if (removed.Count > 0) { var restore = new Button { Content = "恢复已移除站点（" + removed.Count + "）" }; restore.Click += (_, _) => { foreach (var site in removed) { var p = SiteRegistry.Preference(site.Key); p.Removed = false; SiteRegistry.Set(site.Key, p); } Render(); }; _rows.Children.Add(restore); }
    }
    async Task AddSite()
    {
        var name = new TextBox { Header = "站点名称" }; var url = new TextBox { Header = "MacCMS JSON接口网址", PlaceholderText = "https://…/api.php/provide/vod/" }; var group = new TextBox { Header = "分组", Text = "自定义" };
        var panel = new StackPanel { Spacing = 10 }; panel.Children.Add(name); panel.Children.Add(url); panel.Children.Add(group);
        var dialog = new ContentDialog { RequestedTheme = ActualTheme, XamlRoot = XamlRoot, Title = "添加点播站点", Content = panel, PrimaryButtonText = "保存", CloseButtonText = "取消" };
        dialog.PrimaryButtonClick += (_, e) => { try { SiteRegistry.Add(new Site { Name = name.Text.Trim(), Api = url.Text.Trim() }, group.Text); } catch (ArgumentException error) { e.Cancel = true; App.Main.ShowNotice(error.Message); } };
        await dialog.ShowAsync(); Render();
    }
}
