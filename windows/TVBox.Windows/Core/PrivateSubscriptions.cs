using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TVBoxForWindows.Models;
namespace TVBoxForWindows.Core;
public sealed class PrivateFeed
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "私人订阅";
    public string Url { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool AllowHttp { get; set; }
    public List<Site> Sites { get; set; } = new();
    public DateTimeOffset Checked { get; set; }
    public string Status { get; set; } = "尚未检查";
}
public record SubscriptionDelta(int Added,int Changed,int Removed);
public static class PrivateSubscriptions
{
    static readonly SemaphoreSlim Gate = new(1,1);
    static List<PrivateFeed> _feeds;
    static List<PrivateFeed> Feeds => _feeds ??= ServiceCredentials.Read("private_subscriptions", () => new List<PrivateFeed>());
    public static event Action Changed;
    public static IReadOnlyList<PrivateFeed> Items => Feeds.ToList();
    public static IEnumerable<Site> Sites => Feeds.Where(f=>f.Enabled).SelectMany(f=>f.Sites);
    public static bool IsPrivate(string key) => key?.StartsWith("private:",StringComparison.Ordinal)==true;
    public static bool Supported(Site site) => site.Type is 0 or 1 or 4 || site.Type==3 && (site.Api.EndsWith(".js",StringComparison.OrdinalIgnoreCase) || site.Api.Contains(".js?",StringComparison.OrdinalIgnoreCase) || site.Ext.EndsWith(".js",StringComparison.OrdinalIgnoreCase));
    public static bool Compatible(string key) => !IsPrivate(key) || Sites.FirstOrDefault(s=>s.Key==key) is {} site && Supported(site);
    public static bool IsSports(Site site) => new[] { "csp_KanqiuGuard","csp_DoubaoGuard","csp_LiveGzGuard","csp_AllliveGuard" }.Contains(site.Api,StringComparer.OrdinalIgnoreCase);
    static void Save() { ServiceCredentials.Save("private_subscriptions",Feeds); Changed?.Invoke(); }
    public static PrivateFeed Add(string name,string url,bool allowHttp=false)
    {
        if(!Uri.TryCreate(url.Trim(),UriKind.Absolute,out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length>0)throw new ArgumentException("请输入有效的HTTP(S)订阅地址；账号不要嵌入网址。");
        if(uri.Scheme!="https"&&!allowHttp)throw new ArgumentException("请优先使用HTTPS；只有明确允许明文传输后才能使用HTTP。");
        if(Feeds.Any(f=>f.Url==uri.AbsoluteUri))throw new ArgumentException("该订阅已存在。");
        var feed=new PrivateFeed { Name=string.IsNullOrWhiteSpace(name)?"私人订阅":name.Trim(),Url=uri.AbsoluteUri,AllowHttp=allowHttp };
        Feeds.Add(feed);Save();return feed;
    }
    public static void SetEnabled(PrivateFeed feed,bool enabled){feed.Enabled=enabled;Save();}
    public static List<Site> Parse(string json,string baseUrl,string id)
    {
        var node=JsonNode.Parse(json);if(node?["sites"] is not JsonArray array || array.Count==0 || array.Count>500)throw new IOException("订阅没有有效站点列表");
        var basis=new Uri(baseUrl);var spider=node["spider"]?.ToString()??"";var list=new List<Site>();
        string Resolve(string value){if(value.Length==0)return value;var p=value.Split(';',2);if(Uri.TryCreate(basis,p[0],out var u)&&u.Scheme is "https" or "http")return u.AbsoluteUri+(p.Length>1?";"+p[1]:"");return value;}
        foreach(var raw in array){var site=ModelJson.Parse<Site>(raw);if(site==null||site.Key.Length==0||site.Api.Length==0)continue;
            site.Key="private:"+id+":"+site.Key;if(list.Any(s=>s.Key==site.Key))continue;
            if(!site.Api.StartsWith("csp_",StringComparison.OrdinalIgnoreCase))site.Api=Resolve(site.Api);
            site.Jar=Resolve(site.Jar.Length>0?site.Jar:spider);
            if(site.Ext.StartsWith('/')||site.Ext.StartsWith("./")||site.Ext.StartsWith("../"))site.Ext=Resolve(site.Ext);
            list.Add(site);
        }
        if(list.Count==0)throw new IOException("订阅没有有效站点");return list;
    }
    public static SubscriptionDelta Difference(List<Site> old,List<Site> current)
    {
        JsonNode Canonical(JsonNode node) => node switch {
            JsonObject obj => new JsonObject(obj.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>KeyValuePair.Create(p.Key,p.Value==null?null:Canonical(p.Value)))),
            JsonArray array => new JsonArray(array.Select(n=>n==null?null:Canonical(n)).ToArray()),
            _ => node.DeepClone()
        };
        string Hash(Site s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(JsonNode.Parse(JsonUtil.Serialize(s))).ToJsonString())));
        var before=old.ToDictionary(s=>s.Key,Hash);var after=current.ToDictionary(s=>s.Key,Hash);
        return new(after.Keys.Count(k=>!before.ContainsKey(k)),after.Count(p=>before.TryGetValue(p.Key,out var hash)&&hash!=p.Value),before.Keys.Count(k=>!after.ContainsKey(k)));
    }
    public static async Task<SubscriptionDelta> Refresh(PrivateFeed feed,CancellationToken ct=default)
    {
        await Gate.WaitAsync(ct);
        try {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var uri=new Uri(feed.Url);
            using var response=await Net.HttpUtil.Client(redirect:false).GetAsync(uri,System.Net.Http.HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            // Subscription authentication and transport are never silently redirected or downgraded.
            response.EnsureSuccessStatusCode();await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);using var memory=new MemoryStream();var buffer=new byte[16384];int n;
            while((n=await stream.ReadAsync(buffer,timeout.Token))>0){if(memory.Length+n>4*1024*1024)throw new IOException("订阅内容过大");memory.Write(buffer,0,n);}
            var sites=Parse(Encoding.UTF8.GetString(memory.ToArray()),feed.Url,feed.Id);var delta=Difference(feed.Sites,sites);
            feed.Sites=sites;feed.Checked=DateTimeOffset.Now;feed.Status=$"共{sites.Count}站，{sites.Count(Supported)}站可由当前Windows引擎处理";Save();return delta;
        } catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
          catch {feed.Status="订阅暂不可用，保留上次成功的站点；可稍后重试。";Save();throw new IOException(feed.Status);}
        finally{Gate.Release();}
    }
    public static async Task RefreshAtStartup()
    {
        foreach(var feed in Items.Where(f=>f.Enabled))try{var delta=await Refresh(feed);if(delta.Added+delta.Changed+delta.Removed>0)App.Post(()=>App.Main.ShowNotice($"{feed.Name}已更新：新增{delta.Added}、修改{delta.Changed}、移除{delta.Removed}站"));}catch{}
    }
}
