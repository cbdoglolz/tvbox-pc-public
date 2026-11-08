using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;
using Windows.UI;

namespace TVBoxForWindows.UI.Controls;

public sealed class ThemeOrbit : UserControl
{
    readonly Grid _orbit = new() { Width = 28, Height = 84, VerticalAlignment = VerticalAlignment.Top };
    bool _initialized, _dark;
    float _degrees;
    public ThemeOrbit()
    {
        var viewport = new Grid { Width = 28, Height = 28, Clip = new RectangleGeometry { Rect = new(0, 0, 28, 28) } };
        var sun = new Grid { Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Top };
        sun.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Color.FromArgb(255, 205, 103, 25)) });
        for (var i = 0; i < 8; i++) { var angle = Math.PI * i / 4; sun.Children.Add(new Line { X1 = 14 + 9 * Math.Cos(angle), Y1 = 14 + 9 * Math.Sin(angle), X2 = 14 + 12 * Math.Cos(angle), Y2 = 14 + 12 * Math.Sin(angle), Stroke = new SolidColorBrush(Color.FromArgb(255, 205, 103, 25)), StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }); }
        var moon = new PathIcon { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Top, Margin = new(2, 58, 2, 0), Foreground = new SolidColorBrush(Color.FromArgb(255, 57, 94, 177)), Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlReader.Load("<PathGeometry xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><PathFigure StartPoint='21,12.79' IsClosed='True'><ArcSegment Point='11.21,3' Size='9,9' IsLargeArc='True' SweepDirection='Clockwise'/><ArcSegment Point='21,12.79' Size='7,7' SweepDirection='Counterclockwise'/></PathFigure></PathGeometry>") };
        _orbit.Children.Add(sun); _orbit.Children.Add(moon); viewport.Children.Add(_orbit); Content = viewport;
    }
    public void SetDark(bool dark)
    {
        var visual = ElementCompositionPreview.GetElementVisual(_orbit); visual.CenterPoint = new(14, 42, 0);
        if (!_initialized) { _initialized = true; _dark = dark; _degrees = dark ? 180 : 0; visual.RotationAngleInDegrees = _degrees; return; }
        if (_dark == dark) return; _dark = dark; _degrees += 180;
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) { visual.RotationAngleInDegrees = _degrees; return; }
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation(); animation.InsertKeyFrame(1, _degrees); animation.Duration = TimeSpan.FromMilliseconds(560); visual.StartAnimation("RotationAngleInDegrees", animation);
    }
}
public static class ExternalLinkIcon
{
    public static PathIcon Create() => new() { Width = 16, Height = 16, Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlReader.Load("<PathGeometry xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><PathFigure StartPoint='14,2' IsClosed='True'><LineSegment Point='23,2'/><LineSegment Point='23,11'/><LineSegment Point='20,11'/><LineSegment Point='20,7'/><LineSegment Point='10,17'/><LineSegment Point='7,14'/><LineSegment Point='17,5'/><LineSegment Point='14,5'/></PathFigure><PathFigure StartPoint='2,6' IsClosed='True'><LineSegment Point='12,6'/><LineSegment Point='12,9'/><LineSegment Point='5,9'/><LineSegment Point='5,20'/><LineSegment Point='17,20'/><LineSegment Point='17,13'/><LineSegment Point='20,13'/><LineSegment Point='20,23'/><LineSegment Point='2,23'/></PathFigure></PathGeometry>") };
}
