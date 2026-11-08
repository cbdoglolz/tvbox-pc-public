using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace TVBoxForWindows.UI.Controls;
public sealed class FilterCountBadge : UserControl
{
    readonly Border _badge;
    readonly TextBlock _number;
    public int Count { set { _badge.Visibility=value>0?Visibility.Visible:Visibility.Collapsed;_number.Text=value.ToString();Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this,"更多筛选，已选"+value+"项"); } }
    public FilterCountBadge() {
        var grid=new Grid { Margin=new(0,3,6,0) };grid.Children.Add(new TextBlock {Text="更多筛选 ▾",Margin=new(0,3,8,0)});
        _number=new TextBlock {FontSize=10,FontWeight=Microsoft.UI.Text.FontWeights.Bold,Foreground=new SolidColorBrush(Microsoft.UI.Colors.White),HorizontalAlignment=HorizontalAlignment.Center};
        _badge=new Border {MinWidth=17,Height=17,Padding=new(3,0,3,0),CornerRadius=new(9),Background=new SolidColorBrush(Windows.UI.Color.FromArgb(255,132,86,213)),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,-6,-8,0),Child=_number,Visibility=Visibility.Collapsed};
        grid.Children.Add(_badge);Content=grid;
    }
}
