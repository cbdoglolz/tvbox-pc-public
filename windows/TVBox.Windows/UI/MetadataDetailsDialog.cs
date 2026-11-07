using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;
using TVBoxForWindows.UI.Controls;
using Windows.System;

namespace TVBoxForWindows.UI;

public sealed class MetadataDetailsDialog : ContentDialog
{
    readonly MetadataDetailsService _service = new();
    readonly StackPanel _body = new() { Spacing = 12 };
    readonly StackPanel _ratings = new() { Spacing = 8 };
    readonly TextBlock _meta = new() { TextWrapping = TextWrapping.Wrap }, _overview = new() { TextWrapping = TextWrapping.Wrap }, _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    readonly TextBlock _hd = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Visibility = Visibility.Collapsed };
    readonly PosterImage _poster = new() { Width = 116, Height = 168 };
    readonly Button _favorite = new(), _unlink = new() { Content = "解除TMDB关联", Visibility = Visibility.Collapsed };
    readonly ComboBox _candidates = new() { PlaceholderText = "核对可能的TMDB作品", HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
    readonly Button _associate = new() { Content = "确认关联", Visibility = Visibility.Collapsed };
    readonly ComboBox _season = new() { Header = "播放季（TMDB剧集）", DisplayMemberPath = "Name", Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly CancellationTokenSource _lifetime = new();
    CatalogItem _work;
    public CatalogItem PlayItem { get; private set; }
    public bool SettingsRequested { get; private set; }
    public event Action<CatalogItem> WorkUpdated;
    public MetadataDetailsDialog(CatalogItem work, bool alreadyPlaying = false)
    {
        _work = work.Copy(); PlayItem = work;
        Title = work.Title; PrimaryButtonText = alreadyPlaying ? "返回播放" : "立即播放"; SecondaryButtonText = "搜索高清源"; CloseButtonText = "关闭";
        DefaultButton = ContentDialogButton.Close; Resources["ContentDialogMaxWidth"] = 760d;
        var header = new Grid { ColumnSpacing = 14 };
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(_poster); Grid.SetColumn(_meta, 1); header.Children.Add(_meta); _body.Children.Add(header);
        var hint = new InfoBar { Title = "可选的TMDB资料", Message = "可在设置中填写个人Key，补充TMDB资料与评分。不填仍能用普通片源看片。", Severity = InfoBarSeverity.Informational, IsOpen = OptionalMetadataHint.TryMarkShown(true) };
        var hintActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var settings = new Button { Content = "去设置" }; settings.Click += (_, _) => { SettingsRequested = true; Hide(); };
        var hide = new Button { Content = "不再提示" }; hide.Click += (_, _) => { OptionalMetadataHint.DismissForever(); hint.IsOpen = false; };
        hintActions.Children.Add(settings); hintActions.Children.Add(hide); hint.Content = hintActions; _body.Children.Add(hint);
        _body.Children.Add(_ratings); _body.Children.Add(_status); _body.Children.Add(_candidates); _body.Children.Add(_associate);
        _favorite.Click += (_, _) =>
        {
            var existing = CatalogLibrary.FindKeep(_work, VodConfigService.Cid);
            if (existing != null) Stores.DeleteKeep(VodConfigService.Cid, existing.Key); else CatalogLibrary.SaveKeep(_work, VodConfigService.Cid);
            RefreshFavorite();
        };
        _body.Children.Add(_favorite); _body.Children.Add(_season); _body.Children.Add(_overview); _body.Children.Add(_unlink); _body.Children.Add(_hd);
        _unlink.Click += (_, _) => { _work = _service.Unlink(_work); Changed(); Render(); _status.Text = "已解除关联；可在候选中重新核对，不会重置收藏和进度"; };
        _associate.Click += (_, _) =>
        {
            if (_candidates.SelectedItem is not MatchOption option) return;
            _work = _service.Associate(_work, option.Work); Changed(); Render(); _status.Text = "关联已保存，原作品身份和进度保留";
            _associate.Visibility = _candidates.Visibility = Visibility.Collapsed;
        };
        SecondaryButtonClick += (_, e) => { e.Cancel = true; _status.Text = HdSourceRegistry.Status; _hd.Text = HdSourceRegistry.Status; _hd.Visibility = Visibility.Visible; };
        PrimaryButtonClick += (_, _) =>
        {
            PlayItem = _work.Copy();
            if (!alreadyPlaying && _work.Provider == "tmdb" && _season.SelectedItem is WorkSeason season)
            {
                PlayItem.IdentityKey = ""; PlayItem.SelectedSeason = season.Number;
                if (season.Number > 1) PlayItem.Title = _work.Title + $" 第{season.Number}季";
                if (season.Year != null) PlayItem.Year = season.Year.ToString();
            }
        };
        Content = new ScrollViewer { MaxHeight = 540, Content = _body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Render(); Loaded += async (_, _) => await LoadAsync(); Closed += (_, _) => _lifetime.Cancel();
    }
    async Task LoadAsync()
    {
        _status.Text = "正在获取影片资料…";
        try
        {
            var result = await _service.LoadAsync(_work, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            _work = result.Work; Changed(); Render(); _status.Text = result.Message;
            if (result.Candidates.Count > 1)
            {
                _candidates.ItemsSource = result.Candidates.Select(c => new MatchOption(c)).ToList();
                _candidates.DisplayMemberPath = "Label"; _candidates.Visibility = _associate.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_lifetime.IsCancellationRequested) _status.Text = error is MetadataServiceException ? error.Message : "资料暂时无法更新，仍可立即播放"; }
    }
    void Changed()
    {
        CatalogLibrary.UpdateSavedMetadata(_work, VodConfigService.Cid); WorkUpdated?.Invoke(_work);
    }
    void RefreshFavorite() => _favorite.Content = CatalogLibrary.FindKeep(_work, VodConfigService.Cid) == null ? "☆ 收藏" : "★ 已收藏（点击取消）";
    void Render()
    {
        Title = _work.Title; _poster.Source = _work.Poster; _poster.FallbackSource = _work.PosterFallback;
        _meta.Text = string.Join("\n", new[]
        {
            _work.OriginalTitle.Length > 0 ? "原名：" + _work.OriginalTitle : "",
            "年份：" + (_work.Year.Length > 0 ? _work.Year : "未知"),
            "地区：" + (_work.RawCountries.Count > 0 ? string.Join(" / ", _work.RawCountries) : "未知"),
            "类型：" + (_work.RawGenres.Count > 0 ? string.Join(" / ", _work.RawGenres) : string.Join(" / ", _work.Genres.Select(CatalogVocabulary.Label))),
            _work.RuntimeMinutes > 0 ? $"片长/单集：{_work.RuntimeMinutes}分钟" : "片长：未知",
            _work.ReleaseDate.Length > 0 ? "上映/首播：" + _work.ReleaseDate : "",
            _work.Aliases.Count > 0 ? "别名：" + string.Join(" / ", _work.Aliases.Take(5)) : "",
        }.Where(s => s.Length > 0));
        _overview.Text = string.Join("\n\n", new[] { _work.Directors.Length > 0 ? "导演：" + _work.Directors : "", _work.Actors.Length > 0 ? "演员：" + _work.Actors : "", _work.Overview.Length > 0 ? _work.Overview : "剧情暂未获取" }.Where(s => s.Length > 0));
        _ratings.Children.Clear();
        foreach (var provider in new[] { "douban", "tmdb" })
        {
            var rating = _work.GetRating(provider); var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var text = new TextBlock { Text = rating.HasScore ? $"{rating.ProviderName}  {rating.Score:0.0} / 10  ·  {(rating.VoteCount != null ? rating.VoteCount.Value.ToString("N0") + "人评价" : "评价人数未知")}" : $"{rating.ProviderName}  暂无数据", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            ToolTipService.SetToolTip(text, $"{rating.Origin}\n{(rating.FetchedAt != null ? "获取时间：" + rating.FetchedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "尚未获取")}\n各平台评分独立，不做混合分数");
            var link = CatalogIdentity.ExternalLink(_work, provider); var button = new Button { Content = "去" + rating.ProviderName, IsEnabled = link != null };
            ToolTipService.SetToolTip(button, link == null ? "暂无可靠匹配链接" : link);
            button.Click += async (_, _) => { if (link != null && !await Launcher.LaunchUriAsync(new Uri(link))) _status.Text = "未能打开默认浏览器，请稍后重试"; };
            row.Children.Add(text); Grid.SetColumn(button, 1); row.Children.Add(button); _ratings.Children.Add(row);
        }
        _unlink.Visibility = _work.Provider == "douban" && _work.ExternalIds.Tmdb > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_work.Provider == "tmdb" && _work.Kind == "tv" && _work.Seasons.Count > 0)
        {
            _season.ItemsSource = _work.Seasons; _season.SelectedItem = _work.Seasons.FirstOrDefault(s => s.Number == _work.SelectedSeason) ?? _work.Seasons.First(); _season.Visibility = Visibility.Visible;
        }
        RefreshFavorite();
    }
    public record MatchOption(CatalogItem Work) { public string Label => $"{Work.Title} · {Work.Year} · {Work.Kind} · TMDB {Work.Id}"; }
}
