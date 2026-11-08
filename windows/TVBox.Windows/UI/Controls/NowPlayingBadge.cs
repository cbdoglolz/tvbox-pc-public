using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace TVBoxForWindows.UI.Controls;

public sealed class NowPlayingBadge : UserControl
{
    readonly Grid _root = new() { Width = 164, MinHeight = 58, CornerRadius = new(8) };
    readonly Grid _fill = new() { Width = 0, HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromArgb(115, 126, 82, 223)) };
    readonly TextBlock _title = new() { TextWrapping = TextWrapping.Wrap, MaxLines = 2, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    readonly TextBlock _episode = new() { FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(255, 205, 178, 255)) };
    readonly TextBlock _measure = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _time = new() { FontSize = 10 };
    readonly Microsoft.UI.Xaml.Shapes.Path _wave = new() { Width = 320, Height = 58, Stretch = Stretch.Fill, Stroke = new SolidColorBrush(Color.FromArgb(140, 194, 163, 255)), StrokeThickness = 2 };
    bool _animated;
    string _lastTitle;
    public NowPlayingBadge()
    {
        _root.Background = new SolidColorBrush(Color.FromArgb(30, 130, 100, 180));
        _root.Children.Add(_fill);
        _wave.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlReader.Load("<PathGeometry xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><PathFigure StartPoint='0,34'><BezierSegment Point1='30,0' Point2='50,58' Point3='80,26'/><BezierSegment Point1='110,0' Point2='130,58' Point3='160,26'/><BezierSegment Point1='190,0' Point2='210,58' Point3='240,26'/><BezierSegment Point1='270,0' Point2='290,58' Point3='320,26'/></PathFigure></PathGeometry>");
        _fill.Children.Add(_wave);
        var labels = new StackPanel { Spacing = 4, Margin = new(8), VerticalAlignment = VerticalAlignment.Center };
        var clip = new Grid { MinHeight = 18 };
        clip.Children.Add(_title);
        labels.Children.Add(clip); labels.Children.Add(_episode); labels.Children.Add(_time); _root.Children.Add(labels); Content = _root;
        Unloaded += (_, _) => Animate(false);
    }
    public void Update(string title, long position, long duration, bool playing)
    {
        _fill.Width = 164 * (duration > 0 ? Math.Clamp((double)position / duration, 0, 1) : 0);
        _fill.Clip = new RectangleGeometry { Rect = new(0, 0, _fill.Width, Math.Max(58, _root.ActualHeight)) };
        if (_lastTitle != title) {
            var marker = title.LastIndexOf(" · 第", StringComparison.Ordinal); if (marker < 0) marker = title.LastIndexOf(" · 特别篇", StringComparison.Ordinal);
            _title.Text = marker >= 0 ? title[..marker] : title; _title.Width = 148; _title.FontSize = 12;
            _episode.Text = marker >= 0 ? title[(marker + 3)..] : ""; _episode.Visibility = marker >= 0 ? Visibility.Visible : Visibility.Collapsed;
            _measure.Text = _title.Text; _measure.FontSize = 12; _measure.FontWeight = _title.FontWeight;
            _measure.Measure(new Windows.Foundation.Size(148, double.PositiveInfinity));
            while (_measure.DesiredSize.Height > _measure.FontSize * 2.7 && _measure.FontSize > 8) { _measure.FontSize -= 0.5; _measure.Measure(new Windows.Foundation.Size(148, double.PositiveInfinity)); }
            _title.FontSize = _measure.FontSize;
            _root.MinHeight = marker >= 0 ? 80 : 58;
            _lastTitle = title; Animate(false);
        }
        _time.Text = (playing ? "▶ " : "Ⅱ ") + Clock(position) + " / " + Clock(duration);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, title + " · " + _time.Text);
        ToolTipService.SetToolTip(this, title + " · " + _time.Text);
        Animate(playing && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled);
    }
    static string Clock(long ms) => TimeSpan.FromMilliseconds(Math.Max(ms, 0)).ToString(ms >= 3600000 ? @"h\:mm\:ss" : @"mm\:ss");
    void Animate(bool play)
    {
        if (_animated == play) return; _animated = play;
        var wave = ElementCompositionPreview.GetElementVisual(_wave);
        var text = ElementCompositionPreview.GetElementVisual(_title);
        wave.StopAnimation("Offset.X"); text.StopAnimation("Offset.X");
        wave.Offset = Vector3.Zero; text.Offset = Vector3.Zero;
        if (!play) return;
        var pulse = wave.Compositor.CreateScalarKeyFrameAnimation(); pulse.InsertKeyFrame(0, -80); pulse.InsertKeyFrame(1, 0);
        pulse.Duration = TimeSpan.FromSeconds(2.4); pulse.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever; wave.StartAnimation("Offset.X", pulse);

    }
}

