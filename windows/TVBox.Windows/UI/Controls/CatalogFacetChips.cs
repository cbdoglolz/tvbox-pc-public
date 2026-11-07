using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.UI.Controls;

public sealed class CatalogFacetChips : UserControl
{
    readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    public event Action<CatalogQuery> Changed;
    public CatalogFacetChips() => Content = new ScrollViewer { Content = _row, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    public void SetQuery(CatalogQuery query)
    {
        _row.Children.Clear();
        void Add(string text, Action<CatalogQuery> remove)
        {
            var button = new Button { Content = text + " ×", Padding = new Thickness(10, 3, 10, 3) };
            button.Click += (_, _) => { var q = query.Copy(); remove(q); DispatcherQueue.TryEnqueue(() => Changed?.Invoke(q)); };
            _row.Children.Add(button);
        }
        if (query.CompanyId > 0) Add((query.CompanyIsNetwork ? "播出：" : "出品：") + query.CompanyName, q => { q.CompanyId = 0; q.CompanyName = ""; q.CompanyIsNetwork = false; });
        if (query.PersonId > 0) Add("演员：" + query.PersonName, q => { q.PersonId = 0; q.PersonName = ""; });
        if (query.Keyword.Length > 0) Add("片名：" + query.Keyword, q => q.Keyword = "");
        foreach (var c in query.Countries) Add("地区：" + CatalogVocabulary.Label(c), q => q.Countries.Remove(c));
        foreach (var g in query.Genres) Add("类型：" + CatalogVocabulary.Label(g), q => q.Genres.Remove(g));
        if (query.YearFrom != null || query.YearTo != null) Add($"年份 {query.YearFrom?.ToString() ?? "不限"}–{query.YearTo?.ToString() ?? "不限"}", q => { q.YearFrom = q.YearTo = null; });
        if (query.MinimumRating != null) Add($"{query.RatingProvider} ≥ {query.MinimumRating}", q => q.MinimumRating = null);
        if (query.MinimumVotes != null) Add($"评价人数 ≥ {query.MinimumVotes}", q => q.MinimumVotes = null);
        if (query.Kind.Length > 0) Add(query.Kind == "tv" ? "剧集" : "电影", q => q.Kind = "");
        if (query.Status.Length > 0) Add("状态：" + query.Status, q => q.Status = "");
        if (query.AnimeForm.Length > 0) Add("动画：" + query.AnimeForm, q => q.AnimeForm = "");
    }
}
