using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TVBoxForWindows.Models;

public partial class CatalogItem : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    string _progress = "";
    [System.Text.Json.Serialization.JsonIgnore] public string ProgressLabel => _progress;
    [System.Text.Json.Serialization.JsonIgnore] public bool CanResume => _progress.Length > 0;
    public void UpdateProgress(History history)
    {
        var label = PlaybackPresentation.Progress(history);
        if (_progress == label) return;
        _progress = label;
        PropertyChanged?.Invoke(this, new(nameof(ProgressLabel)));
        PropertyChanged?.Invoke(this, new(nameof(CanResume)));
    }
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Poster { get; set; } = "";
    public string PosterFallback { get; set; } = "";
    public string Kind { get; set; } = "tv";
    public string Year { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public double? Rating { get; set; }
    public string RatingLabel => Rating is > 0 and <= 10 ? $"★ {Rating.Value.ToString("0.0", CultureInfo.InvariantCulture)}" : "暂无评分";
}

public class CatalogPage
{
    public List<CatalogItem> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public bool IsStale { get; set; }
}

public static class CatalogFeedParser
{
    public static CatalogPage Parse(string json, string kind, int page)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if ((!root.TryGetProperty("items", out var items) && !root.TryGetProperty("subject_collection_items", out items)) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("豆瓣没有返回有效的热播列表");
        var output = new CatalogPage { Page = page, FetchedAt = DateTimeOffset.UtcNow };
        if (root.TryGetProperty("total", out var total) && total.TryGetInt32(out var count)) output.Total = count;
        foreach (var item in items.EnumerateArray())
        {
            var title = String(item, "title").Trim();
            if (String(item, "type") is "playlist" or "doulist" || String(item, "card") == "doulist") continue;
            var id = String(item, "id");
            if (title.Length == 0 || !long.TryParse(id, out _) || output.Items.Any(i => i.Id == id)) continue;
            var subtitle = String(item, "card_subtitle");
            double? rating = null;
            if (item.TryGetProperty("rating", out var score) && score.ValueKind == JsonValueKind.Object &&
                score.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && number is > 0 and <= 10)
                rating = number;
            var poster = "";
            var fallback = "";
            if (item.TryGetProperty("pic", out var pic) && pic.ValueKind == JsonValueKind.Object)
            {
                poster = String(pic, "large");
                fallback = String(pic, "normal");
                if (poster.Length == 0) poster = String(pic, "normal");
            }
            if (poster.Length == 0) poster = String(item, "cover_url");
            if (!Uri.TryCreate(poster, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) poster = "";
            if (!Uri.TryCreate(fallback, UriKind.Absolute, out var alternate) || alternate.Scheme is not ("https" or "http")) fallback = "";
            var work = new CatalogItem
            {
                Id = id, Title = title, Poster = poster, PosterFallback = fallback, Kind = kind, Subtitle = subtitle, Rating = rating,
                Year = Regex.Match(subtitle + " " + String(item, "year"), @"\b(?:19|20)\d{2}\b").Value,
            };
            Engine.CatalogMetadataParser.EnrichDoubanCard(work, item);
            output.Items.Add(work);
        }
        return output;
    }

    static string String(JsonElement element, string name) => element.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.String or JsonValueKind.Number ? value.ToString() : "";
}
