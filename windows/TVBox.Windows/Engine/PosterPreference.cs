using System.Text.Json;
using System.Text.RegularExpressions;

namespace TVBoxForWindows.Engine;

public static class PosterPreference
{
    public static (string Path, string Language) ChooseTmdb(JsonElement root)
    {
        var text = CatalogMetadataParser.Text;
        var posters = CatalogMetadataParser.Array(CatalogMetadataParser.Get(CatalogMetadataParser.Get(root, "images"), "posters"));
        var chinese = posters.Where(p => text(p, "iso_639_1") == "zh" && Valid(text(p, "file_path")))
            .OrderByDescending(p => int.TryParse(text(p, "vote_count"), out var votes) ? votes : 0)
            .ThenByDescending(p => int.TryParse(text(p, "width"), out var width) ? width : 0).ToList();
        return chinese.Count > 0 ? (text(chinese[0], "file_path"), "zh") : (Valid(text(root, "poster_path")) ? text(root, "poster_path") : "", "");
    }
    static bool Valid(string path) => Regex.IsMatch(path, @"^/[a-zA-Z0-9_./-]+$") && !path.StartsWith("//") && !path.Contains("..");
    public static string OriginalPoster(JsonElement root) => Valid(CatalogMetadataParser.Text(root, "poster_path")) ? CatalogMetadataParser.Text(root, "poster_path") : "";
    public static (string Path, string Language) ChooseLogo(JsonElement root)
    {
        var logos = CatalogMetadataParser.Array(CatalogMetadataParser.Get(CatalogMetadataParser.Get(root, "images"), "logos"))
            .Where(n => Valid(CatalogMetadataParser.Text(n, "file_path")))
            .OrderBy(n => CatalogMetadataParser.Text(n, "iso_639_1") switch { "zh" => 0, "en" => 1, "" => 2, _ => 3 })
            .ThenByDescending(n => double.TryParse(CatalogMetadataParser.Text(n, "vote_average"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var score) ? score : 0)
            .ThenByDescending(n => int.TryParse(CatalogMetadataParser.Text(n, "width"), out var width) ? width : 0).ToList();
        return logos.Count > 0 ? (CatalogMetadataParser.Text(logos[0], "file_path"), CatalogMetadataParser.Text(logos[0], "iso_639_1")) : ("", "");
    }
    public static string ChooseBackdrop(JsonElement root)
    {
        var images = CatalogMetadataParser.Array(CatalogMetadataParser.Get(CatalogMetadataParser.Get(root, "images"), "backdrops"))
            .Where(n => Valid(CatalogMetadataParser.Text(n, "file_path")))
            .OrderBy(n => CatalogMetadataParser.Text(n, "iso_639_1") switch { "" => 0, "zh" => 1, _ => 2 })
            .ThenByDescending(n => int.TryParse(CatalogMetadataParser.Text(n, "vote_count"), out var votes) ? votes : 0).ToList();
        return images.Count > 0 ? CatalogMetadataParser.Text(images[0], "file_path") : Valid(CatalogMetadataParser.Text(root, "backdrop_path")) ? CatalogMetadataParser.Text(root, "backdrop_path") : "";
    }
}
