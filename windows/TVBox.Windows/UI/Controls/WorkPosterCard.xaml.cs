using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Hosting;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.UI.Controls;

public sealed partial class WorkPosterCard : UserControl
{
    public bool Compact { get; set; }
    bool _compactApplied;
    CatalogItem Work => DataContext as CatalogItem;
    public WorkPosterCard()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Compact && !_compactApplied)
            {
                _compactApplied = true;
                 Grid.SetColumn(Scores, 1); Grid.SetRow(Scores, 2); Scores.Width = 320; Scores.HorizontalAlignment = HorizontalAlignment.Left;
                Root.Width = 600; Art.Width = 90; Art.Height = 138; Grid.SetRowSpan(Art, 4);
                Grid.SetColumn(TitleButton, 1); Grid.SetRow(TitleButton, 0); TitleButton.MaxWidth = 460;
                Grid.SetColumn(Subtitle, 1); Grid.SetRow(Subtitle, 1); Subtitle.MaxWidth = 460;
            }
            RefreshFavorite();
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PlayButton, Work?.Title ?? "影片");
        };
        DataContextChanged += (_, _) => { RefreshFavorite(); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PlayButton, Work?.Title ?? "影片"); };
    }
    void RefreshFavorite() { if (Work != null) Favorite.Content = CatalogLibrary.FindKeep(Work, VodConfigService.Cid) == null ? "☆" : "★"; }
    void OnPlay(object sender, RoutedEventArgs e) { if (Work != null) App.Main.OpenCatalogWork(Work); }
    void OnResume(object sender, RoutedEventArgs e) { if (Work != null) App.Main.OpenCatalogWork(Work); }
    void OnDetails(object sender, RoutedEventArgs e) { if (Work != null) CatalogActions.Details(Work); }
    void OnFavorite(object sender, RoutedEventArgs e)
    {
        if (Work == null) return;
        var saved = CatalogActions.ToggleFavorite(Work); RefreshFavorite();
        App.Main.ShowNotice(saved ? "已收藏" : "未收藏");
        ToolTipService.SetToolTip(Favorite, saved ? "已收藏" : "未收藏");
    }
    void OnEnter(object sender, PointerRoutedEventArgs e)
    {
        RefreshFavorite(); Actions.Opacity = 1;
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) return;
        var v = ElementCompositionPreview.GetElementVisual(Art); var a = v.Compositor.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(1, 1.03f); a.Duration = TimeSpan.FromMilliseconds(160); v.StartAnimation("Scale.X", a); v.StartAnimation("Scale.Y", a);
    }
    void OnExit(object sender, PointerRoutedEventArgs e)
    {
        Actions.Opacity = 0.85; var v = ElementCompositionPreview.GetElementVisual(Art);
        v.StopAnimation("Scale.X"); v.StopAnimation("Scale.Y"); v.Scale = System.Numerics.Vector3.One;
    }
}
