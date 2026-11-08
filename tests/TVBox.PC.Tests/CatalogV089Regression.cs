using System.Text.Json;
using Net=TVBoxForWindows.Net;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;
internal static class CatalogV089Regression
{
    public static async Task RunAsync(Action<bool,string> check)
    {
        var q=new CatalogQuery{Provider="douban",Category="anime",ScopeId="anime",Keyword="保留片名",YearFrom=2025,YearTo=2025,Countries=new(){"JP","JP"},Genres=new(){"animation"},CompanyId=10,CompanyName="公司",IncludeUnknownYear=true};
        check(CatalogFacetRules.Count(q)==5,"v089 active filter count includes one year range, distinct country, genre, company and unknown-year flag");
        var clear=CatalogFacetRules.Clear(q);
        check(CatalogFacetRules.Count(clear)==0&&clear.Keyword==q.Keyword&&clear.ScopeId==q.ScopeId&&q.YearFrom==2025,"v089 clear all facets preserves page and search without mutating original");
        var dir=Path.Combine(AppPaths.Root,"update-receipt-tests");var op=Path.Combine(dir,"updates","one");Directory.CreateDirectory(op);var installation=Path.Combine(dir,"app");
        File.WriteAllText(Path.Combine(op,"result.json"),JsonSerializer.Serialize(new{State="installed",Version="0.8.9"}));
        File.WriteAllText(Path.Combine(op,"install.json"),JsonSerializer.Serialize(new{InstallRoot=installation,FromVersion="0.8.8"}));
        check(UpdateStartup.Consume(dir,installation+"-other","0.8.9")==null,"v089 unrelated installation cannot consume another update receipt");
        check(UpdateStartup.Consume(dir,installation,"0.8.8")==null,"v089 current version must equal installed receipt");
        var receipt=UpdateStartup.Consume(dir,installation,"0.8.9");
        check(receipt is{From:"0.8.8",To:"0.8.9"}&&UpdateStartup.Consume(dir,installation,"0.8.9")==null,"v089 successful upgrade notice consumes from-to receipt exactly once");
        var old=Path.Combine(dir,"updates","legacy");Directory.CreateDirectory(Path.Combine(old,"backup","libs"));
        File.Copy(typeof(CatalogV089Regression).Assembly.Location,Path.Combine(old,"backup","libs","TVBox.dll"));
        File.WriteAllText(Path.Combine(old,"result.json"),"{\"State\":\"installed\",\"Version\":\"9.0.0\"}");
        File.WriteAllText(Path.Combine(old,"install.json"),JsonSerializer.Serialize(new{InstallRoot=installation}));
        check(UpdateStartup.Consume(dir,installation,"9.0.0")?.From=="1.0.0","v089 old hidden updater without FromVersion recovers prior assembly version from backup");
        var parsed=PrivateSubscriptions.ParseLives("{\"lives\":[{\"name\":\"体育列表\",\"url\":\"../sports.m3u\",\"ua\":\"fixture\",\"header\":{\"Referer\":\"https://example.test/\"}},{\"name\":\"unsafe\",\"url\":\"file:///x\"}]}","https://example.test/config/main.json");
        check(parsed.Count==1&&parsed[0].Url=="https://example.test/sports.m3u"&&parsed[0].BuildHeaders()["User-Agent"]=="fixture","v089 private live import resolves relative URLs, preserves headers and rejects file schemes");
        var live=new Live{Groups=new(){new(){Name="央视",Channel=new(){new(){Name="CCTV5+"},new(){Name="CCTV16"},new(){Name="CCTV1"}}},new(){Name="体育赛事",Channel=new(){new(){Name="频道A"}}},new(){Name="普通",Channel=new(){new(){Name="ESPN2"},new(){Name="电视剧"}}}}};
        SportsChannels.Filter(live);
        check(live.Groups.SelectMany(g=>g.Channel).Select(c=>c.Name).SequenceEqual(new[]{"CCTV5+","CCTV16","频道A","ESPN2"})&&live.Groups[0].Channel[0].Live==live,"v089 sports channel filtering retains sport groups and binds channels to native live player");
        var running=0;var maxRunning=0;var stripped=false;
        try {
            Net.ProbeHandler.BeforeSend=async(_,ct)=>{var count=Interlocked.Increment(ref running);maxRunning=Math.Max(maxRunning,count);try{await Task.Delay(15,ct);}finally{Interlocked.Decrement(ref running);}};
            Net.ProbeHandler.ResponseOverride=req=>{
                if(req.RequestUri.Host=="redirect.example") {var redirect=new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Redirect);redirect.Headers.Location=new Uri("https://target.example/list");return redirect;}
                if(req.RequestUri.Host=="target.example")stripped=!req.Headers.Contains("Authorization")&&!req.Headers.Contains("Cookie");
                var text=req.RequestUri.Host=="bad.example"?"<html>unavailable</html>":"#EXTM3U\n#EXTINF:-1 group-title=\"央视\",CCTV16\nhttps://media.example/live.m3u8\n#EXTINF:-1 group-title=\"央视\",CCTV1\nhttps://media.example/other.m3u8";
                return new(System.Net.HttpStatusCode.OK){Content=new System.Net.Http.StringContent(text)};
            };
            var sources=Enumerable.Range(0,6).Select(i=>new Live{Name="fixture"+i,Url=$"https://sport{i}.example/list"}).ToList();sources.Add(new(){Name="invalid",Url="https://bad.example/list"});sources.Add(new(){Name="redirect",Url="https://redirect.example/list",Header=new(){["Cookie"]="fixture",["Authorization"]="fixture"}});
            var loaded=await TVBoxForWindows.Live.SportsCatalog.Load(sources,default);
            check(loaded.Count==7&&loaded.All(l=>l.Groups.Single().Channel.Single().Name=="CCTV16")&&maxRunning<=3&&maxRunning>1,"v089 sports concurrently validates at most three playlists, drops HTML and exposes only real sport channels");
            check(stripped,"v089 cross-host playlist redirect never forwards private cookie or authorization");
        }finally{Net.ProbeHandler.BeforeSend=null;Net.ProbeHandler.ResponseOverride=null;}
        const string anime="""{"data":{"Page":{"pageInfo":{"hasNextPage":true},"media":[{"id":900,"title":{"native":"テスト","romaji":"Test","english":null},"synonyms":[],"format":"TV","startDate":{"year":2025,"month":4,"day":2},"episodes":12,"averageScore":null,"coverImage":{"large":"https://example.test/poster.jpg"},"bannerImage":null,"genres":["Action"],"countryOfOrigin":"JP","popularity":100}]}}}""";
        var page=AnimeCatalog.Parse(anime);var item=page.Items.Single();
        check(item.WorkKey=="anilist@900"&&item.Year=="2025"&&item.ReleaseDate=="2025-04-02"&&page.HasMore&&!item.GetRating("douban").HasScore,"v089 new anime preserves year, source identity, null scores and pagination");
        AnimeCatalog.Supplement(page.Items,"""{"data":[{"name":"テスト","name_cn":"测试动画","date":"2025-04-02","summary":"中文简介"},{"name":"テスト","name_cn":"错误年份","date":"2024-04-02"}]}""");
        check(item.Title=="测试动画"&&item.Overview=="中文简介"&&item.OriginalTitle=="テスト","v089 Chinese anime title joins exact original title and matching premiere year");
        item.Rating=8;check(!item.GetRating("douban").HasScore,"v089 third-party anime score never masquerades as a Douban rating");
        var provider=new AnimeFixtureProvider();var coordinator=new CatalogQueryCoordinator(provider,new MetadataStore(Path.Combine(dir,"catalog.sqlite")));
        var seasonal=await coordinator.LoadNextAsync(new(){Query=new(){Provider="anilist",Category="anime",YearFrom=2025,YearTo=2025}},null,default);
        check(provider.Requests==1&&seasonal.Items.Count==1&&!seasonal.HasMore,"v089 seasonal catalogue uses one pagination shard rather than duplicate film and series requests");
        seasonal.Candidates.Add(new(){Provider="anilist",Id="older",Title="已加载后续页",Year="2025",Kind="tv",Genres=new(){"animation"}});seasonal.Offset=1234;seasonal.NextPages["anime:"]=9;seasonal.IsStale=true;
        var refreshed=await coordinator.RefreshHeadAsync(seasonal,default);
        check(!refreshed.IsStale&&refreshed.Items.Count==2&&refreshed.Offset==1234&&refreshed.NextPages["anime:"]==9,"v089 fresh recommendation head clears stale flag while preserving loaded waterfall, pagination and scroll position");
    }
    sealed class AnimeFixtureProvider:ICatalogQueryProvider
    {
        public int Requests;
        public Task<CatalogItem> DetailsAsync(CatalogItem w,CancellationToken ct)=>Task.FromResult(w);
        public Task<CatalogProviderPage> FetchAsync(CatalogQuery q,string kind,int page,string country,CancellationToken ct){Requests++;return Task.FromResult(new CatalogProviderPage(new(){new(){Provider="anilist",Id="fixture",Title="fixture",Year="2025",Kind="tv",Genres=new(){"animation"}}},false,1,"fixture"));}
    }
}
