using Microsoft.UI.Xaml.Controls;
using TVBoxForWindows.Models;
using TVBoxForWindows.UI.Pages;

namespace TVBoxForWindows.UI;

public static class PlaybackNavigation
{
    public static void OpenHistory(Frame frame, History history)
    {
        frame.Navigate(typeof(PlayerPage), new PlayerArgs
        {
            Catalog = Engine.CatalogLibrary.FromHistory(history),
            SeedSiteKey = string.IsNullOrEmpty(history.SourceKey) ? history.SiteKey : history.SourceKey,
            SeedVodId = string.IsNullOrEmpty(history.SourceVodId) ? history.VodId : history.SourceVodId,
            EpisodeName = history.VodRemarks,
        });
    }
}
