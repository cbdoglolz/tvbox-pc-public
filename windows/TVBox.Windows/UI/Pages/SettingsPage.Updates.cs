using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TVBoxForWindows.Core;

namespace TVBoxForWindows.UI.Pages;

public sealed partial class SettingsPage
{
    async void OnViewChanges(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 12, MaxWidth = 520 };
        try {
            using var notes = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppPaths.InstallRoot, "assets", "release-notes.json")));
            foreach (var version in notes.RootElement.EnumerateArray()) {
                panel.Children.Add(new TextBlock { Text = "v" + version.GetProperty("version").GetString() + " · " + version.GetProperty("date").GetString(), FontSize = 19, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                foreach (var change in version.GetProperty("changes").EnumerateArray()) panel.Children.Add(new TextBlock { Text = "• " + change.GetString(), TextWrapping = TextWrapping.Wrap });
            }
        } catch { panel.Children.Add(new TextBlock { Text = "本版本的更新说明暂不可用。" }); }
        await new ContentDialog { RequestedTheme = ActualTheme, XamlRoot = XamlRoot, Title = "版本更新", Content = new ScrollViewer { Content = panel, MaxHeight = 440, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, CloseButtonText = "关闭" }.ShowAsync();
    }
    public void FocusUpdateSettings(ReleaseDownload available=null) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=>{
        UpdateLayout();SoftwareUpdatePanel.StartBringIntoView(new BringIntoViewOptions {VerticalAlignmentRatio=0,AnimationDesired=false});
        if(available!=null){_availableUpdate=available;DownloadUpdateButton.Visibility=Visibility.Visible;UpdateStatusText.Text=$"发现 v{available.Version} · {available.Size/1024d/1024:0.0} MiB，可下载安装";}
    });
    ReleaseDownload _availableUpdate;
    StagedUpdate _stagedUpdate;
    bool _updateBusy;
    async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        if (_updateBusy) return; _updateBusy = true; CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查公开稳定版…";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            _availableUpdate = await new ReleaseUpdate().CheckAsync(BuildInfo.Version, timeout.Token);
            DownloadUpdateButton.Visibility = _availableUpdate == null ? Visibility.Collapsed : Visibility.Visible;
            UpdateStatusText.Text = _availableUpdate == null ? $"当前 v{BuildInfo.Version} 已是最新稳定版（预览版不参与自动更新）" : $"发现 v{_availableUpdate.Version} · {_availableUpdate.Size / 1024d / 1024:0.0} MiB，下载期间可继续看片";
        }
        catch { UpdateStatusText.Text = "暂时无法检查更新；可稍后重试，或点击“版本与下载”手动下载。"; }
        finally { _updateBusy = false; CheckUpdateButton.IsEnabled = true; }
    }

    async void OnDownloadUpdate(object sender, RoutedEventArgs e)
    {
        if (_updateBusy || _availableUpdate == null) return;
        _updateBusy = true; DownloadUpdateButton.IsEnabled = CheckUpdateButton.IsEnabled = false;
        UpdateDownloadProgress.Value = 0; UpdateDownloadProgress.Visibility = Visibility.Visible;
        var progress = new Progress<double>(value => { UpdateDownloadProgress.Value = value; UpdateStatusText.Text = $"下载 v{_availableUpdate.Version}：{value:0}% · 当前播放继续"; });
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
            _stagedUpdate = await new ReleaseUpdate().DownloadAsync(_availableUpdate, Path.Combine(AppPaths.Root, "updates"), progress, timeout.Token);
            UpdateStatusText.Text = $"v{_stagedUpdate.Version} 已下载，SHA-256和纯净目录校验通过。点击安装将保存进度并正常退出，随后重启。";
            InstallUpdateButton.Visibility = Visibility.Visible;
        }
        catch { UpdateStatusText.Text = "下载或校验失败，当前程序未修改；请重试或从版本页面下载。"; }
        finally { _updateBusy = false; DownloadUpdateButton.IsEnabled = CheckUpdateButton.IsEnabled = true; UpdateDownloadProgress.Visibility = Visibility.Collapsed; }
    }

    async void OnInstallUpdate(object sender, RoutedEventArgs e)
    {
        if (_updateBusy || _stagedUpdate == null) return;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = $"安装 v{_stagedUpdate.Version}",
            Content = "将保存播放进度并退出本窗口，安装校验后的更新，再重新启动。收藏、历史和设置保留。若同目录还有其他窗口正在运行，请先正常退出它们。", PrimaryButtonText = "保存并安装", CloseButtonText = "稍后", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var manifest = await Task.Run(() => UpdateInstaller.Prepare(_stagedUpdate, AppPaths.InstallRoot, Path.Combine(AppPaths.AssetDir, "updates", "install-update.ps1")));
            UpdateInstaller.Launch(manifest);
            App.Main.ExitForUpdate();
        }
        catch { UpdateStatusText.Text = "无法准备安装，当前程序未修改。请确认程序目录可写，或从版本页面下载完整便携包。"; }
    }
}
