using System.Text.Json;
using System.Text.RegularExpressions;
using TVBoxForWindows.Core;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Engine;

public sealed class WorkMediaService(MetadataHttp http = null, MetadataStore store = null)
{
    readonly MetadataHttp _http = http ?? new();
    MetadataStore Store => store ?? MetadataStore.Instance;
    public static int? TmdbId(CatalogItem work) => work.ExternalIds.Tmdb is > 0 ? work.ExternalIds.Tmdb : work.Provider == "tmdb" && int.TryParse(work.Id, out var id) ? id : null;
    public static string Image(string path, string size = "w500") => Regex.IsMatch(path ?? "", @"^/[a-zA-Z0-9_-][a-zA-Z0-9_.-]*$") ? "https://image.tmdb.org/t/p/" + size + path : "";
    public static List<WorkEpisode> ParseSeason(string json, int season)
    {
        using var doc = JsonDocument.Parse(json); var p = CatalogMetadataParser.Text;
        int? N(JsonElement n, string key) => int.TryParse(p(n, key), out var x) ? x : null;
        double? D(JsonElement n, string key) => double.TryParse(p(n, key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) && double.IsFinite(x) ? x : null;
        return CatalogMetadataParser.Array(CatalogMetadataParser.Get(doc.RootElement, "episodes"))
            .Where(n => N(n, "episode_number") is > 0 && (N(n, "season_number") ?? season) == season)
            .Select(n => new WorkEpisode(season, N(n, "episode_number").Value, p(n, "name"), p(n, "overview"), Image(p(n, "still_path"), "w780"), p(n, "air_date"), N(n, "runtime"), D(n, "vote_average"), N(n, "vote_count")))
            .DistinctBy(e => e.Number).OrderBy(e => e.Number).Take(500).ToList();
    }
    public List<WorkEpisode> CachedSeason(CatalogItem work, int season) => TmdbId(work) is int id ? Store.Read<List<WorkEpisode>>("season-media-v1", id + ":" + season)?.Value : null;
    public async Task<List<WorkEpisode>> SeasonAsync(CatalogItem work, int season, CancellationToken ct)
    {
        if (TmdbId(work) is not int id || season < 0) return new();
        var key = id + ":" + season; var cache = Store.Read<List<WorkEpisode>>("season-media-v1", key);
        if (cache?.IsFresh == true) return cache.Value;
        try {
            var text = await _http.GetAsync("tmdb", $"tv/{id}/season/{season}", new() { ["language"] = "zh-CN" }, ct);
            var episodes = ParseSeason(text, season); ct.ThrowIfCancellationRequested();
            Store.Write("season-media-v1", key, episodes, TimeSpan.FromDays(7)); return episodes;
        } catch (OperationCanceledException) { throw; }
        catch when (cache != null) { return cache.Value; }
    }
    public async Task<List<CatalogItem>> CollectionAsync(CatalogItem work, CancellationToken ct)
    {
        if (work.CollectionId is not > 0) return new();
        var key = work.CollectionId.ToString(); var cached = Store.Read<List<CatalogItem>>("movie-collection-v1", key);
        if (cached?.IsFresh == true) return cached.Value;
        using var doc = JsonDocument.Parse(await _http.GetAsync("tmdb", "collection/" + key, new() { ["language"] = "zh-CN" }, ct));
        var items = CatalogMetadataParser.Array(CatalogMetadataParser.Get(doc.RootElement, "parts")).Select(n => CatalogMetadataParser.TmdbItem(n, "movie")).Where(w => w != null).OrderBy(w => w.Year).ToList();
        ct.ThrowIfCancellationRequested(); Store.Write("movie-collection-v1", key, items, TimeSpan.FromDays(7)); return items;
    }
    public async Task<List<CatalogItem>> OtherVersionsAsync(CatalogItem work, CancellationToken ct)
    {
        var title = Regex.Split(work.Title, "[:：]")[0].Trim();
        if (title.Length < 2) return new();
        var normalized = TitleMatchRules.Normalize(title); var key = normalized + ":" + work.WorkKey; var cache = Store.Read<List<CatalogItem>>("work-versions-v1", key);
        if (cache?.IsFresh == true) return cache.Value;
        var provider = new CatalogQueryProvider(_http, Store); var hits = new List<CatalogItem>();
        foreach (var kind in new[] { "movie", "tv" }) {
            var page = await provider.FetchAsync(new CatalogQuery { Provider = "tmdb", Category = kind, Keyword = title }, kind, 1, "", ct);
            hits.AddRange(page.Items.Where(w => TitleMatchRules.Normalize(w.Title).Contains(normalized) || normalized.Contains(TitleMatchRules.Normalize(w.Title))));
        }
        var items = hits.Where(w => w.ExternalIds.Tmdb != TmdbId(work) || w.Kind != work.Kind).DistinctBy(w => w.WorkKey).OrderBy(w => w.Year).Take(16).ToList();
        ct.ThrowIfCancellationRequested(); Store.Write("work-versions-v1", key, items, TimeSpan.FromDays(1)); return items;
    }
    public static CatalogItem ForSeason(CatalogItem original, WorkSeason season)
    {
        var work = original.Copy(); var originalSeason = original.SelectedSeason ?? Math.Max(1, CatalogIdentity.Season(original));
        if (work.Provider != "tmdb" && season.Number == originalSeason) { work.SelectedSeason = season.Number; return work; }
        if (TmdbId(work) is not int id) return work;
        work.Provider = "tmdb"; work.Id = id.ToString(); work.IdentityKey = ""; work.SelectedSeason = season.Number;
        var baseTitle = Regex.Replace(work.Title, @"第\s*[一二三四五六七八九十\d]+\s*季|Season\s*\d+", "", RegexOptions.IgnoreCase).Trim();
        work.Title = baseTitle + (season.Number > 1 ? $" 第{season.Number}季" : "");
        if (season.Year != null) work.Year = season.Year.ToString();
        if (season.Number != originalSeason) { work.ExternalIds.Douban = ""; work.Ratings.RemoveAll(r => r.Provider == "douban"); work.Rating = null; }
        return work;
    }
}
