using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.UI.Pages;

public sealed partial class PlayerPage
{
    PlayerArgs _catalogArgs;
    void OnWorkDetails(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_menuOpen > 0) return;
        var work = _catalogArgs?.Catalog ?? (_session?.History != null ? CatalogLibrary.FromHistory(_session.History) : null);
        if (work == null) { ShowToast("当前资源没有足够影片资料，可在目录中查看详情"); return; }
        App.Main.OpenWorkDetails(work);
    }
    async void OnHdSource(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_menuOpen > 0) return; _menuOpen++; ShowControls();
        try { OnRetryDiscovery(sender, e); await Task.CompletedTask; }
        finally { _menuOpen = Math.Max(0, _menuOpen - 1); if (!_closed) ShowControls(); }
    }
    CancellationTokenSource _discoveryCts;
    CancellationTokenSource _refreshSourcesCts;
    List<PlaybackCandidate> _refreshedCandidates;
    bool _refreshingSources;
    readonly Dictionary<string, DiscoveryStatus> _sourceStatuses = new();
    bool _catalogStarted, _discoveryDone, _waitingForCandidate;
    long _resumeAnchorMs;
    string _anchorEpisode;
    string _requestedEpisode;
    int _attemptGeneration;
    bool _qualityWarmup, _qualityTimerStarted, _updatingPreference;
    TaskCompletionSource<bool> _preferredCompletion;
    string _resumeSiteKey, _appliedPreference;
    bool _additionalSourcesStarted;

    async Task StartCatalogAsync(PlayerArgs args)
    {
        _closed = false; _unavailableFlags.Clear();
        _catalogArgs = args;
        TitleText.Text = args.Catalog.Title;
        DiscoveryPanel.Visibility = Visibility.Visible;
        _updatingPreference = true;
        SourcePreferenceCombo.SelectedIndex = Setting.SourcePreference == "quality" ? 1 : 0;
        _updatingPreference = false;
        _qualityWarmup = Setting.SourcePreference == "quality";
        _appliedPreference = Setting.SourcePreference;
        RefreshRequiredBadge.Visibility = Visibility.Collapsed;
        DiscoveryStatusText.Text = "正在准备全网选源…";
        try
        {
            await App.Main.InitialVodRestoreTask;
            if (_closed) return;
            var item = args.Catalog;
            var key = item.WorkKey.Length > 0 ? item.WorkKey : (args.SeedSiteKey ?? "") + "@" + (args.SeedVodId ?? item.Title);
            var history = Stores.FindHistory(VodConfigService.Cid, key);
            if (history == null && !string.IsNullOrEmpty(item.Id))
            {
                var previous = CatalogHistory.Find(item, Stores.GetHistories(VodConfigService.Cid));
                if (previous != null) history = CatalogHistory.Promote(previous, item);
            }
            history ??= new History
            {
                Key = key, Cid = VodConfigService.Cid, VodName = item.Title, VodPic = item.Poster,
                CatalogId = item.Id, CatalogKind = item.Kind, CatalogYear = item.Year,
                CatalogProvider = item.Provider, CatalogSnapshot = JsonUtil.Serialize(item),
            };
            _requestedEpisode = string.IsNullOrEmpty(args.EpisodeName) ? history.VodRemarks : args.EpisodeName;
            item.IdentityKey = history.Key;
            var episodeProgress = ViewingState.Get(item, item.SelectedSeason ?? Math.Max(1, CatalogIdentity.Season(item)), item.Kind == "tv" ? EpisodeIdentity.Parse(_requestedEpisode).Number : null);
            _resumeAnchorMs = args.StartOver ? 0 : episodeProgress.Position > 0 ? episodeProgress.Position : TitleMatchRules.ResumePosition(history.VodRemarks, _requestedEpisode, item.Kind, history.Position);
            ViewingState.Unhide(history);
            _resumePending = _resumeAnchorMs > 5000;
            if (!string.IsNullOrEmpty(item.Kind)) history.CatalogKind = item.Kind;
            if (!string.IsNullOrEmpty(item.Year)) history.CatalogYear = item.Year;
            _session = new PlaySession { Catalog = item, Vod = new Vod { Id = item.Id, Name = item.Title, Pic = item.Poster }, History = history };
            RestoreFromHistory();
            InitTimers();
            InitPlayer();
            SubscribeServer();
            RefreshKeepButton();
            if (_core == null) return;
            _catalogStarted = false;
            _discoveryDone = false;
            _waitingForCandidate = false;
            var cts = _discoveryCts = new CancellationTokenSource();
            ShowLoading("正在并发检测播放源（最多10站）…", false);
            _resumeSiteKey = args.ManualSource ? args.SeedSiteKey : args.ForceReselect ? "" : string.IsNullOrEmpty(history.SourceKey) ? args.SeedSiteKey : history.SourceKey;
            if (!args.ManualSource && SourceAdPolicy.Rank(item, _resumeSiteKey) >= 2) _resumeSiteKey = "";
            if (!string.IsNullOrEmpty(_resumeSiteKey))
            {
                Logger.D(TAG, "续播优先检测上次来源：" + _resumeSiteKey);
                _qualityWarmup = false;
                _preferredCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                DiscoveryStatusText.Text = args.ManualSource ? "手动：优先连接所选来源…" : "续播：优先连接上次观看的来源…";
                await SourceDiscovery.RunAsync(item, _resumeSiteKey, args.ManualSource ? args.SeedVodId : string.IsNullOrEmpty(history.SourceVodId) ? args.SeedVodId : history.SourceVodId, _resumeSiteKey, _requestedEpisode,
                    AddCandidate, UpdateDiscovery, cts.Token, onlySiteKey: _resumeSiteKey, ignoreCache: args.ForceReselect || args.ManualSource, auditAll: true);
                await Task.Yield();
                if (_session.Flags.Count == 0) _preferredCompletion.TrySetResult(false);
                bool ready;
                try { ready = await _preferredCompletion.Task.WaitAsync(TimeSpan.FromSeconds(65), cts.Token); }
                catch (TimeoutException) { ready = false; }
                _preferredCompletion = null;
                if (_closed || cts.IsCancellationRequested) return;
                if (ready) { Logger.D(TAG, "上次来源已恢复，后台继续检测其余站点"); _additionalSourcesStarted = true; await SourceDiscovery.RunAsync(item, null, null, null, _requestedEpisode, AddCandidate, UpdateDiscovery, cts.Token, excludeSiteKey: _resumeSiteKey, ignoreCache: true, auditAll: true); _discoveryDone = true; UpdateDiscoveryText(); return; }
                if (_catalogStarted) { _triedFlags.Add(_session.FlagIndex); CancelPlayRequest(); _core.Stop(); }
                _catalogStarted = false; _waitingForCandidate = false;
                _qualityWarmup = Setting.SourcePreference == "quality";
                Logger.D(TAG, "上次来源恢复失败，按偏好检测其余来源");
                ShowToast("上次来源未能恢复播放，按当前偏好查找其他来源");
            }
            _additionalSourcesStarted = true;
            await SourceDiscovery.RunAsync(item, null, null, null, _requestedEpisode,
                AddCandidate, UpdateDiscovery, cts.Token, excludeSiteKey: _resumeSiteKey, ignoreCache: _catalogArgs.ForceReselect || _catalogArgs.ManualSource, auditAll: true);
            if (_closed || cts.IsCancellationRequested) return;
            _discoveryDone = true;
            _qualityWarmup = false;
            UpdateDiscoveryText();
            if (!_catalogStarted || _waitingForCandidate)
            {
                if (!TryNextCandidate()) ShowError("没有找到匹配且可播放的来源。可重新选源，或在资源页手动浏览；不会自动播放同名解说或其他季。");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_closed) ShowError("自动选源失败：" + error.Message); }
    }

    void AddCandidate(PlaybackCandidate candidate)
    {
        if (_closed || _session == null) return;
        if (_session.Flags.Any(f => f.SourceSite?.Key == candidate.Site.Key && f.OriginalFlag == candidate.Flag.OriginalFlag)) return;
        _session.Flags.Add(candidate.Flag);
        UpdateFlagUi();
        if (!_catalogStarted && _qualityWarmup)
        {
            DiscoveryStatusText.Text = "高清优先：比较首批候选（最多等 6 秒）…";
            if (!_qualityTimerStarted) { _qualityTimerStarted = true; _ = FinishQualityWarmup(); }
            if (_session.Flags.Count < 3) return;
            _qualityWarmup = false;
        }
        if (!_catalogStarted || _waitingForCandidate) TryNextCandidate();
    }

    async Task FinishQualityWarmup()
    {
        await Task.Delay(6000);
        if (_closed || _catalogStarted) return;
        _qualityWarmup = false;
        TryNextCandidate();
    }

    void OnSourcePreferenceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingPreference || SourcePreferenceCombo.SelectedIndex < 0) return;
        Setting.SourcePreference = SourcePreferenceCombo.SelectedIndex == 1 ? "quality" : "speed";
        RefreshRequiredBadge.Visibility = Setting.SourcePreference == _appliedPreference ? Visibility.Collapsed : Visibility.Visible;
        UiHints.Explain(RecheckSourcesButton, "后台检测本片本季本集的10个并发来源；当前视频继续播放，完成后自行选择切换");
        ShowToast("选源偏好已保存，点击右侧刷新图标立即应用");
    }

    void OnPreferenceMenuOpened(object sender, object e) { _menuOpen++; ShowControls(); }
    void OnPreferenceMenuClosed(object sender, object e) { _menuOpen = Math.Max(0, _menuOpen - 1); if (!_closed) ShowControls(); }

    bool TryNextCandidate()
    {
        var name = _catalogStarted ? _session.CurrentEpisode?.Name : _requestedEpisode;
        foreach (var index in PlaybackPresentation.OrderFlags(_session.Flags, Setting.SourcePreference == "quality").OrderBy(i => SourceAdPolicy.Rank(_session.Catalog, _session.Flags[i].SourceSite?.Key)))
        {
            if (_triedFlags.Contains(index)) continue;
            var flag = _session.Flags[index];
            var episode = string.IsNullOrEmpty(name) ? flag.Episodes.FirstOrDefault() : FindSameEpisode(flag, name);
            if (episode == null) { _triedFlags.Add(index); continue; }
            _session.FlagIndex = index;
            _session.EpisodeIndex = flag.Episodes.IndexOf(episode);
            _session.Site = flag.SourceSite;
            _catalogStarted = true;
            _waitingForCandidate = false;
            ErrorBar.IsOpen = false;
            UpdateFlagUi();
            DiscoveryStatusText.Text = $"正在尝试 {flag.SourceSite.Name} · 后台继续补齐来源";
            Logger.D(TAG, "自动尝试来源：" + flag.SourceSite.Key + " / " + episode.Name);
            _ = PlayCurrent(_resumeAnchorMs, showRestoreToast: _resumeAnchorMs > 5000);
            return true;
        }
        return false;
    }

    Episode FindSameEpisode(VodFlag flag, string name)
    {
        if (flag == null) return null;
        if (_catalogArgs == null) return flag.Find(name, false);
        var match = SourceDiscovery.FindEpisode(flag, _session.Catalog, name);
        if (match == null && _session.Catalog?.Kind == "movie") match = flag.Episodes.FirstOrDefault();
        if (match == null && Episode.Digit(name) < 0 && flag.Episodes.Count == 1) match = flag.Episodes[0];
        return match;
    }

    void UpdateDiscovery(DiscoveryStatus status)
    {
        if (_closed) return;
        _sourceStatuses[status.SourceKey] = status;
        UpdateDiscoveryText();
        if (_selectionKind == PlayerSelectionKind.Flag) RefreshSelectionPanel();
    }

    void UpdateDiscoveryText()
    {
        var total = SiteRegistry.Active.Count(s => (s.IsSearchable || s.Type == InternationalSources.SiteType) && !s.IsHidden);
        var current = _catalogStarted ? $"当前：{_session.CurrentSite?.Name} · " : "";
        var verified = _session.Flags.Count(f => f.IsMediaVerified);
        DiscoveryStatusText.Text = $"{current}已检查 {_sourceStatuses.Count}/{total} · {verified} 个媒体检测通过";
        RefreshAdMark();
        var latency = _session.CurrentFlag?.ProbeLatencyMs;
        SourceLatencyText.Text = "探测 " + PlaybackPresentation.Delay(latency);
        SourceLatencyText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorFromHex(PlaybackPresentation.DelayColor(latency)));
        UiHints.Explain(DiscoveryStatusText, string.Join("\n", _sourceStatuses.Values.Select(s => $"{s.SourceName}：{s.Message}")));
    }

    IEnumerable<PlayerSelectionItem> SourceSelectionItems()
    {
        var items = new List<PlayerSelectionItem>();
        if (_refreshedCandidates != null)
        {
            foreach (var candidate in _refreshedCandidates.OrderByDescending(c => Setting.SourcePreference == "quality" ? c.Flag.ProbeHeight : 0).ThenBy(c => c.Flag.ProbeLatencyMs ?? long.MaxValue))
                items.Add(new(candidate.Flag.Flag + (candidate.Flag.ProbeHeight > 0 ? $" · {candidate.Flag.ProbeHeight}p" : " · 画质待解码确认"), candidate, false)
                { AdStatus = SourceAdPolicy.Get(_session.Catalog, candidate.Site.Key), AdSiteKey = candidate.Site.Key, AdLabel = SourceAdPolicy.Label(SourceAdPolicy.Get(_session.Catalog, candidate.Site.Key)), Detail = PlaybackPresentation.Delay(candidate.Flag.ProbeLatencyMs), DetailColor = PlaybackPresentation.DelayColor(candidate.Flag.ProbeLatencyMs) });
            foreach (var status in _sourceStatuses.Values.Where(s => !_refreshedCandidates.Any(c => c.Site.Key == s.SourceKey)))
                items.Add(new(status.SourceName + " · " + status.Message, -1, false)
                { IsUnavailable = true, AdStatus = SourceAdPolicy.Get(_session.Catalog, status.SourceKey), AdSiteKey = status.SourceKey, AdLabel = SourceAdPolicy.Label(SourceAdPolicy.Get(_session.Catalog, status.SourceKey)), Detail = status.NoMatch ? "NA" : "ERR", DetailColor = "#99AAAAAA" });
            return items;
        }
        foreach (var index in PlaybackPresentation.OrderFlags(_session.Flags, Setting.SourcePreference == "quality").OrderBy(i => SourceAdPolicy.Rank(_session.Catalog, _session.Flags[i].SourceSite?.Key)).OrderBy(i => _unavailableFlags.Contains(i)))
        {
            var flag = _session.Flags[index];
            var quality = flag.ProbeHeight > 0 ? $" · 清单 {flag.ProbeHeight}p" : "";
            items.Add(new(flag.Flag + quality, index, index == _session.FlagIndex)
            { IsUnavailable = _unavailableFlags.Contains(index), AdStatus = SourceAdPolicy.Get(_session.Catalog, flag.SourceSite?.Key), AdSiteKey = flag.SourceSite?.Key ?? "", AdLabel = SourceAdPolicy.Label(SourceAdPolicy.Get(_session.Catalog, flag.SourceSite?.Key)), Detail = PlaybackPresentation.Delay(flag.ProbeLatencyMs), DetailColor = PlaybackPresentation.DelayColor(flag.ProbeLatencyMs) });
        }
        foreach (var status in _sourceStatuses.Values.Where(s => !_session.Flags.Any(f => f.SourceSite?.Key == s.SourceKey)))
            items.Add(new(status.SourceName + " · " + status.Message, -1, false)
            { AdStatus = SourceAdPolicy.Get(_session.Catalog, status.SourceKey), AdSiteKey = status.SourceKey, AdLabel = SourceAdPolicy.Label(SourceAdPolicy.Get(_session.Catalog, status.SourceKey)), Detail = status.NoMatch ? "NA" : "ERR", DetailColor = "#99AAAAAA" });
        return items;
    }

    void RefreshAdMark()
    {
        if (_session == null) return;
        CurrentAdMark.Work = _session.Catalog ?? CatalogLibrary.FromHistory(_session.History);
        CurrentAdMark.Site = _session.CurrentSite; CurrentAdMark.Refresh();
    }
    void OnMarkSourceRow(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string key) return;
        var site = SiteRegistry.All.FirstOrDefault(s => s.Key == key); if (site == null) return;
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "手动标记本片本季的广告，自动选源会优化优先级。", IsEnabled = false });
        menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var mark in Enum.GetValues<AdMark>()) { var item = new MenuFlyoutItem { Text = SourceAdPolicy.Label(mark) }; item.Click += (_, _) => { SourceAdPolicy.Set(_session.Catalog, site, mark); RefreshAdMark(); QueueSelectionMutation(RefreshSelectionPanel); ShowToast("已标记“" + site.Name + "”：" + SourceAdPolicy.Label(mark) + "，应用到本片本季"); }; menu.Items.Add(item); }
        menu.ShowAt(button);
    }

    static Windows.UI.Color ColorFromHex(string hex) => Windows.UI.Color.FromArgb(255,
        Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16), Convert.ToByte(hex.Substring(7, 2), 16));

    async Task WatchStartAsync(int attempt)
    {
        var ticks = _resumePending ? 120 : 40;
        for (var tick = 0; tick < ticks; tick++)
        {
            await Task.Delay(500);
            if (_closed || _pauseWhenOpened || attempt != _attemptGeneration) return;
            if (!_sourceTransitionInProgress && !_resumePending && _core?.HasRenderedVideoFrame == true) return;
        }
        if (!_closed && attempt == _attemptGeneration) OnPlayError(_resumePending ? "来源未能在 60 秒内恢复到观看进度" : "来源在 20 秒内没有输出视频画面");
    }

    void RefreshKeepButton()
    {
        if (_session?.History == null) return;
        var kept = (_session.Catalog != null ? CatalogLibrary.FindKeep(_session.Catalog, VodConfigService.Cid) : Stores.FindKeep(VodConfigService.Cid, _session.History.Key)) != null;
        PlayerKeepIcon.Symbol = kept ? Symbol.SolidStar : Symbol.OutlineStar;
        UiHints.Explain(PlayerKeepButton, kept ? "已收藏，点击取消" : "未收藏，点击收藏");
    }

    void OnPlayerKeep(object sender, RoutedEventArgs e)
    {
        var h = _session?.History;
        if (h == null) return;
        var existing = _session.Catalog != null ? CatalogLibrary.FindKeep(_session.Catalog, h.Cid) : Stores.FindKeep(h.Cid, h.Key);
        if (existing != null) Stores.DeleteKeep(h.Cid, existing.Key);
        else Stores.SaveKeep(new Keep
        {
            Key = h.Key, Cid = h.Cid, VodName = h.VodName, VodPic = h.VodPic,
            SiteName = string.IsNullOrEmpty(h.CatalogId) ? _session.CurrentSite?.Name ?? "" : h.CatalogProvider == "tmdb" ? "TMDB资料" : "豆瓣推荐",
            CatalogId = h.CatalogId, CatalogKind = h.CatalogKind, CatalogYear = h.CatalogYear,
            CatalogProvider = h.CatalogProvider, CatalogSnapshot = h.CatalogSnapshot,
        });
        RefreshKeepButton();
        ShowToast(existing == null ? "已收藏" : "未收藏");
    }

    async void OnRetryDiscovery(object sender, RoutedEventArgs e)
    {
        if (_catalogArgs == null || _refreshingSources) return;
        SaveHistoryNow();
        _discoveryCts?.Cancel(); _discoveryDone = true; _additionalSourcesStarted = true;
        CancelSourceRefresh();
        var cts = _refreshSourcesCts = new CancellationTokenSource();
        var work = _session.Catalog.Copy(); var episode = _session.CurrentEpisode?.Name ?? _requestedEpisode;
        var preference = Setting.SourcePreference;
        _refreshedCandidates = new(); _refreshingSources = true; RecheckSourcesButton.IsEnabled = false;
        RefreshRequiredBadge.Visibility = Visibility.Collapsed;
        DiscoveryStatusText.Text = "后台10站并发检测中；当前视频继续播放，不会自动切换…";
        var checkedCount = 0;
        ShowToast("当前播放保留。检测完成后，请从成功来源列表选择切换");
        try
        {
            await SourceDiscovery.RunAsync(work, null, null, null, episode, candidate =>
            {
                if (_closed || cts.IsCancellationRequested) return;
                if (candidate.Flag.IsMediaVerified) _refreshedCandidates.Add(candidate);
                if (_selectionKind == PlayerSelectionKind.Flag) RefreshSelectionPanel();
            }, update =>
            {
                if (_closed || cts.IsCancellationRequested) return;
                checkedCount++; _sourceStatuses[update.SourceKey] = update;
                DiscoveryStatusText.Text = $"后台检测 {checkedCount} 站 · {_refreshedCandidates.Count} 个成功来源；当前播放保留";
            }, cts.Token, ignoreCache: true, preference: preference, auditAll: true);
            // SourceDiscovery posts ordered UI callbacks; yield to the dispatcher before reading the completed list.
            await Task.Yield();
            if (_closed || cts.IsCancellationRequested) return;
            _appliedPreference = preference;
            RefreshRequiredBadge.Visibility = Setting.SourcePreference == preference ? Visibility.Collapsed : Visibility.Visible;
            DiscoveryStatusText.Text = $"检测完成 · {_refreshedCandidates.Count} 个成功来源；点击底部线路选择，当前播放保留";
            if (_refreshedCandidates.Count > 0) OpenSelectionPanel(PlayerSelectionKind.Flag, RecheckSourcesButton);
            else { _refreshedCandidates = null; ShowToast("本次未检测到新来源，保留当前播放和原线路"); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_closed && !cts.IsCancellationRequested) { _refreshedCandidates = null; ShowToast("检测暂时失败，当前播放保留"); Logger.D(TAG, "后台选源：" + error.GetType().Name); } }
        finally { if (ReferenceEquals(_refreshSourcesCts, cts)) { _refreshingSources = false; RecheckSourcesButton.IsEnabled = true; } }
    }

    void CancelSourceRefresh()
    {
        _refreshSourcesCts?.Cancel(); _refreshSourcesCts = null;
        _refreshedCandidates = null; _refreshingSources = false;
        RecheckSourcesButton.IsEnabled = true;
    }

    void SelectRefreshedCandidate(PlaybackCandidate candidate, long? preservedPosition = null)
    {
        if (candidate == null || _refreshedCandidates?.Contains(candidate) != true) return;
        var episode = SourceDiscovery.FindEpisode(candidate.Flag, _session.Catalog, _session.CurrentEpisode?.Name ?? _requestedEpisode);
        if (episode == null) { ShowToast("该来源已没有对应本集，请重新检测"); return; }
        var decoderPosition = Math.Max(0, _core?.PositionMs ?? 0);
        var position = preservedPosition ?? ResumeProgress.SourceSwitchPosition(decoderPosition, _resumeAnchorMs, _displayPositionMs, _pendingSeekMs);
        Logger.D(TAG, $"应用检测来源 {candidate.Site.Key}：定位={position}，解码={decoderPosition}，已验证={_resumeAnchorMs}，显示={_displayPositionMs}");
        SaveHistoryNow();
        // Replace this site's old variant only after an explicit choice. Do not grow duplicate source rows.
        var index = _session.Flags.FindIndex(f => f.SourceSite?.Key == candidate.Site.Key && f.OriginalFlag == candidate.Flag.OriginalFlag);
        if (index < 0) { index = _session.Flags.Count; _session.Flags.Add(candidate.Flag); }
        else _session.Flags[index] = candidate.Flag;
        _session.FlagIndex = index; _session.EpisodeIndex = candidate.Flag.Episodes.IndexOf(episode); _session.Site = candidate.Site;
        _triedFlags.Clear(); _unavailableFlags.Remove(index); CancelSourceRefresh(); UpdateFlagUi();
        _catalogStarted = true; _waitingForCandidate = false;
        _ = PlayCurrent(position > 5000 ? position : 0);
    }

    bool _adFallbackStarted;
    bool ReselectAdvertisedEpisode(string episode)
    {
        if (_catalogArgs == null || SourceAdPolicy.Rank(_session.Catalog, _session.CurrentSite?.Key) < 2) return false;
        App.Main.OpenPlayer(new PlayerArgs { Catalog = _session.Catalog.Copy(), EpisodeName = episode, StartOver = true, ForceReselect = true }); return true;
    }
    async void EnsureAdFallback()
    {
        if (_adFallbackStarted || _closed || _session.Catalog == null) return; _adFallbackStarted = true;
        var ct = _discoveryCts?.Token ?? default; _discoveryDone = false;
        ShowToast("其他来源未能播放，最后尝试已标记广告的来源");
        try { await SourceDiscovery.RunAsync(_session.Catalog, null, null, null, _session.CurrentEpisode?.Name ?? _requestedEpisode, AddCandidate, UpdateDiscovery, ct, ignoreCache: true, adOnly: true); }
        catch (OperationCanceledException) { } catch { }
        finally { if (!_closed && !ct.IsCancellationRequested) { _discoveryDone = true; if (_waitingForCandidate && !TryNextCandidate()) ShowError("所有可用来源均未能播放，请稍后刷新或手动选择其他来源。"); } }
    }

    async void EnsureAdditionalSources()
    {
        if (_additionalSourcesStarted || _catalogArgs == null || _discoveryCts == null || _closed) return;
        _additionalSourcesStarted = true; _discoveryDone = false; var ct = _discoveryCts.Token;
        try { await SourceDiscovery.RunAsync(_session.Catalog, null, null, null, _session.CurrentEpisode?.Name, AddCandidate, UpdateDiscovery, ct, excludeSiteKey: _resumeSiteKey, ignoreCache: _catalogArgs.ForceReselect || _catalogArgs.ManualSource, auditAll: true); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_closed) Logger.E(TAG, "补充来源失败：" + error.GetType().Name); }
        finally
        {
            if (!_closed && !ct.IsCancellationRequested)
            {
                _discoveryDone = true; UpdateDiscoveryText();
                if (_waitingForCandidate && !TryNextCandidate()) ShowError("备用来源已检查，暂时没有可播放的视频。可点击刷新选源重试。");
            }
        }
    }
}
