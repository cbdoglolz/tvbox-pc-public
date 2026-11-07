using System.Text.Json;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;

internal static class CatalogV06Regression
{
    public static void Run(Action<bool, string> require)
    {
        var preferences = new[] { "ui_theme", "config_vod", "home_provider", "home_channel_douban" }.ToDictionary(k => k, k => Setting.GetString(k));
        var q = new CatalogQuery { Sort = "name", SortDirection = "default", Countries = new() { "US", "GB" }, MinimumRating = 7 };
        var filters = q.Copy();
        require(q.EffectiveSort == "popularity" && !q.EffectiveAscending, "v06 default uses column ordering rather than last chosen field");
        q.CycleDirection(); require(q.SortDirection == "asc" && q.EffectiveSort == "name" && q.EffectiveAscending, "v06 default to ascending uses chosen field");
        q.CycleDirection(); require(q.SortDirection == "desc" && !q.EffectiveAscending, "v06 ascending to descending");
        q.CycleDirection(); require(q.SortDirection == "default" && q.EffectiveSort == "popularity", "v06 descending returns to column default");
        require(q.Countries.SequenceEqual(filters.Countries) && q.MinimumRating == 7, "v06 direction cycle never clears conditions");
        foreach (var key in new[] { "douban:top250", "netflix:top-rated", "douban:new-movie", "netflix:new-tv", "douban:anime" })
        {
            q.Collection = key;
            require(q.EffectiveSort == (key.Contains("top") ? "rating" : "latest"), "v06 restores default for " + key);
        }
        q.ColumnSort = "latest"; q.Collection = "";
        require(q.EffectiveSort == "latest", "v06 channel default independent from selected sort field");
        var legacyAsc = JsonSerializer.Deserialize<CatalogQuery>("{\"Sort\":\"year\",\"SortAscending\":true}");
        var legacyDesc = JsonSerializer.Deserialize<CatalogQuery>("{\"Sort\":\"year\",\"SortAscending\":false}");
        require(legacyAsc.SortDirection == null && legacyAsc.EffectiveAscending && legacyAsc.EffectiveSort == "year", "v06 legacy ascending preset remains ascending");
        require(!legacyDesc.EffectiveAscending && legacyDesc.EffectiveSort == "year", "v06 legacy descending preset remains descending");
        q.SortDirection = "asc"; var ascendingKey = q.CacheKey(); q.SortDirection = "desc";
        require(ascendingKey != q.CacheKey(), "v06 opposite directions cannot share result cache");
        q.SortDirection = "default";
        require(q.CacheKey() != ascendingKey, "v06 default has independent state and offset");
        q.Sort = "rating"; var cleared = q.ClearFilters();
        require(cleared.Countries.Count == 0 && cleared.MinimumRating == null && cleared.Sort == "rating" && cleared.SortDirection == "default" && cleared.ColumnSort == "latest", "v06 clear conditions preserves sorting and column");
        var rows = new[] { new CatalogItem { Id = "601", Kind = "movie", Title = "Older", Year = "2000", Popularity = 10 }, new CatalogItem { Id = "602", Kind = "movie", Title = "Newer", Year = "2025", Popularity = 1 } };
        q = new() { Sort = "name", SortDirection = "default", ColumnSort = "latest" };
        require(CatalogFilters.Apply(rows, q).First().Id == "602", "v06 default date order is used by local filters");
        var remote = CatalogRequestBuilder.Tmdb(q, "movie", 1);
        require(remote.Parameters["sort_by"] == "primary_release_date.desc", "v06 remote and local default sort agree");
        q.Sort = "year"; q.SortDirection = "asc";
        require(CatalogRequestBuilder.Tmdb(q, "movie", 1).Parameters["sort_by"].EndsWith(".asc"), "v06 remote direction follows cycle state");
        var store = new MetadataStore(Path.Combine(AppPaths.Root, "v06-state.sqlite")); var coordinator = new CatalogQueryCoordinator(store: store);
        coordinator.Save(new() { Query = q, Candidates = rows.ToList(), Items = rows.ToList(), Offset = 2345, ScannedPages = 3 });
        var state = coordinator.Restore(q);
        require(state.Offset == 2345 && state.ScannedPages == 3 && state.Query.SortDirection == "asc", "v06 page state retains offset pages and direction");
        store.Write("query", legacyAsc.LegacyCacheKey(), new CatalogQueryState { Query = legacyAsc, Items = rows.ToList(), Candidates = rows.ToList(), Offset = 6789, ScannedPages = 4 });
        require(coordinator.Restore(legacyAsc).Offset == 6789 && coordinator.Restore(legacyAsc).Items.Count == 2, "v06 upgrade reads legacy waterfall cache without resetting list or offset");
        var savedState = new CatalogQueryState { Query = new() { MinimumRating = 7, RatingProvider = "tmdb" }, Candidates = new() { rows[0].Copy() }, Items = new() { rows[0].Copy() } };
        var enriched = rows[0].Copy(); enriched.Ratings.Add(new("tmdb", 8.2, 100, null, "fixture")); enriched.Overview = "enriched overview";
        savedState.ApplyMetadata(enriched); savedState.Items = CatalogFilters.Apply(savedState.Candidates, savedState.Query);
        require(savedState.Items.Count == 1 && savedState.Items[0].TmdbScore == "TMDB 8.2" && savedState.Items[0].Overview == "enriched overview", "v06 re-filtering restored candidates cannot erase hydrated scores or overview");
        var doubanView = new CatalogQuery { ScopeId = "movie:latest", Provider = "douban", YearFrom = 2020, SortDirection = "asc" };
        var tmdbView = new CatalogQuery { ScopeId = "movie:latest", Provider = "tmdb", YearFrom = 2010, SortDirection = "desc" };
        coordinator.RememberView(doubanView); coordinator.RememberView(tmdbView);
        require(coordinator.LastView(new() { ScopeId = "movie:latest", Provider = "douban" }).YearFrom == 2020, "v06 Douban channel conditions remain isolated from TMDB");
        require(coordinator.LastView(new() { ScopeId = "movie:latest", Provider = "tmdb" }).SortDirection == "desc", "v06 TMDB channel restores its own direction");
        Setting.Remove("ui_theme"); Setting.Remove("home_provider"); Setting.Remove("home_channel_douban"); Setting.Remove("config_vod"); Setting.InitializeAppearance();
        require(Setting.GetString("ui_theme") == "dark", "v06 new profile starts dark");
        Setting.Remove("ui_theme"); Setting.ConfigVod = "assets://catalog/default.json"; Setting.InitializeAppearance();
        require(Setting.GetString("ui_theme") == "brand", "v06 existing configuration keeps brand theme on upgrade");
        Setting.Put("ui_theme", "light"); Setting.InitializeAppearance();
        require(Setting.GetString("ui_theme") == "light", "v06 explicit appearance choice survives initialization");
        foreach (var pair in preferences) { if (pair.Value.Length == 0) Setting.Remove(pair.Key); else Setting.Put(pair.Key, pair.Value); }
    }
}
