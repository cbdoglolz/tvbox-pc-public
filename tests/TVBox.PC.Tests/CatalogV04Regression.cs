using System.Net;
using System.Text;
using System.Text.Json;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;

internal static class CatalogV04Regression
{
    public static async Task RunAsync(Action<bool, string> require)
    {
        var movie = new CatalogItem { Id = "4", Title = "复仇计划", OriginalTitle = "Revenge", Year = "2023", Kind = "movie", Countries = new() { "KR", "US" }, Genres = new() { "science-fiction", "crime" },
            Ratings = new() { new("douban", 8.5, 4000, DateTimeOffset.UtcNow, "fixture"), new("tmdb", 7.2, 300, DateTimeOffset.UtcNow, "fixture") } };
        var query = new CatalogQuery { Category = "movie", Keyword = "复仇", YearFrom = 2020, YearTo = 2026, Countries = new() { "JP", "KR" }, Genres = new() { "science-fiction", "comedy" }, MinimumRating = 8, MinimumVotes = 1000 };
        require(CatalogFilters.Matches(movie, query), "v04 cross-dimension AND and within-dimension OR");
        query.AllGenres = true; require(!CatalogFilters.Matches(movie, query), "v04 advanced genre-all differs from genre-any"); query.AllGenres = false;
        query.Countries = new() { "JP" }; require(!CatalogFilters.Matches(movie, query), "v04 country filter does not use spoken language"); query.Countries = new() { "KR" };
        query.RatingProvider = "tmdb"; require(!CatalogFilters.Matches(movie, query), "v04 TMDB score never substitutes for Douban score");
        query.RatingProvider = "imdb"; require(!CatalogFilters.Matches(movie, query), "v04 unavailable IMDb scores do not participate in filters"); query.RatingProvider = "douban";
        var unknown = new CatalogItem { Id = "5", Title = "无元数据", Kind = "movie" };
        require(CatalogFilters.Matches(unknown, new CatalogQuery()), "v04 missing metadata stays visible in unfiltered catalogue");
        require(!CatalogFilters.Matches(unknown, new CatalogQuery { YearFrom = 2020 }), "v04 year constraint excludes unknown by default");
        require(CatalogFilters.Matches(unknown, new CatalogQuery { YearFrom = 2020, IncludeUnknownYear = true }), "v04 unknown-year opt-in is explicit");
        require(!CatalogFilters.Matches(unknown, new CatalogQuery { MinimumRating = 0 }), "v04 unknown score is not zero");
        require(!new WorkRating("tmdb", 8, 0, null, "fixture").HasScore, "v04 zero-vote rating displays as missing");
        var anime = movie.Copy(); anime.Provider = "tmdb"; anime.Genres.Add("animation");
        require(CatalogFilters.Matches(anime, new CatalogQuery { Category = "anime" }) && !CatalogFilters.Matches(movie, new CatalogQuery { Category = "anime" }), "v04 animation crosses underlying media kinds without inventing a TMDB type");
        var tv = anime.Copy(); tv.Kind = "tv";
        require(CatalogFilters.Apply(new[] { anime, anime.Copy(), tv }, new CatalogQuery { Category = "anime" }).Count == 2, "v04 same provider ID in movie and TV remains distinct");
        var db1 = query.CacheKey(); var reordered = query.Copy(); reordered.Countries.Reverse(); reordered.Genres.Reverse();
        require(db1 == reordered.CacheKey(), "v04 cache key stable across multiselect order");
        reordered.Sort = "latest"; require(db1 != reordered.CacheKey() && query.CacheKey(1) != query.CacheKey(2), "v04 cache key includes sorting and page");
        var cleared = query.ClearFilters(); require(cleared.Keyword == "复仇" && cleared.Countries.Count == 0 && query.Countries.Count == 1, "v04 clear conditions retains keyword and leaves prior state intact");
        var invalid = false; try { new CatalogQuery { YearFrom = 2026, YearTo = 2020 }.Validate(); } catch (ArgumentException) { invalid = true; }
        require(invalid, "v04 invalid year interval is rejected before querying");
        var request = CatalogRequestBuilder.Tmdb(query, "movie", 2, "KR");
        require(request.Route == "search/movie" && request.Parameters["query"] == "复仇" && !request.Parameters.ContainsKey("with_genres"), "v04 keyword search is not disguised as discover");
        var discovery = query.Copy(); discovery.Keyword = ""; discovery.RatingProvider = "tmdb";
        request = CatalogRequestBuilder.Tmdb(discovery, "tv", 3, "KR");
        require(request.Route == "discover/tv" && request.Parameters.ContainsKey("first_air_date.gte") && !request.Parameters.ContainsKey("air_date.gte"), "v04 television year uses first air date rather than newest episode");
        require(request.Parameters["vote_average.gte"] == "8" && request.Parameters["with_origin_country"] == "KR", "v04 native server constraints include proper score provider and country");
        request = CatalogRequestBuilder.Tmdb(discovery, "movie", 1, "US"); require(request.Parameters["with_genres"].Contains('|'), "v04 OR genres use TMDB pipe syntax");
        discovery.AllGenres = true; request = CatalogRequestBuilder.Tmdb(discovery, "movie", 1, "US"); require(request.Parameters["with_genres"].Contains(','), "v04 advanced AND genres use comma syntax");
        var parsed = CatalogMetadataParser.DoubanSearch("{\"items\":[{\"target_type\":\"movie\",\"target\":{\"id\":\"4\",\"title\":\"复仇\",\"year\":\"2023\",\"card_subtitle\":\"韩国 / 科幻 犯罪\",\"rating\":{\"value\":8.5,\"count\":4000}}},{\"target_type\":\"book\",\"target\":{\"id\":\"6\"}}]}", "复仇");
        require(parsed.Count == 1 && parsed[0].Countries.Contains("KR") && parsed[0].Genres.Contains("science-fiction"), "v04 Douban search unwraps verified works and structured country/genre fields");
        using var json = JsonDocument.Parse("{\"id\":4,\"title\":\"电影\",\"release_date\":\"2023-05-01\",\"genre_ids\":[878],\"vote_average\":7.2,\"vote_count\":300}");
        var tmdb = CatalogMetadataParser.TmdbItem(json.RootElement, "movie");
        require(tmdb.WorkKey == "tmdb@movie:4" && tmdb.Rating == null && tmdb.GetRating("tmdb").Score == 7.2, "v04 TMDB identity and rating stay namespaced");
        require(CatalogVocabulary.Country("中国香港") == "HK" && CatalogVocabulary.Country("中国台湾") == "TW" && CatalogVocabulary.Country("美国") == "US", "v04 source country mapping retains region distinctions");
        require(!OptionalMetadataHint.ShouldShow(false, false, false, false) && OptionalMetadataHint.ShouldShow(false, true, false, false), "v04 optional-key hint is never triggered by ordinary startup/playback");
        require(!OptionalMetadataHint.ShouldShow(false, true, true, false) && !OptionalMetadataHint.ShouldShow(false, true, false, true) && !OptionalMetadataHint.ShouldShow(true, true, false, false), "v04 seen or dismissed optional hint stays quiet across pages");
        if (OperatingSystem.IsWindows())
        {
            const string personalFixtureKey = "0123456789abcdef0123456789abcdef";
            TmdbCredentials.Save(personalFixtureKey);
            require(TmdbCredentials.Read() == personalFixtureKey && !Encoding.UTF8.GetString(File.ReadAllBytes(TmdbCredentials.CredentialPath)).Contains(personalFixtureKey), "v04 Windows encrypted credential round-trip without plaintext at rest");
            TmdbCredentials.Clear(); require(!TmdbCredentials.HasCredential, "v04 key can be removed without affecting ordinary settings");
        }
        var store = new MetadataStore(Path.Combine(AppPaths.Root, "test-metadata.sqlite"));
        store.Write("scope", "fixture", query); store.Write("work", movie.WorkKey, movie, TimeSpan.FromSeconds(-1)); store.Write("preset", "my-filter", query);
        var reopened = new MetadataStore(Path.Combine(AppPaths.Root, "test-metadata.sqlite"));
        require(reopened.Read<CatalogQuery>("scope", "fixture")?.Value.Keyword == "复仇", "v04 query filters survive database reopen");
        require(reopened.Read<CatalogItem>("work", movie.WorkKey) is { IsFresh: false, Value: not null }, "v04 expired metadata remains available as explicitly stale fallback");
        reopened.ClearCache(); require(reopened.Read<CatalogQuery>("preset", "my-filter") != null && reopened.Read<CatalogQuery>("scope", "fixture") != null && reopened.Read<CatalogItem>("work", movie.WorkKey) == null, "v04 clearing metadata cache preserves presets and saved filters");
        var fake = new PageProvider(movie);
        var coordinator = new CatalogQueryCoordinator(fake, store);
        var state = await coordinator.LoadNextAsync(new() { Query = query }, null, default);
        require(state.ScannedPages == 2 && state.Items.Count == 1 && state.Completeness == ResultCompleteness.Complete, "v04 filtering advances beyond first page instead of pretending twenty cards are the whole catalogue");
        require(coordinator.Restore(query).Items.Count == 1, "v04 paginated result state survives restore");
        using var cancel = new CancellationTokenSource(2); fake.Delay = 30;
        var cancelledQuery = query.Copy(); cancelledQuery.Keyword = "cancelled"; var cancelled = false;
        try { await coordinator.LoadNextAsync(new() { Query = cancelledQuery }, null, cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
        require(cancelled && store.Read<CatalogQueryState>("query", cancelledQuery.CacheKey()) == null, "v04 late provider result cannot overwrite state after query cancellation");
        var handler = new RecordingHandler(); using var client = new HttpClient(handler);
        var transport = new MetadataHttp(client, () => ""); var unconfigured = false;
        try { await transport.GetAsync("tmdb", "search/movie", new() { ["query"] = "test" }, default); } catch (MetadataServiceException e) { unconfigured = e.Code == "unconfigured"; }
        require(unconfigured && handler.Count == 0, "v04 missing optional key does not send unauthenticated TMDB requests");
        transport = new MetadataHttp(client, () => "0123456789abcdef0123456789abcdef"); handler.Status = HttpStatusCode.Unauthorized; var safe = false;
        try { await transport.GetAsync("tmdb", "search/movie", new() { ["query"] = "test" }, default); } catch (MetadataServiceException e) { safe = e.Code == "unauthorized" && !e.Message.Contains("012345") && !e.Message.Contains("api_key"); }
        require(safe && handler.Count == 1, "v04 401 is not retried and error messages cannot leak the key");
        handler.Status = HttpStatusCode.OK;
        await transport.GetAsync("douban", "movie/4", new(), default);
        require(!handler.LastUrl.Contains("api_key") && handler.LastAuthorization == "", "v04 TMDB credentials never travel to Douban or video sources");
        var links = tmdb.Copy(); links.ExternalIds.Imdb = "tt1234567"; links.ExternalIds.Douban = "1292052";
        require(CatalogIdentity.ExternalLink(links, "tmdb") == "https://www.themoviedb.org/movie/4" && CatalogIdentity.ExternalLink(links, "imdb") == "https://www.imdb.com/title/tt1234567/", "v04 external links use provider-native type and ID");
        links.ExternalIds.Imdb = "https://example.com"; require(CatalogIdentity.ExternalLink(links, "imdb") == null, "v04 invalid external identifiers never become clickable links");
        require(!CatalogIdentity.SameWork(movie, tmdb), "v04 equal numeric IDs across providers do not bind unrelated titles");
        var season2 = new CatalogItem { Id = "88", Provider = "douban", Title = "示例 第二季", Kind = "tv", Year = "2022" };
        var whole = new CatalogItem { Id = "88", Provider = "tmdb", Title = "示例", Kind = "tv", Year = "2020", Seasons = new() { new(1, "第一季", 2020, 10), new(2, "第二季", 2022, 8) } };
        require(CatalogIdentity.ReliablePair(season2, whole), "v04 split seasons match TMDB season year instead of series first-air year");
        whole.Seasons.RemoveAt(1); require(!CatalogIdentity.ReliablePair(season2, whole), "v04 missing season evidence prevents automatic association");
        var season1 = season2.Copy(); season1.SelectedSeason = 1; season2.SelectedSeason = 2;
        require(!CatalogIdentity.SameWork(season1, season2), "v04 distinct seasons never share playback progress");
        require(CatalogIdentity.PlaybackNames(season2).All(n => TitleMatchRules.Normalize(n).Contains("第2季")), "v04 source search aliases preserve the requested season");
        var saved = new Keep { Cid = 801, Key = "douban@4", CatalogId = "4", CatalogProvider = "douban", CatalogKind = "movie", VodName = movie.Title };
        Stores.SaveKeep(saved); var enriched = movie.Copy(); enriched.IdentityKey = saved.Key; enriched.ExternalIds.Tmdb = 42;
        CatalogLibrary.UpdateSavedMetadata(enriched, 801);
        var kept = Stores.GetKeeps(801).Single();
        require(kept.Key == saved.Key && kept.CatalogId == "4" && kept.CatalogProvider == "douban" && CatalogLibrary.FromKeep(kept).ExternalIds.Tmdb == 42, "v04 metadata enrichment preserves original favorite key and provider identity");
        var oldUnknown = new Keep { Key = "legacy@site", VodName = "旧收藏", CatalogKind = "", CatalogId = "" };
        require(CatalogFilters.Matches(CatalogLibrary.FromKeep(oldUnknown), new CatalogQuery { Category = "all" }), "v04 old resource favorites without catalogue metadata remain visible offline");
        require(coordinator.QueryLocal(new CatalogQuery { Category = "all" }, new[] { movie, unknown }).Completeness == ResultCompleteness.Complete, "v04 local favorites query covers all saved candidates without network paging");
        OptionalMetadataHint.DismissForever(); Setting.Load(); require(!OptionalMetadataHint.TryMarkShown(true), "v04 do-not-remind choice remains silent after settings reload");
        var throttled = new RecordingHandler { Status = HttpStatusCode.TooManyRequests }; using var throttleClient = new HttpClient(throttled);
        var throttledHttp = new MetadataHttp(throttleClient, () => "0123456789abcdef0123456789abcdef"); var backedOff = false;
        try { await throttledHttp.GetAsync("tmdb", "configuration", new(), default); } catch (MetadataServiceException e) { backedOff = e.Code == "backoff"; }
        require(backedOff && throttled.Count == 3, "v04 rate limit uses bounded retries then stops");
        try { await throttledHttp.GetAsync("tmdb", "configuration", new(), default); } catch (MetadataServiceException) { }
        require(throttled.Count == 3, "v04 retrying during cooldown cannot hammer a throttled provider");
    }
    sealed class PageProvider(CatalogItem match) : ICatalogQueryProvider
    {
        public int Delay;
        public async Task<CatalogProviderPage> FetchAsync(CatalogQuery q, string kind, int page, string country, CancellationToken ct)
        {
            if (Delay > 0) await Task.Delay(Delay);
            var work = match.Copy(); if (page == 1) { work.Id = "wrong"; work.Countries = new() { "JP" }; }
            return new(new() { work }, page < 2, 2, "fixture complete collection");
        }
        public Task<CatalogItem> DetailsAsync(CatalogItem work, CancellationToken ct) => Task.FromResult(work);
    }
    sealed class RecordingHandler : HttpMessageHandler
    {
        public int Count; public string LastUrl = "", LastAuthorization = ""; public HttpStatusCode Status = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++; LastUrl = request.RequestUri.AbsoluteUri; LastAuthorization = request.Headers.Authorization?.ToString() ?? "";
            var response = new HttpResponseMessage(Status) { Content = new StringContent("{}") };
            if (Status == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        }
    }
}
