using System.Globalization;
using System.Text.RegularExpressions;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Engine;

public record CatalogChoice(string Key, string Label);
public static class CatalogVocabulary
{
    public static readonly CatalogChoice[] Countries = { new("CN", "中国大陆"), new("HK", "中国香港"), new("TW", "中国台湾"), new("US", "美国"), new("JP", "日本"), new("KR", "韩国"), new("GB", "英国"), new("OTHER", "其他地区") };
    public static readonly CatalogChoice[] Genres = { new("action", "动作"), new("science-fiction", "科幻"), new("mystery", "悬疑"), new("thriller", "惊悚"), new("comedy", "喜剧"), new("romance", "爱情"), new("drama", "剧情"), new("crime", "犯罪"), new("horror", "恐怖"), new("animation", "动画"), new("documentary", "纪录片"), new("fantasy", "奇幻"), new("adventure", "冒险"), new("war", "战争"), new("history", "历史"), new("family", "家庭"), new("variety", "综艺") };
    public static string Country(string raw) => raw.Trim() switch
    {
        "中国大陆" or "中国" or "大陆" or "CN" => "CN", "中国香港" or "香港" or "HK" => "HK", "中国台湾" or "台湾" or "TW" => "TW",
        "美国" or "US" => "US", "日本" or "JP" => "JP", "韩国" or "KR" => "KR", "英国" or "GB" => "GB",
        "法国" => "FR", "德国" => "DE", "加拿大" => "CA", "澳大利亚" => "AU", "俄罗斯" => "RU", "印度" => "IN", "泰国" => "TH",
        "" => "", _ => Regex.IsMatch(raw, "^[A-Z]{2}$") ? raw : "OTHER",
    };
    public static List<string> GenreKeys(IEnumerable<string> raw) => Genres.Where(g => raw.Any(r => r.Contains(g.Label, StringComparison.OrdinalIgnoreCase) || r.Equals(g.Key, StringComparison.OrdinalIgnoreCase))).Select(g => g.Key).ToList();
    public static string Label(string key) => Genres.Concat(Countries).FirstOrDefault(c => c.Key == key)?.Label ?? key;
    public static int[] TmdbGenre(string key, string kind) => key switch
    {
        "action" or "adventure" when kind == "tv" => new[] { 10759 }, "science-fiction" or "fantasy" when kind == "tv" => new[] { 10765 },
        "action" => new[] { 28 }, "adventure" => new[] { 12 }, "science-fiction" => new[] { 878 }, "fantasy" => new[] { 14 },
        "comedy" => new[] { 35 }, "drama" => new[] { 18 }, "mystery" => new[] { 9648 }, "crime" => new[] { 80 },
        "animation" => new[] { 16 }, "documentary" => new[] { 99 }, "romance" when kind == "movie" => new[] { 10749 },
        "horror" when kind == "movie" => new[] { 27 }, "thriller" when kind == "movie" => new[] { 53 }, "family" => new[] { 10751 },
        "history" when kind == "movie" => new[] { 36 }, "war" when kind == "movie" => new[] { 10752 }, "variety" when kind == "tv" => new[] { 10764, 10767 }, _ => Array.Empty<int>(),
    };
    public static List<string> TmdbGenreKeys(IEnumerable<int> ids, string kind) => Genres.Where(g => TmdbGenre(g.Key, kind).Intersect(ids).Any()).Select(g => g.Key).ToList();
}
public static class CatalogFilters
{
    public static bool Matches(CatalogItem work, CatalogQuery q)
    {
        if (string.IsNullOrEmpty(work.Kind)) { if (q.Category != "all" || q.Kind.Length > 0) return false; }
        else if (!q.Kinds.Contains(work.Kind)) return false;
        if (q.Category == "anime" && !work.Genres.Contains("animation") || q.Category == "documentary" && !work.Genres.Contains("documentary") || q.Category == "variety" && !work.Genres.Contains("variety")) return false;
        if (!string.IsNullOrWhiteSpace(q.Keyword))
        {
            var key = TitleMatchRules.Normalize(q.Keyword);
            if (!new[] { work.Title, work.OriginalTitle }.Concat(work.Aliases).Concat(work.MatchedTerms).Any(t => TitleMatchRules.Normalize(t).Contains(key, StringComparison.Ordinal))) return false;
        }
        if (q.YearFrom != null || q.YearTo != null)
        {
            if (!int.TryParse(work.Year, out var year)) { if (!q.IncludeUnknownYear) return false; }
            else if (q.YearFrom != null && year < q.YearFrom || q.YearTo != null && year > q.YearTo) return false;
        }
        if (q.Countries.Count > 0 && !work.Countries.Any(c => q.Countries.Contains(c) || q.Countries.Contains("OTHER") && c.Length > 0 && !CatalogVocabulary.Countries.Take(7).Any(p => p.Key == c))) return false;
        if (q.Genres.Count > 0 && (q.AllGenres ? !q.Genres.All(work.Genres.Contains) : !q.Genres.Any(work.Genres.Contains))) return false;
        var rating = work.GetRating(q.RatingProvider);
        if ((q.MinimumRating != null || q.MinimumVotes != null) && (!rating.HasScore || q.MinimumRating != null && rating.Score < q.MinimumRating || q.MinimumVotes != null && (rating.VoteCount == null || rating.VoteCount < q.MinimumVotes))) return false;
        if (!string.IsNullOrEmpty(q.Status) && work.Status != q.Status) return false;
        if (!string.IsNullOrEmpty(q.AnimeForm) && work.AnimeForm != q.AnimeForm) return false;
        return true;
    }
    public static List<CatalogItem> Apply(IEnumerable<CatalogItem> works, CatalogQuery q)
    {
        q.Validate();
        var items = works.Where(w => Matches(w, q)).GroupBy(w => string.IsNullOrEmpty(w.WorkKey) ? w.Provider + w.Kind + w.Title + w.Year : CatalogIdentity.DedupKey(w)).Select(g => g.OrderByDescending(w => w.DetailsLoaded).First());
        if (!string.IsNullOrWhiteSpace(q.Keyword) && q.SortDirection == "default") {
            var keyword = TitleMatchRules.Normalize(q.Keyword);
            bool Exact(CatalogItem w) => new[] { w.Title, w.OriginalTitle }.Concat(w.Aliases).Any(t => TitleMatchRules.Normalize(t) == keyword);
            bool Future(CatalogItem w) => DateTime.TryParse(w.ReleaseDate, out var date) ? date.Date > DateTime.Today : int.TryParse(w.Year, out var year) && year > DateTime.Today.Year;
            return items.OrderByDescending(Exact).ThenBy(Future).ThenByDescending(w => w.Popularity).ThenBy(w => w.WorkKey, StringComparer.Ordinal).ToList();
        }
        IOrderedEnumerable<CatalogItem> Order<T>(Func<CatalogItem, T> key, Func<CatalogItem, bool> unknown = null, IComparer<T> comparer = null)
        {
            var knownFirst = items.OrderBy(w => unknown?.Invoke(w) ?? false);
            return q.EffectiveAscending ? knownFirst.ThenBy(key, comparer) : knownFirst.ThenByDescending(key, comparer);
        }
        return (q.EffectiveSort switch
        {
            "rating" => Order(w => w.GetRating(q.RatingProvider).Score ?? 0, w => !w.GetRating(q.RatingProvider).HasScore),
            "votes" => Order(w => w.GetRating(q.RatingProvider).VoteCount ?? 0, w => w.GetRating(q.RatingProvider).VoteCount == null),
            "year" => Order(w => int.TryParse(w.Year, out var year) ? year : 0, w => !int.TryParse(w.Year, out _)),
            "latest" => Order(w => w.ReleaseDate.Length > 0 ? w.ReleaseDate : w.Year + "-01-01", w => w.ReleaseDate.Length == 0 && w.Year.Length == 0, StringComparer.Ordinal),
            "name" => Order(w => w.Title, w => w.Title.Length == 0, StringComparer.Create(CultureInfo.GetCultureInfo("zh-CN"), true)),
            _ => Order(w => w.Popularity),
        }).ThenBy(w => w.WorkKey, StringComparer.Ordinal).ToList();
    }
}
