namespace TVBoxForWindows.Engine;
internal sealed class SpiderLoader
{
    public static SpiderLoader Instance {get;}=new();
    public Task<NoLiveSpider> GetLiveSpider(Models.Live live)=>throw new InvalidOperationException("Tests must never execute an external live script");
}
internal sealed class NoLiveSpider {public Task<string> LiveContent(string url)=>throw new InvalidOperationException("Unexpected live runtime");}
