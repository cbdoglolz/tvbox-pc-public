using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.UI.Controls;

/// <summary>Hold the title slot until logo selection/download completes; show exactly one image or text fallback.</summary>
public class WorkTitleArt : Grid
{
    public static readonly DependencyProperty WorkProperty = DependencyProperty.Register(nameof(Work), typeof(CatalogItem), typeof(WorkTitleArt), new PropertyMetadata(null, (d, e) => _ = ((WorkTitleArt)d).LoadAsync(e.NewValue as CatalogItem)));
    public CatalogItem Work { get => (CatalogItem)GetValue(WorkProperty); set => SetValue(WorkProperty, value); }
    public Brush TextForeground { get => _text.Foreground; set => _text.Foreground = value; }
    readonly Image _logo = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 480, MaxHeight = 122 };
    readonly Image _relief = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 480, MaxHeight = 122, Opacity = 0.24, IsHitTestVisible = false, RenderTransform = new TranslateTransform { X = 3, Y = 5 } };
    readonly TextBlock _text = new() { FontSize = 34, FontWeight = Microsoft.UI.Text.FontWeights.Bold, TextWrapping = TextWrapping.Wrap, MaxLines = 2, CharacterSpacing = 65, VerticalAlignment = VerticalAlignment.Center };
    CancellationTokenSource _cts;
    string _workKey;
    int? _tmdbId;
    bool _suppressed;
    bool _active;
    public void SetActive(bool active) { _active = active; if (!active) _cts?.Cancel(); else if (IsLoaded && Work != null && (_cts == null || _cts.IsCancellationRequested)) _ = LoadAsync(Work); }
    public WorkTitleArt()
    {
        MinHeight = 70; MaxHeight = 126;
        Children.Add(_relief); Children.Add(_logo); Children.Add(_text);
        _logo.Shadow = new ThemeShadow(); _logo.Translation = new(0, 0, 18);
        PointerMoved += (_, e) => {
            if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled || ActualWidth < 1) return;
            var p = e.GetCurrentPoint(this).Position;
            _logo.RenderTransform = new CompositeTransform { TranslateX = (p.X / ActualWidth - 0.5) * 5, TranslateY = (p.Y / Math.Max(1, ActualHeight) - 0.5) * 3, SkewY = (p.X / ActualWidth - 0.5) * 1.6 };
            _logo.Projection = new PlaneProjection { RotationY = (p.X / ActualWidth - 0.5) * 8, RotationX = (0.5 - p.Y / Math.Max(1, ActualHeight)) * 4, LocalOffsetZ = 12 };
        };
        PointerExited += (_, _) => { _logo.RenderTransform = null; _logo.Projection = null; };
        Loaded += (_, _) => SetActive(CatalogArtworkLifecycle.IsActive(this));
        Unloaded += (_, _) => { _active = false; _cts?.Cancel(); _cts = null; };
    }
    async Task LoadAsync(CatalogItem work)
    {
        if (!_active) { if (!IsLoaded || !CatalogArtworkLifecycle.IsActive(this)) return; _active = true; }
        _cts?.Cancel(); var cts = _cts = new();
        if (work == null || work.WorkKey != _workKey) { _relief.Source = _logo.Source = null; _text.Text = ""; }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, work?.Title ?? "作品标题");
        try {
            var full = await CatalogArtworkResolver.Shared.ResolveAsync(work, cts.Token);
            if (full == null || cts.IsCancellationRequested) return;
            if (full.WorkKey == _workKey && full.ExternalIds.Tmdb == _tmdbId && full.TmdbLinkSuppressed == _suppressed && (_logo.Source != null || _text.Text.Length > 0)) return;
            var image = await ImageLoader.Load(full.TitleLogo);
            if (cts.IsCancellationRequested) return;
            _workKey = full.WorkKey; _tmdbId = full.ExternalIds.Tmdb; _suppressed = full.TmdbLinkSuppressed;
            if (image != null) { _text.Text = ""; _relief.Source = _logo.Source = image; ToolTipService.SetToolTip(this, work.Title); }
            else { _relief.Source = _logo.Source = null; _text.Text = work.Title; }
        } catch (OperationCanceledException) { }
        catch { if (!cts.IsCancellationRequested) { _relief.Source = _logo.Source = null; _text.Text = work?.Title ?? ""; } }
        finally { if (ReferenceEquals(_cts, cts)) _cts = null; cts.Dispose(); }
    }
}
