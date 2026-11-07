using System.ComponentModel;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;

namespace TVBoxForWindows.Models;

public sealed class ContinueCard : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    public History History { get; init; }
    public CatalogItem Work { get; init; }
    public string Title => Work.Title.Length > 0 ? Work.Title : History.VodName;
    public string Episode { get { if (Work.Kind != "tv") return "电影"; var n = History.EpisodeNumber ?? TVBoxForWindows.Player.DanmakuSearch.EpisodeNumber(History.VodRemarks); return $"S{History.EpisodeSeason ?? Work.SelectedSeason ?? Math.Max(1, CatalogIdentity.Season(Work))}" + (n > 0 ? " E" + n : " · 集数待核对"); } }
    public string Progress => PlaybackPresentation.Progress(History);
    public string Timestamp => History.Duration > History.Position && History.Duration > 0 ? "剩余 " + Clock(History.Duration - History.Position) : "已看 " + Clock(History.Position);
    public double Percent => History.Duration > 0 ? Math.Clamp(History.Position * 100d / History.Duration, 0, 100) : 0;
    string _image = "", _episodeTitle = "";
    public string Image => _image;
    public string EpisodeTitle => _episodeTitle;
    public void SetArtwork(string image, string episodeTitle) { _image = image; _episodeTitle = episodeTitle; PropertyChanged?.Invoke(this, new(nameof(Image))); PropertyChanged?.Invoke(this, new(nameof(EpisodeTitle))); }
    public static string Clock(long ms) { var t = TimeSpan.FromMilliseconds(Math.Max(0, ms)); return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}"; }
}
