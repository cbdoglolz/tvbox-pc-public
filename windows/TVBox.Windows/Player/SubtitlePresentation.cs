using System.Net;
using System.Text.RegularExpressions;

namespace TVBoxForWindows.Player;

/// <summary>FlyleafHost renders video only; text subtitles need a separate WinUI layer.</summary>
public static partial class SubtitlePresentation
{
    public static string Text(string decoded)
    {
        if (string.IsNullOrEmpty(decoded)) return "";
        var text = AssOverrides().Replace(decoded, "");
        text = text.Replace("\\N", "\n").Replace("\\n", "\n").Replace("\\h", " ");
        return WebUtility.HtmlDecode(HtmlTags().Replace(text, "")).Trim();
    }

    [GeneratedRegex(@"\{\\[^}]*\}")]
    private static partial Regex AssOverrides();
    [GeneratedRegex(@"</?(?:b|i|u|font|span)(?:\s[^>]*)?>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTags();
}
