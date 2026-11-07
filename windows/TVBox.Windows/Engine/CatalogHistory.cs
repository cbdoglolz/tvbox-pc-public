using TVBoxForWindows.Core;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Engine;

public static class CatalogHistory
{
    public static History Find(CatalogItem item, IEnumerable<History> histories)
    {
        var saved = histories as IReadOnlyCollection<History> ?? histories.ToList();
        var exact = saved.Where(h => !string.IsNullOrEmpty(item.WorkKey) && h.Key == item.WorkKey ||
            !string.IsNullOrEmpty(item.Id) && CatalogIdentity.SameWork(CatalogLibrary.FromHistory(h), item)).MaxBy(h => h.CreateTime);
        if (exact != null) return exact;
        // Unknown year or a different known subject ID cannot be safely promoted.
        if (string.IsNullOrEmpty(item.Year) || string.IsNullOrEmpty(item.Kind)) return null;
        return saved.Where(h => string.IsNullOrEmpty(h.CatalogId) && h.Position > 0 &&
            h.CatalogYear == item.Year && h.CatalogKind == item.Kind &&
            TitleMatchRules.Matches(item.Title, item.Year, item.Kind, h.VodName, h.CatalogYear, "")).MaxBy(h => h.CreateTime);
    }

    public static History Promote(History previous, CatalogItem item)
    {
        var copy = JsonUtil.Deserialize<History>(JsonUtil.Serialize(previous));
        copy.SourceKey = string.IsNullOrEmpty(previous.SourceKey) ? previous.SiteKey : previous.SourceKey;
        copy.SourceVodId = string.IsNullOrEmpty(previous.SourceVodId) ? previous.VodId : previous.SourceVodId;
        copy.Key = string.IsNullOrEmpty(previous.CatalogId) ? item.WorkKey : previous.Key;
        if (string.IsNullOrEmpty(previous.CatalogId)) { copy.CatalogProvider = item.Provider; copy.CatalogId = item.Id; }
        copy.CatalogSnapshot = JsonUtil.Serialize(item);
        copy.CatalogYear = item.Year; copy.CatalogKind = item.Kind;
        copy.VodName = item.Title;
        if (!string.IsNullOrEmpty(item.Poster)) copy.VodPic = item.Poster;
        return copy;
    }
}
