using System.Text.Json;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Core;

public enum AdMark { Unknown, Clean, Ads }
public sealed class SitePreference
{
    public bool Enabled { get; set; } = true;
    public bool Removed { get; set; }
    public string Group { get; set; } = "默认点播";
}
public static class SiteRegistry
{
    public static event Action Changed;
    static string PrefKey => "sites_policy_" + VodConfigService.Cid;
    static Dictionary<string, SitePreference> Read() => JsonUtil.Deserialize<Dictionary<string, SitePreference>>(Setting.GetString(PrefKey, "{}")) ?? new();
    public static SitePreference Preference(string key) => Read().GetValueOrDefault(key) ?? new() { Enabled = !key.StartsWith("nuvio_"), Group = key.StartsWith("nuvio_") ? "国际高清" : "默认点播" };
    public static void Set(string key, SitePreference value) { var data = Read(); data[key] = value; Setting.Put(PrefKey, JsonSerializer.Serialize(data)); Changed?.Invoke(); }
    public static List<Site> All => VodConfigService.Instance.Sites.Concat(InternationalSources.Sites).Concat(JsonUtil.Deserialize<List<Site>>(Setting.GetString("custom_sites_" + VodConfigService.Cid, "[]")) ?? new()).DistinctBy(s => s.Key).ToList();
    public static List<Site> Active => All.Where(s => { var p = Preference(s.Key); return p.Enabled && !p.Removed; }).ToList();
    public static bool Enabled(string key) { var p = Preference(key); return p.Enabled && !p.Removed; }
    public static void Add(Site site, string group)
    {
        if (string.IsNullOrWhiteSpace(site.Name) || !Uri.TryCreate(site.Api, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0) throw new ArgumentException("请填写名称和有效的HTTP(S)接口地址；凭据不能嵌入网址");
        site.Key = "user_" + Guid.NewGuid().ToString("N"); site.Type = 1;
        var key = "custom_sites_" + VodConfigService.Cid; var sites = JsonUtil.Deserialize<List<Site>>(Setting.GetString(key, "[]")) ?? new(); sites.Add(site); Setting.Put(key, JsonSerializer.Serialize(sites)); Set(site.Key, new() { Group = string.IsNullOrWhiteSpace(group) ? "自定义" : group.Trim() });
    }
}
public sealed record SiteAdRecord(string SiteKey, string Name, AdMark Mark, long Updated);
public static class SourceAdPolicy
{
    public static event Action Changed;
    static Dictionary<string, SiteAdRecord> Read() => JsonUtil.Deserialize<Dictionary<string, SiteAdRecord>>(Setting.GetString("source_ad_marks", "{}")) ?? new();
    public static string WorkKey(CatalogItem work) => VodConfigService.Cid + ":" + (work != null && WorkMediaService.TmdbId(work) is int id ? "tmdb:" + work.Kind + ":" + id : work?.WorkKey ?? "unknown") + ":season:" + (work?.Kind == "tv" ? work.SelectedSeason ?? Math.Max(1, CatalogIdentity.Season(work)) : 0);
    public static AdMark Get(CatalogItem work, string siteKey) => Read().GetValueOrDefault(WorkKey(work) + ":" + siteKey)?.Mark ?? AdMark.Unknown;
    public static void Set(CatalogItem work, Site site, AdMark mark) { if (work == null || site == null) return; var data = Read(); var key = WorkKey(work) + ":" + site.Key; if (mark == AdMark.Unknown) data.Remove(key); else data[key] = new(site.Key, site.Name, mark, Stores.Now()); Setting.Put("source_ad_marks", JsonSerializer.Serialize(data)); Changed?.Invoke(); }
    public static int Rank(CatalogItem work, string siteKey) => Get(work, siteKey) switch { AdMark.Clean => 0, AdMark.Unknown => 1, _ => 2 };
    public static string Label(AdMark mark) => mark switch { AdMark.Ads => "😈 有广告", AdMark.Clean => "🪽 无广告", _ => "❔ 未标记" };
    public static IEnumerable<(string Name, int Ads, int Clean)> Statistics() => Read().Values.GroupBy(r => r.SiteKey).Select(g => (g.First().Name, g.Count(r => r.Mark == AdMark.Ads), g.Count(r => r.Mark == AdMark.Clean))).OrderByDescending(r => r.Item2);
}
