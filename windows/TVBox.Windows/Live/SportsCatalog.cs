using System.Net.Http;
using System.Text;
using TVBoxForWindows.Core;
namespace TVBoxForWindows.Live;
public static class SportsCatalog
{
    static readonly Dictionary<string,(DateTimeOffset Time,Models.Live Value)> Cache=new();
    public static async Task<List<Models.Live>> Load(IEnumerable<Models.Live> sources,CancellationToken ct)
    {
        using var gate=new SemaphoreSlim(3);
        var loaded=await Task.WhenAll(sources.Take(100).Select(async live=>{
            await gate.WaitAsync(ct);
            try {
                var key=live.Url+JsonUtil.Serialize(live.BuildHeaders());
                lock(Cache)if(Cache.TryGetValue(key,out var cached)&&DateTimeOffset.UtcNow-cached.Time<TimeSpan.FromMinutes(15))return JsonUtil.Deserialize<Models.Live>(JsonUtil.Serialize(cached.Value));
                // Sports playlists are data; Android/JS portals are handled by the site compatibility layer.
                if(!string.IsNullOrWhiteSpace(live.Api))return null;
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var address=new Uri(live.Url);var headers=live.BuildHeaders();
                for(var redirects=0;redirects<5;redirects++) {
                    using var req=new HttpRequestMessage(HttpMethod.Get,address);
                    foreach(var pair in headers)req.Headers.TryAddWithoutValidation(pair.Key,pair.Value);
                    using var response=await Net.HttpUtil.Client(Net.NetworkConfig.GetProxyFor(address.Host),redirect:false).SendAsync(req,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
                    if((int)response.StatusCode is >=300 and <400&&response.Headers.Location is {} location) {
                        var next=new Uri(address,location);if(next.Scheme is not ("http" or "https")||address.Scheme=="https"&&next.Scheme!="https")return null;
                        if(address.Authority!=next.Authority||address.Scheme!=next.Scheme)foreach(var h in headers.Keys.Where(h=>h.Equals("Authorization",StringComparison.OrdinalIgnoreCase)||h.Equals("Cookie",StringComparison.OrdinalIgnoreCase)).ToList())headers.Remove(h);
                        address=next;continue;
                    }
                    response.EnsureSuccessStatusCode();await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);using var memory=new MemoryStream();var buffer=new byte[16384];int n;
                    while((n=await stream.ReadAsync(buffer,timeout.Token))>0){if(memory.Length+n>4*1024*1024)throw new IOException("频道表过大");memory.Write(buffer,0,n);}
                    LiveParser.Text(live,Encoding.UTF8.GetString(memory.ToArray()));Models.SportsChannels.Filter(live);
                    if(live.Groups.Count==0)return null;
                    lock(Cache){if(Cache.Count>=100)Cache.Clear();Cache[key]=(DateTimeOffset.UtcNow,live);}
                    return live;
                }
                return null;
            }catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}catch{return null;}finally{gate.Release();}
        }));
        return loaded.Where(l=>l!=null).OrderByDescending(l=>l.Groups.SelectMany(g=>g.Channel).Any(c=>Models.SportsChannels.IsBroadcast(c.Name))).ThenByDescending(l=>l.Groups.Sum(g=>g.Channel.Count)).ToList();
    }
}
