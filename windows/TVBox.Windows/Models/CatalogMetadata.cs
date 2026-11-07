using System.Text.Json.Serialization;
using TVBoxForWindows.Core;

namespace TVBoxForWindows.Models;

public record WorkRating(string Provider, double? Score, int? VoteCount, DateTimeOffset? FetchedAt, string Origin)
{
    [JsonIgnore] public bool HasScore => Score is > 0 and <= 10 && VoteCount != 0;
    [JsonIgnore] public string ProviderName => Provider switch { "douban" => "豆瓣", "tmdb" => "TMDB", "imdb" => "IMDb", _ => Provider };
    [JsonIgnore] public string Label => HasScore ? $"{ProviderName} {Score:0.0}" : $"{ProviderName} 暂无评分";
}
public class WorkExternalIds
{
    public string Douban { get; set; } = "";
    public int? Tmdb { get; set; }
    public string Imdb { get; set; } = "";
}
public record CatalogPerson(string Provider, string Id, string Name, string Character, string Portrait, string Job = "演员")
{
    [JsonIgnore] public string RoleLabel => Character.Length > 0 ? Name + " 饰演 " + Character : Name + " · " + Job;
}
public record WorkSeason(int Number, string Name, int? Year, int? EpisodeCount, string Poster = "", string Overview = "")
{ [JsonIgnore] public string Label => Number == 0 ? "特别篇" : "第" + Number + "季"; [JsonIgnore] public string Caption => Label + (EpisodeCount is > 0 ? " · " + EpisodeCount + "集" : ""); }
public record WorkEpisode(int Season, int Number, string Name, string Overview, string Still, string AirDate, int? Runtime, double? Rating, int? Votes)
{
    [JsonIgnore] public string Code => $"S{Season} E{Number}";
    [JsonIgnore] public string Heading => Code + " · " + (Name.Length > 0 ? Name : "第" + Number + "集");
    [JsonIgnore] public string Facts => (Runtime is > 0 ? Runtime + "分钟 · " : "") + "TMDB " + (Rating is > 0 && Votes != 0 ? Rating.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "-") + (AirDate.Length > 0 ? " · " + AirDate : "");
}
public record WorkCompany(string Id, string Name, string Logo, string Role)
{ [JsonIgnore] public string LogoText => string.IsNullOrWhiteSpace(Logo) ? Name : ""; }

