using System.Text.Json;
using TVBoxForWindows.Core;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Engine;

public sealed class PersonCatalog(MetadataHttp http = null, MetadataStore store = null)
{
    readonly MetadataHttp _http = http ?? new();
    MetadataStore Store => store ?? MetadataStore.Instance;
    public async Task<int?> ResolveAsync(CatalogPerson person, CancellationToken ct)
    {
        if (person.Provider == "tmdb" && int.TryParse(person.Id, out var id) && id > 0) return id;
        var json = await _http.GetAsync("tmdb", "search/person", new() { ["language"] = "zh-CN", ["query"] = person.Name, ["include_adult"] = "false" }, ct);
        using var doc = JsonDocument.Parse(json);
        var hits = CatalogMetadataParser.Array(CatalogMetadataParser.Get(doc.RootElement, "results"))
            .Where(p => new[] { "name", "original_name" }.Any(k => string.Equals(CatalogMetadataParser.Text(p, k), person.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(p => int.TryParse(CatalogMetadataParser.Text(p, "id"), out var value) ? value : 0).Where(v => v > 0).Distinct().ToList();
        return hits.Count == 1 ? hits[0] : null;
    }
    public static List<CatalogItem> ParseCredits(string json, string kind)
    {
        using var doc = JsonDocument.Parse(json);
        return CatalogMetadataParser.Array(CatalogMetadataParser.Get(doc.RootElement, "cast"))
            .Concat(CatalogMetadataParser.Array(CatalogMetadataParser.Get(doc.RootElement, "crew")).Where(n => CatalogMetadataParser.Text(n, "job") == "Director"))
            .Where(n => CatalogMetadataParser.Text(n, "media_type") == kind)
            .Select(n => CatalogMetadataParser.TmdbItem(n, kind)).Where(w => w?.Title.Length > 0)
            .DistinctBy(w => w.WorkKey).ToList();
    }
    public async Task<CatalogProviderPage> FetchAsync(CatalogQuery query, string kind, int page, CancellationToken ct)
    {
        var key = query.PersonId.ToString();
        var cached = Store.Read<string>("person-credits-v2", key);
        var json = cached?.IsFresh == true ? cached.Value : await _http.GetAsync("tmdb", $"person/{query.PersonId}/combined_credits", new() { ["language"] = "zh-CN" }, ct);
        ct.ThrowIfCancellationRequested();
        if (cached?.IsFresh != true) Store.Write("person-credits-v2", key, json, TimeSpan.FromHours(12));
        var all = CatalogFilters.Apply(ParseCredits(json, kind), query);
        var start = (page - 1) * query.PageSize;
        return new(all.Skip(start).Take(query.PageSize).ToList(), start + query.PageSize < all.Count, all.Count, "TMDB人物参与作品（演出/导演） · 按人物ID查询；筛选与排序覆盖已返回的作品资料");
    }
}
