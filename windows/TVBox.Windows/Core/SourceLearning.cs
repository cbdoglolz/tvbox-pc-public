using System.Security.Cryptography;
using System.Text;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Core;

public record SourceHealth(bool Good, DateTimeOffset CheckedAt);
public class SourcePerformance
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public int Attempts { get; set; }
    public int Failures { get; set; }
    public int SpeedWins { get; set; }
    public int QualityWins { get; set; }
    public int Samples { get; set; }
    public double TotalStartMs { get; set; }
    public int MaximumHeight { get; set; }
    public int Wins => SpeedWins + QualityWins;
    public string Summary => $"{Name} · 成功开播 {Wins} 次（速度 {SpeedWins} / 高清 {QualityWins}） · 连接尝试 {Attempts} / 失败 {Failures} · 平均出画面 {(Samples > 0 ? (TotalStartMs / Samples / 1000).ToString("0.00") + " s" : "-")} · 最高实测 {(MaximumHeight > 0 ? MaximumHeight + "p" : "-")}";
}
public static class SourceLearning
{
    static readonly object Sync = new();
    public static string EpisodeIdentity(string kind, string episode) => kind == "movie" ? "movie" : Models.EpisodeIdentity.Parse(episode).Number is >= 0 and var number ? number.ToString() : string.IsNullOrEmpty(episode) ? "1" : "label:" + Engine.TitleMatchRules.Normalize(episode);
    static string LegacyFileFor(CatalogItem work, string episode, int cid) => Path.Combine(AppPaths.Cache, "source-health",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cid + "|" + Engine.TitleMatchRules.Normalize(work.Title) + "|" + work.Year + "|" + work.Kind + "|" + EpisodeIdentity(work.Kind, episode)))) + ".json");
    static string PreviousFileFor(CatalogItem work, string episode, int cid) => Path.Combine(AppPaths.Cache, "source-health",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("season-query-v2|" + work.WorkKey + "|" + cid + "|" + Engine.TitleMatchRules.Normalize(work.Title) + "|" + work.Year + "|" + work.Kind + "|" + EpisodeIdentity(work.Kind, episode)))) + ".json");
    static string FileFor(CatalogItem work, string episode, int cid) => Path.Combine(AppPaths.Cache, "source-health",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("episode-v3|" + work.WorkKey + "|" + Engine.TitleMatchRules.Normalize(work.Title) + "|" + work.Year + "|s:" + (work.SelectedSeason ?? Math.Max(1, Engine.CatalogIdentity.Season(work))) + "|" + cid + "|" + work.Kind + "|" + EpisodeIdentity(work.Kind, episode)))) + ".json");
    public static Dictionary<string, SourceHealth> Health(CatalogItem work, string episode, int cid)
    {
        lock (Sync) {
            var current = FileFor(work, episode, cid);
            if (File.Exists(current)) return DurableJsonFile.Read(current, JsonUtil.Deserialize<Dictionary<string, SourceHealth>>, () => new());
            // Keep proven sources, but do not let a negative result from the old season-query spelling suppress the corrected lookup.
            var previous = DurableJsonFile.Read(PreviousFileFor(work, episode, cid), JsonUtil.Deserialize<Dictionary<string, SourceHealth>>, () => new());
            foreach (var p in DurableJsonFile.Read(LegacyFileFor(work, episode, cid), JsonUtil.Deserialize<Dictionary<string, SourceHealth>>, () => new()))
                if (!previous.ContainsKey(p.Key)) previous[p.Key] = p.Value;
            return previous.Where(p => p.Value.Good).ToDictionary(p => p.Key, p => p.Value);
        }
    }
    public static bool Eligible(Dictionary<string, SourceHealth> records, string key, DateTimeOffset now)
    {
        var fresh = records.Where(p => now - p.Value.CheckedAt < (p.Value.Good ? TimeSpan.FromDays(7) : TimeSpan.FromMinutes(30))).ToDictionary(p => p.Key, p => p.Value);
        if (fresh.Values.Any(v => v.Good)) return fresh.TryGetValue(key, out var item) && item.Good;
        return !fresh.TryGetValue(key, out var failed) || failed.Good;
    }
    public static void Remember(CatalogItem work, string episode, int cid, string site, bool good)
    {
        if (Setting.Incognito || string.IsNullOrEmpty(site)) return;
        lock (Sync)
        {
            var path = FileFor(work, episode, cid); var map = Health(work, episode, cid);
            map[site] = new(good, DateTimeOffset.UtcNow); DurableJsonFile.Write(path, JsonUtil.Serialize(map));
        }
    }
    static string StatsPath => Path.Combine(AppPaths.Root, "source-statistics.json");
    public static List<SourcePerformance> Statistics()
    {
        lock (Sync) return DurableJsonFile.Read(StatsPath, JsonUtil.Deserialize<List<SourcePerformance>>, () => new());
    }
    public static void ObserveHeight(string key, int height)
    {
        if (Setting.Incognito || height <= 0) return;
        lock (Sync) {
            var all = Statistics(); var row = all.FirstOrDefault(r => r.Key == key);
            if (row == null || row.MaximumHeight >= height) return;
            row.MaximumHeight = height; DurableJsonFile.Write(StatsPath, JsonUtil.Serialize(all));
        }
    }
    public static void Record(string key, string name, string outcome, string preference, double elapsedMs = 0, int height = 0)
    {
        if (Setting.Incognito || string.IsNullOrEmpty(key)) return;
        lock (Sync)
        {
            var all = Statistics(); var row = all.FirstOrDefault(r => r.Key == key);
            if (row == null) { row = new() { Key = key, Name = name }; all.Add(row); }
            row.Name = name;
            if (outcome == "attempt") row.Attempts++;
            if (outcome == "failure") row.Failures++;
            if (outcome == "win")
            {
                if (preference == "quality") row.QualityWins++; else row.SpeedWins++;
                row.Samples++; row.TotalStartMs += Math.Max(0, elapsedMs); row.MaximumHeight = Math.Max(row.MaximumHeight, height);
            }
            DurableJsonFile.Write(StatsPath, JsonUtil.Serialize(all));
        }
    }
}
