using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Input;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;
using TVBoxForWindows.UI.Controls;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.ObjectModel;

namespace TVBoxForWindows.UI.Pages;

public sealed partial class WorkDetailsPage : Page
{
    readonly MetadataDetailsService _service = new();
    CancellationTokenSource _lifetime;
    CatalogItem _work;
    List<CatalogItem> _matches = new();
    bool _similarLoading, _renderingSeasons;
    CancellationTokenSource _seasonCts;
    readonly WorkMediaService _media = new();
    readonly WorkRecommendations _recommendations = new();
    readonly ObservableCollection<CatalogItem> _similarItems = new();
    WorkRecommendationState _similarState;
    bool _similarWheel;
    public WorkDetailsPage() { InitializeComponent(); Similar.ItemsSource = _similarItems; ReturnButtons.Attach(DetailScroll, () => _similarWheel = false);
        DetailScroll.AddHandler(PointerWheelChangedEvent, new PointerEventHandler((_, e) => { if (e.GetCurrentPoint(DetailScroll).Properties.MouseWheelDelta < 0) _similarWheel = true; }), true);
    }
    void OnDetailScroll(object sender, ScrollViewerViewChangedEventArgs e)
    {
        var sticky = DetailScroll.VerticalOffset > 270 ? Visibility.Visible : Visibility.Collapsed;
        if (StickyTitle.Visibility != sticky) { StickyTitle.Visibility = sticky; StickyTitle.SetActive(sticky == Visibility.Visible); }
        if (_similarWheel && !e.IsIntermediate && !_similarLoading && _similarState?.HasMore == true && DetailScroll.ScrollableHeight - DetailScroll.VerticalOffset < 260)
        { _similarWheel = false; _ = LoadSimilarAsync(); }
    }
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e); _lifetime?.Cancel(); _lifetime = new();
        _work = (e.Parameter as CatalogItem)?.Copy() ?? new(); _similarLoading = false; _similarState = null;
        Sources.Children.Clear(); _similarItems.Clear(); MoreSimilar.IsEnabled = true;
        CollectionPanel.Visibility = VersionsPanel.Visibility = Visibility.Collapsed;
        Render(); _ = LoadAsync(_lifetime.Token);
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { _lifetime?.Cancel(); _seasonCts?.Cancel(); base.OnNavigatedFrom(e); }
    public void RefreshAfterNavigation()
    {
        if (_lifetime?.IsCancellationRequested != true) return;
        _lifetime = new(); _ = LoadAsync(_lifetime.Token);
    }
    public void EnterBackground() { _lifetime?.Cancel(); _seasonCts?.Cancel(); _similarLoading = false; FindSources.IsEnabled = true; MoreSimilar.IsEnabled = true; }
    async Task LoadAsync(CancellationToken ct)
    {
        ShowStatus("正在补齐资料；普通播放不必等待", InfoBarSeverity.Informational);
        try
        {
            var prepared = await CatalogArtworkResolver.Shared.ResolveAsync(_work, ct);
            if (ct.IsCancellationRequested) return;
            _work = prepared; Render();
            var result = await _service.LoadAsync(prepared, ct);
            if (ct.IsCancellationRequested) return;
            _work = result.Work; _work.ArtworkResolved = true; _matches = result.Candidates;
            Matches.ItemsSource = _matches.Select(w => new MatchOption(w)).ToList();
            Matches.Visibility = Associate.Visibility = _matches.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            Changed(); Render(); Status.IsOpen = result.Message.Length > 0;
            if (Status.IsOpen) Status.Message = result.Message;
            _ = LoadRelatedAsync(ct); _ = LoadSimilarAsync();
        }
        catch (OperationCanceledException) { }
        catch { if (!ct.IsCancellationRequested) ShowStatus("资料暂不可用，已有资料和普通播放仍保留", InfoBarSeverity.Warning); }
    }
    void Changed() => CatalogLibrary.UpdateSavedMetadata(_work, VodConfigService.Cid);
    void Render()
    {
        var history = CatalogHistory.Find(_work, Stores.GetHistories(VodConfigService.Cid));
        ResumePosition.Text = history?.Position > 1000 ? "继续观看 · " + PlaybackPresentation.Progress(history) : "尚未观看";
        WorkTitle.Work = StickyTitle.Work = _work; Poster.Work = _work;
        if (_work.ArtworkResolved) DetailBackdrop.Source = _work.Backdrop.Length > 0 ? _work.Backdrop : _work.Poster;
        RenderFacts();
        var synopsis = LocalizedSynopsis.Chinese(_work.Overview);
        Overview.Text = "《" + _work.Title + "》\n" + (synopsis.Length > 0 ? synopsis : "暂无可靠的中文剧情简介，保留已有作品资料；不以外语剧情冒充中文。");
        HeroSummary.Text = "《" + _work.Title + "》 · " + (synopsis.Length > 0 ? synopsis : int.TryParse(_work.Year, out var releaseYear) && releaseYear > DateTime.Now.Year ? "尚未上映，详细资料尚未公开。可在下方核对其他年份的版本。" : "详细资料尚未补齐，可核对TMDB匹配或浏览下方相关版本。");
        WatchStateButton.Content = WatchStateContent(ViewingState.Get(_work).Status);
        Companies.ItemsSource = _work.Companies;
        CompaniesPanel.Visibility = _work.Companies.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Credits.Text = "导演：" + (_work.Directors.Length > 0 ? _work.Directors : "暂无资料") + (_work.Cast.Count == 0 ? "\n演员：" + (_work.Actors.Length > 0 ? _work.Actors : "暂无资料") : "");
        ExtraFacts.Text = string.Join("\n", new[] { _work.OriginalTitle, "上映/首播：" + _work.ReleaseDate, "片长：" + (_work.RuntimeMinutes?.ToString() ?? "未知") + " 分钟", "语言：" + string.Join(" / ", _work.Languages), "角色资料按提供方返回展示，未返回角色名时不猜测。" });
        CastWall.ItemsSource = _work.Crew.Concat(_work.Cast).DistinctBy(p => p.Provider + ":" + (p.Id.Length > 0 ? p.Id : p.Name) + ":" + p.Job).ToList();
        Favorite.Content = CatalogLibrary.FindKeep(_work, VodConfigService.Cid) == null ? "☆ 收藏" : "★ 已收藏";
        Ratings.Children.Clear();
        foreach (var provider in new[] { "douban", "tmdb" })
        {
            var rating = _work.GetRating(provider);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var logo = new Image { Width = 58, Height = 26, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
            var uri = new Uri(Path.Combine(AppPaths.IconDir, provider == "tmdb" ? "tmdb.svg" : provider + ".png"));
            logo.Source = provider == "tmdb" ? new SvgImageSource(uri) : new BitmapImage(uri); row.Children.Add(logo);
            var color = provider == "douban" ? Windows.UI.Color.FromArgb(255, 38, 166, 104) : provider == "tmdb" ? Windows.UI.Color.FromArgb(255, 32, 163, 194) : Windows.UI.Color.FromArgb(255, 208, 159, 20);
            var score = new TextBlock { Text = rating.HasScore ? $"{rating.Score:0.0}" : "-", Width = 48, FontSize = 23, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(score);
            var link = CatalogIdentity.ExternalLink(_work, provider);
            var open = new Button { Content = ExternalLinkIcon.Create(), IsEnabled = link != null, Padding = new(7, 3, 7, 3) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(open, "打开" + rating.ProviderName + "作品页面");
            ToolTipService.SetToolTip(open, link ?? "暂无可靠作品ID");
            open.Click += async (_, _) => { try { if (link != null && !await Launcher.LaunchUriAsync(new Uri(link))) App.Main.ShowNotice("未能打开浏览器"); } catch { App.Main.ShowNotice("未能打开浏览器"); } };
            row.Children.Add(open);
            var id = provider == "douban" ? _work.ExternalIds.Douban : provider == "tmdb" ? _work.ExternalIds.Tmdb?.ToString() ?? "" : _work.ExternalIds.Imdb;
            var copy = new Button { Content = id.Length > 0 ? id : "ID 暂缺", IsEnabled = id.Length > 0, Padding = new(8, 3, 8, 3) };
            ToolTipService.SetToolTip(copy, "复制" + rating.ProviderName + "作品ID");
            copy.Click += (_, _) => { try { var data = new DataPackage(); data.SetText(id); Clipboard.SetContent(data); App.Main.ShowNotice("已复制 " + rating.ProviderName + " ID：" + id); } catch { App.Main.ShowNotice("暂时无法写入剪贴板，请重试"); } };
            row.Children.Add(copy);
            row.Children.Add(new TextBlock { Text = rating.VoteCount is int n ? n.ToString("N0") + " 人评价" : "评价人数未知", FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            if (provider == "tmdb" && _work.Provider == "douban") {
                var correct = new Button { Content = "校正匹配", Padding = new(8, 3, 8, 3) };
                ToolTipService.SetToolTip(correct, "TMDB资料或评分对应错片时，搜索片名或输入TMDB ID重新绑定；也可清除错误匹配并暂停自动关联。收藏与观看进度保留。");
                correct.Click += OnCorrectMatch; row.Children.Add(correct);
            }
            if (provider == "tmdb" && !TmdbCredentials.HasCredential) { var configure = new Button { Content = "去配置" }; configure.Click += (_, _) => App.Main.OpenMetadataSettings(); row.Children.Add(configure); }
            Ratings.Children.Add(new Border { Child = row, Padding = new(10, 7, 10, 7), CornerRadius = new(10), BorderThickness = new(1), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 130, 120, 160)) });
        }
        var resumeSeries = _work.Kind == "tv" && WorkMediaService.TmdbId(_work) is int seriesId ? Stores.GetHistories(VodConfigService.Cid).Where(h => { var saved = CatalogLibrary.FromHistory(h); return saved.Kind == "tv" && WorkMediaService.TmdbId(saved) == seriesId; }).MaxBy(h => h.CreateTime) : null;
        var oldSeason = (Season.SelectedItem as WorkSeason)?.Number ?? _work.SelectedSeason ?? resumeSeries?.EpisodeSeason ?? Math.Max(1, CatalogIdentity.Season(_work));
        foreach (var entry in _work.Seasons) entry.FallbackImage = _work.Poster;
        var changedSeasons = Season.ItemsSource != _work.Seasons;
        _renderingSeasons = true; Season.ItemsSource = _work.Seasons;
        Season.SelectedItem = _work.Seasons.FirstOrDefault(s => s.Number == oldSeason) ?? _work.Seasons.FirstOrDefault(s => s.Number > 0) ?? _work.Seasons.FirstOrDefault();
        SeasonsPanel.Visibility = _work.Kind == "tv" && _work.Seasons.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _renderingSeasons = false;
        UpdateResumePosition();
        if ((changedSeasons || _seasonCts?.IsCancellationRequested == true) && Season.SelectedItem is WorkSeason selected) _ = LoadEpisodesAsync(selected);
    }
    CatalogItem PlayItem()
    {
        return _work.Kind == "tv" && Season.SelectedItem is WorkSeason season ? WorkMediaService.ForSeason(_work, season) : _work.Copy();
    }
    void UpdateResumePosition()
    {
        var history = CatalogHistory.Find(PlayItem(), Stores.GetHistories(VodConfigService.Cid));
        var season = Season.SelectedItem as WorkSeason;
        ResumePosition.Text = history?.Position > 1000 ? "继续观看 · " + (season != null ? season.Label + " · " : "") + PlaybackPresentation.Progress(history) : season != null ? season.Label + " · 尚未观看" : "尚未观看";
    }
    void OnSeasonChanged(object sender, SelectionChangedEventArgs e) { if (!_renderingSeasons && Season.SelectedItem is WorkSeason season) { UpdateResumePosition(); _ = LoadEpisodesAsync(season); } }
    async Task LoadEpisodesAsync(WorkSeason season)
    {
        _seasonCts?.Cancel(); _seasonCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); var ct = _seasonCts.Token;
        Episodes.ItemsSource = PrepareEpisodes(_media.CachedSeason(_work, season.Number)); EpisodeStatus.Text = season.Label + " · 正在加载单集截图与资料…";
        try {
            var episodes = await _media.SeasonAsync(_work, season.Number, ct); if (ct.IsCancellationRequested) return;
            Episodes.ItemsSource = PrepareEpisodes(episodes);
            EpisodeStatus.Text = season.Label + " · " + episodes.Count + "集 · 评分来自TMDB；点击单集查找播放源";
        } catch (OperationCanceledException) { }
        catch { if (!ct.IsCancellationRequested) EpisodeStatus.Text = "本季资料暂不可用，仍可使用播放按钮或普通线路选集；点击季卡片可重试。"; }
    }
    void OnEpisodeClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is WorkEpisode episode) App.Main.OpenPlayer(new PlayerArgs { Catalog = PlayItem(), EpisodeName = "第" + episode.Number.ToString("00") + "集", StartOver = true });
    }
    List<WorkEpisode> PrepareEpisodes(List<WorkEpisode> episodes)
    {
        if (episodes == null) return null;
        var work = PlayItem(); var history = CatalogHistory.Find(work, Stores.GetHistories(VodConfigService.Cid));
        foreach (var episode in episodes) {
            var state = ViewingState.Get(work, episode.Season, episode.Number);
            var pos = state.Position > 0 ? state.Position : history?.EpisodeNumber == episode.Number && (history.EpisodeSeason ?? 1) == episode.Season ? Math.Max(0, history.Position) : 0;
            episode.FallbackImage = _work.Poster; episode.PreviewImage = episode.Still.Length > 0 ? episode.Still : _work.Poster;
            episode.CanResume = pos >= 1000 && state.Status != WatchStatus.Completed; episode.ResumeLabel = "播放到 " + TimeSpan.FromMilliseconds(pos).ToString(pos >= 3600000 ? @"h\:mm\:ss" : @"mm\:ss");
            var effective = state.Status == WatchStatus.Unseen && pos > 1000 ? WatchStatus.Watching : state.Status;
            episode.StateLabel = StateLabel(effective); episode.StateGlyph = effective == WatchStatus.Completed ? "\uE73E" : "\uE890"; episode.StateColor = effective == WatchStatus.Completed ? "#FF65D392" : effective == WatchStatus.Watching ? "#FFBB92FF" : "#A8FFFFFF";
        }
        return episodes;
    }
    static object WatchStateContent(WatchStatus state)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        row.Children.Add(new FontIcon { Glyph = state == WatchStatus.Completed ? "\uE73E" : "\uE890", FontSize = 14, Foreground = new SolidColorBrush(state == WatchStatus.Completed ? Windows.UI.Color.FromArgb(255, 101, 211, 146) : Windows.UI.Color.FromArgb(255, 187, 146, 255)) });
        row.Children.Add(new TextBlock { Text = state == WatchStatus.Completed ? "已看完" : state == WatchStatus.Watching ? "正在看" : "观看状态" }); return row;
    }
    static string StateLabel(WatchStatus state) => state switch { WatchStatus.Completed => "✓ 已看完", WatchStatus.Watching => "◉ 正在看", _ => "观看状态" };
    void OnResumeEpisode(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is WorkEpisode episode) App.Main.OpenPlayer(new PlayerArgs { Catalog = PlayItem(), EpisodeName = "第" + episode.Number.ToString("00") + "集" }); }
    void OnWorkWatchState(object sender, RoutedEventArgs e) => WatchMenu((Button)sender, _work, null);
    void OnEpisodeWatchState(object sender, RoutedEventArgs e) { if (sender is Button button && button.DataContext is WorkEpisode episode) WatchMenu(button, PlayItem(), episode); }
    void WatchMenu(Button button, CatalogItem work, WorkEpisode episode)
    {
        var menu = new MenuFlyout();
        foreach (var state in Enum.GetValues<WatchStatus>()) { var item = new MenuFlyoutItem { Text = state == WatchStatus.Unseen ? "清除观看标记" : StateLabel(state), Icon = new FontIcon { Glyph = state == WatchStatus.Completed ? "\uE73E" : "\uE890", Foreground = new SolidColorBrush(state == WatchStatus.Completed ? Windows.UI.Color.FromArgb(255, 80, 200, 140) : Windows.UI.Color.FromArgb(255, 182, 130, 246)) } }; item.Click += (_, _) => { ViewingState.Set(work, state, episode?.Season, episode?.Number); WatchStateButton.Content = WatchStateContent(ViewingState.Get(_work).Status); if (Episodes.ItemsSource is List<WorkEpisode> list) { Episodes.ItemsSource = null; Episodes.ItemsSource = PrepareEpisodes(list); } App.Main.ShowNotice("已更新观看状态"); }; menu.Items.Add(item); }
        menu.ShowAt(button);
    }
    void OnCompanyClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not WorkCompany company || !int.TryParse(company.Id, out var id)) return;
        if (!TmdbCredentials.HasCredential) { App.Main.ShowNotice("出品与播出筛选需要在设置填写个人TMDB Key"); return; }
        var network = company.Role.Contains("播出"); var q = new CatalogQuery { Scope = "company", ScopeId = "tmdb:" + (network ? "network:" : "company:") + id, Category = network ? "tv" : "all", Provider = "tmdb", RatingProvider = "tmdb", CompanyId = id, CompanyName = company.Name, CompanyIsNetwork = network, SortDirection = "default" };
        App.Main.OpenCatalog(new CatalogNavigation(q.Category, q.Scope, Preset: q));
    }
    async Task LoadRelatedAsync(CancellationToken ct)
    {
        try {
            var collection = await _media.CollectionAsync(_work, ct); if (ct.IsCancellationRequested) return;
            CollectionWall.ItemsSource = collection; CollectionHeading.Text = _work.CollectionName.Length > 0 ? _work.CollectionName : "同系列作品";
            CollectionPanel.Visibility = collection.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (collection.Count == 0 && TmdbCredentials.HasCredential) {
                var versions = await _media.OtherVersionsAsync(_work, ct); if (ct.IsCancellationRequested) return;
                VersionsWall.ItemsSource = versions; VersionsPanel.Visibility = versions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        } catch (OperationCanceledException) { } catch { /* Missing optional metadata never blocks playback. */ }
    }
    void OnBack(object sender, RoutedEventArgs e) => App.Main.ReturnFromWorkDetails();
    void OnPlay(object sender, RoutedEventArgs e) => CatalogActions.Open(Frame, PlayItem(), true);
    void OnFavorite(object sender, RoutedEventArgs e) { var saved = CatalogActions.ToggleFavorite(_work); Render(); ShowStatus(saved ? "已收藏" : "未收藏", InfoBarSeverity.Success); }
    async void OnHd(object sender, RoutedEventArgs e) { SourceExpander.IsExpanded = true; await FindSourcesAsync(true); }
    void OnAssociate(object sender, RoutedEventArgs e)
    {
        if (Matches.SelectedItem is not MatchOption match) return;
        _lifetime.Cancel(); _seasonCts?.Cancel(); _lifetime = new(); _similarLoading = false; _similarState = null; _similarItems.Clear();
        _work = _service.Associate(_work, match.Work); Changed(); Render(); Matches.Visibility = Associate.Visibility = Visibility.Collapsed;
        _ = LoadRelatedAsync(_lifetime.Token); _ = LoadSimilarAsync();
        ShowStatus("关联已保存，原作品身份和观看进度保留", InfoBarSeverity.Success);
    }
    async void OnCorrectMatch(object sender, RoutedEventArgs e)
    {
        try {
            var dialog = new TmdbMatchDialog(_work, _service) { XamlRoot = XamlRoot, RequestedTheme = ActualTheme };
            await dialog.ShowAsync(); if (dialog.Result == null || _lifetime.IsCancellationRequested) return;
            _lifetime.Cancel(); _seasonCts?.Cancel(); _lifetime = new(); _similarLoading = false;
            _work = dialog.Result; CatalogArtworkResolver.Shared.RememberCorrection(_work); Changed(); Render();
            _similarState = null; _similarItems.Clear(); CollectionPanel.Visibility = VersionsPanel.Visibility = Visibility.Collapsed;
            _ = LoadRelatedAsync(_lifetime.Token); _ = LoadSimilarAsync();
            ShowStatus(_work.TmdbLinkSuppressed ? "已清除错误匹配，暂停自动关联；可随时在校正匹配中重新绑定" : "已保存正确匹配；收藏与观看进度保留", InfoBarSeverity.Success);
        } catch { ShowStatus("匹配窗口暂不可用，请重试", InfoBarSeverity.Warning); }
    }
    async void OnFindSources(object sender, RoutedEventArgs e) => await FindSourcesAsync(false);
    async Task FindSourcesAsync(bool internationalOnly)
    {
        if (!FindSources.IsEnabled) return;
        if (_lifetime.IsCancellationRequested) _lifetime = new();
        var ct = _lifetime.Token; FindSources.IsEnabled = false; Sources.Children.Clear();
        SourceStatus.Text = internationalOnly ? "正在查询已启用的国际实验站点…" : "正在搜索已启用来源；可随时直接播放或离开页面取消";
        try
        {
            await App.Main.InitialVodRestoreTask.WaitAsync(ct);
            var requested = PlayItem();
            await SourceDiscovery.RunAsync(requested, null, null, null, null, found =>
            {
                if (ct.IsCancellationRequested) return;
                var panel = new StackPanel { Spacing = 6 };
                panel.Children.Add(new TextBlock { Text = found.Flag.Flag + (found.Flag.ProbeHeight > 0 ? $" · 探测高度 {found.Flag.ProbeHeight}p" : " · 画质待播放器确认"), TextWrapping = TextWrapping.Wrap });
                var episodes = new ComboBox { ItemsSource = found.Flag.Episodes, DisplayMemberPath = "Name", SelectedIndex = 0, MinWidth = 160 };
                var play = new Button { Content = "播放此线路 / 所选集" };
                play.Click += (_, _) => App.Main.OpenPlayer(new PlayerArgs { Catalog = requested, ManualSource = true, SeedSiteKey = found.Site.Key, SeedVodId = found.Vod.Id,
                    EpisodeName = (episodes.SelectedItem as Episode)?.Name });
                var adMark = new SourceAdButton { Work = requested, Site = found.Site }; adMark.Refresh(); panel.Children.Add(adMark);
                panel.Children.Add(episodes); panel.Children.Add(play);
                Sources.Children.Add(new Border { Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["DiscoveryPanel"], Child = panel });
            }, update => { if (!ct.IsCancellationRequested) SourceStatus.Text = update.SourceName + "：" + update.Message; }, ct, internationalOnly: internationalOnly);
            if (!ct.IsCancellationRequested) SourceStatus.Text = $"搜索完成，找到 {Sources.Children.Count} 条来源；点选后由播放器实际验证";
        }
        catch (OperationCanceledException) { }
        catch { if (!ct.IsCancellationRequested) SourceStatus.Text = "搜索失败，可重试；普通自动播放仍可使用"; }
        finally { if (!ct.IsCancellationRequested) FindSources.IsEnabled = true; }
    }
    async void OnMoreSimilar(object sender, RoutedEventArgs e) => await LoadSimilarAsync();
    async Task LoadSimilarAsync()
    {
        if (_similarLoading || _similarState?.HasMore == false) return;
        _similarLoading = true; MoreSimilar.IsEnabled = false;
        if (_lifetime.IsCancellationRequested) _lifetime = new(); var ct = _lifetime.Token;
        _similarState ??= _recommendations.Restore(_work);
        SimilarStatus.Text = "正在继续查询同类型/题材作品…";
        try
        {
            var result = await _recommendations.MoreAsync(_work, _similarState, ct);
            if (ct.IsCancellationRequested) return;
            _similarState = result;
            foreach (var work in result.Items.Where(w => w.WorkKey != _work.WorkKey))
                if (!_similarItems.Any(w => w.WorkKey == work.WorkKey)) _similarItems.Add(work);
            SimilarStatus.Text = result.Note + $" · {_similarItems.Count} 部";
            MoreSimilar.Content = result.HasMore ? "继续推荐 ↓" : "已加载全部可查询推荐";
        }
        catch (OperationCanceledException) { }
        catch { if (!ct.IsCancellationRequested) SimilarStatus.Text = "推荐暂不可用，可点击继续推荐重试"; }
        finally { if (!ct.IsCancellationRequested) { _similarLoading = false; MoreSimilar.IsEnabled = _similarState?.HasMore != false; } }
    }
    void RenderFacts()
    {
        Facts.Children.Clear();
        void Facet(string label, Action<CatalogQuery> set)
        {
            var button = new Button { Content = label, Padding = new(9, 4, 9, 4), VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(button, "查看“" + label + "”的作品 · 使用统一筛选");
            button.Click += (_, _) => {
                if (button.Tag is Func<bool> copied && copied()) return;
                var q = new CatalogQuery { Scope = "facet", ScopeId = _work.WorkKey + ":" + label, Category = _work.Kind, Provider = _work.Provider == "tmdb" ? "tmdb" : "douban", SortDirection = "default" };
                set(q); App.Main.OpenCatalog(new CatalogNavigation(q.Category, q.Scope, Preset: q));
            };
            Facts.Children.Add(button);
        }
        var baseTitle = System.Text.RegularExpressions.Regex.Replace(_work.Title, @"第\s*[一二三四五六七八九十百\d]+\s*季|Season\s*\d+|\s+S\d+$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        Facet(baseTitle, q => q.Keyword = baseTitle);
        if (Facts.Children.FirstOrDefault() is Button titleButton) {
            var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(650); timer.IsRepeating = false; var copied = false;
            timer.Tick += (_, _) => { var data = new DataPackage(); data.SetText(baseTitle); Clipboard.SetContent(data); copied = true; App.Main.ShowNotice("已复制“" + baseTitle + "”"); };
            titleButton.PointerPressed += (_, e) => { if (e.GetCurrentPoint(titleButton).Properties.IsLeftButtonPressed) { copied = false; timer.Start(); } };
            titleButton.PointerReleased += (_, _) => timer.Stop(); titleButton.PointerCanceled += (_, _) => timer.Stop(); titleButton.PointerExited += (_, _) => timer.Stop(); titleButton.Unloaded += (_, _) => timer.Stop();
            titleButton.AddHandler(Button.TappedEvent, new TappedEventHandler((_, e) => { if (copied) e.Handled = true; }), true);
            titleButton.Tag = new Func<bool>(() => copied);
            ToolTipService.SetToolTip(titleButton, "点击按片名筛选；按住鼠标左键约0.65秒复制完整片名");
        }
        if (int.TryParse(_work.Year, out var year)) Facet(_work.Year, q => q.YearFrom = q.YearTo = year);
        Facet(_work.Kind == "tv" ? "电视剧" : "电影", q => q.Kind = _work.Kind);
        foreach (var c in _work.Countries) Facet(CatalogVocabulary.Label(c), q => q.Countries = new() { c });
        foreach (var g in _work.Genres) Facet(CatalogVocabulary.Label(g), q => q.Genres = new() { g });
    }
    async void OnPersonClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not CatalogPerson person) return;
        if (!TmdbCredentials.HasCredential) { App.Main.ShowNotice("人物作品需要选填TMDB Key；普通看片不受影响。"); return; }
        var ct = _lifetime.Token;
        try
        {
            var id = await new PersonCatalog().ResolveAsync(person, ct);
            if (ct.IsCancellationRequested) return;
            if (id == null) { App.Main.ShowNotice("人物资料尚无唯一可靠匹配，未按同名人物混合列片。"); return; }
            var q = new CatalogQuery { Scope = "person", ScopeId = "tmdb-person:" + id, PersonId = id.Value, PersonName = person.Name, Provider = "tmdb", RatingProvider = "tmdb", Category = "all", SortDirection = "default" };
            App.Main.OpenCatalog(new CatalogNavigation("all", "person", Preset: q));
        }
        catch (OperationCanceledException) { }
        catch { if (!ct.IsCancellationRequested) App.Main.ShowNotice("人物作品暂不可用，请稍后重试"); }
    }
    void OnSimilarClick(object sender, ItemClickEventArgs e) { if (e.ClickedItem is CatalogItem work) CatalogActions.Details(work); }
    void ShowStatus(string message, InfoBarSeverity severity) { Status.Message = message; Status.Severity = severity; Status.IsOpen = true; }
    public record MatchOption(CatalogItem Work) { public string Label => $"{Work.Title} · {Work.Year} · {Work.Kind}"; }
}
