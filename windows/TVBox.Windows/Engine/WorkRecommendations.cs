using System.Text.Json;
using TVBoxForWindows.Core;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Engine;

public sealed class WorkRecommendationState
{
    public int Tier { get; set; }
    public int Page { get; set; } = 1;
    public bool SeedDone { get; set; }
    public int SeedPage { get; set; } = 1;
    public bool HasMore { get; set; } = true;
    public List<CatalogItem> Items { get; set; } = new();
    public string Note { get; set; } = "";
}
public sealed class WorkRecommendations
{
    readonly ICatalogQueryProvider _provider;
    readonly MetadataHttp _http;
    readonly MetadataStore _store;
    readonly bool _seedEnabled;
    public WorkRecommendations(ICatalogQueryProvider provider = null, MetadataStore store = null, MetadataHttp http = null)
    { _http = http ?? new(); _provider = provider ?? new CatalogQueryProvider(_http, store); _store = store; _seedEnabled = provider == null || http != null; }
    MetadataStore Store => _store ?? MetadataStore.Instance;
    static string CacheKey(CatalogItem work) => work.WorkKey + ":" + WorkMediaService.TmdbId(work) + ":" + work.Kind + ":" + string.Join(",", work.Genres.OrderBy(g => g, StringComparer.Ordinal));
    public WorkRecommendationState Restore(CatalogItem work) => Store.Read<WorkRecommendationState>("work-recommend-v3", CacheKey(work))?.Value ?? new();
    public static bool Eligible(CatalogItem work, CatalogItem candidate, int tier)
    {
        if (candidate.WorkKey == work.WorkKey || WorkMediaService.TmdbId(candidate) is int id && id == WorkMediaService.TmdbId(work) && candidate.Kind == work.Kind) return false;
        if (tier < 4 && candidate.Kind != work.Kind) return false;
        if (tier <= 1 && work.Genres.Contains("animation") && !candidate.Genres.Contains("animation")) return false;
        return true;
    }
    async Task<CatalogProviderPage> SeedAsync(CatalogItem work, int page, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await _http.GetAsync("tmdb", $"{work.Kind}/{WorkMediaService.TmdbId(work)}/recommendations", new() { ["language"] = "zh-CN", ["page"] = page.ToString() }, ct));
        var root = doc.RootElement;
        var items = CatalogMetadataParser.Array(CatalogMetadataParser.Get(root, "results")).Select(n => CatalogMetadataParser.TmdbItem(n, work.Kind)).Where(w => w != null).ToList();
        var more = int.TryParse(CatalogMetadataParser.Text(root, "total_pages"), out var total) && page < total;
        return new CatalogProviderPage(items, more, null, "TMDB相似作品");
    }
    public async Task<WorkRecommendationState> MoreAsync(CatalogItem work, WorkRecommendationState state, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!state.HasMore) return state;
        state = JsonUtil.Deserialize<WorkRecommendationState>(JsonUtil.Serialize(state));
        for (var attempt = 0; attempt < 3 && state.HasMore; attempt++) {
            var seed = !state.SeedDone && _seedEnabled && TmdbCredentials.HasCredential && WorkMediaService.TmdbId(work) != null;
            CatalogProviderPage page;
            if (seed) {
                page = await SeedAsync(work, state.SeedPage, ct);
                if (!page.HasMore || state.SeedPage >= 50) state.SeedDone = true; else state.SeedPage++;
            } else {
                state.SeedDone = true;
                var kind = state.Tier < 4 ? work.Kind : work.Kind == "tv" ? "movie" : "tv";
                var themes = work.Genres.Where(g => g != "animation").Take(2).ToList();
                var animation = work.Genres.Contains("animation");
                var genres = animation && state.Tier <= 1 ? (state.Tier == 0 ? new[] { "animation" }.Concat(themes).ToList() : new() { "animation" })
                    : state.Tier is 1 or 3 ? new() : themes;
                var q = new CatalogQuery { Provider = TmdbCredentials.HasCredential ? "tmdb" : "douban", Category = kind, Genres = genres, AllGenres = animation && state.Tier == 0, SortDirection = "default" };
                page = await _provider.FetchAsync(q, kind, state.Page, "", ct);
            }
            ct.ThrowIfCancellationRequested(); var tier = state.Tier;
            var accepted = page.Items.Where(w => Eligible(work, w, tier)).OrderByDescending(w => w.Genres.Intersect(work.Genres).Count()).ThenByDescending(w => w.GetRating("tmdb").Score ?? w.Rating ?? 0);
            var before = state.Items.Count;
            foreach (var item in accepted) if (!state.Items.Any(w => w.WorkKey == item.WorkKey)) state.Items.Add(item);
            state.Note = tier <= 1 && work.Genres.Contains("animation") ? "优先同为" + (work.Kind == "tv" ? "动画剧集" : "动画电影") + " · 先相似作品，再扩展同类型"
                : tier < 4 ? "优先" + (work.Kind == "tv" ? "剧集" : "电影") + " · 同题材，再补充同类作品" : "同类可查询范围已完成，扩展其他类型";
            if (!seed) {
                if (!page.HasMore || state.Page >= 50) { state.Tier++; if (!work.Genres.Contains("animation") && state.Tier == 2) state.Tier = 4; state.Page = 1; if (state.Tier > 4) state.HasMore = false; }
                else state.Page++;
            }
            if (state.Items.Count > before) break;
        }
        ct.ThrowIfCancellationRequested(); Store.Write("work-recommend-v3", CacheKey(work), state, TimeSpan.FromHours(12)); return state;
    }
}
