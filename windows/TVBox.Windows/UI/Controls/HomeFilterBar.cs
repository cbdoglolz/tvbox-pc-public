using Microsoft.UI.Xaml.Controls;
using TVBoxForWindows.Models;
namespace TVBoxForWindows.UI.Controls;

// Same filter fields, semantics and sorting as all other catalog lists.
public sealed class HomeFilterBar : UserControl
{
    readonly CatalogFilterBar _bar = new();
    public event Action<CatalogQuery> Changed;
    CatalogQuery _query = new();
    public HomeFilterBar()
    {
        Content = _bar;
        _bar.QueryChanged += q => { _query = q; Changed?.Invoke(q); };
        _bar.ClearRequested += () => { var q = _query.ClearFilters(); SetQuery(q); Changed?.Invoke(q); };
        _bar.ResetRequested += () => { var q = _query.ClearFilters(); q.SortDirection = "default"; SetQuery(q); Changed?.Invoke(q); };
    }
    public void SetQuery(CatalogQuery q) { _query = q; _bar.SetQuery(q); }
}
