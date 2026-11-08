namespace TVBoxForWindows.Models;
public static class CatalogFacetRules
{
    public static int Count(CatalogQuery q) => q.Countries.Distinct().Count()+q.Genres.Distinct().Count()
        +(q.YearFrom!=null||q.YearTo!=null?1:0)+(q.IncludeUnknownYear?1:0)
        +(q.MinimumRating!=null?1:0)+(q.MinimumVotes!=null?1:0)+(q.Kind.Length>0?1:0)
        +(q.Status.Length>0?1:0)+(q.AnimeForm.Length>0?1:0)+(q.PersonId>0?1:0)+(q.CompanyId>0?1:0);
    public static CatalogQuery Clear(CatalogQuery q) { var clear=q.ClearFilters();clear.PersonId=0;clear.PersonName="";clear.CompanyId=0;clear.CompanyName="";clear.CompanyIsNetwork=false;clear.SortDirection="default";clear.SortAscending=false;return clear; }
}
