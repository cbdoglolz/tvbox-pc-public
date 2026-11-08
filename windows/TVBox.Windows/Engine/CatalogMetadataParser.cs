using System.Text.Json;
using System.Text.RegularExpressions;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Engine;

public static class CatalogMetadataParser
{
    public static string Text(JsonElement node, string name) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number ? value.ToString() : "";
    public static JsonElement Get(JsonElement node, string name) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) ? value : default;
    public static IEnumerable<JsonElement> Array(JsonElement node) => node.ValueKind == JsonValueKind.Array ? node.EnumerateArray().ToArray() : System.Array.Empty<JsonElement>();
    static int? Int(JsonElement node, string name) => int.TryParse(Text(node, name), out var value) ? value : null;
    static double? Number(JsonElement node, string name) => Get(node, name).ValueKind == JsonValueKind.Number && Get(node, name).TryGetDouble(out var value) && double.IsFinite(value) ? value : null;
    static List<string> Strings(JsonElement node) => Array(node).Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
    public static void EnrichDoubanCard(CatalogItem work, JsonElement node)
    {
        work.Provider = "douban"; work.ExternalIds.Douban = work.Id;
        var rawType = Text(node, "type");
        if (rawType is "movie" or "tv") work.Kind = rawType;
        if (work.Year.Length == 0) work.Year = Regex.Match(Text(node, "year"), @"(?:19|20)\d{2}").Value;
        var chunks = work.Subtitle.Split('/').Select(s => s.Trim()).ToArray();
        if (work.Year.Length == 0) work.Year = Regex.Match(work.Subtitle, @"(?<!\d)(?:18|19|20)\d{2}(?!\d)").Value;
        if (work.Year.Length == 0) work.Year = Regex.Match(string.Join(" ", Strings(Get(node, "pubdate"))), @"(?:18|19|20)\d{2}").Value;
        var offset = chunks.Length > 0 && Regex.IsMatch(chunks[0], @"^\d{4}$") ? 1 : 0;
        work.RawCountries = Strings(Get(node, "countries"));
        if (work.RawCountries.Count == 0 && chunks.Length > offset) work.RawCountries = chunks[offset].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        work.Countries = work.RawCountries.Select(CatalogVocabulary.Country).Where(c => c.Length > 0).Distinct().ToList();
        work.RawGenres = Strings(Get(node, "genres"));
        if (work.RawGenres.Count == 0 && chunks.Length > offset + 1) work.RawGenres = chunks[offset + 1].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        work.Genres = CatalogVocabulary.GenreKeys(work.RawGenres);
        if (work.RawGenres.Any(g => g.Contains("真人秀") || g.Contains("脱口秀"))) work.Genres.Add("variety");
        var rating = Get(node, "rating");
        var score = Number(rating, "value"); var votes = Int(rating, "count");
        if (score is <= 0 or > 10 || votes == 0) score = null;
        work.Rating = score;
        work.Ratings.RemoveAll(r => r.Provider == "douban");
        work.Ratings.Add(new("douban", score, votes, DateTimeOffset.UtcNow, "豆瓣接口（获取时间）"));
        work.MetadataFetchedAt = DateTimeOffset.UtcNow;
        work.OriginalTitle = Text(node, "original_title"); work.Aliases = Strings(Get(node, "aka"));
        if (work.Genres.Contains("animation")) work.AnimeForm = work.Kind == "movie" ? "movie" : "series";
        if (Regex.IsMatch(work.Title, @"\bOVA\b", RegexOptions.IgnoreCase)) work.AnimeForm = "ova";
        ApplyEpisodeState(work, Text(node, "episodes_info"));
    }
    public static CatalogItem DoubanDetails(string json, CatalogItem existing)
    {
        using var doc = JsonDocument.Parse(json); var node = doc.RootElement;
        if (Text(node, "id") != existing.Id || string.IsNullOrWhiteSpace(Text(node, "title"))) throw new IOException("豆瓣详情身份不匹配");
        var work = existing.Copy();
        work.Title = Text(node, "title"); work.Year = Text(node, "year");
        EnrichDoubanCard(work, node);
        work.Overview = Text(node, "intro");
        work.Directors = string.Join(" / ", Array(Get(node, "directors")).Take(12).Select(d => Text(d, "name")));
        work.Actors = string.Join(" / ", Array(Get(node, "actors")).Take(18).Select(d => Text(d, "name")));
        work.Cast = Array(Get(node, "actors")).Take(40).Select(a => new CatalogPerson("douban", Text(a, "id"), Text(a, "name"), "", Text(a, "cover_url").Length > 0 ? Text(a, "cover_url") : Text(Get(a, "avatar"), "normal"))).ToList();
        work.DetailsSchema = 8;
        work.Crew = Array(Get(node, "directors")).Take(12).Select(d => new CatalogPerson("douban", Text(d, "id"), Text(d, "name"), "", Text(d, "cover_url").Length > 0 ? Text(d, "cover_url") : Text(Get(d, "avatar"), "normal"), "导演")).ToList();
        work.Languages = Strings(Get(node, "languages"));
        var duration = Strings(Get(node, "durations")).FirstOrDefault() ?? "";
        work.RuntimeMinutes = int.TryParse(Regex.Match(duration, @"\d+").Value, out var minutes) ? minutes : null;
        work.EpisodeCount = Int(node, "episodes_count") is > 0 ? Int(node, "episodes_count") : null;
        work.ReleaseDate = Strings(Get(node, "pubdate")).FirstOrDefault(d => d.StartsWith(work.Year, StringComparison.Ordinal)) ?? "";
        var cover = Text(node, "cover_url"); if (Uri.TryCreate(cover, UriKind.Absolute, out var url) && url.Scheme is "https" or "http") work.Poster = cover;
        work.DetailsLoaded = true;
        return work;
    }
    static void ApplyEpisodeState(CatalogItem work, string info)
    {
        if (Regex.IsMatch(info, @"全\s*\d+\s*集|已完结|全集")) work.Status = "Ended";
        else if (info.Contains("更新至")) work.Status = "Returning";
    }
    public static List<CatalogItem> DoubanSearch(string json, string keyword)
    {
        using var doc = JsonDocument.Parse(json); var output = new List<CatalogItem>();
        foreach (var wrapper in Array(Get(doc.RootElement, "items")))
        {
            var node = Get(wrapper, "target"); var kind = Text(wrapper, "target_type");
            if (kind is not ("movie" or "tv")) continue;
            var id = Text(node, "id"); if (!long.TryParse(id, out _)) continue;
            var work = new CatalogItem { Id = id, Title = Text(node, "title"), Kind = kind, Year = Text(node, "year"), Subtitle = Text(node, "card_subtitle"), Poster = Text(node, "cover_url") };
            if (work.Title.Length == 0) continue;
            EnrichDoubanCard(work, node); work.MatchedTerms.Add(keyword); output.Add(work);
        }
        return output;
    }
    public static CatalogItem TmdbItem(JsonElement node, string kind)
    {
        var id = Int(node, "id"); if (id is not > 0) return null;
        var date = Text(node, kind == "movie" ? "release_date" : "first_air_date");
        var work = new CatalogItem { Provider = "tmdb", Id = id.ToString(), Kind = kind, Title = Text(node, kind == "movie" ? "title" : "name"),
            OriginalTitle = Text(node, kind == "movie" ? "original_title" : "original_name"), ReleaseDate = date,
            Year = Regex.Match(date, @"^(?:19|20)\d{2}").Value, MetadataFetchedAt = DateTimeOffset.UtcNow, PrimaryRatingProvider = "tmdb" };
        work.ExternalIds.Tmdb = id;
        var artwork = PosterPreference.ChooseTmdb(node); var poster = artwork.Path;
        work.PosterLanguage = artwork.Language; work.ArtworkSchema = Get(node, "images").ValueKind == JsonValueKind.Object ? 2 : 0;
        work.Poster = Regex.IsMatch(poster, @"^/[a-zA-Z0-9_./-]+$") ? "https://image.tmdb.org/t/p/w342" + poster : "";
        var originalPoster = PosterPreference.OriginalPoster(node);
        if (originalPoster.Length > 0 && originalPoster != poster) work.PosterFallback = "https://image.tmdb.org/t/p/w342" + originalPoster;
        var logo = PosterPreference.ChooseLogo(node); work.TitleLogoLanguage = logo.Language;
        work.TitleLogo = logo.Path.Length > 0 ? "https://image.tmdb.org/t/p/w500" + logo.Path : "";
        var backdrop = PosterPreference.ChooseBackdrop(node); work.Backdrop = backdrop.Length > 0 ? "https://image.tmdb.org/t/p/w1280" + backdrop : "";
        work.Overview = Text(node, "overview");
        var genreIds = Array(Get(node, "genre_ids")).Where(g => g.TryGetInt32(out _)).Select(g => g.GetInt32()).ToArray();
        if (genreIds.Length == 0) genreIds = Array(Get(node, "genres")).Select(g => Int(g, "id") ?? -1).ToArray();
        work.RawGenres = Array(Get(node, "genres")).Select(g => Text(g, "name")).ToList();
        work.Genres = CatalogVocabulary.TmdbGenreKeys(genreIds, kind);
        work.RawCountries = kind == "movie" ? Array(Get(node, "production_countries")).Select(c => Text(c, "iso_3166_1")).ToList() : Strings(Get(node, "origin_country"));
        work.Countries = work.RawCountries.Select(CatalogVocabulary.Country).Where(s => s.Length > 0).Distinct().ToList();
        var score = Number(node, "vote_average"); var count = Int(node, "vote_count");
        work.Ratings.Add(new("tmdb", score is > 0 and <= 10 && count != 0 ? score : null, count, DateTimeOffset.UtcNow, "TMDB API（获取时间）"));
        work.Popularity = Number(node, "popularity") ?? 0;
        work.RuntimeMinutes = Int(node, "runtime") ?? Array(Get(node, "episode_run_time")).Where(n => n.ValueKind == JsonValueKind.Number).Select(n => (int?)n.GetInt32()).FirstOrDefault();
        work.EpisodeCount = Int(node, "number_of_episodes");
        work.Status = Text(node, "status") switch { "Ended" => "Ended", "Returning Series" or "In Production" => "Returning", "Canceled" => "Canceled", _ => "" };
        work.Languages = Array(Get(node, "spoken_languages")).Select(l => Text(l, "iso_639_1")).ToList();
        if (work.Genres.Contains("animation")) work.AnimeForm = kind == "movie" ? "movie" : "series";
        var credits = Get(node, "credits");
        work.Actors = string.Join(" / ", Array(Get(credits, "cast")).Take(18).Select(a => Text(a, "name")));
        work.Cast = Array(Get(credits, "cast")).Take(60).Select(a => {
            var path = Text(a, "profile_path");
            return new CatalogPerson("tmdb", Text(a, "id"), Text(a, "name"), Text(a, "character"), Regex.IsMatch(path, @"^/[a-zA-Z0-9_./-]+$") ? "https://image.tmdb.org/t/p/w185" + path : "");
        }).ToList();
        work.DetailsSchema = 8;
        work.Crew = Array(Get(credits, "crew")).Where(c => Text(c, "job") == "Director").Take(12).Select(d => new CatalogPerson("tmdb", Text(d, "id"), Text(d, "name"), "", Regex.IsMatch(Text(d, "profile_path"), @"^/[a-zA-Z0-9_./-]+$") ? "https://image.tmdb.org/t/p/w185" + Text(d, "profile_path") : "", "导演")).ToList();
        work.Directors = string.Join(" / ", Array(Get(credits, "crew")).Where(c => Text(c, "job") == "Director").Take(8).Select(a => Text(a, "name")));
        var imdb = Text(Get(node, "external_ids"), "imdb_id"); if (imdb.Length == 0) imdb = Text(node, "imdb_id");
        work.ExternalIds.Imdb = Regex.IsMatch(imdb, "^tt[0-9]+$") ? imdb : "";
        var aliases = Get(node, "alternative_titles");
        work.Aliases = Array(Get(aliases, kind == "movie" ? "titles" : "results")).Select(a => Text(a, "title")).Where(t => t.Length > 0).Distinct().ToList();
        work.Seasons = Array(Get(node, "seasons")).Select(s => new WorkSeason(Int(s, "season_number") ?? 0, Text(s, "name"), int.TryParse(Regex.Match(Text(s, "air_date"), "^\\d{4}").Value, out var year) ? year : null, Int(s, "episode_count"), WorkMediaService.Image(Text(s, "poster_path"), "w342"), Text(s, "overview"))).Where(s => s.Number >= 0).ToList();
        work.CollectionId = Int(Get(node, "belongs_to_collection"), "id"); work.CollectionName = Text(Get(node, "belongs_to_collection"), "name");
        work.Companies = Array(Get(node, "production_companies")).Select(c => new WorkCompany(Text(c, "id"), Text(c, "name"), WorkMediaService.Image(Text(c, "logo_path"), "w185"), "出品方"))
            .Concat(Array(Get(node, "networks")).Select(c => new WorkCompany(Text(c, "id"), Text(c, "name"), WorkMediaService.Image(Text(c, "logo_path"), "w185"), "播出平台"))).DistinctBy(c => c.Role + c.Id).ToList();
        work.PresentationSchema = Get(node, "images").ValueKind == JsonValueKind.Object ? 1 : 0;
        work.Subtitle = string.Join(" / ", new[] { work.Year, string.Join(" ", work.Countries.Select(CatalogVocabulary.Label)), string.Join(" ", work.Genres.Select(CatalogVocabulary.Label)) }.Where(s => s.Length > 0));
        return work;
    }
}
