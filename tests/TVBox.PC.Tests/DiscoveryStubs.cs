using TVBoxForWindows.Models;

namespace TVBoxForWindows
{
    internal static class App { public static void Post(Action action) => action(); }
}
namespace TVBoxForWindows.Models
{
    public class Result
    {
        public List<Vod> List { get; set; } = new();
        public string Error { get; set; } = "";
        public string GetMsg() => Error;
    }
}
namespace TVBoxForWindows.Engine
{
    internal class VodConfigService
    {
        public static int Cid => 0;
        public static VodConfigService Instance { get; } = new();
        public List<Site> Sites { get; set; } = new();
    }
    internal static class SiteService
    {
        public static Func<Site, string, Task<Result>> Search = (_, _) => Task.FromResult(new Result());
        public static Func<Site, string, Task<Result>> Detail = (_, _) => Task.FromResult(new Result());
        public static Task<Result> SearchContent(Site site, string keyword, bool quick) => Search(site, keyword);
        public static Task<Result> DetailContent(Site site, string id) => Detail(site, id);
    }
}
