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
        var number = h.EpisodeNumber ?? EpisodeIdentity.Parse(h.VodRemarks).Number;
        var episode = number >= 0 ? number.ToString() : "";
        if (episode.Length > 0) episode = episode.TrimStart('0') is { Length: > 0 } digits ? digits : "0";
        var label = h.CatalogKind == "movie" ? "" :
            (h.EpisodeSeason is >= 0 ? (h.EpisodeSeason == 0 ? "特别篇" : "第" + h.EpisodeSeason + "季") + " · " : season.Success ? season.Value + " · " : "") + (episode.Length > 0 ? $"第{episode}集 · " : (!string.IsNullOrEmpty(h.VodRemarks) ? h.VodRemarks + " · " : ""));
        return $"{label}{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
    }
    public static string SidebarTitle(CatalogItem work, Vod vod, Episode episode)
    {
        var title = work?.Title ?? vod?.CleanName ?? "正在连接";
        if ((work?.Kind ?? Engine.TitleMatchRules.GuessKind(vod?.TypeName)) != "tv") return title;
        var identity = EpisodeIdentity.Parse(episode?.Name); var season = identity.Season >= 0 ? identity.Season : work?.SelectedSeason ?? (work == null ? 1 : Math.Max(1, Engine.CatalogIdentity.Season(work)));
        return title + (season == 0 ? " · 特别篇" : $" · 第{season}季") + (identity.Number > 0 ? $" 第{identity.Number}集" : "");
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
