using Microsoft.UI.Xaml.Controls;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;
using TVBoxForWindows.UI.Pages;

namespace TVBoxForWindows.UI;

public static class CatalogActions
{
    public static void Open(Frame frame, CatalogItem work, bool explicitPlay = false)
    {
        if (!explicitPlay) { Details(work); return; }
        var history = CatalogHistory.Find(work, Stores.GetHistories(VodConfigService.Cid));
        if (history != null && history.Position > 1000) PlaybackNavigation.OpenHistory(frame, history);
        else
        {
            var keep = CatalogLibrary.FindKeep(work, VodConfigService.Cid);
            frame.Navigate(typeof(PlayerPage), new PlayerArgs { Catalog = work,
                SeedSiteKey = keep != null && work.Id.Length == 0 ? keep.SiteKey : null,
                SeedVodId = keep != null && work.Id.Length == 0 ? keep.VodId : null });
        }
    }
    public static void Details(CatalogItem work) => App.Main.OpenWorkDetails(work);
    public static bool ToggleFavorite(CatalogItem work)
    {
        var keep = CatalogLibrary.FindKeep(work, VodConfigService.Cid);
        if (keep != null) { Stores.DeleteKeep(VodConfigService.Cid, keep.Key); return false; }
        CatalogLibrary.SaveKeep(work, VodConfigService.Cid); return true;
    }
}
