using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TVBoxForWindows.UI;

public static class UiHints
{
    static readonly HashSet<ToolTip> OpenTips = new();
    public const string SearchScope = "仅本分类：在当前电影、剧集等分类内搜索。全部电影和剧集：跨分类搜索所有影视作品；不会搜索NAS文件或播放来源。已有年份、地区等条件仍然生效。";
    public static void Explain(FrameworkElement element, string text)
    {
        if (ToolTipService.GetToolTip(element) is ToolTip current) { current.Content = text; return; }
        var tip = new ToolTip { Content = text };
        tip.Opened += (_, _) => OpenTips.Add(tip); tip.Closed += (_, _) => OpenTips.Remove(tip);
        ToolTipService.SetToolTip(element, tip);
    }
    public static void CloseAll() { foreach (var tip in OpenTips.ToList()) tip.IsOpen = false; }
    public static void Attach(DependencyObject root)
    {
        if (root is FrameworkElement element && ToolTipService.GetToolTip(element) is string text) Explain(element, text);
        if (root is ComboBox combo && ToolTipService.GetToolTip(combo) == null)
        {
            var name = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(combo);
            if (name.Length > 0) Explain(combo, name switch
            {
                "目录搜索范围" => SearchScope,
                "评分体系" => "选择最低评分和评价人数使用的平台；各平台分数不混合，未知评分显示-。",
                "排序方向" => "升序从小到大/从早到晚/A到Z；降序相反。未知数值始终排在后面。",
                "排序字段" => "按热度、年份、上映日期、分数、人数或名称排序。中文名称按文化/拼音顺序，排序覆盖已经检索到的结果。",
                "资料目录提供方" => "豆瓣和TMDB提供影片资料与海报；播放来源由播放器独立查找。TMDB在设置里选填个人Key。",
                _ => "选择" + name + "，其他筛选条件继续保留。"
            });
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) Attach(VisualTreeHelper.GetChild(root, i));
    }
}
