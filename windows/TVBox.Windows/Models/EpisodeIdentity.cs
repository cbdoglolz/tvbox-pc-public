using System.Text.RegularExpressions;

namespace TVBoxForWindows.Models;

/// <summary>One parser for source selection, resume, cache and danmaku episode identities.</summary>
public readonly record struct EpisodeIdentity(int Season, int Number)
{
    public static EpisodeIdentity Parse(string label)
    {
        var text = Core.Trans.T2S((label ?? "").Normalize(System.Text.NormalizationForm.FormKC));
        var season = Regex.Match(text, @"(?:\bS(?:eason)?\s*0*(\d+)|第\s*([零〇一二两三四五六七八九十百\d]+)\s*季)", RegexOptions.IgnoreCase);
        var s = season.Success ? NumberValue(season.Groups[1].Success ? season.Groups[1].Value : season.Groups[2].Value) : -1;
        var episode = Regex.Match(text, @"(?:\bEP(?:isode)?\s*0*(\d+)|(?<=\d)\s*E(?:P)?\s*0*(\d+)|\bE\s*0*(\d+)|第\s*([零〇一二两三四五六七八九十百\d]+)\s*[集话期]|^(\d+)\s*[集话期])", RegexOptions.IgnoreCase);
        if (episode.Success) return new(s, NumberValue(episode.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value));
        // Bare source labels such as 01 / 01(1080P) are common. Resolution/year digits elsewhere are not episode numbers.
        var bare = Regex.Match(text.Trim(), @"^(\d{1,3})(?:\s*$|\s*[:：._、\-\[(（])");
        return new(s, bare.Success ? NumberValue(bare.Groups[1].Value) : -1);
    }

    static int NumberValue(string value)
    {
        if (int.TryParse(value, out var number)) return number;
        const string digits = "零一二三四五六七八九";
        var total = 0; var current = 0;
        foreach (var c in value.Replace('两', '二').Replace('〇', '零'))
        {
            if (c is '十' or '百') { total += Math.Max(1, current) * (c == '十' ? 10 : 100); current = 0; }
            else { current = digits.IndexOf(c); if (current < 0) return -1; }
        }
        return total + current;
    }

    public bool Matches(EpisodeIdentity other, int expectedSeason = -1) => Number >= 0 && Number == other.Number
        && (Season < 0 || other.Season < 0 || Season == other.Season)
        && (expectedSeason < 0 || (Season < 0 || Season == expectedSeason) && (other.Season < 0 || other.Season == expectedSeason));
}
