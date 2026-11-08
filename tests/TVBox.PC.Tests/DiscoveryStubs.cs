using TVBoxForWindows.Models;

namespace TVBoxForWindows
{
    internal static class App { public static void Post(Action action) => action(); public static NoticeStub Main {get;}=new(); }
    internal class NoticeStub { public void ShowNotice(string message) {} }
}
namespace TVBoxForWindows.Models
{
    public class Sub { public string Lang { get; set; } = ""; public string Name { get; set; } = ""; public string Url { get; set; } = ""; }
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
    internal static class NodeRuntime { public static bool EnsureNode() => false; public static string NodePath => ""; }
    internal static class SiteService
    {
        public static Func<Site, string, Task<Result>> Search = (_, _) => Task.FromResult(new Result());
        public static Func<Site, string, Task<Result>> Detail = (_, _) => Task.FromResult(new Result());
        public static Task<Result> SearchContent(Site site, string keyword, bool quick) => Search(site, keyword);
        public static Task<Result> DetailContent(Site site, string id) => Detail(site, id);
    }
}
