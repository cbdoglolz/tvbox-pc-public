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
    readonly Grid _root = new() { Width = 150, Height = 58, CornerRadius = new(8) };
    readonly Grid _fill = new() { Width = 0, HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromArgb(115, 126, 82, 223)) };
    readonly TextBlock _title = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
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
        var clip = new Grid { Height = 18 };
        clip.Clip = new RectangleGeometry { Rect = new(0, 0, 134, 18) }; clip.Children.Add(_title);
        labels.Children.Add(clip); labels.Children.Add(_time); _root.Children.Add(labels); Content = _root;
        Unloaded += (_, _) => Animate(false);
    }
    public void Update(string title, long position, long duration, bool playing)
    {
        _fill.Width = 150 * (duration > 0 ? Math.Clamp((double)position / duration, 0, 1) : 0);
        _fill.Clip = new RectangleGeometry { Rect = new(0, 0, _fill.Width, 58) };
        if (_lastTitle != title) {
            _title.Text = title; _title.Width = double.NaN;
            _title.Measure(new Windows.Foundation.Size(double.PositiveInfinity, 18)); _title.Width = _title.DesiredSize.Width;
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
        if (_title.Width > 134)
        {
            var marquee = text.Compositor.CreateScalarKeyFrameAnimation(); marquee.InsertKeyFrame(0, 0); marquee.InsertKeyFrame(0.2f, 0);
            marquee.InsertKeyFrame(0.8f, (float)(134 - _title.Width)); marquee.InsertKeyFrame(1, 0);
            marquee.Duration = TimeSpan.FromSeconds(12); marquee.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever; text.StartAnimation("Offset.X", marquee);
        }
    }
}

