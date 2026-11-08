using TVBoxForWindows.Core;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Engine;

/// <summary>One query state per condition set. No UI callback from a cancelled generation is accepted.</summary>
public sealed class CatalogQueryCoordinator(ICatalogQueryProvider provider = null, MetadataStore store = null)
{
    readonly ICatalogQueryProvider _provider = provider ?? new CatalogQueryProvider();
    readonly Dictionary<string, CatalogQueryState> _session = new();
    readonly Dictionary<string, CatalogQuery> _views = new();
    readonly Dictionary<string, CatalogQuery> _scopes = new();
    MetadataStore Store => store ?? MetadataStore.Instance;
    public CatalogQuery LastView(CatalogQuery fallback) => _views.TryGetValue(ViewKey(fallback), out var saved) ? saved.Copy() : Store.Read<CatalogQuery>("view-scope", ViewKey(fallback))?.Value ?? fallback;
    public void RememberView(CatalogQuery query) { _views[ViewKey(query)] = query.Copy(); Store.Write("view-scope", ViewKey(query), query); }
    static string ViewKey(CatalogQuery query) => query.Scope + ":" + query.ScopeId + ":" + query.Provider;
    public CatalogQuery LastQuery(string scope, string category, CatalogQuery fallback = null) => _scopes.TryGetValue(scope + ":" + category, out var saved) ? saved.Copy() : Store.Read<CatalogQuery>("scope", scope + ":" + category)?.Value ?? fallback ??
        new() { Scope = scope, ScopeId = category, Category = category, Provider = "douban", RatingProvider = "douban", SortDirection = "default" };
    public CatalogQueryState Restore(CatalogQuery query)
    {
        query.Validate(); if (_session.TryGetValue(query.CacheKey(), out var warm)) return warm;
        var cached = Store.Read<CatalogQueryState>("query", query.CacheKey());
        if (cached == null && query.SortDirection == null && query.ColumnSort.Length == 0)
            cached = Store.Read<CatalogQueryState>("query", query.LegacyCacheKey());
        if (cached == null) return new() { Query = query.Copy() };
        cached.Value.IsStale |= !cached.IsFresh; _session[query.CacheKey()] = cached.Value; return cached.Value;
    }
    public void Save(CatalogQueryState state)
    {
        var key = state.Query.CacheKey();
        if (_session.Count >= 16 && !_session.ContainsKey(key)) _session.Remove(_session.Keys.First());
        _session[key] = state; _scopes[state.Query.Scope + ":" + state.Query.ScopeId] = state.Query.Copy();
        _views[ViewKey(state.Query)] = state.Query.Copy();
        Store.WriteBatch(("query", key, state, TimeSpan.FromHours(12)),
            ("scope", state.Query.Scope + ":" + state.Query.ScopeId, state.Query, null),
            ("view-scope", ViewKey(state.Query), state.Query, null));
    }
    public async Task<CatalogQueryState> LoadNextAsync(CatalogQueryState previous, Func<CatalogItem, CancellationToken, Task<CatalogItem>> enrich, CancellationToken ct, Action<CatalogQueryState> progress = null)
    {
        var state = JsonUtil.Deserialize<CatalogQueryState>(JsonUtil.Serialize(previous));
        var query = state.Query; query.Validate();
        var countries = string.IsNullOrEmpty(query.Keyword) && query.Countries.Count > 0 && !query.Countries.Contains("OTHER") ? query.Countries : new List<string> { "" };
        var kinds = query.Provider == "douban" && query.Keyword.Trim().Length > 0 ? new[] { query.Kinds.First() } : query.Kinds.ToArray();
        var shards = kinds.SelectMany(kind => countries.Select(country => (Key: kind + ":" + country, Kind: kind, Country: country))).ToList();
        if(query.Provider=="anilist")shards=new(){("anime:","tv","")};
        if (query.Collection == "douban:top250") shards = new() { ("movie:", "movie", "") };
        var before = CatalogFilters.Apply(state.Candidates, query).Count;
        using var parallel = new SemaphoreSlim(3);
        for (var request = 0; request < 5; request++)
        {
            ct.ThrowIfCancellationRequested();
            var available = shards.Where(s => !state.Exhausted.Contains(s.Key)).OrderBy(s => state.NextPages.GetValueOrDefault(s.Key, 1)).ToList();
            if (available.Count == 0) break;
            var shard = available[0]; var page = state.NextPages.GetValueOrDefault(shard.Key, 1);
            var result = await _provider.FetchAsync(query, shard.Kind, page, shard.Country, ct);
            ct.ThrowIfCancellationRequested();
            var items = await Task.WhenAll(result.Items.Select(async work =>
            {
                var cached = Store.Read<CatalogItem>("work-detail", work.WorkKey);
                if (cached?.IsFresh == true)
                {
                    var full = cached.Value.Copy(); full.Popularity = work.Popularity;
                    full.MatchedTerms = full.MatchedTerms.Concat(work.MatchedTerms).Distinct().ToList();
                    var latest = work.Ratings.FirstOrDefault(r => r.Provider == work.Provider);
                    if (latest != null) { full.Ratings.RemoveAll(r => r.Provider == latest.Provider); full.Ratings.Add(latest); }
                    work = full;
                }
                var needed = query.Countries.Count > 0 && work.Countries.Count == 0 || query.Status.Length > 0 || query.AnimeForm == "ova" ||
                    (query.MinimumRating != null || query.MinimumVotes != null || query.EffectiveSort is "rating" or "votes") && query.RatingProvider != work.Provider && (query.RatingProvider == "douban" || TmdbCredentials.HasCredential);
                if (!needed || enrich == null) return work;
                await parallel.WaitAsync(ct);
                try { return await enrich(work, ct); }
                catch (OperationCanceledException) { throw; }
                catch { return work; }
                finally { parallel.Release(); }
            }));
            ct.ThrowIfCancellationRequested();
            foreach (var work in items)
            {
                state.Candidates.RemoveAll(w => w.WorkKey == work.WorkKey); state.Candidates.Add(work);
            }
            state.NextPages[shard.Key] = page + 1; state.ScannedPages++;
            if (!result.HasMore) state.Exhausted.Add(shard.Key);
            state.ScopeNote = result.ScopeNote; state.IsStale |= result.IsStale;
            state.Items = CatalogFilters.Apply(state.Candidates, query);
            progress?.Invoke(state);
            if (state.Items.Count - before >= query.PageSize) break;
        }
        ct.ThrowIfCancellationRequested();
        state.Items = CatalogFilters.Apply(state.Candidates, query);
        state.MetadataMissing = state.Candidates.Any(w => w.Year.Length == 0 || w.Countries.Count == 0 || w.Genres.Count == 0 || !w.GetRating(query.RatingProvider).HasScore);
        state.HasMore = shards.Any(s => !state.Exhausted.Contains(s.Key));
        state.Completeness = state.HasMore ? ResultCompleteness.Partial : ResultCompleteness.Complete;
        if (query.Provider == "tmdb" && query.RatingProvider != "tmdb" && (query.EffectiveSort is "rating" or "votes" || query.MinimumRating != null)) state.ScopeNote += "；外部评分未关联完整，筛选和排序仅覆盖已获得的对应评分";
        if (query.RatingProvider == "imdb" && (query.MinimumRating != null || query.MinimumVotes != null)) state.ScopeNote += "；尚无可靠IMDb评分数据";
        Save(state); return state;
    }
    public async Task<CatalogQueryState> RefreshHeadAsync(CatalogQueryState previous,CancellationToken ct)
    {
        var state=JsonUtil.Deserialize<CatalogQueryState>(JsonUtil.Serialize(previous));var query=state.Query;
        var kinds=query.Provider=="anilist"?new[]{"tv"}:query.Provider=="douban"&&query.Keyword.Length>0?new[]{query.Kinds.First()}:query.Kinds;
        var countries=query.Countries.Count>0&&!query.Countries.Contains("OTHER")?query.Countries:new List<string>{""};
        var pages=new List<CatalogProviderPage>();
        foreach(var kind in kinds)foreach(var country in countries)pages.Add(await _provider.FetchAsync(query,kind,1,country,ct));
        ct.ThrowIfCancellationRequested();
        foreach(var item in pages.SelectMany(p=>p.Items)){state.Candidates.RemoveAll(w=>w.WorkKey==item.WorkKey);state.Candidates.Add(item);}
        // Preserve loaded waterfall, pagination cursor and scroll position while refreshing the head.
        state.Items=CatalogFilters.Apply(state.Candidates,query);state.IsStale=pages.Any(p=>p.IsStale);state.ScopeNote=string.Join("；",pages.Select(p=>p.ScopeNote).Distinct());
        Save(state);return state;
    }
    public CatalogQueryState QueryLocal(CatalogQuery query, IEnumerable<CatalogItem> collection)
    {
        query.Validate(); var candidates = collection.ToList();
        var state = new CatalogQueryState { Query = query.Copy(), Candidates = candidates, Items = CatalogFilters.Apply(candidates, query), HasMore = false,
            Completeness = ResultCompleteness.Complete, MetadataMissing = candidates.Any(w => w.Countries.Count == 0 || w.Genres.Count == 0 || !w.GetRating(query.RatingProvider).HasScore), ScopeNote = "完整本地收藏/历史集合；筛选仅用本地资料，图片可联网补齐；指定条件会排除未知者" };
        Save(state); return state;
    }
}
