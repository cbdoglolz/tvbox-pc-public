using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TVBoxForWindows.Models;

public enum ResultCompleteness { Partial, Complete, Unknown }
public class CatalogQuery
{
    public string Scope { get; set; } = "category";
    public string ScopeId { get; set; } = "movie";
    public string Category { get; set; } = "movie";
    public string Provider { get; set; } = "douban";
    public int PersonId { get; set; }
    public string PersonName { get; set; } = "";
    public string Keyword { get; set; } = "";
    public string Kind { get; set; } = "";
    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }
    public bool IncludeUnknownYear { get; set; }
    public List<string> Countries { get; set; } = new();
    public List<string> Genres { get; set; } = new();
    public bool AllGenres { get; set; }
    public string RatingProvider { get; set; } = "douban";
    public double? MinimumRating { get; set; }
    public int? MinimumVotes { get; set; }
    public string Sort { get; set; } = "popularity";
    public bool SortAscending { get; set; }
    // Null is an older saved query: preserve its explicit boolean direction.
    public string SortDirection { get; set; }
    public string ColumnSort { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string EffectiveSort => SortDirection == "default"
        ? ColumnSort.Length > 0 ? ColumnSort : Collection.EndsWith(":top250") || Collection.EndsWith(":top-rated") ? "rating"
        : Collection.EndsWith(":new-movie") || Collection.EndsWith(":new-tv") || Collection.EndsWith(":anime") ? "latest" : "popularity"
        : Sort;
    [System.Text.Json.Serialization.JsonIgnore] public bool EffectiveAscending => SortDirection == "asc" || SortDirection == null && SortAscending;
    [System.Text.Json.Serialization.JsonIgnore] public string DirectionLabel => SortDirection == "default" ? "默认排序 ↕" :
        EffectiveSort == "name" ? EffectiveAscending ? "正序 A–Z ↑" : "逆序 Z–A ↓" :
        EffectiveSort is "year" or "latest" ? EffectiveAscending ? "正序 旧→新 ↑" : "逆序 新→旧 ↓" :
        EffectiveSort is "rating" or "votes" ? EffectiveAscending ? "正序 低→高 ↑" : "逆序 高→低 ↓" : EffectiveAscending ? "正序 热度低→高 ↑" : "逆序 热度高→低 ↓";
    public void CycleDirection()
    {
        var mode = SortDirection ?? (SortAscending ? "asc" : "desc");
        SortDirection = mode switch { "default" => "asc", "asc" => "desc", _ => "default" };
        SortAscending = SortDirection == "asc";
    }
    public string Status { get; set; } = "";
    public string AnimeForm { get; set; } = "";
    public string Collection { get; set; } = "";
    public int PageSize { get; set; } = 20;
    public CatalogQuery Copy() => JsonSerializer.Deserialize<CatalogQuery>(JsonSerializer.Serialize(this));
    public CatalogQuery ClearFilters()
    {
        var q = new CatalogQuery { Scope = Scope, ScopeId = ScopeId, Category = Category, Provider = Provider, PersonId = PersonId, PersonName = PersonName, Keyword = Keyword, Collection = Collection, RatingProvider = RatingProvider, Sort = Sort, SortDirection = SortDirection, ColumnSort = ColumnSort, SortAscending = SortAscending };
        return q;
    }
    public void Validate()
    {
        if (RatingProvider == "imdb") { RatingProvider = "douban"; MinimumRating = null; MinimumVotes = null; }
        if (YearFrom is < 1800 or > 2100 || YearTo is < 1800 or > 2100 || YearFrom > YearTo) throw new ArgumentException("年份无效：起始年份不能晚于结束年份");
        if (MinimumRating is < 0 or > 10 || MinimumRating is double score && !double.IsFinite(score)) throw new ArgumentException("最低评分必须在0到10之间");
        if (MinimumVotes < 0) throw new ArgumentException("最低评价人数不能小于0");
        if (PageSize is < 1 or > 100) throw new ArgumentException("分页大小无效");
    }
    public string CacheKey(int? page = null)
    {
        var stable = JsonSerializer.Serialize(new { Schema = 5, Scope, ScopeId, Category, Provider, Keyword = Keyword.Trim().Normalize().ToLowerInvariant(), Kind,
            YearFrom, YearTo, IncludeUnknownYear, Countries = Countries.Distinct().Order().ToArray(), Genres = Genres.Distinct().Order().ToArray(), AllGenres,
            RatingProvider, MinimumRating, MinimumVotes, Sort, SortAscending, SortDirection, ColumnSort, Status, AnimeForm, Collection, PageSize, Page = page });
        if (PersonId > 0) stable += "|person:" + PersonId;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stable))).ToLowerInvariant();
    }
    public string LegacyCacheKey(int? page = null)
    {
        var stable = JsonSerializer.Serialize(new { Schema = 5, Scope, ScopeId, Category, Provider, Keyword = Keyword.Trim().Normalize().ToLowerInvariant(), Kind,
            YearFrom, YearTo, IncludeUnknownYear, Countries = Countries.Distinct().Order().ToArray(), Genres = Genres.Distinct().Order().ToArray(), AllGenres,
            RatingProvider, MinimumRating, MinimumVotes, Sort, SortAscending, Status, AnimeForm, Collection, PageSize, Page = page });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stable))).ToLowerInvariant();
    }
    public IEnumerable<string> Kinds => Kind is "movie" or "tv" ? new[] { Kind } : Category switch
    { "movie" => new[] { "movie" }, "tv" or "variety" => new[] { "tv" }, _ => new[] { "movie", "tv" } };
}
public class CatalogQueryState
{
    public CatalogQuery Query { get; set; } = new();
    public List<CatalogItem> Candidates { get; set; } = new();
    public List<CatalogItem> Items { get; set; } = new();
    public Dictionary<string, int> NextPages { get; set; } = new();
    public List<string> Exhausted { get; set; } = new();
    public int ScannedPages { get; set; }
    public double Offset { get; set; }
    public string ScopeNote { get; set; } = "";
    public bool IsStale { get; set; }
    public bool HasMore { get; set; } = true;
    public bool MetadataMissing { get; set; }
    public ResultCompleteness Completeness { get; set; } = ResultCompleteness.Partial;
    public void ApplyMetadata(CatalogItem full)
    {
        // Restored JSON has distinct objects in Candidates and Items. Both must
        // receive enrichment before filtering re-materializes the visible list.
        foreach (var work in Candidates.Concat(Items).Where(w => w.WorkKey == full.WorkKey)) work.ApplyMetadata(full);
    }
}
public record CatalogProviderPage(List<CatalogItem> Items, bool HasMore, int? Total, string ScopeNote, bool IsStale = false);
public record CatalogNavigation(string Category, string Scope = "category", string Keyword = "", CatalogQuery Preset = null);
public record CatalogPreset(string Name, CatalogQuery Query, bool Pinned);
