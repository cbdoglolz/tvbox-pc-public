using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;

internal static class CatalogV05Regression
{
    public static void Run(Action<bool, string> require)
    {
        var a = new CatalogItem { Id = "501", Kind = "movie", Title = "阿凡达", Year = "2020", ReleaseDate = "2020-12-20", Ratings = new() { new("douban", 9.1, 300, null, "fixture"), new("tmdb", 7.2, 200, null, "fixture") } };
        var b = new CatalogItem { Id = "502", Kind = "movie", Title = "流浪地球", Year = "2019", ReleaseDate = "2019-02-05", Ratings = new() { new("douban", 8.5, 500, null, "fixture") } };
        var unknown = new CatalogItem { Id = "503", Kind = "movie", Title = "未知" };
        var rows = new[] { a, unknown, b };
        foreach (var sort in new[] { "year", "latest", "rating", "votes", "name" })
        {
            var q = new CatalogQuery { Sort = sort, SortAscending = true };
            var asc = CatalogFilters.Apply(rows, q); q.SortAscending = false; var desc = CatalogFilters.Apply(rows, q);
            require(asc.Count == 3 && desc.Count == 3 && asc[0].Id != desc[0].Id, "v05 " + sort + " supports both sort directions");
            if (sort != "name") require(asc.Last().Id == unknown.Id && desc.Last().Id == unknown.Id, "v05 " + sort + " keeps unknown values last in both directions");
        }
        require(CatalogFilters.Apply(new[] { b, a }, new CatalogQuery { Sort = "name", SortAscending = true }).First().Id == a.Id, "v05 Chinese names sort by cultural/pinyin order");
        require(a.DoubanScore == "豆瓣 9.1" && a.TmdbScore == "TMDB 7.2" && a.ImdbScore == "IMDb -", "v05 poster strip independently labels all three ratings");
        require(unknown.DoubanScore == "豆瓣 -" && unknown.TmdbScore == "TMDB -", "v05 missing poster ratings use short hyphens");
        var copy = a.Copy(); copy.IdentityKey = "original@stable"; copy.UpdateProgress(new History { Position = 65000 }); copy.ApplyMetadata(b);
        require(copy.Id == a.Id && copy.IdentityKey == "original@stable" && copy.CanResume && copy.Title == a.Title, "v05 in-place metadata preserves card identity and progress");
        var q1 = new CatalogQuery { Sort = "rating" }; var q2 = q1.Copy(); q2.SortAscending = true;
        require(q1.CacheKey() != q2.CacheKey(), "v05 cache keys separate ascending and descending queries");
        require(CatalogRequestBuilder.Tmdb(q2, "movie", 1).Parameters["sort_by"] == "popularity.asc", "v05 provider supports explicit ascending request direction");
        var home = new CatalogQuery { Scope = "home", ScopeId = "netflix:hot-tv", Category = "tv", Collection = "netflix:hot-tv" };
        var coordinator = new CatalogQueryCoordinator(store: new MetadataStore(Path.Combine(AppPaths.Root, "home-v05-test.sqlite")));
        require(coordinator.LastQuery("home", home.ScopeId, home).Category == "tv" && coordinator.LastQuery("home", home.ScopeId, home).Collection == "netflix:hot-tv", "v05 new home queries retain channel kind and provider tag");
        require(CatalogRequestBuilder.Douban(new CatalogQuery { Collection = "douban:hot-tv", Category = "tv" }, "tv", 1).Route == "subject/recent_hot/tv", "v05 unfiltered hot channel keeps its original recommendation endpoint");
        require(!ResumeProgress.Reached(3300000, 0) && ResumeProgress.Reached(3300000, 3299500), "v05 first frames at zero cannot count as a restored long playback offset");
        require(!ResumeProgress.Restored(3300000, 3300000, true, false, false) && !ResumeProgress.Restored(3300000, 3301000, true, true, false) && !ResumeProgress.Restored(3300000, 3301000, true, false, true) && !ResumeProgress.Restored(3300000, 3301000, false, false, false) && ResumeProgress.Restored(3300000, 3301000, true, false, false), "v05 a jumped clock or stalled seek cannot masquerade as restored video");
        require(!ResumeProgress.CanCommit(true, false, true) && !ResumeProgress.CanCommit(false, false, false) && ResumeProgress.CanCommit(true, false, false), "v05 pending resume and unverified media cannot overwrite saved progress");
        coordinator.Save(new CatalogQueryState { Query = q2, Items = new() { a, b }, Candidates = new() { a, b }, Offset = 1234 });
        var restored = coordinator.Restore(q2); require(restored.Query.SortAscending && restored.Items.Count == 2 && restored.Offset == 1234, "v05 directional ordering and waterfall position survive restart storage");
    }
}
