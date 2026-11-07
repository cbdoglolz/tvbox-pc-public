using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.UI.Controls;

public sealed class CatalogMultiPicker : UserControl
{
    readonly DropDownButton _button = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 130 };
    readonly StackPanel _choices = new() { Spacing = 3 };
    readonly AutoSuggestBox _search = new() { PlaceholderText = "搜索选项" };
    readonly HashSet<string> _selected = new();
    readonly CatalogChoice[] _options;
    readonly string _label;
    public event Action Changed;
    public CatalogMultiPicker(string label, CatalogChoice[] options)
    {
        _label = label; _options = options;
        var panel = new StackPanel { Width = 240, Spacing = 8 };
        panel.Children.Add(_search); panel.Children.Add(new ScrollViewer { MaxHeight = 280, Content = _choices });
        _button.Flyout = new Flyout { Content = panel }; Content = _button;
        AutomationProperties.SetName(_button, label + "多选筛选");
        _search.TextChanged += (_, _) => RefreshChoices(); RefreshChoices(); RefreshLabel();
    }
    public List<string> Selection => _selected.Order().ToList();
    public void SetSelection(IEnumerable<string> selection)
    {
        var next = selection.ToHashSet();
        if (_selected.SetEquals(next)) { RefreshLabel(); return; }
        _selected.Clear(); foreach (var key in next) _selected.Add(key);
        // Never destroy the checkbox/input island inside its own pointer callback.
        DispatcherQueue.TryEnqueue(() => { RefreshChoices(); RefreshLabel(); });
    }
    void RefreshChoices()
    {
        _choices.Children.Clear();
        foreach (var option in _options.Where(o => o.Label.Contains(_search.Text ?? "", StringComparison.OrdinalIgnoreCase)))
        {
            var check = new CheckBox { Content = option.Label, IsChecked = _selected.Contains(option.Key) };
            check.Checked += (_, _) => { _selected.Add(option.Key); RefreshLabel(); Changed?.Invoke(); };
            check.Unchecked += (_, _) => { _selected.Remove(option.Key); RefreshLabel(); Changed?.Invoke(); };
            _choices.Children.Add(check);
        }
    }
    void RefreshLabel()
    {
        var selected = _options.Where(o => _selected.Contains(o.Key)).Select(o => o.Label).ToList();
        _button.Content = _label + "：" + (selected.Count == 0 ? "全部" : string.Join("、", selected));
        ToolTipService.SetToolTip(_button, _button.Content.ToString());
    }
}

