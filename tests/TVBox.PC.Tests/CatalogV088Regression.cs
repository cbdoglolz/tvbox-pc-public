using System.Net;
using System.Net.Sockets;
using System.Text;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;
using TVBoxForWindows.Net;
using TVBoxForWindows.Player;

internal static class CatalogV088Regression
{
    public static async Task RunAsync(Action<bool,string> check)
    {
        var options=new Dictionary<string,string>();
        PlaybackNetworkPolicy.Apply(options,"https://media.example/v.m3u8?token=fixture","",false);
        check(options["http_persistent"]=="1" && options["reconnect_streamed"]=="0" && options["http_multiple"]=="1","v088 signed HLS reuses connections without reconnecting normal segment EOF");
        PlaybackNetworkPolicy.Apply(options,"https://media.example/v.mp4","",false);
        check(options["http_persistent"]=="0" && options["reconnect_streamed"]=="1" && !options.ContainsKey("http_multiple"),"v088 switching from HLS to MP4 resets reused demuxer options");
        PlaybackNetworkPolicy.Apply(options,"https://media.example/v.m3u8","hls",true);
        check(options["http_persistent"]=="0","v088 live transport retains its original reconnect behavior");
        var health=new BufferingHealth();for(var i=0;i<4;i++){health.Observe(true,i*1000);health.Observe(false,i*1000+32);}
        check(health.Count==0 && health.TotalMs==0,"v088 one-frame HLS timestamp transitions do not count as repeated buffering");
        var work=new CatalogItem {Title="回归剧",Id="880001",Kind="tv",SelectedSeason=1};
        var clean=new Site {Key="v088_clean",Name="干净源",Api="https://clean.example"};
        var text=new Site {Key="v088_text",Name="文字源",Api="https://text.example"};
        var inserted=new Site {Key="v088_insert",Name="插入源",Api="https://insert.example"};
        SourceAdPolicy.Set(work,clean,AdMark.Clean);SourceAdPolicy.Set(work,text,AdMark.TextAds);SourceAdPolicy.Set(work,inserted,AdMark.Ads);
        check((int)AdMark.Ads==2 && SourceAdPolicy.Rank(work,clean.Key)<SourceAdPolicy.Rank(work,text.Key) && SourceAdPolicy.Rank(work,text.Key)<SourceAdPolicy.Rank(work,inserted.Key),"v088 legacy ad value remains inserted ads and text ads rank above inserted video");
        var saved=VodConfigService.Instance.Sites;
        try {
            VodConfigService.Instance.Sites=new(){clean,text,inserted};
            var contacted=new System.Collections.Concurrent.ConcurrentBag<string>();
            SiteService.Search=(site,_)=>{contacted.Add(site.Key);return Task.FromResult(new Result{List=new(){new(){Id=site.Key,Name=work.Title,TypeName="日本剧"}}});};
            SiteService.Detail=(site,_)=>Task.FromResult(new Result{List=new(){new(){Id=site.Key,Name=work.Title,TypeName="日本剧",PlayFrom="hls",PlayUrl="第01集$https://audit.example/a.mp4"}}});
            var bytes=new byte[64];Encoding.ASCII.GetBytes("ftyp").CopyTo(bytes,4);HttpUtil.Responses["https://audit.example/a.mp4"]=bytes;
            var found=new List<PlaybackCandidate>();
            await SourceDiscovery.RunAsync(work,null,null,null,"S1EP01",found.Add,_=>{},default,ignoreCache:true,auditAll:true);
            check(new[]{clean,text,inserted}.All(s=>contacted.Contains(s.Key)) && found.Count==3,"v088 complete resume audit reports all enabled sites including marked ad fallbacks");
            contacted.Clear();found.Clear();
            await SourceDiscovery.RunAsync(work,null,null,null,"S1EP01",found.Add,_=>{},default,ignoreCache:true,adOnly:true);
            check(found.Count==1 && found[0].Site.Key==text.Key && !contacted.Contains(inserted.Key),"v088 final ad fallback still tries text ads before inserted video rather than racing them together");
        } finally {VodConfigService.Instance.Sites=saved;foreach(var site in new[]{clean,text,inserted})SourceAdPolicy.Set(work,site,AdMark.Unknown);}
        var cards=CatalogMetadataParser.DoubanSearch("""{"items":[{"target_type":"tv","target":{"id":"880002","title":"北斗神拳","card_subtitle":"2026 / 日本 / 动画"}},{"target_type":"movie","target":{"id":"880003","title":"日期回归","pubdate":["1995-10-01"]}},{"target_type":"movie","target":{"id":"880004","title":"无年份"}}]}""","回归");
        check(cards[0].Year=="2026" && cards[1].Year=="1995" && cards[1].CardSubtitle.Contains("1995") && cards[2].Year=="","v088 search cards recover subtitle and release years without inventing unknown years");
        await Subscriptions(check);
        await Relay(check);
    }
    static async Task Subscriptions(Action<bool,string> check)
    {
        const string address="https://subscription.example/private/config.json";
        var feed=PrivateSubscriptions.Add("隔离测试",address);
        const string json="""{"spider":"../spider.jar;md5;fixture","sites":[{"key":"sports","name":"体育","type":3,"api":"csp_KanqiuGuard"},{"key":"api","name":"点播","type":1,"api":"../api","header":{"B":"2","A":"1"}}]}""";
        var sites=PrivateSubscriptions.Parse(json,address,feed.Id);
        check(sites.Count==2 && sites[0].Key=="private:"+feed.Id+":sports" && sites[0].Jar=="https://subscription.example/spider.jar;md5;fixture" && sites[1].Api=="https://subscription.example/api","v088 private subscription namespaces stable keys and resolves relative APIs and jars");
        check(PrivateSubscriptions.IsSports(sites[0]) && !PrivateSubscriptions.Supported(sites[0]) && PrivateSubscriptions.Supported(sites[1]),"v088 Android sports spiders are explicitly unsupported instead of being treated as HTTP sources");
        check(!PrivateSubscriptions.Supported(new(){Type=3,Api="https://metadata.example/config.json"}) && PrivateSubscriptions.Supported(new(){Type=3,Api="https://scripts.example/source.js?version=1"}),"v088 JSON URLs cannot be misclassified as executable JavaScript adapters");
        var reorder=PrivateSubscriptions.Parse(json.Replace("\"B\":\"2\",\"A\":\"1\"","\"A\":\"1\",\"B\":\"2\""),address,feed.Id);
        check(PrivateSubscriptions.Difference(sites,reorder)==new SubscriptionDelta(0,0,0),"v088 harmless JSON dictionary ordering does not produce subscription updates");
        reorder[1].Name="点播更新";reorder.RemoveAt(0);reorder.Add(new(){Key="private:"+feed.Id+":new",Name="新增",Api="https://new.example",Type=1});
        check(PrivateSubscriptions.Difference(sites,reorder)==new SubscriptionDelta(1,1,1),"v088 subscription changes distinguish added changed and removed stable identities");
        HttpUtil.Responses[address]=Encoding.UTF8.GetBytes(json);await PrivateSubscriptions.Refresh(feed);
        check(feed.Sites.Count==2 && !PrivateSubscriptions.Compatible(sites[0].Key),"v088 imported unsupported sources remain visible while excluded from playback");
        HttpUtil.Responses.Remove(address);try{await PrivateSubscriptions.Refresh(feed);}catch(IOException){}
        check(feed.Sites.Count==2 && feed.Status.Contains("保留"),"v088 failed startup refresh preserves last-known-good private stations");
        var blob=Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(AppPaths.Root,"services.credential")));
        check(!blob.Contains(address) && ServiceCredentials.Read("private_subscriptions",()=>new List<PrivateFeed>()).Any(f=>f.Url==address),"v088 private subscription is encrypted on disk and decrypts for this user only");
        var refused=false;try{PrivateSubscriptions.Add("HTTP","http://clear.example/private");}catch(ArgumentException){refused=true;}
        check(refused,"v088 private feeds do not silently accept unencrypted HTTP");
        PrivateSubscriptions.SetEnabled(feed,false);
    }
    static async Task Relay(Action<bool,string> check)
    {
        var manifest="#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI=\"key.bin\"\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:2,\nseg.ts?token=fixture\n#EXT-X-ENDLIST\n";
        var mapped=new List<string>();HlsConnectionRelay.Rewrite(manifest,new Uri("https://origin.example/dir/v.m3u8"),u=>{mapped.Add(u.AbsoluteUri);return "mapped";});
        check(mapped.SequenceEqual(new[]{"https://origin.example/dir/key.bin","https://origin.example/dir/init.mp4","https://origin.example/dir/seg.ts?token=fixture"}),"v088 HLS rewriting preserves key map and signed segment resource identities");
        var reserve=new TcpListener(IPAddress.Loopback,0);reserve.Start();var port=((IPEndPoint)reserve.LocalEndpoint).Port;reserve.Stop();
        using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();
        var serving=Task.Run(async()=>{while(listener.IsListening)try{var context=await listener.GetContextAsync();if(!await HlsConnectionRelay.HandleAsync(context)){context.Response.StatusCode=404;context.Response.Close();}}catch(HttpListenerException){break;}catch(ObjectDisposedException){break;}});
        using var client=new HttpClient(new HttpClientHandler{UseProxy=false});
        var calls=new List<(string Host,bool Cookie,bool Auth,string Range)>();
        ProbeHandler.ResponseOverride=req=>{
            calls.Add((req.RequestUri.Host,req.Headers.Contains("Cookie"),req.Headers.Contains("Authorization"),req.Headers.Range?.ToString()??""));
            var result=new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=req};
            if(req.RequestUri.AbsolutePath.EndsWith(".m3u8"))result.Content=new StringContent(manifest,Encoding.UTF8,"application/vnd.apple.mpegurl");
            else if(req.RequestUri.AbsolutePath.EndsWith("key.bin")){result.StatusCode=HttpStatusCode.Found;result.Headers.Location=new Uri("https://cdn.example/key.bin");result.Content=new ByteArrayContent(Array.Empty<byte>());if(req.RequestUri.Host=="cdn.example"){result.StatusCode=HttpStatusCode.OK;result.Content=new ByteArrayContent(Enumerable.Range(0,16).Select(i=>(byte)i).ToArray());}}
            else {result.StatusCode=HttpStatusCode.PartialContent;result.Content=new ByteArrayContent(new byte[]{1,2,3,4});result.Content.Headers.ContentRange=new System.Net.Http.Headers.ContentRangeHeaderValue(0,3,100);}
            return result;
        };
        try {
            using var relay=new HlsConnectionRelay("https://origin.example/dir/v.m3u8",new(){["Cookie"]="private=fixture",["Authorization"]="Bearer fixture"},port);
            var text=await client.GetStringAsync(relay.Url);var lines=text.Split('\n');
            check(text.Contains("127.0.0.1")&&!text.Contains("origin.example")&&!text.Contains("token=fixture"),"v088 FFmpeg playlist uses opaque loopback tokens without exposing signed upstream URLs");
            var keyUrl=System.Text.RegularExpressions.Regex.Match(text,"URI=\"([^\"]+)\"").Groups[1].Value;
            check((await client.GetByteArrayAsync(keyUrl)).SequenceEqual(Enumerable.Range(0,16).Select(i=>(byte)i)),"v088 relay preserves real AES key bytes through a CDN redirect");
            check(calls.Any(c=>c.Host=="origin.example"&&c.Cookie&&c.Auth)&&calls.Any(c=>c.Host=="cdn.example"&&!c.Cookie&&!c.Auth),"v088 cross-origin HLS redirects strip Cookie and Authorization on every hop");
            using var request=new HttpRequestMessage(HttpMethod.Get,lines.First(l=>l.Contains("resource.ts")));request.Headers.Range=new(0,3);
            using var partial=await client.SendAsync(request);
            check(partial.StatusCode==HttpStatusCode.PartialContent && partial.Content.Headers.ContentRange?.ToString()=="bytes 0-3/100" && (await partial.Content.ReadAsByteArrayAsync()).SequenceEqual(new byte[]{1,2,3,4}) && calls.Last().Range=="bytes=0-3","v088 relay forwards byte ranges without altering media bytes");
            using var foreign=new HttpRequestMessage(HttpMethod.Get,relay.Url);foreign.Headers.TryAddWithoutValidation("Origin","https://untrusted.example");using var rejected=await client.SendAsync(foreign);
            check(rejected.StatusCode==HttpStatusCode.Forbidden,"v088 web origins cannot read a local media session");
            relay.Dispose();relay.Dispose();using var stale=await client.GetAsync(relay.Url);
            check(stale.StatusCode==HttpStatusCode.NotFound,"v088 stopping playback invalidates old relay tokens and repeated disposal is safe");
        } finally {ProbeHandler.ResponseOverride=null;listener.Stop();await serving;}
    }
}
