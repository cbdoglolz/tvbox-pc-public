using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TVBoxForWindows.Core;
using Windows.UI;

namespace TVBoxForWindows.UI.Controls;

/// <summary>Code-native, theme-independent marks; no emoji glyph substitution.</summary>
public sealed class AdMarkGlyph : UserControl
{
    public static readonly DependencyProperty MarkProperty = DependencyProperty.Register(nameof(Mark), typeof(AdMark), typeof(AdMarkGlyph), new PropertyMetadata(AdMark.Unknown, (d, _) => ((AdMarkGlyph)d).Render()));
    public AdMark Mark { get => (AdMark)GetValue(MarkProperty); set => SetValue(MarkProperty, value); }
    public AdMarkGlyph() { Width = Height = 24; IsHitTestVisible = false; Render(); }
    static Brush Brush(byte r, byte g, byte b) => new SolidColorBrush(Color.FromArgb(255, r, g, b));
    void Render()
    {
        var grid = new Grid { Width = 24, Height = 24 };
        if (Mark == AdMark.Unknown) {
            grid.Children.Add(new Ellipse { Width = 21, Height = 21, Stroke = Brush(230, 176, 56), StrokeThickness = 1.5 });
            grid.Children.Add(new TextBlock { Text = "?", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = Brush(230, 176, 56), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        } else if (Mark == AdMark.TextAds) {
            grid.Children.Add((UIElement)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Fill='#F2C98A' Stroke='#AB763A' StrokeThickness='1.1' StrokeLineJoin='Round' Data='M2,5 Q7,3 12,6 Q17,3 22,5 L22,20 Q17,18 12,21 Q7,18 2,20 Z M12,6 L12,21'/>"));
            grid.Children.Add((UIElement)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Stroke='#E65060' StrokeThickness='2.1' StrokeStartLineCap='Round' StrokeEndLineCap='Round' Data='M8,9 L17,18 M17,9 L8,18'/>"));
        } else if (Mark == AdMark.Ads) {
            grid.Children.Add((UIElement)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Fill='#F06470' Data='M5,8 L3,1 10,6 14,6 21,1 19,8 A9,9 0 1 1 5,8 Z'/>"));
            grid.Children.Add((UIElement)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Stroke='#421328' StrokeThickness='1.7' StrokeStartLineCap='Round' StrokeEndLineCap='Round' Data='M6,11 L9,12 M15,12 L18,11 M8,17 Q12,20 16,17'/>"));
        } else {
            grid.Children.Add((UIElement)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Fill='#88D7D8' Data='M11,18 C2,19 0,11 1,5 C4,9 6,5 11,14 L11,18 Z M13,18 C22,19 24,11 23,5 C20,9 18,5 13,14 L13,18 Z'/>"));
            grid.Children.Add((UIElement)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Stroke='#347A96' StrokeThickness='1' Data='M3,10 Q4,14 9,15 M2,13 Q4,17 9,17 M21,10 Q20,14 15,15 M22,13 Q20,17 15,17'/>"));
            grid.Children.Add(new Ellipse { Width = 8, Height = 3, Stroke = Brush(233, 190, 82), StrokeThickness = 1.3, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 2, 0, 0) });
        }
        Content = grid;
    }
}
