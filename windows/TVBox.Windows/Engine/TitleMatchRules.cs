using System.Text;
using System.Text.RegularExpressions;

namespace TVBoxForWindows.Engine;

public static class TitleMatchRules
{
    public static string Normalize(string text)
    {
        text = Core.Trans.T2S((text ?? "").Normalize(NormalizationForm.FormKC)).ToLowerInvariant();
        text = Regex.Replace(text, @"\bseason\s*0*(\d+)\b|\bs0*(\d+)\b", match => "第" + (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) + "季");
        text = Regex.Replace(text, @"第\s*([一二三四五六七八九十]+)\s*季", match => "第" + ChineseNumber(match.Groups[1].Value) + "季");
        text = Regex.Replace(text, @"(?:国语|粤语|普通话|中字|完整版|高清版|版)$", "");
        return new string(text.Where(char.IsLetterOrDigit).ToArray());
    }

    public static bool Matches(string title, string year, string kind, string candidate, string candidateYear, string type)
    {
        if (Regex.IsMatch(candidate ?? "", "解说|预告|花絮|剪辑|幕后|抢先看")) return false;
        if (Normalize(title) != Normalize(candidate)) return false;
        var expectedYear = Regex.Match(year ?? "", @"(?:19|20)\d{2}").Value;
        var actualYear = Regex.Match(candidateYear ?? "", @"(?:19|20)\d{2}").Value;
        if (expectedYear.Length > 0 && actualYear.Length > 0 && expectedYear != actualYear) return false;
        type ??= "";
        if (kind == "movie" && Regex.IsMatch(type, "电视剧|连续剧|国产剧|香港剧|韩国剧|欧美剧|日本剧|台湾剧|海外剧|泰国剧|短剧|综艺|动漫")) return false;
        if (kind == "tv" && Regex.IsMatch(type, "电影|动作片|喜剧片|爱情片|科幻片|恐怖片|剧情片|纪录片")) return false;
        return true;
    }

    public static string GuessKind(string type)
    {
        type ??= "";
        if (Regex.IsMatch(type, "电影|片$")) return "movie";
        return Regex.IsMatch(type, "剧|综艺|动漫") ? "tv" : "";
    }

    static int ChineseNumber(string value)
    {
        const string digits = "零一二三四五六七八九";
        if (value == "十") return 10;
        if (value.Contains('十'))
        {
            var parts = value.Split('十');
            return (parts[0].Length == 0 ? 1 : digits.IndexOf(parts[0][0])) * 10 + (parts[1].Length == 0 ? 0 : digits.IndexOf(parts[1][0]));
        }
        return digits.IndexOf(value[0]);
    }

    public static long ResumePosition(string savedEpisode, string requestedEpisode, string kind, long position)
    {
        if (position <= 0) return 0;
        if (kind == "movie") return position;
        if (string.IsNullOrEmpty(requestedEpisode) || savedEpisode == requestedEpisode) return position;
        return Models.EpisodeIdentity.Parse(savedEpisode).Matches(Models.EpisodeIdentity.Parse(requestedEpisode)) ? position : 0;
    }
}
