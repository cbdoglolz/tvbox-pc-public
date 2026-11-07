using System.Security.Cryptography;
using System.Text;
using TVBoxForWindows.Core;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Engine;

public record DailyPicks(string Day, string Provider, List<CatalogItem> Items, bool ReusedRecent = false);
public sealed class DailyDiscovery(MetadataStore store = null, ICatalogQueryProvider provider = null)
{
    MetadataStore Store => store ?? MetadataStore.Instance;
    readonly ICatalogQueryProvider _provider = provider ?? new CatalogQueryProvider();
    public DailyPicks Read(string source, DateOnly day) => Store.Read<DailyPicks>("daily-picks", source + ":" + day.ToString("yyyy-MM-dd"))?.Value;
    public static string Identity(CatalogItem work) => TitleMatchRules.Normalize(work.Title) + "|" + work.Year + "|" + work.Kind;
    HashSet<string> Recent(DateOnly day)
    {
        var recent = new HashSet<string>();
        for (var n = 1; n <= 10; n++)
            foreach (var p in new[] { "douban", "netflix", "tmdb" })
                foreach (var item in Read(p, day.AddDays(-n))?.Items ?? new()) recent.Add(Identity(item));
        return recent;
    }
    public DailyPicks Choose(string source, DateOnly day, IEnumerable<CatalogItem> pool)
    {
        var saved = Read(source, day); if (saved?.Items.Count == 5) return saved;
        var recent = Recent(day);
        var all = pool.Where(w => w.Title.Length > 0 && w.Poster.Length > 0).DistinctBy(Identity)
            .OrderBy(w => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source + day + Identity(w)))), StringComparer.Ordinal).ToList();
        var fresh = all.Where(w => !recent.Contains(Identity(w))).ToList();
        var chosen = fresh.Concat(all.Where(w => recent.Contains(Identity(w)))).Take(5).Select(w => w.Copy()).ToList();
        if (chosen.Count != 5) return null; // Never freeze an incomplete daily set.
        var result = new DailyPicks(day.ToString("yyyy-MM-dd"), source, chosen, fresh.Count < 5);
        Store.Write("daily-picks", source + ":" + result.Day, result);
        foreach (var old in Store.List<DailyPicks>("daily-picks"))
            if (DateOnly.TryParse(old.Value.Day, out var date) && date < day.AddDays(-11)) Store.Delete("daily-picks", old.Id);
        return result;
    }
    public async Task<DailyPicks> LoadAsync(string source, DateOnly day, CancellationToken ct)
    {
        var saved = Read(source, day); if (saved?.Items.Count == 5) return saved;
        var pool = new List<CatalogItem>();
        var recent = Recent(day);
        for (var page = 1; page <= 4; page++)
        {
            foreach (var kind in new[] { "movie", "tv" })
            {
                var q = new CatalogQuery { Provider = source == "tmdb" ? "tmdb" : "douban", Category = kind,
                    Collection = source == "netflix" ? "netflix:hot-" + (kind == "movie" ? "movie" : "tv") : "", Sort = "popularity" };
                var result = await _provider.FetchAsync(q, kind, page, "", ct); pool.AddRange(result.Items);
            }
            if (pool.Where(w => w.Poster.Length > 0 && !recent.Contains(Identity(w))).DistinctBy(Identity).Count() >= 5) break;
        }
        ct.ThrowIfCancellationRequested(); return Choose(source, day, pool);
    }
}
