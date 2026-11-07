using System.Text.Json;
using TVBoxForWindows.Models;
using TVBoxForWindows.Engine;

namespace TVBoxForWindows.Core;

public enum WatchStatus { Unseen, Watching, Completed }
public sealed class WatchEntry
{
    public WatchStatus Status { get; set; }
    public bool Manual { get; set; }
    public long Position { get; set; }
    public long Duration { get; set; }
    public long Updated { get; set; }
}
/// <summary>User-owned state stays in prefs; hiding Continue never deletes resume history.</summary>
public static class ViewingState
{
    public static event Action Changed;
    static readonly object Sync = new();
    static string _cachedJson;
    static Dictionary<string, WatchEntry> _cache = new();
    static Dictionary<string, WatchEntry> Read() { var json = Setting.GetString("viewing_state", "{}"); if (json != _cachedJson) { _cache = JsonUtil.Deserialize<Dictionary<string, WatchEntry>>(json) ?? new(); _cachedJson = json; } return _cache; }
    static string Key(CatalogItem work, int? season = null, int? episode = null) => VodConfigService.Cid + ":" + (WorkMediaService.TmdbId(work) is int id ? $"tmdb:{work.Kind}:{id}" : work.WorkKey) + (episode.HasValue ? $":s{season ?? 1}:e{episode}" : ":work");
    public static WatchEntry Get(CatalogItem work, int? season = null, int? episode = null)
    { lock (Sync) return Read().GetValueOrDefault(Key(work, season, episode)) ?? new(); }
    public static void Set(CatalogItem work, WatchStatus status, int? season = null, int? episode = null)
    {
        lock (Sync) { var entries = Read(); var key = Key(work, season, episode); var item = entries.GetValueOrDefault(key) ?? new(); item.Status = status; item.Manual = status != WatchStatus.Unseen; item.Updated = Stores.Now(); entries[key] = item; Save(entries); }
        Changed?.Invoke();
    }
    public static void Observe(CatalogItem work, History history)
    {
        if (work == null || history.Position < 1000 || history.Duration <= 0 || Setting.Incognito) return;
        lock (Sync) {
            var entries = Read(); var key = Key(work, history.EpisodeSeason, work.Kind == "tv" ? history.EpisodeNumber : null);
            var current = entries.GetValueOrDefault(key) ?? new();
            current.Position = history.Position; current.Duration = history.Duration; current.Updated = Stores.Now();
            if (!current.Manual) current.Status = history.Duration - history.Position <= 15000 ? WatchStatus.Completed : WatchStatus.Watching;
            entries[key] = current;
            var wholeKey = Key(work); var whole = entries.GetValueOrDefault(wholeKey) ?? new();
            if (!whole.Manual) whole.Status = work.Kind == "movie" ? current.Status : WatchStatus.Watching;
            whole.Updated = Stores.Now(); entries[wholeKey] = whole; Save(entries);
        }
        Changed?.Invoke();
    }
    static void Save(Dictionary<string, WatchEntry> entries) => Setting.Put("viewing_state", JsonSerializer.Serialize(entries.OrderByDescending(p => p.Value.Updated).Take(3000).ToDictionary(p => p.Key, p => p.Value)));
    static HashSet<string> Hidden() => JsonUtil.Deserialize<HashSet<string>>(Setting.GetString("continue_hidden", "[]")) ?? new();
    static string HistoryKey(History h) => h.Cid + ":" + h.Key;
    public static bool IsContinue(History h) { lock (Sync) return h.Position >= 1000 && h.Duration > h.Position + 10000 && !Hidden().Contains(HistoryKey(h)) && Get(CatalogLibrary.FromHistory(h)).Status != WatchStatus.Completed; }
    public static void Hide(IEnumerable<History> histories) { lock (Sync) { var set = Hidden(); foreach (var h in histories) set.Add(HistoryKey(h)); Setting.Put("continue_hidden", JsonSerializer.Serialize(set)); } Changed?.Invoke(); }
    public static void Unhide(History history) { lock (Sync) { var set = Hidden(); if (!set.Remove(HistoryKey(history))) return; Setting.Put("continue_hidden", JsonSerializer.Serialize(set)); } Changed?.Invoke(); }
}

public static class SearchHistory
{
    public static List<string> Items => JsonUtil.Deserialize<List<string>>(Setting.GetString("search_history", "[]")) ?? new();
    public static void Add(string query) { query = query.Trim(); if (query.Length == 0 || Setting.Incognito) return; var items = Items; items.RemoveAll(x => x.Equals(query, StringComparison.OrdinalIgnoreCase)); items.Insert(0, query); Setting.Put("search_history", JsonSerializer.Serialize(items.Take(10))); }
    public static void Remove(string query) => Setting.Put("search_history", JsonSerializer.Serialize(Items.Where(x => x != query)));
    public static void Clear() => Setting.Remove("search_history");
}
