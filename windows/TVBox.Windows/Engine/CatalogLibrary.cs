using TVBoxForWindows.Core;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Engine;

public static class CatalogLibrary
{
    public static CatalogItem FromHistory(History history)
    {
        var item = JsonUtil.Deserialize<CatalogItem>(history.CatalogSnapshot) ?? new CatalogItem
        { Id = history.CatalogId, Provider = string.IsNullOrEmpty(history.CatalogId) ? "legacy" : history.CatalogProvider, Title = history.VodName, Poster = history.VodPic, Kind = history.CatalogKind, Year = history.CatalogYear };
        item.IdentityKey = history.Key; item.UpdateProgress(history); return item;
    }
    public static CatalogItem FromKeep(Keep keep)
    {
        var item = JsonUtil.Deserialize<CatalogItem>(keep.CatalogSnapshot) ?? new CatalogItem
        { Id = keep.CatalogId, Provider = string.IsNullOrEmpty(keep.CatalogId) ? "legacy" : keep.CatalogProvider, Title = keep.VodName, Poster = keep.VodPic, Kind = keep.CatalogKind, Year = keep.CatalogYear };
        item.IdentityKey = keep.Key; return item;
    }
    public static Keep FindKeep(CatalogItem work, int cid) => Stores.GetKeeps(cid).FirstOrDefault(k => k.Key == work.WorkKey || CatalogIdentity.SameWork(FromKeep(k), work));
    public static void SaveKeep(CatalogItem work, int cid)
    {
        if (FindKeep(work, cid) != null) return;
        Stores.SaveKeep(new Keep { Cid = cid, Key = work.WorkKey, CatalogId = work.Id, CatalogKind = work.Kind, CatalogYear = work.Year, CatalogProvider = work.Provider,
            CatalogSnapshot = JsonUtil.Serialize(work), VodName = work.Title, VodPic = work.Poster, SiteName = work.Provider == "tmdb" ? "TMDB资料" : work.Provider == "douban" ? "豆瓣资料" : "原资源" });
    }
    public static void UpdateSavedMetadata(CatalogItem work, int cid)
    {
        var keys = Stores.GetHistories(cid).Where(h => h.Key == work.WorkKey || CatalogIdentity.SameWork(FromHistory(h), work)).Select(h => h.Key)
            .Concat(Stores.GetKeeps(cid).Where(k => k.Key == work.WorkKey || CatalogIdentity.SameWork(FromKeep(k), work)).Select(k => k.Key)).Distinct();
        foreach (var key in keys.ToList()) Stores.UpdateCatalogMetadata(cid, key, work);
    }
}
