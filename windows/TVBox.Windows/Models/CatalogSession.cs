using System.Collections.ObjectModel;

namespace TVBoxForWindows.Models;

public record CatalogChannel(string Key, string Label, string Kind, string Sort = "U", string Tags = "");

public static class CatalogChannels
{
    public static IReadOnlyList<CatalogChannel> For(string provider) => provider == "tmdb" ? new CatalogChannel[]
    {
        new("hot-tv", "热门剧集", "tv"), new("hot-movie", "热门电影", "movie"),
        new("new-movie", "最新电影", "movie", "R"), new("new-tv", "最新剧集", "tv", "R"),
        new("anime", "动画作品", "tv", "R"), new("top-rated", "高分作品", "movie", "S"),
    } : provider == "netflix" ? new CatalogChannel[]
    {
        new("hot-tv", "热门剧集", "tv", "U", "Netflix"),
        new("hot-movie", "热门电影", "movie", "U", "Netflix"),
        new("new-movie", "最新电影", "movie", "R", "Netflix"),
        new("new-tv", "最新剧集", "tv", "R", "Netflix"),
        new("anime", "动画作品", "tv", "R", "Netflix,动画"),
        new("top-rated", "高分作品", "movie", "S", "Netflix"),
    } : new CatalogChannel[]
    {
        new("hot-tv", "热门剧集", "tv"), new("hot-movie", "热门电影", "movie"),
        new("new-movie", "最新电影", "movie", "R", "电影"), new("new-tv", "最新剧集", "tv", "R", "电视剧"),
        new("anime", "动画作品", "tv", "R", "动画"), new("top250", "TOP250", "movie", "S"),
    };
}

/// <summary>Process-lifetime state, independent of Frame/Page lifetime. Each provider/category owns its list and position.</summary>
public sealed class CatalogSession
{
    static readonly Dictionary<string, CatalogSession> Sessions = new();
    public static CatalogSession Get(string provider, string channel)
    {
        var key = provider + ":" + channel;
        if (!Sessions.TryGetValue(key, out var value)) Sessions[key] = value = new();
        return value;
    }
    public ObservableCollection<CatalogItem> Items { get; } = new();
    public int Page { get; private set; }
    public int Total { get; private set; }
    public double Offset { get; set; }
    public bool HasMore { get; private set; } = true;
    public bool IsStale { get; private set; }
    public void Apply(CatalogPage result, bool reset)
    {
        if (reset) { Items.Clear(); Page = 0; Offset = 0; }
        foreach (var item in result.Items)
            if (!Items.Any(i => i.Id == item.Id)) Items.Add(item);
        Page = result.Page; Total = result.Total; IsStale = result.IsStale;
        HasMore = result.Items.Count > 0 && Page * 20 < Total;
    }
}
