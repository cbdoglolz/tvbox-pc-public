using Microsoft.UI.Xaml.Controls;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.UI.Controls;

public sealed class SortDirectionButton : Button
{
    CatalogQuery _query = new();
    public string Direction => _query.SortDirection;
    public event Action DirectionChanged;
    public SortDirectionButton()
    {
        Click += (_, _) => { _query.CycleDirection(); Refresh(); DirectionChanged?.Invoke(); };
    }
    public void SetQuery(CatalogQuery query) { _query = query.Copy(); Refresh(); }
    public void SelectField(string sort)
    {
        if (_query.Sort == sort) return;
        _query.Sort = sort; _query.SortDirection = "desc"; _query.SortAscending = false; Refresh();
    }
    void Refresh()
    {
        Content = _query.DirectionLabel;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, "排序方向：" + _query.DirectionLabel);
        ToolTipService.SetToolTip(this, "点击循环：默认 → 正序 → 逆序 → 默认。默认恢复栏目顺序，不清除筛选；当前仅对已检索结果排序。");
    }
}
