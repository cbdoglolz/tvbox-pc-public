using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.UI;

public sealed class TmdbMatchDialog : ContentDialog
{
    readonly CatalogItem _original;
    readonly MetadataDetailsService _service;
    readonly CancellationTokenSource _cts = new();
    readonly TextBox _query = new() { Header = "片名或TMDB作品ID", PlaceholderText = "输入片名、数字ID或TMDB作品链接" };
    readonly ComboBox _matches = new() { Header = "核对片名、年份和类型后选择", DisplayMemberPath = "Label", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    public CatalogItem Result { get; private set; }
    int _searchVersion;
    public TmdbMatchDialog(CatalogItem work, MetadataDetailsService service)
    {
        _original = work.Copy(); _service = service;
        Title = "校正TMDB匹配"; PrimaryButtonText = "确认绑定"; CloseButtonText = "取消"; IsPrimaryButtonEnabled = false;
        SecondaryButtonText = work.ExternalIds.Tmdb > 0 ? "清除错误匹配" : "";
        DefaultButton = ContentDialogButton.Close;
        var body = new StackPanel { Spacing = 12, MinWidth = 380, MaxWidth = 600 };
        body.Children.Add(new TextBlock { Text = "当前作品：" + work.Title + " · " + work.Year + "\n只更改TMDB资料关联；收藏和观看进度保留。清除后暂停自动关联，仍可在这里手动绑定。", TextWrapping = TextWrapping.Wrap });
        _query.Text = work.Title; body.Children.Add(_query);
        var search = new Button { Content = "搜索匹配作品" }; search.Click += async (_, _) => await SearchAsync(); body.Children.Add(search);
        body.Children.Add(_matches); body.Children.Add(_status); Content = body;
        _matches.SelectionChanged += (_, _) => IsPrimaryButtonEnabled = _matches.SelectedItem is Option;
        Opened += async (_, _) => await SearchAsync(); Closed += (_, _) => _cts.Cancel();
        SecondaryButtonClick += (_, _) => Result = _service.Unlink(_original);
        PrimaryButtonClick += async (_, e) => {
            var deferral = e.GetDeferral(); e.Cancel = true;
            try {
                if (_matches.SelectedItem is not Option option) return;
                IsPrimaryButtonEnabled = false; _status.Text = "正在核对完整作品资料…";
                var full = await _service.CompleteMatchAsync(option.Work, _cts.Token);
                _cts.Token.ThrowIfCancellationRequested(); Result = _service.Associate(_original, full); e.Cancel = false;
            } catch (OperationCanceledException) { }
            catch { _status.Text = "绑定未完成，请检查网络或重新选择"; IsPrimaryButtonEnabled = true; }
            finally { deferral.Complete(); }
        };
    }
    async Task SearchAsync()
    {
        if (_query.Text.Trim().Length == 0 || _cts.IsCancellationRequested) return;
        var version = ++_searchVersion; _matches.ItemsSource = null; IsPrimaryButtonEnabled = false; _status.Text = "正在搜索…";
        try { var hits = await _service.FindMatchesAsync(_original, _query.Text, _cts.Token); if (_cts.IsCancellationRequested || version != _searchVersion) return; _matches.ItemsSource = hits.Select(w => new Option(w)).ToList(); _status.Text = hits.Count > 0 ? "请选择正确版本，再点击确认绑定" : "未找到作品，可改用原名或数字ID"; }
        catch (OperationCanceledException) { }
        catch (MetadataServiceException e) { if (!_cts.IsCancellationRequested && version == _searchVersion) _status.Text = e.Message; }
        catch { if (!_cts.IsCancellationRequested && version == _searchVersion) _status.Text = "搜索暂不可用，请稍后再试"; }
    }
    public record Option(CatalogItem Work) { public string Label => $"{Work.Title} · {Work.Year} · {(Work.Kind == "tv" ? "剧集" : "电影")} · ID {Work.Id}"; }
}
