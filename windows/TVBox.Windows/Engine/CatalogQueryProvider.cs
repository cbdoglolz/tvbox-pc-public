using System.Globalization;
using System.Text.Json;
using TVBoxForWindows.Core;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Engine;

public record CatalogRequest(string Route, Dictionary<string, string> Parameters);
public static class CatalogRequestBuilder
{
    public static CatalogRequest Tmdb(CatalogQuery q, string kind, int page, string country = "")
    {
        var args = new Dictionary<string, string> { ["language"] = "zh-CN", ["include_adult"] = "false", ["page"] = page.ToString() };
        if (!string.IsNullOrWhiteSpace(q.Keyword) && q.CompanyId == 0)
        {
            args["query"] = q.Keyword.Trim();
            return new("search/" + kind, args);
        }
        if (q.CompanyId > 0) args[q.CompanyIsNetwork && kind == "tv" ? "with_networks" : "with_companies"] = q.CompanyId.ToString();
        args["sort_by"] = q.EffectiveSort switch { "latest" => kind == "movie" ? "primary_release_date.desc" : "first_air_date.desc", "rating" when q.RatingProvider == "tmdb" => "vote_average.desc", "votes" when q.RatingProvider == "tmdb" => "vote_count.desc", "name" => kind == "movie" ? "title.asc" : "name.asc", _ => "popularity.desc" };
        if (q.EffectiveSort == "year") args["sort_by"] = kind == "movie" ? "primary_release_date.desc" : "first_air_date.desc";
        args["sort_by"] = args["sort_by"].Replace(".asc", ".desc");
        if (q.EffectiveAscending) args["sort_by"] = args["sort_by"].Replace(".desc", ".asc");
        if (!q.IncludeUnknownYear && q.YearFrom != null) args[kind == "movie" ? "primary_release_date.gte" : "first_air_date.gte"] = $"{q.YearFrom}-01-01";
        if (!q.IncludeUnknownYear && q.YearTo != null) args[kind == "movie" ? "primary_release_date.lte" : "first_air_date.lte"] = $"{q.YearTo}-12-31";
        if (kind == "tv" && country.Length > 0 && country != "OTHER") args["with_origin_country"] = country;
        var baseGenre = q.Category switch { "anime" => "animation", "documentary" => "documentary", "variety" => "variety", _ => "" };
        var genres = baseGenre.Length > 0 ? CatalogVocabulary.TmdbGenre(baseGenre, kind) : q.Genres.SelectMany(g => CatalogVocabulary.TmdbGenre(g, kind)).Distinct().ToArray();
        if (genres.Length > 0) args["with_genres"] = string.Join(q.AllGenres ? "," : "|", genres);
        if (q.RatingProvider == "tmdb")
        {
            if (q.MinimumRating != null) args["vote_average.gte"] = q.MinimumRating.Value.ToString(CultureInfo.InvariantCulture);
            if (q.MinimumVotes != null) args["vote_count.gte"] = q.MinimumVotes.Value.ToString(CultureInfo.InvariantCulture);
        }
        if (kind == "tv" && q.Status == "Ended") args["with_status"] = "3";
        return new("discover/" + kind, args);
    }
    public static CatalogRequest Douban(CatalogQuery q, string kind, int page, string country = "")
    {
        var args = new Dictionary<string, string> { ["start"] = ((page - 1) * 20).ToString(), ["count"] = "20" };
        if (q.Keyword.Trim().Length > 0) { args["q"] = q.Keyword.Trim(); return new("search/movie", args); }
        if (q.Collection is "douban:hot-tv" or "douban:hot-movie" && q.EffectiveSort == "popularity" && !q.EffectiveAscending && q.Countries.Count == 0 && q.Genres.Count == 0 && q.YearFrom == null && q.YearTo == null && q.MinimumRating == null && q.MinimumVotes == null)
        { args["limit"] = "20"; args["category"] = kind == "tv" ? "tv" : "热门"; args["type"] = kind == "tv" ? "tv" : "全部"; return new("subject/recent_hot/" + kind, args); }
        if (q.Collection == "douban:top250") return new("subject_collection/movie_top250/items", args);
        var tags = new List<string>();
        if (q.Collection.StartsWith("netflix:", StringComparison.Ordinal)) tags.Add("Netflix");
        if (country.Length > 0 && country != "OTHER") tags.Add(CatalogVocabulary.Label(country));
        var baseGenre = q.Category switch { "anime" => "animation", "documentary" => "documentary", "variety" => "variety", _ => "" };
        if (baseGenre.Length > 0) tags.Add(CatalogVocabulary.Label(baseGenre));
        // User genres are always verified locally; optional remote tags only narrow a safe single genre.
        if (q.Genres.Count == 1) tags.Add(CatalogVocabulary.Label(q.Genres[0]));
        if (tags.Count == 0) tags.Add(kind == "movie" ? "电影" : "电视剧");
        args["tags"] = string.Join(',', tags);
        args["sort"] = q.EffectiveSort == "rating" && q.RatingProvider == "douban" ? "S" : q.EffectiveSort == "latest" ? "R" : "U";
        if (q.MinimumRating != null && q.RatingProvider == "douban") args["score_range"] = q.MinimumRating.Value.ToString(CultureInfo.InvariantCulture) + ",10";
        return new(kind + "/recommend", args);
    }
}