public partial class CatalogItem
{
    public string Provider { get; set; } = "douban";
    public string IdentityKey { get; set; } = "";
    public string OriginalTitle { get; set; } = "";
    public List<string> Aliases { get; set; } = new();
    public List<string> Countries { get; set; } = new();
    public List<string> RawCountries { get; set; } = new();
    public List<string> Genres { get; set; } = new();
    public List<string> RawGenres { get; set; } = new();
    public List<string> Languages { get; set; } = new();
    public List<string> MatchedTerms { get; set; } = new();
    public List<WorkRating> Ratings { get; set; } = new();
    public WorkExternalIds ExternalIds { get; set; } = new();
    public List<WorkSeason> Seasons { get; set; } = new();
    public List<WorkCompany> Companies { get; set; } = new();
    public int? CollectionId { get; set; }
    public string CollectionName { get; set; } = "";
    public int PresentationSchema { get; set; }
    public int? SelectedSeason { get; set; }
    public List<CatalogPerson> Cast { get; set; } = new();
    public List<CatalogPerson> Crew { get; set; } = new();
    public string Backdrop { get; set; } = "";
    public string TitleLogo { get; set; } = "";
    public string TitleLogoLanguage { get; set; } = "";
    [JsonIgnore] public bool ArtworkResolved { get; set; }
    public int DetailsSchema { get; set; }
    public int ArtworkSchema { get; set; }
    public string PosterLanguage { get; set; } = "";
    public string Overview { get; set; } = "";
    public string Directors { get; set; } = "";
    public string Actors { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public int? RuntimeMinutes { get; set; }
    public int? EpisodeCount { get; set; }
    public string Status { get; set; } = "";
    public string AnimeForm { get; set; } = "";
    public double Popularity { get; set; }
    public bool DetailsLoaded { get; set; }
    public bool TmdbLinkSuppressed { get; set; }
    public DateTimeOffset? MetadataFetchedAt { get; set; }
    public string PrimaryRatingProvider { get; set; } = "douban";
    [JsonIgnore] public string WorkKey => !string.IsNullOrEmpty(IdentityKey) ? IdentityKey : string.IsNullOrEmpty(Id) ? "" :
        Provider == "tmdb" ? $"tmdb@{Kind}:{Id}" + (SelectedSeason >= 0 ? $":s{SelectedSeason}" : "") : "douban@" + Id;
    [JsonIgnore] public string PrimaryRatingText => GetRating(PrimaryRatingProvider).Label;
    [JsonIgnore] public string DoubanScore => CompactScore("douban", "豆瓣");
    [JsonIgnore] public string TmdbScore => CompactScore("tmdb", "TMDB");
    [JsonIgnore] public string ImdbScore => CompactScore("imdb", "IMDb");
    string CompactScore(string provider, string label) { var r = GetRating(provider); return label + " " + (r.HasScore ? r.Score.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "-"); }
    public void ApplyMetadata(CatalogItem full)
    {
        if (!string.IsNullOrEmpty(full.Poster) && (PosterLanguage != "zh" || full.PosterLanguage == "zh")) { Poster = full.Poster; PosterFallback = full.PosterFallback; PosterLanguage = full.PosterLanguage; }
        ArtworkSchema = Math.Max(ArtworkSchema, full.ArtworkSchema);
        if (!string.IsNullOrEmpty(full.Year)) Year = full.Year;
        Ratings = full.Ratings; ExternalIds = full.ExternalIds; DetailsLoaded = full.DetailsLoaded;
        MetadataFetchedAt = full.MetadataFetchedAt; OriginalTitle = full.OriginalTitle; Aliases = full.Aliases;
        Countries = full.Countries; RawCountries = full.RawCountries; Genres = full.Genres; RawGenres = full.RawGenres;
        Cast = full.Cast; if (full.DetailsSchema >= 8 || Crew.Count == 0) Crew = full.Crew; DetailsSchema = full.DetailsSchema;
        if (full.ArtworkSchema >= 2) { Backdrop = full.Backdrop; TitleLogo = full.TitleLogo; TitleLogoLanguage = full.TitleLogoLanguage; }
        else if (Backdrop.Length == 0 && full.Backdrop.Length > 0) Backdrop = full.Backdrop;
        ArtworkResolved |= full.ArtworkResolved;
        Overview = full.Overview; Directors = full.Directors; Actors = full.Actors; ReleaseDate = full.ReleaseDate;
        RuntimeMinutes = full.RuntimeMinutes; Seasons = full.Seasons; Status = full.Status; EpisodeCount = full.EpisodeCount;
        if (full.PresentationSchema >= 1) { Companies = full.Companies; CollectionId = full.CollectionId; CollectionName = full.CollectionName; PresentationSchema = full.PresentationSchema; }
        Languages = full.Languages; AnimeForm = full.AnimeForm; TmdbLinkSuppressed = full.TmdbLinkSuppressed;
        NotifyMetadataChanged();
    }
    [JsonIgnore] public string SecondaryRatingText => Setting.GetBool("catalog_secondary_rating", true) ? string.Join(" · ", Ratings.Where(r => r.Provider is "douban" or "tmdb" && r.Provider != PrimaryRatingProvider && r.HasScore).Select(r => r.Label)) : "";
    public WorkRating GetRating(string provider) => Ratings.FirstOrDefault(r => r.Provider == provider) ??
        new WorkRating(provider, provider == "douban" ? Rating : null, null, MetadataFetchedAt, provider == "douban" ? "豆瓣" : "尚未获取");
    public CatalogItem Copy() { var copy = JsonUtil.Deserialize<CatalogItem>(JsonUtil.Serialize(this)); copy.ArtworkResolved = ArtworkResolved; return copy; }
    public void NotifyMetadataChanged()
    {
        foreach (var property in new[] { nameof(PosterFallback), nameof(Poster), nameof(PrimaryRatingText), nameof(SecondaryRatingText), nameof(DoubanScore), nameof(TmdbScore), nameof(ImdbScore), nameof(Subtitle), nameof(Title) })
            PropertyChanged?.Invoke(this, new(property));
    }
}
