using TVBoxForWindows.Core;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Engine;

public record MetadataDetailsResult(CatalogItem Work, List<CatalogItem> Candidates, string Message);
public record MetadataLink(WorkExternalIds Ids, bool Suppressed);
public sealed class MetadataDetailsService(ICatalogQueryProvider provider = null, MetadataStore store = null)
{
    readonly ICatalogQueryProvider _provider = provider ?? new CatalogQueryProvider();
    MetadataStore Store => store ?? MetadataStore.Instance;
    public async Task<CatalogItem> EnrichAsync(CatalogItem work, CancellationToken ct)
    {
        var cache = Store.Read<CatalogItem>("work-detail", work.WorkKey);
        if (cache?.IsFresh == true && cache.Value.Ratings.Any(r => r.Provider == "tmdb") && cache.Value.DetailsSchema >= 8 && cache.Value.ArtworkSchema >= 2 && cache.Value.PresentationSchema >= 1) return cache.Value.Copy();
        var result = await LoadAsync(work, ct); return result.Work;
    }
    public async Task<MetadataDetailsResult> LoadAsync(CatalogItem original, CancellationToken ct)
    {
        var cached = Store.Read<CatalogItem>("work-detail", original.WorkKey);
        if (cached?.IsFresh == true && cached.Value.DetailsSchema >= 8 && (cached.Value.PresentationSchema >= 1 || !TmdbCredentials.HasCredential || cached.Value.TmdbLinkSuppressed) && (cached.Value.ArtworkSchema >= 2 || !TmdbCredentials.HasCredential) &&
            (original.Provider != "douban" || !TmdbCredentials.HasCredential || cached.Value.TmdbLinkSuppressed || cached.Value.Ratings.Any(r => r.Provider == "tmdb")))
            return new(cached.Value.Copy(), new(), "");
        var work = original.Copy(); var messages = new List<string>(); var candidates = new List<CatalogItem>();
        if (work.Provider == "legacy" || work.Id.Length == 0)
        {
            if (work.Title.Length == 0 || work.Year.Length == 0 || work.Kind.Length == 0) return new(work, candidates, "旧记录缺少年份或类型，保留原播放方式，暂不自动关联");
            var hits = await _provider.FetchAsync(new CatalogQuery { Provider = "douban", Category = work.Kind, Keyword = work.Title }, work.Kind, 1, "", ct);
            var reliable = hits.Items.Where(c => CatalogIdentity.ReliablePair(work, c)).ToList();
            if (reliable.Count != 1) return new(work, new(), "旧记录尚无唯一可靠豆瓣资料匹配，未自动绑定；仍保留原播放方式");
            var matched = reliable[0]; matched.IdentityKey = work.WorkKey; work = matched;
        }
        try { work = await _provider.DetailsAsync(work, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { messages.Add(error is MetadataServiceException ? error.Message : "详情暂时无法刷新，保留已有资料"); }
        if (original.IdentityKey.Length > 0) work.IdentityKey = original.IdentityKey;
        var savedLink = Store.Read<MetadataLink>("mapping", work.WorkKey)?.Value;
        if (savedLink != null)
        {
            work.TmdbLinkSuppressed = savedLink.Suppressed;
            if (!savedLink.Suppressed) work.ExternalIds = savedLink.Ids;
        }
        if (work.Provider == "douban" && TmdbCredentials.HasCredential && !work.TmdbLinkSuppressed)
        {
            try
            {
                CatalogItem matched = null;
                if (work.ExternalIds.Tmdb is > 0)
                    matched = await _provider.DetailsAsync(new CatalogItem { Provider = "tmdb", Id = work.ExternalIds.Tmdb.ToString(), Kind = work.Kind }, ct);
                else
                {
                    var queryTitle = work.Title;
                    if (CatalogIdentity.Season(work) > 0) queryTitle = System.Text.RegularExpressions.Regex.Replace(queryTitle, @"第\s*[一二三四五六七八九十\d]+\s*季|Season\s*\d+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                    var hits = await _provider.FetchAsync(new CatalogQuery { Provider = "tmdb", Category = work.Kind, Keyword = queryTitle }, work.Kind, 1, "", ct);
                    foreach (var candidate in hits.Items.Where(c => CatalogIdentity.NamesOverlap(work, c)).Take(4))
                    {
                        var full = await _provider.DetailsAsync(candidate, ct);
                        if (CatalogIdentity.ReliablePair(work, full)) candidates.Add(full);
                    }
                    if (candidates.Count == 1) matched = candidates[0];
                }
                ct.ThrowIfCancellationRequested();
                if (matched != null) { work = Associate(work, matched); candidates.Clear(); }
                else if (candidates.Count > 1) messages.Add("找到多个可能的TMDB作品，请核对候选后确认关联");
                else messages.Add("尚无唯一可靠TMDB匹配，未自动绑定");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { messages.Add(error is MetadataServiceException ? error.Message : "TMDB资料暂时不可用"); }
        }
        else if (work.Provider == "tmdb" && work.ExternalIds.Douban.Length == 0)
        {
            try
            {
                var hits = await _provider.FetchAsync(new CatalogQuery { Provider = "douban", Category = work.Kind, Keyword = work.Title }, work.Kind, 1, "", ct);
                var matches = hits.Items.Where(c => CatalogIdentity.ReliablePair(work, c)).ToList();
                if (matches.Count == 1)
                {
                    var douban = await _provider.DetailsAsync(matches[0], ct); ct.ThrowIfCancellationRequested(); work.ExternalIds.Douban = douban.Id;
                    MergeRating(work, douban.GetRating("douban")); Store.Write("mapping", work.WorkKey, new MetadataLink(work.ExternalIds, false));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { messages.Add("豆瓣评分尚未可靠关联"); }
        }
        ct.ThrowIfCancellationRequested();
        work.PrimaryRatingProvider = Setting.GetString("catalog_primary_rating", "douban");
        Store.Write("work-detail", work.WorkKey, work, TimeSpan.FromDays(3)); Store.Write("work", work.WorkKey, work, TimeSpan.FromDays(7));
        return new(work, candidates, string.Join("；", messages));
    }
    public CatalogItem Associate(CatalogItem work, CatalogItem tmdb)
    {
        if (tmdb.Provider != "tmdb" || tmdb.Kind != work.Kind || tmdb.ExternalIds.Tmdb is not > 0) throw new ArgumentException("关联作品类型或标识不匹配");
        var result = work.Copy(); result.ExternalIds.Tmdb = tmdb.ExternalIds.Tmdb; result.ExternalIds.Imdb = tmdb.ExternalIds.Imdb; result.TmdbLinkSuppressed = false;
        if (tmdb.Cast.Count > 0) result.Cast = tmdb.Cast;
        if (tmdb.Crew.Count > 0) result.Crew = tmdb.Crew;
        if (result.Directors.Length == 0) result.Directors = tmdb.Directors;
        result.TitleLogo = tmdb.TitleLogo; result.TitleLogoLanguage = tmdb.TitleLogoLanguage; result.Backdrop = tmdb.Backdrop;
        result.Overview = LocalizedSynopsis.Chinese(result.Overview, tmdb.Overview);
        result.ArtworkSchema = tmdb.ArtworkSchema;
        if (tmdb.PosterLanguage == "zh" && tmdb.Poster.Length > 0) { result.PosterFallback = result.Poster; result.Poster = tmdb.Poster; result.PosterLanguage = "zh"; }
        result.DetailsSchema = 8;
        result.Companies = tmdb.Companies; result.CollectionId = tmdb.CollectionId; result.CollectionName = tmdb.CollectionName; result.PresentationSchema = tmdb.PresentationSchema;
        result.Seasons = tmdb.Seasons; MergeRating(result, tmdb.GetRating("tmdb") with { Origin = work.Kind == "tv" ? "TMDB整剧评分（非豆瓣分季评分）" : "TMDB API" });
        Store.Write("mapping", result.WorkKey, new MetadataLink(result.ExternalIds, false)); Store.Write("work-detail", result.WorkKey, result, TimeSpan.FromDays(3)); return result;
    }
    public CatalogItem Unlink(CatalogItem work)
    {
        var result = work.Copy();
        if (result.Provider == "douban") { result.ExternalIds.Tmdb = null; result.ExternalIds.Imdb = ""; result.Ratings.RemoveAll(r => r.Provider is "tmdb" or "imdb"); result.TmdbLinkSuppressed = true; result.TitleLogo = result.TitleLogoLanguage = result.Backdrop = ""; result.Cast.RemoveAll(p => p.Provider == "tmdb"); result.Crew.RemoveAll(p => p.Provider == "tmdb"); if (result.PosterLanguage == "zh" && result.PosterFallback.Length > 0) { result.Poster = result.PosterFallback; result.PosterLanguage = ""; } result.ArtworkSchema = 2; result.Companies.Clear(); result.CollectionId = null; result.CollectionName = ""; result.Seasons.Clear(); result.PresentationSchema = 1; }
        Store.Write("mapping", result.WorkKey, new MetadataLink(result.ExternalIds, true)); Store.Write("work-detail", result.WorkKey, result, TimeSpan.FromDays(3)); return result;
    }
    public async Task<List<CatalogItem>> FindMatchesAsync(CatalogItem work, string text, CancellationToken ct)
    {
        if (!TmdbCredentials.HasCredential) throw new MetadataServiceException("请先在设置中填写个人TMDB Key", "unconfigured");
        text = text.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out var supplied) && supplied.Scheme is "http" or "https") {
            if (supplied.Scheme != "https" || supplied.Host is not ("themoviedb.org" or "www.themoviedb.org")) throw new MetadataServiceException("请输入TMDB作品链接、片名或数字ID", "identity");
            text = supplied.GetLeftPart(UriPartial.Path);
        }
        var linkedType = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"^https://(?:www\.)?themoviedb\.org/(movie|tv)/");
        if (linkedType.Success && linkedType.Groups[1].Value != work.Kind) throw new MetadataServiceException("链接中的电影/剧集类型与当前作品不同，请核对", "identity");
        var id = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"^(?:https://(?:www\.)?themoviedb\.org/(?:movie|tv)/)?(\d+)(?:-[^/?#]+)?/?$");
        if (id.Success) {
            if (!int.TryParse(id.Groups[1].Value, out var number) || number <= 0) throw new MetadataServiceException("请输入有效的TMDB作品ID", "identity");
            return new() { await _provider.DetailsAsync(new CatalogItem { Provider = "tmdb", Kind = work.Kind, Id = number.ToString(System.Globalization.CultureInfo.InvariantCulture) }, ct) };
        }
        var page = await _provider.FetchAsync(new CatalogQuery { Provider = "tmdb", Category = work.Kind, Keyword = text.Trim() }, work.Kind, 1, "", ct);
        return page.Items.Where(c => c.Kind == work.Kind).Take(20).ToList();
    }
    public Task<CatalogItem> CompleteMatchAsync(CatalogItem candidate, CancellationToken ct) => _provider.DetailsAsync(candidate, ct);
    static void MergeRating(CatalogItem work, WorkRating rating) { work.Ratings.RemoveAll(r => r.Provider == rating.Provider); work.Ratings.Add(rating); }
}
