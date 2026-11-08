using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.UI.Pages;

public sealed partial class CatalogBrowsePage : Page
{
    readonly ObservableCollection<CatalogItem> _items = new();
    readonly CatalogQueryCoordinator _queries = new();
    readonly MetadataDetailsService _details = new();
    readonly PosterMetadataLoader _metadata = new();
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _debounce;
    CatalogNavigation _root = new("movie");
    CatalogQueryState _state;
    CancellationTokenSource _cts;
    ScrollViewer _scroll;
    CatalogItem _contextWork;
    long _generation, _historyRevision = -1, _keepRevision = -1;
    bool _channelUpdating, _listView;
    bool _updating, _loading, _restoring, _wheelArmed, _closed;
    bool Local => _root.Scope is "favorites" or "history";
    string RootKey => _root.Scope + ":" + (_root.Preset?.ScopeId ?? _root.Category);
    public CatalogBrowsePage()
    {
        InitializeComponent(); PosterWall.ItemsSource = _items;
        SelectedConditions.Changed += q => Apply(q);
        _debounce = DispatcherQueue.CreateTimer(); _debounce.Interval = TimeSpan.FromMilliseconds(400); _debounce.IsRepeating = false;
        _debounce.Tick += (_, _) => ApplyInput();
        Filters.QueryChanged += query => { if (_updating) return; query.Keyword = KeywordBox.Text ?? ""; if (query.RatingProvider == "tmdb") Hint(true); DispatcherQueue.TryEnqueue(() => { if (!_closed) Apply(query); }); };
        Loaded += (_, _) => UiHints.Attach(this);
        QuickDirection.DirectionChanged += () => { if (_state == null) return; var q = _state.Query.Copy(); q.SortDirection = QuickDirection.Direction; q.SortAscending = q.SortDirection == "asc"; Apply(q); };
        Filters.ClearRequested += () => { var q = Filters.CurrentQuery().ClearFilters(); q.Keyword = KeywordBox.Text ?? ""; SetControls(q); Apply(q); };
        Filters.ResetRequested += () => { var q = DefaultQuery(); SetControls(q); Apply(q); };
        PosterWall.Loaded += (_, _) => AttachScroll();
        PosterWall.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnWheel), true);
        PosterWall.RightTapped += OnRightTapped;
    }
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e); _closed = false;
        _state = null; // Previous navigation already saved its own scope in OnNavigatedFrom.
        _root = e.Parameter as CatalogNavigation ?? new("movie");
        _cts?.Cancel(); _debounce.Stop();
        var query = _root.Preset?.Copy() ?? _queries.LastQuery(_root.Scope, _root.Category);
        if (_root.Keyword.Length > 0) query.Keyword = _root.Keyword;
        SetControls(query);
        PageTitle.Text = query.CompanyId > 0 ? query.CompanyName + (query.CompanyIsNetwork ? " · 播出作品" : " · 出品 / 参与作品") : query.PersonId > 0 ? query.PersonName + " · 参演作品" : _root.Scope == "collection" ? CollectionHeading(query) : CategoryName(_root.Category, _root.Scope);
        ProviderCombo.Visibility = Local||query.Provider=="anilist" ? Visibility.Collapsed : Visibility.Visible;
        SearchScopeCombo.IsEnabled = !Local;
        ClearHistoryButton.Visibility = _root.Scope == "history" ? Visibility.Visible : Visibility.Collapsed;
        _channelUpdating = true;
        ChannelTabs.Visibility = Local || _root.Scope is "global" or "person" or "collection" ? Visibility.Collapsed : Visibility.Visible;
        ChannelTabs.Items.Clear(); foreach(var label in (_root.Category=="anime" ? new[]{"推荐","新番","热门","最新","高分"}:new[]{"推荐","热门","最新","高分"}))ChannelTabs.Items.Add(label);
        var seasonalOffset=_root.Category=="anime"?1:0;
        ChannelTabs.SelectedIndex = query.ScopeId.EndsWith(":seasonal") ? 1 : query.ScopeId.EndsWith(":latest") ? 2+seasonalOffset : query.ScopeId.EndsWith(":rating") ? 3+seasonalOffset : query.ScopeId.EndsWith(":hot") ? 1+seasonalOffset : 0;
        _channelUpdating = false;
        Apply(query);
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        SavePosition(); _closed = true; _cts?.Cancel(); _generation++; _debounce.Stop(); base.OnNavigatedFrom(e);
    }
    static string CollectionHeading(CatalogQuery query)
    {
        var platform = query.ScopeId.StartsWith("netflix:") ? "netflix" : query.Provider;
        var key = query.ScopeId.Split(':').Last();
        var label = CatalogChannels.For(platform).FirstOrDefault(c => c.Key == key)?.Label ?? "推荐作品";
        return (platform == "netflix" ? "奈飞标签" : platform == "tmdb" ? "TMDB" : "豆瓣") + " · " + label;
    }
    void OnBackToOrigin(object sender, RoutedEventArgs e) => App.Main.ReturnFromCatalog();
    CatalogQuery DefaultQuery() => new() { Scope = _root.Scope, ScopeId = _root.Preset?.ScopeId ?? _root.Category, Category = _root.Category, Collection = _root.Preset?.Collection ?? "", SortDirection = "default" };
    public static string CategoryName(string category, string scope = "category") => scope switch { "favorites" => "收藏", "history" => "观看历史", "global" => "搜索影视", _ => category switch { "movie" => "电影", "tv" => "电视剧", "anime" => "动漫", "documentary" => "纪录片", "variety" => "综艺", _ => "影视目录" } };
    void SetControls(CatalogQuery q)
    {
        _updating = true; ProviderCombo.Visibility=Local||q.Provider=="anilist"?Visibility.Collapsed:Visibility.Visible; ProviderCombo.IsEnabled = q.PersonId == 0 && q.CompanyId == 0 && q.Provider!="anilist"; KeywordBox.Text = q.Keyword; Filters.SetQuery(q); ProviderCombo.SelectedIndex = q.Provider == "tmdb" ? 1 : 0; SearchScopeCombo.SelectedIndex = q.Scope == "global" ? 1 : 0; _updating = false;
    }
    void Hint(bool userRequested) { if (OptionalMetadataHint.TryMarkShown(userRequested)) OptionalHint.IsOpen = true; }
    void OnNeverHint(object sender, RoutedEventArgs e) { OptionalMetadataHint.DismissForever(); OptionalHint.IsOpen = false; }
    void OnSettings(object sender, RoutedEventArgs e) => App.Main.OpenMetadataSettings();
    void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || _state == null) return; var q = Filters.CurrentQuery(); q.Keyword = KeywordBox.Text ?? ""; q.Provider = ProviderCombo.SelectedIndex == 1 ? "tmdb" : "douban";
        if (q.Provider == "tmdb") Hint(true); Apply(_queries.LastView(q));
    }
    void OnSearchScopeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || _state == null || Local) return;
        var q = Filters.CurrentQuery(); q.Keyword = KeywordBox.Text ?? "";
        q.Scope = SearchScopeCombo.SelectedIndex == 1 ? "global" : _root.Scope;
        q.Category = SearchScopeCombo.SelectedIndex == 1 ? "all" : _root.Category;
        q.Collection = SearchScopeCombo.SelectedIndex == 1 ? "" : _root.Preset?.Collection ?? "";
        if (SearchScopeCombo.SelectedIndex == 1) { q.PersonId = 0; q.PersonName = ""; q.CompanyId = 0; q.CompanyName = ""; }
        Apply(q);
    }
    void OnKeywordChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
    { if (!_updating && e.Reason == AutoSuggestionBoxTextChangeReason.UserInput) { _debounce.Stop(); _debounce.Start(); } }
    void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs e) { SearchHistory.Add(sender.Text); _debounce.Stop(); ApplyInput(); }
    public void SearchKeyword(string keyword) { _updating = true; KeywordBox.Text = keyword; _updating = false; ApplyInput(); }
    void ApplyInput() { if (_state == null || _closed) return; var q = Filters.CurrentQuery(); q.Keyword = KeywordBox.Text ?? ""; q.Provider = q.ScopeId.EndsWith(":seasonal")?"anilist":ProviderCombo.SelectedIndex == 1 ? "tmdb" : "douban"; Apply(q); }
    void OnRefresh(object sender, RoutedEventArgs e) { if (_state != null) Apply(_state.Query, true); }
    void OnMore(object sender, RoutedEventArgs e) { if (!_loading && _state?.HasMore == true) _ = LoadAsync(_generation); }
    void OnFiltersOpening(object sender, object e)
    { FilterScroll.Width = Math.Clamp((XamlRoot?.Size.Width ?? 1000) - 100, 320, 840); }
    void OnViewMode(object sender, RoutedEventArgs e)
    {
        SavePosition(); _listView = !_listView;
        PosterWall.ItemTemplate = (DataTemplate)Resources[_listView ? "WorkRow" : "WorkCard"];
        RestorePosition();
    }
    void OnChannelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_channelUpdating || _state == null || _closed) return;
        var index=ChannelTabs.SelectedIndex; var seasonal=_root.Category=="anime"&&index==1;
        if(_root.Category=="anime"&&index>1)index--;
        var label = seasonal?"seasonal":index switch { 1 => "hot", 2 => "latest", 3 => "rating", _ => "recommended" };
        var fallback = _state.Query.Copy(); fallback.ScopeId = _root.Category + ":" + label;
        if(seasonal){fallback.Provider="anilist";fallback.Collection="";fallback.YearFrom=fallback.YearTo=DateTime.Now.Year;fallback.Keyword="";}else if(fallback.Provider=="anilist"){fallback.Provider="douban";fallback.YearFrom=fallback.YearTo=null;}
        fallback.ColumnSort = label == "latest" ? "latest" : label == "rating" ? "rating" : "popularity";
        fallback.Sort = fallback.ColumnSort; fallback.SortDirection = "default"; fallback.SortAscending = false;
        Apply(_queries.LastView(fallback));
    }
    void RenderConditions()
    {
        SelectedConditions.SetQuery(_state.Query); FilterBadge.Count = CatalogFacetRules.Count(_state.Query);
    }
    void OnToggleFilters(object sender, RoutedEventArgs e) => Filters.Visibility = Filters.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    void OnAdvancedSearch(object sender, RoutedEventArgs e) { SavePosition(); Frame.Navigate(typeof(SearchPage), KeywordBox.Text ?? ""); }
    void Apply(CatalogQuery query, bool refresh = false)
    {
        _debounce.Stop();
        try { query.Validate(); }
        catch (ArgumentException error) { ResultStatus.Text = "筛选无效：" + error.Message + "；尚未应用"; PosterWall.IsEnabled = false; return; }
        SavePosition(); _cts?.Cancel(); _cts = new(); _generation++; _loading = false; _wheelArmed = false;
        if (query.Provider == "tmdb" && !TmdbCredentials.HasCredential && !Local)
        { query.Provider = "douban"; App.Main.ShowNotice("TMDB目录需个人Key；当前显示豆瓣目录。普通点播不受影响。"); }
        SetControls(query); PosterWall.IsEnabled = true;
        if (_root.Scope == "person") PageTitle.Text = query.CompanyId > 0 ? query.CompanyName + (query.CompanyIsNetwork ? " · 播出作品" : " · 出品 / 参与作品") : query.PersonId > 0 ? query.PersonName + " · 参演作品" : "全部影视";
        var previousScope = _state?.Query.ScopeId;
        _state = refresh ? new() { Query = query.Copy() } : _queries.Restore(query);

        MetadataStore.Instance.Write("scope", RootKey, query);
        _queries.RememberView(query);
        Render(); RestorePosition();
        if (Local) _ = LoadLocalAsync(_generation);
        else if (_state.Items.Count == 0 || refresh) _ = LoadAsync(_generation);
        else _ = HydrateAsync(_state, _generation, _cts.Token);
    }
    async Task LoadLocalAsync(long generation)
    {
        try
        {
            await App.Main.InitialVodRestoreTask;
            if (_closed || generation != _generation) return;
            var histories = Stores.GetHistories(VodConfigService.Cid); var keeps = Stores.GetKeeps(VodConfigService.Cid);
            var works = _root.Scope == "history" ? histories.Select(CatalogLibrary.FromHistory) : keeps.Select(CatalogLibrary.FromKeep);
            var offset = _state.Offset; _state = _queries.QueryLocal(_state.Query, works); _state.Offset = offset;
            _historyRevision = Stores.HistoryRevision; _keepRevision = Stores.KeepRevision; Render(); RestorePosition();
        }
        catch { if (!_closed && generation == _generation) ResultStatus.Text = "本地列表暂时无法读取，原记录未删除"; }
    }
    public void EnterBackground() { SavePosition(); _closed = true; _cts?.Cancel(); _loading = false; _debounce.Stop(); }
    public void RefreshLocalIfChanged()
    {
        if (_closed) { _closed = false; _cts = new(); _generation++; if (_state != null && !Local && _items.Count == 0 && _state.HasMore) _ = LoadAsync(_generation); }
        if (Local && (_historyRevision != Stores.HistoryRevision || _keepRevision != Stores.KeepRevision)) _ = LoadLocalAsync(_generation);
        else if (_state != null) Render();
    }
    async Task LoadAsync(long generation)
    {
        if (_closed || _loading || _state == null) return;
        var ct = _cts.Token; _loading = true; Busy.IsActive = true; MoreButton.IsEnabled = false;
        EmptyText.Text = "正在检索目录，条件会跨页持续生效…";
        ResultStatus.Text = "正在查询；可切换页面或修改条件取消旧请求";
        try
        {
            var result = await _queries.LoadNextAsync(_state, _details.EnrichAsync, ct, partial =>
            {
                if (_closed || generation != _generation || ct.IsCancellationRequested) return;
                _state = partial; Render();
            });
            if (_closed || generation != _generation || ct.IsCancellationRequested) return;
            _state = result; Render(); _ = HydrateAsync(result, generation, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (generation != _generation || _closed) return;
            ResultStatus.Text = error is MetadataServiceException ? error.Message : "资料查询暂时失败，已加载内容保留，可重试";
            EmptyText.Text = ResultStatus.Text;
        }
        finally { if (generation == _generation && !_closed) { _loading = false; Busy.IsActive = false; MoreButton.IsEnabled = true; } }
    }
    void Render()
    {
        if (_state == null) return;
        QuickDirection.SetQuery(_state.Query); RenderConditions();
        var histories = Stores.GetHistories(VodConfigService.Cid);
        var offset = _scroll?.VerticalOffset ?? 0;
        _restoring = true;
        for (var index = 0; index < _state.Items.Count; index++)
        {
            var work = _state.Items[index];
            work.PrimaryRatingProvider = Setting.GetString("catalog_primary_rating", "douban"); work.NotifyMetadataChanged(); work.UpdateProgress(CatalogHistory.Find(work, histories));
            if (index >= _items.Count) _items.Add(work);
            else if (_items[index].WorkKey == work.WorkKey) { _items[index].ApplyMetadata(work); _items[index].UpdateProgress(CatalogHistory.Find(work, histories)); }
            else
            {
                var existing = _items.FirstOrDefault(w => w.WorkKey == work.WorkKey);
                if (existing != null) { _items.Move(_items.IndexOf(existing), index); existing.ApplyMetadata(work); }
                else _items.Insert(index, work);
            }
        }
        while (_items.Count > _state.Items.Count) _items.RemoveAt(_items.Count - 1);
        _scroll?.ChangeView(null, offset, null, true); _restoring = false;
        ResultStatus.Text = Local ? $"本地全量匹配 {_items.Count} 部" : $"已检索 {_state.ScannedPages} 页，当前匹配 {_items.Count} 部 · {(_state.HasMore ? "结果仍在补全；排序覆盖已检索范围" : "已遍历本次提供方可查询集合")}";
        ScopeStatus.Text = (_state.IsStale ? "缓存 · " : "") + _state.ScopeNote + (_state.MetadataMissing ? " · 部分作品缺少元数据" : "") + MetadataStore.Instance.RecoveryNotice;
        ToolTipService.SetToolTip(ScopeStatus, ScopeStatus.Text);
        EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _state.HasMore ? "当前页段还没有匹配作品，可继续向下滚动或点击继续补全" : "没有符合这些条件的已知作品，可调整条件或使用资源搜索";
        MoreButton.Visibility = !Local && _state.HasMore ? Visibility.Visible : Visibility.Collapsed;
        MoreButton.IsEnabled = !_loading; Busy.IsActive = _loading;
    }
    public void CaptureState() => SavePosition();
    void SavePosition()
    {
        if (_state == null) return;
        if (!_restoring && _scroll != null) _state.Offset = _scroll.VerticalOffset;
        _queries.Save(_state); MetadataStore.Instance.Write("scope", RootKey, _state.Query);
    }
    async Task HydrateAsync(CatalogQueryState state, long generation, CancellationToken ct)
    {
        await _metadata.LoadAsync(state.Items, (card, full) =>
        {
            if (_closed || ct.IsCancellationRequested || generation != _generation) return;
            state.ApplyMetadata(full);
            card.ApplyMetadata(full); _items.FirstOrDefault(w => w.WorkKey == card.WorkKey)?.ApplyMetadata(full);
        }, ct);
        if (_closed || ct.IsCancellationRequested || generation != _generation) return;
        state.Items = CatalogFilters.Apply(state.Candidates, state.Query); _queries.Save(state); Render();
    }
    void RestorePosition()
    {
        if (_scroll == null || _state == null) return; _restoring = true; var state = _state; var offset = state.Offset;
        DispatcherQueue.TryEnqueue(() => { if (_state == state && !_closed) { PosterWall.UpdateLayout(); _scroll.ChangeView(null, offset, null, true); } _restoring = false; });
    }
    void AttachScroll()
    {
        static ScrollViewer Find(DependencyObject root)
        {
            if (root is ScrollViewer viewer) return viewer;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (Find(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
            return null;
        }
        var scroll = Find(PosterWall); if (scroll == null) return;
        if (_scroll != scroll) { if (_scroll != null) _scroll.ViewChanged -= OnViewChanged; _scroll = scroll; _scroll.ViewChanged += OnViewChanged; ReturnButtons.Attach(_scroll, () => _wheelArmed = false); }
        RestorePosition();
    }
    void OnWheel(object sender, PointerRoutedEventArgs e) { if (e.GetCurrentPoint(PosterWall).Properties.MouseWheelDelta < 0) { _wheelArmed = true; DispatcherQueue.TryEnqueue(TryMore); } }
    void OnViewChanged(object sender, ScrollViewerViewChangedEventArgs e) { if (!_restoring && !_closed) { if (_state != null) _state.Offset = _scroll.VerticalOffset; if (!e.IsIntermediate) { SavePosition(); TryMore(); } } }
    void TryMore() { if (_wheelArmed && !_restoring && !_loading && _state?.HasMore == true && _scroll != null && _scroll.ScrollableHeight - _scroll.VerticalOffset < 200) { _wheelArmed = false; _ = LoadAsync(_generation); } }
    void OnPosterClick(object sender, ItemClickEventArgs e) { if (e.ClickedItem is CatalogItem work) Play(work); }
    void OnResume(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is CatalogItem work) Play(work); }
    void Play(CatalogItem work)
    {
        CatalogActions.Details(work);
    }
    void OnInfo(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CatalogItem work) { SavePosition(); CatalogActions.Details(work); }
    }
    void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var node = e.OriginalSource as DependencyObject; FrameworkElement target = null;
        while (node != null) { if (node is FrameworkElement element && element.DataContext is CatalogItem) { target = element; break; } node = VisualTreeHelper.GetParent(node); }
        if (target?.DataContext is not CatalogItem work) return; _contextWork = work;
        var flyout = new MenuFlyout();
        var info = new MenuFlyoutItem { Text = "影片详情与评分" }; info.Click += (_, _) => OnInfo(target, new RoutedEventArgs()); flyout.Items.Add(info);
        var play = new MenuFlyoutItem { Text = "播放 / 续播" }; play.Click += (_, _) => Play(work); flyout.Items.Add(play);
        if (Local) { var remove = new MenuFlyoutItem { Text = _root.Scope == "history" ? "删除这部作品的历史" : "取消这部作品收藏" }; remove.Click += (_, _) => OnRemove(null, null); flyout.Items.Add(remove); }
        flyout.ShowAt(target); e.Handled = true;
    }
    void OnRemove(object sender, RoutedEventArgs e)
    {
        if (_contextWork == null || !Local) return;
        if (_root.Scope == "history") foreach (var h in Stores.GetHistories(VodConfigService.Cid).Where(h => CatalogIdentity.SameWork(CatalogLibrary.FromHistory(h), _contextWork)).ToList()) Stores.DeleteHistory(h.Cid, h.Key);
        else foreach (var k in Stores.GetKeeps(VodConfigService.Cid).Where(k => CatalogIdentity.SameWork(CatalogLibrary.FromKeep(k), _contextWork)).ToList()) Stores.DeleteKeep(k.Cid, k.Key);
        _contextWork = null; _ = LoadLocalAsync(_generation);
    }
    async void OnClearHistory(object sender, RoutedEventArgs e)
    {
        var result = await new ContentDialog { XamlRoot = XamlRoot, Title = "清空全部观看历史？", Content = "此操作只清除观看记录，不删除收藏、设置或任何视频文件。", PrimaryButtonText = "清空历史", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close }.ShowAsync();
        if (result == ContentDialogResult.Primary) { Stores.DeleteHistories(VodConfigService.Cid); _ = LoadLocalAsync(_generation); }
    }
    void OnClearFilters(object sender, RoutedEventArgs e) { if (_state != null) Apply(CatalogFacetRules.Clear(_state.Query)); }
}
