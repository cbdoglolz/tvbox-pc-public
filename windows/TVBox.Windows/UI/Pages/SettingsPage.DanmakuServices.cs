using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TVBoxForWindows.Core;
using TVBoxForWindows.Models;
using TVBoxForWindows.Player;

namespace TVBoxForWindows.UI.Pages;

public sealed partial class SettingsPage
{
    void LoadDanmakuServices()
    {
        DanmakuBiliToggle.IsOn = Setting.GetBool("danmaku_bili", true);
        DanmakuServicesList.Children.Clear();
        foreach (var service in DanmakuService.All())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var toggle = new ToggleSwitch { Header = service.Name, IsOn = service.Enabled, Tag = service.Id };
            toggle.Toggled += (_, _) =>
            {
                try { var all = DanmakuService.All(); all = all.Select(p => p.Id == (string)toggle.Tag ? p with { Enabled = toggle.IsOn } : p).ToList(); DanmakuService.Save(all); DanmakuSearch.NotifySettingsChanged(); }
                catch { DanmakuApiStatus.Text = "服务设置暂时无法保存，原资料保留"; }
            };
            var remove = new Button { Content = "移除", Tag = service.Id, VerticalAlignment = VerticalAlignment.Bottom };
            remove.Click += (_, _) =>
            {
                try { var all = DanmakuService.All(); all.RemoveAll(p => p.Id == (string)remove.Tag); DanmakuService.Save(all); LoadDanmakuServices(); DanmakuSearch.NotifySettingsChanged(); }
                catch { DanmakuApiStatus.Text = "服务暂时无法移除，原资料保留"; }
            };
            row.Children.Add(toggle); row.Children.Add(remove); DanmakuServicesList.Children.Add(row);
        }
    }
    void OnDanmakuBiliToggled(object sender, RoutedEventArgs e)
    { if (!_updating) { Setting.Put("danmaku_bili", DanmakuBiliToggle.IsOn); DanmakuSearch.NotifySettingsChanged(); } }
    void OnDanmakuServiceTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || DanmakuServiceUrl == null) return;
        var official = DanmakuServiceType.SelectedIndex == 1;
        DanmakuServiceUrl.IsReadOnly = official;
        DanmakuServiceUrl.Text = official ? "https://api.dandanplay.net/api/v2/" : "";
        DanmakuServiceName.Text = official ? "弹弹play官方API" : "";
    }
    void OnAddDanmakuService(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = DanmakuServiceName.Text.Trim(); if (name.Length == 0) throw new ArgumentException("请填写服务名称");
            var url = DanmakuSearch.BaseUrl(DanmakuServiceUrl.Text);
            var official = url.TrimEnd('/') == "https://api.dandanplay.net/api/v2";
            if (official && (DanmakuOfficialAppId.Text.Trim().Length == 0 || DanmakuOfficialSecret.Password.Length == 0)) throw new ArgumentException("官方API需要自己的AppId和AppSecret；也可使用公共代理或自建服务");
            var all = DanmakuService.All(); if (all.Count >= 10) throw new ArgumentException("最多配置10个附加服务，请先移除不用的服务");
            all.Add(new(Guid.NewGuid().ToString("N"), name, url, true, official ? DanmakuOfficialAppId.Text.Trim() : "", official ? DanmakuOfficialSecret.Password : ""));
            DanmakuService.Save(all); DanmakuOfficialSecret.Password = ""; DanmakuServiceUrl.Text = ""; DanmakuServiceName.Text = "";
            LoadDanmakuServices(); DanmakuSearch.NotifySettingsChanged(); DanmakuApiStatus.Text = "已加密保存并启用；播放时自动尝试，未匹配会继续其他来源";
        }
        catch (ArgumentException error) { DanmakuApiStatus.Text = error.Message; }
        catch { DanmakuApiStatus.Text = "无法安全保存，请稍后重试；原配置保留"; }
    }
}
