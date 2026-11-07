using System.Text.RegularExpressions;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Engine;

public static class CatalogIdentity
{
    public static string ExternalLink(CatalogItem work, string provider)
    {
        if (provider == "douban")
        {
            var id = work.ExternalIds.Douban;
            if (id.Length == 0 && work.Provider == "douban") id = work.Id;
            return Regex.IsMatch(id, "^[0-9]+$") ? "https://movie.douban.com/subject/" + id + "/" : null;
        }
        if (provider == "tmdb") return work.ExternalIds.Tmdb is > 0 && work.Kind is "movie" or "tv" ? $"https://www.themoviedb.org/{work.Kind}/{work.ExternalIds.Tmdb}" : null;
        if (provider == "imdb") return Regex.IsMatch(work.ExternalIds.Imdb, "^tt[0-9]+$") ? "https://www.imdb.com/title/" + work.ExternalIds.Imdb + "/" : null;
        return null;
    }
    public static int Season(CatalogItem work)
    {
        if (work.SelectedSeason > 0) return work.SelectedSeason.Value;
        var normalized = TitleMatchRules.Normalize(work.Title);
        var match = Regex.Match(normalized, @"第(\d+)季|season(\d+)|s0*(\d+)$", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups.Cast<Group>().Skip(1).FirstOrDefault(g => g.Success)?.Value, out var number) ? number : 0;
    }
    public static string BaseTitle(string title) => Regex.Replace(TitleMatchRules.Normalize(title), @"第\d+季|season\d+|s\d+$", "");
    public static bool NamesOverlap(CatalogItem left, CatalogItem right) =>
        new[] { left.Title, left.OriginalTitle }.Concat(left.Aliases).Where(n => n.Length > 0).Select(BaseTitle)
            .Intersect(new[] { right.Title, right.OriginalTitle }.Concat(right.Aliases).Where(n => n.Length > 0).Select(BaseTitle)).Any();
    public static bool ReliablePair(CatalogItem subject, CatalogItem candidate)
    {
        if (subject.Kind != candidate.Kind || !NamesOverlap(subject, candidate) || string.IsNullOrEmpty(subject.Year)) return false;
        var season = Season(subject);
        if (season > 0 && candidate.Provider == "tmdb" && candidate.Kind == "tv")
            return candidate.Seasons.Any(s => s.Number == season && s.Year?.ToString() == subject.Year);
        return subject.Year == candidate.Year;
    }
    public static bool SameWork(CatalogItem left, CatalogItem right)
    {
        if (left.Kind.Length > 0 && right.Kind.Length > 0 && left.Kind != right.Kind) return false;
        var ls = Season(left); var rs = Season(right); if (ls > 0 && rs > 0 && ls != rs) return false;
        if (left.Kind == "tv" && (left.SelectedSeason != null || right.SelectedSeason != null) && (left.SelectedSeason ?? Math.Max(1, ls)) != (right.SelectedSeason ?? Math.Max(1, rs))) return false;
        if (left.Provider == right.Provider && left.Id.Length > 0 && right.Id.Length > 0) return left.Id == right.Id;
        if (left.WorkKey.Length > 0 && left.WorkKey == right.WorkKey) return true;
        if (left.Year.Length > 0 && right.Year.Length > 0 && left.Year != right.Year) return false;
        if (left.ExternalIds.Douban.Length > 0 && left.ExternalIds.Douban == right.ExternalIds.Douban) return true;
        return left.ExternalIds.Tmdb is > 0 && left.ExternalIds.Tmdb == right.ExternalIds.Tmdb && ls == rs;
    }
    public static string DedupKey(CatalogItem work) => !string.IsNullOrEmpty(work.ExternalIds.Douban) ? "douban@" + work.ExternalIds.Douban :
        work.Provider == "douban" && work.Id.Length > 0 ? "douban@" + work.Id : work.WorkKey;
    public static IEnumerable<string> PlaybackNames(CatalogItem work)
    {
        var season = Season(work);
        return new[] { work.Title, work.OriginalTitle }.Concat(work.Aliases).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n =>
            season > 0 && !Regex.IsMatch(TitleMatchRules.Normalize(n), @"第\d+季|season\d+|s\d+$") ? n + $" 第{season}季" : n).Distinct().Take(4);
    }
    public static IEnumerable<string> PlaybackSearchNames(CatalogItem work)
    {
        static string Chinese(int n) { const string digits = "零一二三四五六七八九"; return n is > 0 and < 10 ? digits[n].ToString() : n is >= 10 and < 100 ? (n / 10 == 1 ? "" : digits[n / 10].ToString()) + "十" + (n % 10 == 0 ? "" : digits[n % 10].ToString()) : n.ToString(); }
        return PlaybackNames(work).SelectMany(name => {
            var localized = Regex.Replace(name, @"第\s*(\d+)\s*季|Season\s*(\d+)", m => "第" + Chinese(int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)) + "季", RegexOptions.IgnoreCase);
            var broad = Regex.Replace(name, @"第\s*[一二三四五六七八九十\d]+\s*季|Season\s*\d+", "", RegexOptions.IgnoreCase).Trim();
            return new[] { localized, name, broad };
        }).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(3);
    }
}
