using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace TVBoxForWindows.UI.Controls;

public sealed class RatingStrip : UserControl
{
    public RatingStrip()
    {
        var grid = new Grid { Padding = new Thickness(3, 6, 3, 6), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(235, 18, 18, 20)) };
        var fields = new[] { "DoubanScore", "TmdbScore" };
        for (var i = 0; i < fields.Length; i++)
        {
            grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var text = new TextBlock { FontSize = 10.5, Foreground = new SolidColorBrush(i == 0 ? Windows.UI.Color.FromArgb(255, 113, 221, 148) : Windows.UI.Color.FromArgb(255, 105, 204, 235)), HorizontalAlignment = HorizontalAlignment.Center };
            text.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(fields[i]) });
            Grid.SetColumn(text, i); grid.Children.Add(text);
        }
        UiHints.Explain(this, "豆瓣 / TMDB 独立评分；- 表示尚无可靠评分。资料在后台补齐，评分不相互替代。");
        Content = grid;
    }
}
