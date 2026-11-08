using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TVBoxForWindows.Core;

namespace TVBoxForWindows.UI.Controls;

public record GlobalSuggestion(string Query, List<string> Hot = null)
{
    public string Title => Hot != null ? "近期热门" : Query;
    public override string ToString() => Query;
}
public sealed class SearchSuggestionRow : UserControl
{
    public SearchSuggestionRow() { DataContextChanged += (_, _) => Render(); }
    void Render()
    {
        if (DataContext is not GlobalSuggestion suggestion) return;
        if (suggestion.Hot != null) {
            var panel = new StackPanel { Spacing = 7, Width = 390 };
            panel.Children.Add(new TextBlock { Text = "近期热门", FontSize = 12, Opacity = 0.7 });
            var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 130, ItemHeight = 50, MaximumRowsOrColumns = 3 };
            foreach (var query in suggestion.Hot) { var button = new Button { Content = new TextBlock { Text = query, TextWrapping = TextWrapping.Wrap, MaxLines = 2, FontSize = 12 }, Margin = new(0, 0, 5, 4), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch }; button.PointerPressed += (_, e) => e.Handled = true; ToolTipService.SetToolTip(button, query); button.Click += (_, _) => App.Main.SubmitGlobalSearch(query); grid.Children.Add(button); }
            panel.Children.Add(grid); Content = panel;
        } else {
            var grid = new Grid { Width = 390, Padding = new(0, 3, 0, 3) }; grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var text = new TextBlock { Text = suggestion.Query, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }; grid.Children.Add(text);
            var remove = new Button { Content = "×", FontSize = 20, Padding = new(7, 0, 7, 0), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0) }; Grid.SetColumn(remove, 1); remove.Click += (_, _) => { SearchHistory.Remove(suggestion.Query); App.Main.RefreshSearchSuggestions(); }; ToolTipService.SetToolTip(remove, "删除这条搜索记录"); grid.Children.Add(remove); Content = grid;
        }
    }
}
