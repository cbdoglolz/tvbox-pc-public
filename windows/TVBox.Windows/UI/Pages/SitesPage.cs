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
    readonly VariableSizedWrapGrid _rows = new() { Orientation = Orientation.Horizontal, ItemWidth = 370, ItemHeight = 88, MaximumRowsOrColumns = 2 };
    readonly ComboBox _groups = new() { PlaceholderText = "全部分组", MinWidth = 160 };
    readonly StackPanel _feeds = new() { Spacing = 8 };
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    public SitesPage()
    {
        InitializeComponent();
        var body = new StackPanel { Spacing = 12, Padding = new(24) };
        body.Children.Add(new TextBlock { Text = "站点", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = "关闭的站点不参与搜索和自动选源；当前播放不受开关影响。设置随本地资料保留。", TextWrapping = TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(_groups); _groups.SelectionChanged += (_, _) => Render();
        void Action(string label, Action callback) { var b = new Button { Content = label }; b.Click += (_, _) => callback(); actions.Children.Add(b); }
        Action("全部开启", () => SetAll(true)); Action("全部关闭", () => SetAll(false)); Action("添加站点", async () => await AddSite());
        body.Children.Add(actions); body.Children.Add(_summary); body.Children.Add(_rows);
        body.Children.Add(new TextBlock { Text = "扩展与云端", FontSize = 22 });
        var clouds = new TextBlock { Text = "国际实验站点默认关闭，尚未通过本机播放验证，可能返回403或没有匹配资源；可自行启用测试。小雅 / G-Box / AList / WebDAV接入准备中。", TextWrapping = TextWrapping.Wrap }; body.Children.Add(clouds);
        var privateActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; var addFeed = new Button { Content = "添加私人订阅" }; addFeed.Click += async (_, _) => await AddPrivateFeed(); privateActions.Children.Add(addFeed); var refreshFeeds = new Button { Content = "检查订阅更新" }; refreshFeeds.Click += async (_, _) => { refreshFeeds.IsEnabled = false; try { foreach(var feed in PrivateSubscriptions.Items.Where(f => f.Enabled)) await PrivateSubscriptions.Refresh(feed); RenderFeeds(); Render(); App.Main.ShowNotice("订阅检查完成，站点信息已同步"); } catch { App.Main.ShowNotice("订阅暂不可用，保留上次成功站点"); } finally { refreshFeeds.IsEnabled = true; } }; privateActions.Children.Add(refreshFeeds); body.Children.Add(privateActions); body.Children.Add(_feeds); RenderFeeds();
        Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Loaded += (_, _) => { var selected = _groups.SelectedItem; _groups.ItemsSource = new[] { "全部分组" }.Concat(SiteRegistry.All.Select(s => SiteRegistry.Preference(s.Key).Group).Distinct()).ToList(); _groups.SelectedItem = selected ?? "全部分组"; Render(); };
    }
    IEnumerable<Site> Visible => SiteRegistry.All.Where(s => !SiteRegistry.Preference(s.Key).Removed && (_groups.SelectedItem as string is not string group || group == "全部分组" || SiteRegistry.Preference(s.Key).Group == group));
    void SetAll(bool enabled) { foreach (var site in Visible.ToList()) { var p = SiteRegistry.Preference(site.Key); p.Enabled = enabled; SiteRegistry.Set(site.Key, p); } Render(); }
    void Render()
    {
        _rows.Children.Clear(); _summary.Text = $"当前配置 {SiteRegistry.All.Count(s => !SiteRegistry.Preference(s.Key).Removed)} 个站点，已开启 {SiteRegistry.Active.Count} 个";
        foreach (var site in Visible) {
            var pref = SiteRegistry.Preference(site.Key); var row = new StackPanel { Spacing = 4 };
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var enabled = new ToggleSwitch { IsEnabled = PrivateSubscriptions.Compatible(site.Key), IsOn = pref.Enabled && PrivateSubscriptions.Compatible(site.Key), OnContent = site.Name, OffContent = site.Name, FontSize = 12, MinWidth = 0, Width = 134, MaxWidth = 134 };
            enabled.Toggled += (_, _) => { var p = SiteRegistry.Preference(site.Key); p.Enabled = enabled.IsOn; SiteRegistry.Set(site.Key, p); _summary.Text = $"已开启 {SiteRegistry.Active.Count} 个站点"; }; controls.Children.Add(enabled);
            var group = new TextBox { Text = pref.Group, PlaceholderText = "分组", MinWidth = 0, Width = 62, FontSize = 11 }; group.LostFocus += (_, _) => { var p = SiteRegistry.Preference(site.Key); p.Group = string.IsNullOrWhiteSpace(group.Text) ? "默认点播" : group.Text.Trim(); SiteRegistry.Set(site.Key, p); }; controls.Children.Add(group);
            var copy = new Button { Content = new SymbolIcon(Symbol.Copy), VerticalAlignment = VerticalAlignment.Bottom, MinWidth = 0, Width = 28, Height = 28, Padding = new(3) }; ToolTipService.SetToolTip(copy, "复制接口网址"); copy.Click += (_, _) => { var data = new DataPackage(); data.SetText(site.Api); Clipboard.SetContent(data); App.Main.ShowNotice("已复制“" + site.Name + "”接口网址"); }; controls.Children.Add(copy);
            var open = new Button { Content = ExternalLinkIcon.Create(), VerticalAlignment = VerticalAlignment.Bottom, MinWidth = 0, Width = 28, Height = 28, Padding = new(3) }; ToolTipService.SetToolTip(open, "打开站点网址"); open.Click += async (_, _) => { try { if (Uri.TryCreate(site.Api, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") await Windows.System.Launcher.LaunchUriAsync(uri); } catch { App.Main.ShowNotice("无法打开站点"); } }; controls.Children.Add(open);
            var remove = new Button { Content = new SymbolIcon(Symbol.Delete), VerticalAlignment = VerticalAlignment.Bottom, MinWidth = 0, Width = 28, Height = 28, Padding = new(3) }; ToolTipService.SetToolTip(remove, "从本地站点列表移除"); remove.Click += async (_, _) => { var d = new ContentDialog { RequestedTheme = ActualTheme, XamlRoot = XamlRoot, Title = "移除站点？", Content = "仅移除“" + site.Name + "”的本地入口，收藏、历史与原订阅不删除。", PrimaryButtonText = "移除", CloseButtonText = "取消" }; if (await d.ShowAsync() == ContentDialogResult.Primary) { var p = SiteRegistry.Preference(site.Key); p.Removed = true; SiteRegistry.Set(site.Key, p); Render(); } }; controls.Children.Add(remove);
            if (!PrivateSubscriptions.Compatible(site.Key)) ToolTipService.SetToolTip(enabled,"需要Android兼容运行环境，当前不参与自动播放");
            row.Children.Add(controls); row.Children.Add(new TextBlock { Text = site.Api, TextWrapping = TextWrapping.Wrap, FontSize = 10, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, IsTextSelectionEnabled = true });
            _rows.Children.Add(new Border { Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["DiscoveryPanel"], Width = 358, Height = 80, Padding = new(8), Margin = new(4), Child = row });
        }
        var removed = SiteRegistry.All.Where(s => SiteRegistry.Preference(s.Key).Removed).ToList();
        if (removed.Count > 0) { var restore = new Button { Content = "恢复已移除站点（" + removed.Count + "）" }; restore.Click += (_, _) => { foreach (var site in removed) { var p = SiteRegistry.Preference(site.Key); p.Removed = false; SiteRegistry.Set(site.Key, p); } Render(); }; _rows.Children.Add(restore); }
    }
    void RenderFeeds()
    {
        _feeds.Children.Clear();
        foreach(var feed in PrivateSubscriptions.Items) {
            var toggle = new ToggleSwitch { Header = feed.Name, IsOn = feed.Enabled, OnContent = "自动检查已开启", OffContent = "自动检查已关闭" };
            toggle.Toggled += (_, _) => { PrivateSubscriptions.SetEnabled(feed,toggle.IsOn); Render(); };
            _feeds.Children.Add(toggle); _feeds.Children.Add(new TextBlock { Text = feed.Status + (feed.Checked==default ? "" : " · " + feed.Checked.ToString("MM-dd HH:mm")), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        }
    }
    async Task AddPrivateFeed()
    {
        var name = new TextBox { Header = "订阅名称", Text = "私人订阅" }; var url = new PasswordBox { Header = "订阅地址（仅在本机加密保存）" }; var http = new CheckBox { Content = "允许HTTP明文传输（建议使用HTTPS）" };
        var panel = new StackPanel { Spacing = 10 }; panel.Children.Add(name);panel.Children.Add(url);panel.Children.Add(http);
        var dialog = new ContentDialog { RequestedTheme = ActualTheme, XamlRoot = XamlRoot, Title = "添加私人订阅", Content = panel, PrimaryButtonText = "保存并读取", CloseButtonText = "取消" }; PrivateFeed added = null;
        dialog.PrimaryButtonClick += (_, args) => { try { added = PrivateSubscriptions.Add(name.Text,url.Password,http.IsChecked==true); } catch (ArgumentException error) { args.Cancel=true;App.Main.ShowNotice(error.Message); } };
        if(await dialog.ShowAsync()==ContentDialogResult.Primary && added!=null) { try { await PrivateSubscriptions.Refresh(added);App.Main.ShowNotice("已读取订阅，站点将在资源和体育页显示"); } catch { App.Main.ShowNotice("地址已保存，读取失败；保留现有站点"); } }
        RenderFeeds();Render();
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
