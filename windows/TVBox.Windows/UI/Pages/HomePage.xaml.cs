using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.UI.Pages;

public sealed partial class HomePage : Page
{
    readonly ObservableCollection<CatalogItem> _items = new();
    readonly CatalogQueryCoordinator _queries = new();
    readonly MetadataDetailsService _details = new();
    readonly PosterMetadataLoader _metadata = new();
    CancellationTokenSource _request;
    CatalogQueryState _state;
    CatalogChannel _channel;
    ScrollViewer _scroll;
    History _continue;
    long _generation;
    CatalogItem _hero;
    string _heroRequestKey;
    DailyPicks _daily;
    CancellationTokenSource _dailyCts;
    string _dailyLoading;
    int _dailyIndex, _dailySwapVersion;
    bool _preparingDaily;
    Task<CatalogItem> _nextDaily;
    CancellationTokenSource _dailyPrefetchCts;
    int _nextDailyIndex = -1;
    CancellationTokenSource _continueCts;
    string _continueSignature;
    DateTimeOffset _heroStarted;
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _carousel;
    bool _ready, _active, _loading, _restoring, _wheelArmed;
    string Provider => ProviderTabs.SelectedIndex == 2 ? "tmdb" : ProviderTabs.SelectedIndex == 1 ? "netflix" : "douban";
    public HomePage()
    {
        InitializeComponent(); PosterWall.ItemsSource = _items;
        _carousel = DispatcherQueue.CreateTimer(); _carousel.Interval = TimeSpan.FromMilliseconds(100);
        _carousel.Tick += (_, _) => {
            if (!_active || _daily?.Items.Count != 5) return;
            if (_daily.Day != DateTime.Now.ToString("yyyy-MM-dd")) { _ = LoadDailyAsync(); return; }
            var elapsed = (DateTimeOffset.UtcNow - _heroStarted).TotalSeconds;
            if (_hero == null) { HeroProgress.Value = 0; if (!_preparingDaily && elapsed >= 8) ShowDaily(0); return; }
            HeroProgress.Value = Math.Min(100, elapsed / 8 * 100);
            if (elapsed >= 2 && !_preparingDaily) PrefetchDaily((_dailyIndex + 1) % 5);
            if (elapsed >= 8 && !_preparingDaily) ShowDaily((_dailyIndex + 1) % 5);
        };
        HomeRoot.ActualThemeChanged += (_, _) => UpdateHighlights(false);
        DoubanTabLogo.Source = new BitmapImage(new Uri(Path.Combine(AppPaths.IconDir, "douban.png")));
        TmdbTabLogo.Source = new SvgImageSource(new Uri(Path.Combine(AppPaths.IconDir, "tmdb.svg")));
        NetflixTabLogo.Source = new SvgImageSource(new Uri(Path.Combine(AppPaths.IconDir, "netflix-wordmark.svg")));
        ProviderTabs.SelectedIndex = Setting.GetString("home_provider") switch { "tmdb" => 2, "netflix" => 1, _ => 0 };
        PopulateCategories(); _ready = true; SwitchChannel();
        HomeChips.Changed += q => Apply(q);
        HomeFilters.Changed += q => DispatcherQueue.TryEnqueue(() => { if (_active) Apply(q); });
        Loaded += (_, _) =>
        {
            if (_request?.IsCancellationRequested == true) _request = new();
            _active = true; VodConfigService.Instance.Loaded -= RefreshLocalState; VodConfigService.Instance.Loaded += RefreshLocalState;
            RefreshLocalState(); LoadPinnedPresets(); AttachScroll(); UiHints.Attach(this); UpdateHighlights(false);
            _ = LoadDailyAsync();
            if (_items.Count == 0 && !_loading) _ = LoadAsync(_generation); else _ = HydrateAsync(_state, _generation, _request.Token);
        };
        Unloaded += (_, _) => { SavePosition(); _active = false; _continueCts?.Cancel(); _continueSignature = null; _request?.Cancel(); _dailyCts?.Cancel(); _carousel.Stop(); _loading = false; VodConfigService.Instance.Loaded -= RefreshLocalState; };
        PosterWall.Loaded += (_, _) => AttachScroll();
        PosterWall.SizeChanged += (_, _) => UpdateHeroLayout();
        PosterWall.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnWheel), true);
        ContinueRows.AddHandler(PointerPressedEvent, new PointerEventHandler(OnContinueDragPressed), true);
        ContinueRows.AddHandler(PointerMovedEvent, new PointerEventHandler(OnContinueDragMoved), true);
        ContinueRows.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnContinueDragReleased), true);
        ContinueRows.PointerCaptureLost += (_, _) => _continueDragging = false;
    }
    void PopulateCategories()
    {
        var channels = CatalogChannels.For(Provider); CategoryTabs.ItemsSource = channels; CategoryTabs.DisplayMemberPath = "Label";
        var saved = Setting.GetString("home_channel_" + Provider, "hot-tv"); CategoryTabs.SelectedItem = channels.FirstOrDefault(c => c.Key == saved) ?? channels[0];
    }
    void UpdateHeroLayout()
    {
        // A compact banner has a fixed height; never derive it from the window's height.
        HeroPoster.Height = 182; HeroPoster.Width = HeroArtwork.Width = 130;
        HeroArtworkColumn.Width = new GridLength(156);
    }
    CatalogQuery DefaultQuery() => new()
    {
        Scope = "home", ScopeId = Provider + ":" + _channel.Key, Collection = Provider == "tmdb" ? "" : Provider + ":" + _channel.Key,
        Provider = Provider == "tmdb" ? "tmdb" : "douban", RatingProvider = Provider == "tmdb" ? "tmdb" : "douban",
        Category = _channel.Key == "anime" ? "anime" : _channel.Kind,
        ColumnSort = _channel.Sort == "R" ? "latest" : _channel.Sort == "S" ? "rating" : "popularity",
        SortDirection = "default", Sort = _channel.Sort == "R" ? "latest" : _channel.Sort == "S" ? "rating" : "popularity"
    };
    void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return; var switchWatch = System.Diagnostics.Stopwatch.StartNew();
        if (Provider == "tmdb" && !TmdbCredentials.HasCredential) { App.Main.ShowNotice("TMDB目录需选填个人Key；豆瓣与普通看片无需配置。"); _ready = false; ProviderTabs.SelectedIndex = Setting.GetString("home_provider") == "netflix" ? 1 : 0; _ready = true; return; }
        SavePosition(); _ready = false; PopulateCategories(); _ready = true;
        Setting.Put("home_provider", Provider); SwitchChannel();
        Logger.D("CatalogPerformance", $"ProviderSwitch {Provider}, cards={_items.Count}, ui-synchronous={switchWatch.Elapsed.TotalMilliseconds:0.0}ms");
    }
    void OnCategoryChanged(object sender, SelectionChangedEventArgs e) { if (_ready) { SavePosition(); SwitchChannel(); } }
    void SwitchChannel()
    {
        _channel = CategoryTabs.SelectedItem as CatalogChannel ?? CatalogChannels.For(Provider)[0];
        Setting.Put("home_channel_" + Provider, _channel.Key);
        _state = null;
        var q = _queries.LastQuery("home", Provider + ":" + _channel.Key, DefaultQuery());
        if (q.Scope != "home") q = DefaultQuery();
        ApplyBrand(); Apply(q); UpdateHighlights(true);
    }
    void ApplyBrand()
    {
        var dark = Provider == "netflix";
        RequestedTheme = ElementTheme.Default;
        FeedLogo.Source = Provider == "tmdb" ? TmdbTabLogo.Source : dark ? NetflixTabLogo.Source : DoubanTabLogo.Source; FeedLogo.Width = 110;
        ToolTipService.SetToolTip(FeedLogo, dark ? "Netflix标签片库 · 豆瓣提供资料，不是Netflix官方榜单" : "豆瓣推荐与评分");
        SourceCountText.Text = Provider == "tmdb" ? "TMDB中文资料目录 · 独立评分，点选先看详情" : dark ? "Netflix标签片库 · 海报、热度与评分来自豆瓣；非Netflix官方榜单" : "豆瓣热播与推荐 · TMDB资料在后台补齐";
        if (_active && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            var visual = ElementCompositionPreview.GetElementVisual(HomeRoot); var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 0.65f); fade.InsertKeyFrame(1, 1); fade.Duration = TimeSpan.FromMilliseconds(180); visual.StartAnimation("Opacity", fade);
        }
    }
    void Apply(CatalogQuery q, bool refresh = false)
    {
        try { q.Validate(); } catch (ArgumentException e) { FeedError.Message = e.Message; FeedError.IsOpen = true; return; }
        SavePosition(); _request?.Cancel(); _request = new(); _generation++; _loading = false; _wheelArmed = false;
        _state = refresh ? new() { Query = q.Copy() } : _queries.Restore(q);
        HomeFilters.SetQuery(q); HomeChips.SetQuery(q); Render(); RestoreOffset();
        if (_active || IsLoaded) _ = LoadDailyAsync();
        if (_active || IsLoaded)
        { if (_state.Items.Count == 0 || refresh) _ = LoadAsync(_generation); else _ = HydrateAsync(_state, _generation, _request.Token); }
    }
    public void RefreshLocalState()
    {
        if (_state == null) return; var histories = Stores.GetHistories(VodConfigService.Cid);
        foreach (var work in _items.Concat(_daily?.Items ?? new List<CatalogItem>())) work.UpdateProgress(CatalogHistory.Find(work, histories));
        _continue = histories.FirstOrDefault(h => ViewingState.IsContinue(h));
        ContinueButton.Visibility = Visibility.Collapsed;
        ContinuePanel.Visibility = _continue == null ? Visibility.Collapsed : Visibility.Visible;
        var recentItems = histories.Where(h => ViewingState.IsContinue(h)).ToList();
        var signature = string.Join("|", recentItems.Select(h => h.VodName + h.EpisodeUrl + h.Position + ":" + h.Duration));
        if (_continueSignature != signature) {
            _continueSignature = signature; _continueCts?.Cancel(); _continueCts = new();
            var cards = recentItems.Select(h => new ContinueCard { History = h, Work = CatalogLibrary.FromHistory(h) }).ToList();
            foreach (var card in cards) card.SetArtwork(card.History.EpisodeStill, card.History.EpisodeTitle);
            ContinueRows.ItemsSource = cards; _ = LoadContinueArtworkAsync(cards, _continueCts.Token);
        }
        if (_continue != null) ContinueButton.Content = "继续观看 · " + _continue.VodName + " · " + PlaybackPresentation.Progress(_continue);
    }
    public void EnterBackground() { SavePosition(); _active = false; _continueCts?.Cancel(); _continueSignature = null; _request?.Cancel(); _dailyCts?.Cancel(); _carousel.Stop(); _loading = false; }
    void OnContinueCard(object sender, ItemClickEventArgs e) { if (Environment.TickCount64 < _suppressContinueUntil) return; if (e.ClickedItem is ContinueCard card) { SavePosition(); CatalogActions.Details(card.Work); } }
    public List<string> SuggestionTitles => _items.Concat(_daily?.Items ?? new()).Select(w => w.Title).Where(t => t.Length > 0).Distinct().Take(8).ToList();
    async void OnClearContinue(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog { RequestedTheme = ActualTheme, XamlRoot = XamlRoot, Title = "清空继续观看？", Content = "从首页移除全部继续观看卡片，历史进度和收藏仍然保留。", PrimaryButtonText = "清空", CloseButtonText = "取消" };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) { ViewingState.Hide(Stores.GetHistories(VodConfigService.Cid).Where(ViewingState.IsContinue)); RefreshLocalState(); }
    }
    void OnRemoveContinue(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is ContinueCard card) { ViewingState.Hide(new[] { card.History }); RefreshLocalState(); } }
    bool _continueDragging;
    double _continueDragX, _continueDragOffset;
    long _suppressContinueUntil;
    ScrollViewer _continueScroll;
    void OnContinueDragPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(ContinueRows).Properties.IsLeftButtonPressed) return;
        var source = e.OriginalSource as DependencyObject;
        while (source != null && source != ContinueRows) { if (source is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase) return; source = VisualTreeHelper.GetParent(source); }
        _continueScroll = FindScroll(ContinueRows); _continueDragging = _continueScroll != null;
        _continueDragX = e.GetCurrentPoint(ContinueRows).Position.X; _continueDragOffset = _continueScroll?.HorizontalOffset ?? 0;
    }
    void OnContinueDragMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_continueDragging || !e.GetCurrentPoint(ContinueRows).Properties.IsLeftButtonPressed) return;
        var delta = e.GetCurrentPoint(ContinueRows).Position.X - _continueDragX; if (Math.Abs(delta) < 6) return;
        ContinueRows.CapturePointer(e.Pointer); _suppressContinueUntil = Environment.TickCount64 + 500;
        _continueScroll.ChangeView(Math.Clamp(_continueDragOffset - delta, 0, _continueScroll.ScrollableWidth), null, null, true); e.Handled = true;
    }
    void OnContinueDragReleased(object sender, PointerRoutedEventArgs e) { _continueDragging = false; ContinueRows.ReleasePointerCapture(e.Pointer); }
    async Task LoadContinueArtworkAsync(List<ContinueCard> cards, CancellationToken ct)
    {
        foreach (var card in cards) {
            try {
                var full = await CatalogArtworkResolver.Shared.ResolveAsync(card.Work, ct); ct.ThrowIfCancellationRequested();
                card.Work.ApplyMetadata(full);
                var image = card.History.EpisodeStill; var title = card.History.EpisodeTitle;
                if (full.Kind == "tv" && TmdbCredentials.HasCredential && WorkMediaService.TmdbId(full) != null) {
                    var season = card.History.EpisodeSeason ?? full.SelectedSeason ?? Math.Max(1, CatalogIdentity.Season(full));
                    var number = card.History.EpisodeNumber ?? TVBoxForWindows.Player.DanmakuSearch.EpisodeNumber(card.History.VodRemarks);
                    var media = new WorkMediaService();
                    var episodes = media.CachedSeason(full, season) ?? await media.SeasonAsync(full, season, ct);
                    var episode = episodes.FirstOrDefault(e => e.Number == number);
                    if (episode != null) { image = episode.Still; title = episode.Name; }
                }
                ct.ThrowIfCancellationRequested(); card.SetArtwork(image.Length > 0 ? image : full.Backdrop.Length > 0 ? full.Backdrop : full.Poster, title);
            } catch (OperationCanceledException) { break; }
            catch { if (!ct.IsCancellationRequested) card.SetArtwork(card.Work.Backdrop.Length > 0 ? card.Work.Backdrop : card.Work.Poster, card.History.EpisodeTitle); }
        }
    }
    public void RefreshAfterNavigation()
    {
        _active = true;
        RefreshLocalState();
        if (_request?.IsCancellationRequested == true) _request = new();
        if (_state != null && _request != null && !_loading) {
            if (_items.Count == 0 && _state.HasMore) _ = LoadAsync(_generation);
            else _ = HydrateAsync(_state, _generation, _request.Token);
            _ = LoadDailyAsync();
        }
    }
    void Render()
    {
        if (_state == null) return; _restoring = true;
        for (var i = 0; i < _state.Items.Count; i++)
        {
            var work = _state.Items[i];
            if (i < _items.Count && _items[i].WorkKey == work.WorkKey) { _items[i].ApplyMetadata(work); continue; }
            var old = _items.FirstOrDefault(w => w.WorkKey == work.WorkKey);
            if (old != null) { _items.Move(_items.IndexOf(old), i); old.ApplyMetadata(work); }
            else _items.Insert(i, work);
        }
        while (_items.Count > _state.Items.Count) _items.RemoveAt(_items.Count - 1);
        _restoring = false; RefreshLocalState();

        ChannelHeading.Text = _channel.Label + " · 频道海报墙";
        FeedStatusText.Text = $"{_channel.Label} · {_items.Count} 部 · 已检索 {_state.ScannedPages} 页";
        ToolTipService.SetToolTip(FeedStatusText, _state.ScopeNote + "；排序覆盖已检索范围，继续滚动可补全。不同平台评分独立。");
        EmptyText.Visibility = _items.Count == 0 && _hero == null ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _loading ? "正在查询频道…" : _state.HasMore ? "当前页段没有匹配，可继续加载补全" : "暂无匹配，可调整本页筛选";
        MoreButton.Visibility = Visibility.Collapsed; MoreButton.IsEnabled = !_loading; Busy.IsActive = _loading;
        FeedError.IsOpen = _state.IsStale; if (_state.IsStale) FeedError.Message = "当前显示缓存，资料服务暂时无法刷新";
    }
    async Task LoadAsync(long generation)
    {
        if (_loading || _state == null || _request.IsCancellationRequested) return;
        var state = _state; var ct = _request.Token; _loading = true; Render();
        try
        {
            var result = await _queries.LoadNextAsync(state, _details.EnrichAsync, ct);
            if (ct.IsCancellationRequested || generation != _generation) return;
            _state = result; Render(); _ = HydrateAsync(result, generation, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (generation == _generation) { FeedError.Message = e is MetadataServiceException ? e.Message : "推荐查询暂时失败，已加载内容保留，可重试"; FeedError.IsOpen = true; MoreButton.Content = "重试加载"; MoreButton.Visibility = Visibility.Visible; } }
        finally { if (generation == _generation) { _loading = false; MoreButton.IsEnabled = true; Busy.IsActive = false; } }
    }
    async Task HydrateAsync(CatalogQueryState state, long generation, CancellationToken ct)
    {
        if (state == null) return;
        await _metadata.LoadAsync(state.Items, (card, full) =>
        {
            if (ct.IsCancellationRequested || generation != _generation) return;
            ApplyVisibleMetadata(full);
        }, ct);
        if (ct.IsCancellationRequested || generation != _generation) return;
        state.Items = CatalogFilters.Apply(state.Candidates, state.Query); _queries.Save(state); Render();
    }
    void OnFilters(object sender, RoutedEventArgs e) => HomeFilters.Visibility = HomeFilters.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    void OnMore(object sender, RoutedEventArgs e) { if (!_loading && _state.HasMore) _ = LoadAsync(_generation); }
    void OnPosterClick(object sender, ItemClickEventArgs e) { if (e.ClickedItem is CatalogItem item) Play(item); }
    void OnCardResume(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is CatalogItem item) Play(item); }
    void Play(CatalogItem item)
    {
        SavePosition(); CatalogActions.Details(item);
    }
    void OnContinue(object sender, RoutedEventArgs e) { if (_continue != null) { SavePosition(); CatalogActions.Details(CatalogLibrary.FromHistory(_continue)); } }
    public void CaptureState() => SavePosition();
    void SavePosition() { if (_state == null) return; if (!_restoring && _scroll != null) _state.Offset = _scroll.VerticalOffset; _queries.Save(_state); }
    static ScrollViewer FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer s) return s;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (FindScroll(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }
    void AttachScroll()
    {
        var scroll = FindScroll(PosterWall); if (scroll == null) return;
        if (_scroll != scroll) { if (_scroll != null) _scroll.ViewChanged -= OnViewChanged; _scroll = scroll; _scroll.ViewChanged += OnViewChanged; ReturnButtons.Attach(_scroll, () => _wheelArmed = false); }
        RestoreOffset();
    }
    void RestoreOffset()
    {
        if (_scroll == null || _state == null) return; _restoring = true; var state = _state; var offset = state.Offset;
        DispatcherQueue.TryEnqueue(() => { if (_state == state) { PosterWall.UpdateLayout(); _scroll.ChangeView(null, offset, null, true); } _restoring = false; });
    }
    void OnWheel(object sender, PointerRoutedEventArgs e) { if (e.GetCurrentPoint(PosterWall).Properties.MouseWheelDelta < 0) { _wheelArmed = true; DispatcherQueue.TryEnqueue(TryLoadAtBottom); } }
    void OnViewChanged(object sender, ScrollViewerViewChangedEventArgs e) { if (!_restoring && !e.IsIntermediate) { SavePosition(); TryLoadAtBottom(); } }
    void TryLoadAtBottom() { if (_wheelArmed && !_loading && !_restoring && _state.HasMore && _scroll != null && _scroll.ScrollableHeight - _scroll.VerticalOffset < 240) { _wheelArmed = false; _ = LoadAsync(_generation); } }
    void LoadPinnedPresets()
    {
        PinnedPresets.Children.Clear();
        foreach (var preset in MetadataStore.Instance.List<CatalogPreset>("preset").Select(p => p.Value).Where(p => p.Pinned).Take(10))
        { var button = new Button { Content = preset.Name }; button.Click += (_, _) => { var q = preset.Query.Copy(); q.Scope = "home"; q.ScopeId = Provider + ":" + _channel.Key; q.Collection = q.ScopeId; Apply(q); }; PinnedPresets.Children.Add(button); }
    }
    void OnHeroPlay(object sender, RoutedEventArgs e) { if (_hero != null) { SavePosition(); CatalogActions.Details(_hero); } }
    void OnHeroDetails(object sender, RoutedEventArgs e) { if (_hero != null) { SavePosition(); CatalogActions.Details(_hero); } }
    void OnHd(object sender, RoutedEventArgs e) { FeedError.Message = HdSourceRegistry.Status; FeedError.Severity = InfoBarSeverity.Informational; FeedError.IsOpen = true; }
    void UpdateHero(CatalogItem work)
    {
        if (work != null) EmptyText.Visibility = Visibility.Collapsed;
        _hero = work; HeroPlay.IsEnabled = work != null;
        HeroTitle.Work = work; HeroPoster.Work = work; HeroBackdrop.Work = work;
        HeroFacts.Text = work?.Subtitle ?? ""; HeroScores.DataContext = work;
        HeroOverview.Text = work == null ? "正在准备今日发现…" : "《" + work.Title + "》 · " + (work.Overview.Length > 0 ? work.Overview : "查看剧情、人物和片源，在详情页确认播放。");
        if (work != null && _heroRequestKey != work.WorkKey && _dailyCts != null && !_dailyCts.IsCancellationRequested)
        { _heroRequestKey = work.WorkKey; _ = LoadHeroAsync(work, _dailyCts.Token); }
    }
    async Task LoadHeroAsync(CatalogItem work, CancellationToken ct)
    {
        try
        {
            var full = await CatalogArtworkResolver.Shared.ResolveAsync(work, ct);
            if (ct.IsCancellationRequested || _hero?.WorkKey != work.WorkKey) return;
            work.ApplyMetadata(full); UpdateHero(work);
            if (_daily?.Items.Any(w => w.WorkKey == work.WorkKey) == true)
                MetadataStore.Instance.Write("daily-picks", _daily.Provider + ":" + _daily.Day, _daily);
        }
        catch (OperationCanceledException) { if (_heroRequestKey == work.WorkKey) _heroRequestKey = null; }
        catch { }
    }
    void ApplyVisibleMetadata(CatalogItem full)
    {
        _state?.ApplyMetadata(full);
        foreach (var work in _items.Concat(_daily?.Items ?? new List<CatalogItem>()).Where(w => w.WorkKey == full.WorkKey)) work.ApplyMetadata(full);
        if (_hero?.WorkKey == full.WorkKey) { _hero.ApplyMetadata(full); UpdateHero(_hero); }
    }
    async Task LoadDailyAsync()
    {
        var provider = Provider; var day = DateOnly.FromDateTime(DateTime.Now);
        if (_dailyLoading == provider && _dailyCts?.IsCancellationRequested == false) return;
        _dailyCts?.Cancel();
        _dailyLoading = null;
        if (_daily?.Provider == provider && _daily.Day == day.ToString("yyyy-MM-dd")) {
            if (_dailyCts == null || _dailyCts.IsCancellationRequested) _dailyCts = new();
            _heroStarted = DateTimeOffset.UtcNow; _dailyPrefetchCts?.Cancel(); _nextDaily = null; _nextDailyIndex = -1; _preparingDaily = false;
            _carousel.Start(); if (_hero == null) ShowDaily(0); return;
        }
        _dailyCts?.Cancel(); var cts = _dailyCts = new(); _dailyLoading = provider; _carousel.Stop();
        DailyHeading.Text = "今日发现 · 正在准备当天五部";
        try {
            var picks = await new DailyDiscovery().LoadAsync(provider, day, cts.Token);
            if (cts.IsCancellationRequested || Provider != provider) return;
            if (picks == null) { DailyHeading.Text = "今日发现暂不可用，分类浏览仍可用"; return; }
            _daily = picks; _dailyPrefetchCts?.Cancel(); _nextDaily = null; _nextDailyIndex = -1; HeroPages.Children.Clear();
            for (var n = 0; n < 5; n++) {
                var index = n; var button = new Button { Content = (n + 1).ToString(), Padding = new Thickness(8, 2, 8, 2), MinWidth = 30 };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "今日发现第" + (n + 1) + "部");
                button.Click += (_, _) => ShowDaily(index); HeroPages.Children.Add(button);
            }
            DailyHeading.Text = "今日发现 · 当天固定五部" + (picks.ReusedRecent ? " · 候选不足，部分近期重复" : " · 优先避开近十天");
            ShowDaily(0); _carousel.Start();
        }
        catch (OperationCanceledException) { }
        catch { if (!cts.IsCancellationRequested) DailyHeading.Text = "今日发现网络暂不可用，分类浏览仍可用"; }
        finally { if (ReferenceEquals(_dailyCts, cts)) _dailyLoading = null; }
    }
    void ShowDaily(int index) => _ = PrepareDailyAsync(index);
    void PrefetchDaily(int index)
    {
        if (_nextDailyIndex == index && _nextDaily != null) return;
        _dailyPrefetchCts?.Cancel(); _dailyPrefetchCts = CancellationTokenSource.CreateLinkedTokenSource(_dailyCts.Token);
        _nextDailyIndex = index; _nextDaily = PrepareDailyArtAsync(_daily.Items[index], _dailyPrefetchCts.Token);
    }
    static async Task<CatalogItem> PrepareDailyArtAsync(CatalogItem work, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try {
            var full = await CatalogArtworkResolver.Shared.ResolveAsync(work, deadline.Token, priority: true);
            await Task.WhenAll(ImageLoader.Load(full.Poster), ImageLoader.Load(full.TitleLogo), ImageLoader.Load(full.Backdrop)).WaitAsync(deadline.Token);
            return full;
        } catch (OperationCanceledException) { return null; } catch { return null; }
    }
    async Task PrepareDailyAsync(int index)
    {
        if (_daily?.Items.Count != 5 || _dailyCts == null || _dailyCts.IsCancellationRequested) return;
        var version = ++_dailySwapVersion; var ct = _dailyCts.Token; var picks = _daily;
        _preparingDaily = true; HeroProgress.IsIndeterminate = false;
        try {
            PrefetchDaily(index); var full = await _nextDaily;
            if (ct.IsCancellationRequested || version != _dailySwapVersion || _daily != picks) return;
            if (full == null) { _heroStarted = DateTimeOffset.UtcNow; _nextDaily = null; _nextDailyIndex = -1; return; }
            picks.Items[index].ApplyMetadata(full); _dailyIndex = index; _heroRequestKey = full.WorkKey;
            UpdateHero(picks.Items[index]); _heroStarted = DateTimeOffset.UtcNow; HeroProgress.Value = 0;
            for (var i = 0; i < HeroPages.Children.Count; i++) {
                var button = (Button)HeroPages.Children[i]; button.Background = i == index ? new SolidColorBrush(ColorFrom("#7758D8")) : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                button.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
            }
            MetadataStore.Instance.Write("daily-picks", picks.Provider + ":" + picks.Day, picks);
        } catch (OperationCanceledException) { }
        catch { if (!ct.IsCancellationRequested && version == _dailySwapVersion) _heroStarted = DateTimeOffset.UtcNow; }
        finally { if (version == _dailySwapVersion) { _preparingDaily = false; HeroProgress.IsIndeterminate = false; } }
    }
    static Windows.UI.Color ColorFrom(string rgb) => Windows.UI.Color.FromArgb(255, Convert.ToByte(rgb.Substring(1,2),16), Convert.ToByte(rgb.Substring(3,2),16), Convert.ToByte(rgb.Substring(5,2),16));
    string _animatedProvider;
    void UpdateHighlights(bool animate)
    {
        // Selection appearance is bound exclusively to ListViewItem.IsSelected in XAML.
        // Never leave a local Background on recycled category containers.
        var changed = _animatedProvider != Provider; _animatedProvider = Provider;
        foreach (var logo in new FrameworkElement[] { DoubanTabLogo, NetflixTabLogo, TmdbTabLogo }) {
            var selected = logo == (Provider == "tmdb" ? TmdbTabLogo : Provider == "netflix" ? NetflixTabLogo : DoubanTabLogo);
            var visual = ElementCompositionPreview.GetElementVisual(logo);
            visual.StopAnimation("Scale"); visual.StopAnimation("RotationAngleInDegrees");
            visual.CenterPoint = new((float)logo.ActualWidth / 2, (float)logo.ActualHeight / 2, 0);
            visual.RotationAngleInDegrees = 0; visual.RotationAxis = System.Numerics.Vector3.UnitY;
            visual.Scale = new System.Numerics.Vector3(selected ? 1.04f : 1);
            if (animate && changed && selected && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) {
                var scale = visual.Compositor.CreateVector3KeyFrameAnimation();
                scale.InsertKeyFrame(0, new System.Numerics.Vector3(Provider == "netflix" ? 0.25f : 0.9f, 0.96f, 1));
                scale.InsertKeyFrame(0.70f, new System.Numerics.Vector3(1.06f, 1.04f, 1));
                scale.InsertKeyFrame(1, new System.Numerics.Vector3(1.04f)); scale.Duration = TimeSpan.FromMilliseconds(1050);
                visual.StartAnimation("Scale", scale);
                if (Provider == "netflix") {
                    var turn = visual.Compositor.CreateScalarKeyFrameAnimation(); turn.InsertKeyFrame(0, -78); turn.InsertKeyFrame(0.70f, 8); turn.InsertKeyFrame(1, 0); turn.Duration = scale.Duration;
                    visual.StartAnimation("RotationAngleInDegrees", turn);
                }
            }
        }
    }
    void OnInfo(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CatalogItem work) { SavePosition(); CatalogActions.Details(work); }
    }
}