public sealed class CatalogFilterBar : UserControl
{
    readonly ComboBox _years, _rating, _sort, _status, _form, _kind;
    readonly SortDirectionButton _direction = new();
    readonly NumberBox _from, _to, _score, _votes;
    readonly StackPanel _customYears = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Spacing = 6 };
    readonly CatalogMultiPicker _countries, _genres;
    readonly CheckBox _unknown = new() { Content = "纳入未知年份" }, _allGenres = new() { Content = "类型全部匹配" };
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    CatalogQuery _query = new(); bool _updating;
    public event Action<CatalogQuery> QueryChanged;
    public event Action ClearRequested;
    public event Action ResetRequested;
    public CatalogFilterBar()
    {
        var root = new StackPanel { Spacing = 6 };
        var main = new VariableSizedWrapGrid { ItemWidth = 140, ItemHeight = 42, Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 6 };
        main.SizeChanged += (_, e) => main.MaximumRowsOrColumns = Math.Max(1, (int)(e.NewSize.Width / 140));
        _years = Combo("年份（可输入四位年份直达）", CatalogYears.Choices(DateTime.Now.Year));
        _years.MaxDropDownHeight = 330; _years.IsEditable = true;
        _years.TextSubmitted += (_, e) => {
            if (int.TryParse(e.Text, out var year) && year >= 1800 && year <= 2100) {
                _updating = true; _from.Value = _to.Value = year;
                Select(_years, CatalogYears.Key(year, year, DateTime.Now.Year)); _updating = false; e.Handled = true; Raise();
            }
        };
        _years.SelectionChanged += (_, _) => {
            if (_updating) return; var value = Value(_years);
            if (value == "custom") { _customYears.Visibility = Visibility.Visible; _from.Focus(FocusState.Programmatic); return; }
            _updating = true; var range = CatalogYears.Range(value, DateTime.Now.Year);
            _from.Value = range.From ?? double.NaN; _to.Value = range.To ?? double.NaN;
            _customYears.Visibility = Visibility.Collapsed; _updating = false; Raise();
        };
        _countries = new("地区", CatalogVocabulary.Countries); _genres = new("类型", CatalogVocabulary.Genres);
        _countries.Changed += Raise; _genres.Changed += Raise;
        _rating = Combo("评分体系", new("douban", "评分：豆瓣"), new("tmdb", "评分：TMDB"));
        _sort = Combo("排序字段", new("popularity", "排序：热度"), new("year", "排序：年份"), new("latest", "排序：上映日期"), new("rating", "排序：评分"), new("votes", "排序：评价人数"), new("name", "排序：名称/首字母"));
        _direction.DirectionChanged += Raise;
        ToolTipService.SetToolTip(_rating, "各平台独立评分。TMDB个人Key选填。");
        _score = Number("最低评分", "最低分（不限）", 0, 10, 0.5);
        foreach (var element in new UIElement[] { _years, _countries, _genres, _rating, _score, _sort, _direction }) main.Children.Add(element);
        root.Children.Add(main);
        var more = new VariableSizedWrapGrid { ItemWidth = 140, ItemHeight = 48, Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 6 };
        more.SizeChanged += (_, e) => more.MaximumRowsOrColumns = Math.Max(1, (int)(e.NewSize.Width / 140));
        _from = Number("起始年份", "起始年份", 1800, 2100, 1); _to = Number("结束年份", "结束年份", 1800, 2100, 1);
        _votes = Number("最低评价人数", "最低评价人数", 0, int.MaxValue, 100);
        _status = Combo("剧集状态", new("", "状态：全部"), new("Ended", "已完结"), new("Returning", "连载/制作中"), new("Canceled", "已取消"));
        _form = Combo("动画形式", new("", "动画形式：全部"), new("movie", "动画电影"), new("series", "动画剧集"), new("ova", "OVA（明确标记）"));
        _kind = Combo("底层影视类型", new("", "电影/剧集：全部"), new("movie", "仅电影"), new("tv", "仅剧集"));
        _customYears.Children.Add(_from); _customYears.Children.Add(_to); root.Children.Add(_customYears);
        foreach (var element in new UIElement[] { _votes, _status, _form, _kind, _unknown, _allGenres }) more.Children.Add(element);
        more.Visibility = Visibility.Collapsed;
        _unknown.Checked += (_, _) => Raise(); _unknown.Unchecked += (_, _) => Raise(); _allGenres.Checked += (_, _) => Raise(); _allGenres.Unchecked += (_, _) => Raise();
        var actions = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 140, ItemHeight = 40, MaximumRowsOrColumns = 6 };
        actions.SizeChanged += (_, e) => actions.MaximumRowsOrColumns = Math.Max(1, (int)(e.NewSize.Width / 140));
        var moreButton = new Button { Content = "更多条件 ▾", Padding = new Thickness(8, 3, 8, 3) };
        moreButton.Click += (_, _) => more.Visibility = more.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        var clear = new Button { Content = "清除条件" }; clear.Click += (_, _) => ClearRequested?.Invoke();
        var reset = new Button { Content = "全部重置" }; reset.Click += (_, _) => ResetRequested?.Invoke();
        _summary.MaxWidth = 430; _summary.VerticalAlignment = VerticalAlignment.Center; _summary.TextTrimming = TextTrimming.CharacterEllipsis; _summary.MaxLines = 1;
        actions.Children.Add(moreButton); actions.Children.Add(clear); actions.Children.Add(reset); actions.Children.Add(_summary); root.Children.Add(actions); root.Children.Add(more); Content = root;
    }
    ComboBox Combo(string label, params CatalogChoice[] options)
    {
        var combo = new ComboBox { ItemsSource = options, DisplayMemberPath = "Label", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(2), MinWidth = 130 };
        AutomationProperties.SetName(combo, label); combo.SelectionChanged += (_, _) => { if (!ReferenceEquals(combo, _years)) Raise(); }; return combo;
    }
    NumberBox Number(string label, string placeholder, double min, double max, double step)
    {
        var number = new NumberBox { PlaceholderText = placeholder, Minimum = min, Maximum = max, SmallChange = step, Value = double.NaN, Margin = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(number, label); number.ValueChanged += (_, _) => Raise(); return number;
    }
    static string Value(ComboBox combo) => (combo.SelectedItem as CatalogChoice)?.Key ?? "";
    static void Select(ComboBox combo, string key) => combo.SelectedItem = combo.Items.OfType<CatalogChoice>().FirstOrDefault(o => o.Key == key) ?? combo.Items.FirstOrDefault();
    static int? Integer(NumberBox input) => double.IsNaN(input.Value) ? null : (int)input.Value;
    public void SetQuery(CatalogQuery query)
    {
        _updating = true; _query = query.Copy();
        Select(_years, CatalogYears.Key(query.YearFrom, query.YearTo, DateTime.Now.Year));
        _customYears.Visibility = Value(_years) == "custom" ? Visibility.Visible : Visibility.Collapsed;
        _from.Value = query.YearFrom ?? double.NaN; _to.Value = query.YearTo ?? double.NaN;
        _score.Value = query.MinimumRating ?? double.NaN; _votes.Value = query.MinimumVotes ?? double.NaN;
        _countries.SetSelection(query.Countries); _genres.SetSelection(query.Genres); Select(_rating, query.RatingProvider); Select(_sort, query.Sort);
        Select(_status, query.Status); Select(_form, query.AnimeForm); Select(_kind, query.Kind);
        _direction.SetQuery(query);
        _unknown.IsChecked = query.IncludeUnknownYear; _allGenres.IsChecked = query.AllGenres; _updating = false;
        RefreshSummary(query);
    }
    public CatalogQuery CurrentQuery()
    {
        var query = _query.Copy(); query.YearFrom = Integer(_from); query.YearTo = Integer(_to); query.MinimumRating = double.IsNaN(_score.Value) ? null : _score.Value; query.MinimumVotes = Integer(_votes);
        query.Countries = _countries.Selection; query.Genres = _genres.Selection; query.RatingProvider = Value(_rating); query.Sort = Value(_sort); query.Status = Value(_status); query.AnimeForm = Value(_form); query.Kind = Value(_kind);
        _direction.SelectField(query.Sort); query.SortDirection = _direction.Direction; query.SortAscending = query.SortDirection == "asc";
        query.IncludeUnknownYear = _unknown.IsChecked == true; query.AllGenres = _allGenres.IsChecked == true; return query;
    }
    void Raise()
    {
        if (_updating || _from == null || _to == null) return;
        var query = CurrentQuery(); RefreshSummary(query); QueryChanged?.Invoke(query);
    }
    void RefreshSummary(CatalogQuery q)
    {
        var parts = new List<string>();
        if (q.YearFrom != null || q.YearTo != null) parts.Add($"年份 {q.YearFrom?.ToString() ?? "不限"}–{q.YearTo?.ToString() ?? "不限"}");
        parts.AddRange(q.Countries.Select(CatalogVocabulary.Label)); parts.AddRange(q.Genres.Select(CatalogVocabulary.Label));
        if (q.MinimumRating != null) parts.Add($"{q.RatingProvider} ≥{q.MinimumRating:0.#}");
        _summary.Text = parts.Count > 0 ? "已选：" + string.Join(" / ", parts) : "当前未设置筛选条件";
    }
}