public interface ICatalogQueryProvider
{
    Task<CatalogProviderPage> FetchAsync(CatalogQuery query, string kind, int page, string country, CancellationToken ct);
    Task<CatalogItem> DetailsAsync(CatalogItem work, CancellationToken ct);
}
public sealed class CatalogQueryProvider(MetadataHttp http = null, MetadataStore store = null) : ICatalogQueryProvider
{
    readonly MetadataHttp _http = http ?? new();
    MetadataStore Store => store ?? MetadataStore.Instance;
    public async Task<CatalogProviderPage> FetchAsync(CatalogQuery query, string kind, int page, string country, CancellationToken ct)
    {
        if(query.Provider=="anilist")return await new AnimeCatalog(store:Store).Fetch(query,page,ct);
        if (query.PersonId > 0) return await new PersonCatalog(_http, Store).FetchAsync(query, kind, page, ct);
        var cacheKey = query.CacheKey(page) + ":" + kind + ":" + country;
        var cache = Store.Read<CatalogProviderPage>("response", cacheKey);
        if (cache?.IsFresh == true) return cache.Value;
        var request = query.Provider == "tmdb" ? CatalogRequestBuilder.Tmdb(query, kind, page, country) : CatalogRequestBuilder.Douban(query, kind, page, country);
        try
        {
            var json = await _http.GetAsync(query.Provider, request.Route, request.Parameters, ct).ConfigureAwait(false);
            List<CatalogItem> items; int? total; bool more; string note;
            using var doc = JsonDocument.Parse(json);
            if (query.Provider == "tmdb")
            {
                items = CatalogMetadataParser.Array(CatalogMetadataParser.Get(doc.RootElement, "results")).Select(n => CatalogMetadataParser.TmdbItem(n, kind)).Where(w => w != null).ToList();
                var pages = int.TryParse(CatalogMetadataParser.Text(doc.RootElement, "total_pages"), out var count) ? Math.Min(count, 500) : page;
                total = int.TryParse(CatalogMetadataParser.Text(doc.RootElement, "total_results"), out var all) ? all : null;
                more = page < pages;
                note = query.Keyword.Length > 0 ? "TMDB关键词候选逐页补全；不是已完成的全量筛选" : "TMDB服务端条件目录（API最多500页），未映射字段继续逐页验证";
                if (kind == "tv") note += "；剧集科幻/奇幻、动作/冒险为TMDB合并分类";
            }
            else
            {
                if (query.Keyword.Trim().Length > 0) items = CatalogMetadataParser.DoubanSearch(json, query.Keyword);
                else items = CatalogFeedParser.Parse(json, kind, page).Items;
                total = int.TryParse(CatalogMetadataParser.Text(doc.RootElement, "total"), out var count) ? Math.Min(count, 1000) : null;
                var raw = CatalogMetadataParser.Get(doc.RootElement, "items");
                if (raw.ValueKind != JsonValueKind.Array) raw = CatalogMetadataParser.Get(doc.RootElement, "subject_collection_items");
                var rawCount = raw.ValueKind == JsonValueKind.Array ? raw.GetArrayLength() : 0;
                more = page < 50 && rawCount > 0 && (total == null ? rawCount >= 20 : page * 20 < total);
                note = query.Keyword.Length > 0 ? "豆瓣关键词可查询集合，结果逐页补全（非豆瓣全库）" : query.Collection == "douban:top250" ? "豆瓣TOP250限定集合" : "豆瓣可查询条件集合（非豆瓣全库；数量受接口上限限制）";
                foreach (var item in items.Select((work, index) => (work, index))) item.work.Popularity = -((page - 1) * 20 + item.index);
            }
            ct.ThrowIfCancellationRequested();
            foreach (var work in items)
            {
                if (query.Keyword.Length > 0 && !work.MatchedTerms.Contains(query.Keyword)) work.MatchedTerms.Add(query.Keyword);

            }
            var output = new CatalogProviderPage(items, more, total, note);
            var writes = items.Select(work => (Kind: "work", Id: work.WorkKey, Value: (object)work, Ttl: (TimeSpan?)TimeSpan.FromDays(7))).ToList();
            writes.Add(("response", cacheKey, output, TimeSpan.FromMinutes(30))); Store.WriteBatch(writes.ToArray()); return output;
        }
        catch (OperationCanceledException) { throw; }
        catch (MetadataServiceException) when (query.Provider == "douban" && query.Keyword.Trim().Length > 0 && query.PersonId == 0 && query.Collection.Length == 0 && TmdbCredentials.HasCredential)
        {
            // Search can fail independently of home feeds. Keep the caller's filters and rating provider;
            // TMDB candidates retain their own identity and ratings rather than masquerading as Douban.
            var fallback = query.Copy(); fallback.Provider = "tmdb";
            var pages = await Task.WhenAll(query.Kinds.Select(k => FetchAsync(fallback, k, page, country, ct)));
            return new(pages.SelectMany(p => p.Items).DistinctBy(w => w.WorkKey).ToList(), pages.Any(p => p.HasMore), null,
                "豆瓣搜索暂不可用，已回退TMDB；保留原筛选，豆瓣评分未知的作品不满足豆瓣分数条件");
        }
        catch when (cache != null) { return cache.Value with { IsStale = true, ScopeNote = cache.Value.ScopeNote + "；服务暂不可用，使用过期缓存" }; }
    }
    public async Task<CatalogItem> DetailsAsync(CatalogItem work, CancellationToken ct)
    {
        if(work.Provider=="anilist")return work.Copy();
        var cache = Store.Read<CatalogItem>("work-detail", work.WorkKey);
        if (cache?.IsFresh == true && cache.Value.DetailsSchema >= 8 && (work.Provider != "tmdb" || cache.Value.ArtworkSchema >= 2 && cache.Value.PresentationSchema >= 1)) return cache.Value;
        try
        {
            CatalogItem result;
            if (work.Provider == "tmdb")
            {
                var json = await _http.GetAsync("tmdb", work.Kind + "/" + work.Id, new() { ["language"] = "zh-CN", ["append_to_response"] = "external_ids,credits,alternative_titles,images", ["include_image_language"] = "zh,en,null" }, ct);
                using var doc = JsonDocument.Parse(json); result = CatalogMetadataParser.TmdbItem(doc.RootElement, work.Kind);
                if (result?.Id != work.Id) throw new MetadataServiceException("影视详情身份不匹配", "identity");
                result.DetailsLoaded = true; result.IdentityKey = work.IdentityKey; result.SelectedSeason = work.SelectedSeason;
            }
            else result = CatalogMetadataParser.DoubanDetails(await _http.GetAsync("douban", work.Kind + "/" + work.Id, new(), ct), work);
            ct.ThrowIfCancellationRequested(); result.MatchedTerms = work.MatchedTerms;
            Store.Write("work-detail", work.WorkKey, result, TimeSpan.FromDays(3)); Store.Write("work", work.WorkKey, result, TimeSpan.FromDays(7)); return result;
        }
        catch (OperationCanceledException) { throw; }
        catch when (cache != null) { return cache.Value; }
    }
}
