using System.Globalization;
using System.Text.RegularExpressions;

namespace TVBoxForWindows.Models;

public static class PlaybackPresentation
{
    public static string Delay(long? ms) => ms == null ? "待测" : ms < 1000 ? $"{ms} ms" : $"{ms.Value / 1000d:0.00} s";
    public static string DelayColor(long? ms) => ms == null ? "#FFAAAAAA" : ms < 1000 ? "#FF69D992" : ms < 3000 ? "#FFFFBC62" : "#FFFF7272";
    public static string Progress(History h)
    {
        if (h == null || h.Position < 1000) return "";
        var time = TimeSpan.FromMilliseconds(h.Position);
        var season = Regex.Match(h.VodName ?? "", @"第\s*([一二三四五六七八九十百\d]+)\s*季|Season\s*(\d+)|\bS(\d+)\b", RegexOptions.IgnoreCase);
        var explicitEpisode = Regex.Match(h.VodRemarks ?? "", @"(?:[Ee]|第\s*)(\d+)\s*(?:集)?");
        var episode = explicitEpisode.Success ? explicitEpisode.Groups[1].Value : Regex.Match(h.VodRemarks ?? "", @"\d+").Value;
        if (episode.Length > 0) episode = episode.TrimStart('0') is { Length: > 0 } digits ? digits : "0";
        var label = h.CatalogKind == "movie" ? "" :
            (season.Success ? season.Value + " · " : "") + (episode.Length > 0 ? $"第{episode}集 · " : (!string.IsNullOrEmpty(h.VodRemarks) ? h.VodRemarks + " · " : ""));
        return $"{label}{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
    }
    public static int QualityScore(VodFlag flag)
    {
        if (flag.ProbeHeight > 0) return flag.ProbeHeight;
        var label = (flag.OriginalFlag ?? (flag.SourceSite == null ? flag.Flag : "")) + " " + flag.Episodes.FirstOrDefault()?.Name;
        if (Regex.IsMatch(label, "2160|4k", RegexOptions.IgnoreCase)) return 2160;
        if (label.Contains("1080")) return 1080;
        if (label.Contains("720")) return 720;
        return 0;
    }
    public static IEnumerable<int> OrderFlags(IReadOnlyList<VodFlag> flags, bool quality) =>
        quality ? Enumerable.Range(0, flags.Count).OrderByDescending(i => QualityScore(flags[i])).ThenBy(i => flags[i].ProbeLatencyMs ?? long.MaxValue) :
        Enumerable.Range(0, flags.Count).OrderBy(i => flags[i].ProbeLatencyMs ?? long.MaxValue);
}
