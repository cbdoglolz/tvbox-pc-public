using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.UI.Controls;

/// <summary>海报图控件（契约 §5.4）：字符串 Source 依赖属性（支持 @Referer=/@User-Agent= 后缀），
/// 异步经 ImageLoader 加载；加载中/失败显示灰色占位块 + 🎬 字形；Stretch=UniformToFill。</summary>
public class PosterImage : Grid
{
    public static readonly DependencyProperty WorkProperty = DependencyProperty.Register(nameof(Work), typeof(CatalogItem), typeof(PosterImage), new PropertyMetadata(null, (d, e) => _ = ((PosterImage)d).LoadWorkAsync(e.NewValue as CatalogItem)));
    public CatalogItem Work { get => (CatalogItem)GetValue(WorkProperty); set => SetValue(WorkProperty, value); }
    public string ArtworkKind { get; set; } = "poster";
    public Stretch ImageStretch { get => _image.Stretch; set => _image.Stretch = value; }
    bool _decorative;
    public bool Decorative { get => _decorative; set { _decorative = value; if (value) _placeholder.Visibility = Visibility.Collapsed; } }
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(string), typeof(PosterImage),
        new PropertyMetadata(null, (d, e) => _ = ((PosterImage)d).LoadAsync(e.NewValue as string)));
    public static readonly DependencyProperty FallbackSourceProperty = DependencyProperty.Register(
        nameof(FallbackSource), typeof(string), typeof(PosterImage), new PropertyMetadata(null));
    public string FallbackSource { get => (string)GetValue(FallbackSourceProperty); set => SetValue(FallbackSourceProperty, value); }

    readonly Image _image = new() { Stretch = Stretch.UniformToFill };
    readonly Border _placeholder;
    int _version;
    string _displayedSource;
    CancellationTokenSource _workCts;
    string _workKey;
    int? _tmdbId;
    bool _suppressed;
    bool _active;
    public void SetActive(bool active) { _active = active; if (!active) _workCts?.Cancel(); else if (IsLoaded && Work != null && (_workCts == null || _workCts.IsCancellationRequested)) _ = LoadWorkAsync(Work); }

    public PosterImage()
    {
        _placeholder = new Border
        {
            Background = FindBrush("ControlFillColorSecondaryBrush"),
            Child = new TextBlock
            {
                Text = "🎬",
                FontSize = 28,
                Opacity = 0.6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Children.Add(_placeholder);
        Children.Add(_image);
        Unloaded += (_, _) => { _active = false; _workCts?.Cancel(); _workCts = null; ++_version; };
        Loaded += (_, _) => {
            SetActive(CatalogArtworkLifecycle.IsActive(this));
            if (Work == null && !string.IsNullOrWhiteSpace(Source)) _ = LoadAsync(Source);
        };
    }

    public string Source
    {
        get => (string)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    static Brush FindBrush(string key)
    {
        try { if (Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b) return b; } catch { }
        return new SolidColorBrush(Colors.Gray);
    }

    async Task LoadAsync(string pic)
    {
        if (pic == _displayedSource && _image.Source != null) return;
        var version = ++_version;
        _image.Source = null;
        _placeholder.Visibility = Decorative ? Visibility.Collapsed : Visibility.Visible;
        if (string.IsNullOrWhiteSpace(pic)) return;
        var bmp = await ImageLoader.Load(pic);
        if (bmp == null && version == _version && !string.IsNullOrWhiteSpace(FallbackSource) && FallbackSource != pic)
            bmp = await ImageLoader.Load(FallbackSource);
        if (version != _version || bmp == null) return; // 已被更新的 Source 覆盖或加载失败
        _image.Source = bmp;
        _displayedSource = pic;
        _placeholder.Visibility = Visibility.Collapsed;
    }
    async Task LoadWorkAsync(CatalogItem work)
    {
        if (!_active) { if (!IsLoaded || !CatalogArtworkLifecycle.IsActive(this)) return; _active = true; }
        _workCts?.Cancel(); var cts = _workCts = new(); var version = ++_version;
        if (work == null || work.WorkKey != _workKey) { _image.Source = null; _displayedSource = null; _placeholder.Visibility = Decorative ? Visibility.Collapsed : Visibility.Visible; }
        try {
            var full = await CatalogArtworkResolver.Shared.ResolveAsync(work, cts.Token);
            if (cts.IsCancellationRequested || version != _version || full == null) return;
            if (full.WorkKey == _workKey && full.ExternalIds.Tmdb == _tmdbId && full.TmdbLinkSuppressed == _suppressed && _image.Source != null) return;
            var source = ArtworkKind == "backdrop" ? full.Backdrop : full.Poster;
            var bmp = await ImageLoader.Load(source);
            if (bmp == null && ArtworkKind == "poster" && !cts.IsCancellationRequested) {
                var fallback = full.PosterFallback.Length > 0 ? full.PosterFallback : work.Poster;
                if (fallback != source) bmp = await ImageLoader.Load(fallback);
            }
            if (cts.IsCancellationRequested || version != _version) return;
            if (bmp == null) { _image.Source = null; _placeholder.Visibility = Decorative ? Visibility.Collapsed : Visibility.Visible; return; }
            _image.Source = bmp; _displayedSource = source; _workKey = full.WorkKey; _tmdbId = full.ExternalIds.Tmdb; _suppressed = full.TmdbLinkSuppressed; _placeholder.Visibility = Visibility.Collapsed;
        } catch (OperationCanceledException) { }
        catch { /* Keep the placeholder; never flash a speculative image before preferred artwork resolves. */ }
        finally { if (ReferenceEquals(_workCts, cts)) _workCts = null; cts.Dispose(); }
    }
}
