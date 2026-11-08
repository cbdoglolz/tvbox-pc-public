using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using TVBoxForWindows.Net;
namespace TVBoxForWindows.Player;

// Keep remote HTTP connections pooled even when FFmpeg's AES crypto wrapper
// closes the per-segment AVIO context. Media is streamed; nothing is written to disk.
public sealed class HlsConnectionRelay : IDisposable
{
    static readonly ConcurrentDictionary<string,HlsConnectionRelay> Sessions = new();
    readonly string _id = Guid.NewGuid().ToString("N");
    readonly ConcurrentDictionary<string,Uri> _urls = new();
    readonly ConcurrentDictionary<string,string> _tokens = new();
    readonly Dictionary<string,string> _headers;
    readonly Uri _root;
    readonly CancellationTokenSource _stop = new();
    readonly int _port;
    int _next;
    int _disposed;
    public string Url { get; }
    public HlsConnectionRelay(string url, Dictionary<string,string> headers, int port)
    {
        _root = new Uri(url); _headers = new(headers ?? new(),StringComparer.OrdinalIgnoreCase); _port = port;
        Sessions[_id] = this; Url = Map(_root);
    }
    string Map(Uri url)
    {
        if (url.Scheme is not ("http" or "https") || url.UserInfo.Length > 0) throw new IOException("不支持的HLS资源地址");
        if (_urls.Count >= 20000) throw new IOException("HLS播放列表过大");
        var token = _tokens.GetOrAdd(url.AbsoluteUri, _ => { var key = Interlocked.Increment(ref _next).ToString(); _urls[key] = url; return key; });
        var ext = Path.GetExtension(url.AbsolutePath); if (ext.Length > 10) ext = "";
        return $"http://127.0.0.1:{_port}/hls-session/{_id}/{token}/resource{ext}";
    }
    public static string Rewrite(string playlist, Uri basis, Func<Uri,string> map)
    {
        string Resolve(string value) => map(new Uri(basis,value));
        return string.Join("\n",playlist.Split('\n').Select(line => {
            var value=line.Trim(); if(value.Length==0) return line;
            if(!value.StartsWith('#')) return Resolve(value);
            return Regex.Replace(line, "URI=\"([^\"]+)\"", m => "URI=\""+Resolve(m.Groups[1].Value)+"\"");
        }));
    }
    public static async Task<bool> HandleAsync(HttpListenerContext context)
    {
        var path=context.Request.Url.AbsolutePath; if(!path.StartsWith("/hls-session/",StringComparison.Ordinal)) return false;
        try {
            var remote=context.Request.RemoteEndPoint?.Address; if(remote?.IsIPv4MappedToIPv6==true) remote=remote.MapToIPv4();
            var parts=path.Split('/');
            if(remote==null || !IPAddress.IsLoopback(remote) || parts.Length!=5 || !Sessions.TryGetValue(parts[2],out var session) || !session._urls.TryGetValue(parts[3],out var url)) { context.Response.StatusCode=404; return true; }
            var origin=context.Request.Headers["Origin"];
            if(origin!=null && (!Uri.TryCreate(origin,UriKind.Absolute,out var originUri) || !originUri.IsLoopback)) { context.Response.StatusCode=403; return true; }
            if(context.Request.HttpMethod is not ("GET" or "HEAD")) { context.Response.StatusCode=405; return true; }
            await session.Serve(context,url);
        } catch(OperationCanceledException) { try { context.Response.StatusCode=504; } catch { } }
          catch { try { context.Response.StatusCode=502; } catch { } }
        finally { try { context.Response.Close(); } catch { } }
        return true;
    }
    async Task Serve(HttpListenerContext context,Uri url)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(45));var ct=timeout.Token;
        using var response=await Fetch(url,context.Request.Headers["Range"],ct);
        context.Response.StatusCode=(int)response.StatusCode;context.Response.Headers["Cache-Control"]="no-store";
        if(!response.IsSuccessStatusCode)return;
        await using var input=await response.Content.ReadAsStreamAsync(ct);
        var mediaType=response.Content.Headers.ContentType?.MediaType??"application/octet-stream";
        if(url.AbsolutePath.EndsWith(".m3u8",StringComparison.OrdinalIgnoreCase) || mediaType.Contains("mpegurl",StringComparison.OrdinalIgnoreCase)) {
            using var memory=new MemoryStream();var buffer=new byte[16384];int n;
            while((n=await input.ReadAsync(buffer,ct))>0){if(memory.Length+n>2*1024*1024)throw new IOException("HLS列表过大");memory.Write(buffer,0,n);}
            var manifest=Encoding.UTF8.GetString(memory.ToArray()).TrimStart('\uFEFF');if(!manifest.TrimStart().StartsWith("#EXTM3U",StringComparison.Ordinal))throw new IOException("不是HLS列表");
            var output=Encoding.UTF8.GetBytes(Rewrite(manifest,response.RequestMessage?.RequestUri??url,Map));context.Response.ContentType="application/vnd.apple.mpegurl";context.Response.ContentLength64=output.Length;
            if(context.Request.HttpMethod!="HEAD")await context.Response.OutputStream.WriteAsync(output,ct);
        } else {
            context.Response.ContentType=mediaType;
            if(response.Content.Headers.ContentRange is {} contentRange)context.Response.Headers["Content-Range"]=contentRange.ToString();
            if(response.Content.Headers.ContentLength is long length)context.Response.ContentLength64=length;else context.Response.SendChunked=true;
            if(context.Request.HttpMethod=="HEAD")return;
            var buffer=new byte[65536];long total=0;int n;
            while((n=await input.ReadAsync(buffer,ct))>0){total+=n;if(total>64L*1024*1024)throw new IOException("HLS分片过大");await context.Response.OutputStream.WriteAsync(buffer.AsMemory(0,n),ct);}
        }
    }
    async Task<HttpResponseMessage> Fetch(Uri url,string range,CancellationToken ct)
    {
        for(var hop=0;hop<=5;hop++) {
            if(url.Scheme is not ("http" or "https") || url.UserInfo.Length>0)throw new IOException("不支持的HLS跳转地址");
            using var request=new HttpRequestMessage(HttpMethod.Get,url);
            foreach(var header in _headers) {
                if(new[]{"Host","Range","Connection"}.Contains(header.Key,StringComparer.OrdinalIgnoreCase))continue;
                var sensitive=new[]{"Authorization","Cookie"}.Contains(header.Key,StringComparer.OrdinalIgnoreCase);
                if(sensitive&&url.GetLeftPart(UriPartial.Authority)!=_root.GetLeftPart(UriPartial.Authority))continue;
                request.Headers.TryAddWithoutValidation(header.Key,header.Value);
            }
            if(!request.Headers.Contains("User-Agent"))request.Headers.TryAddWithoutValidation("User-Agent","Mozilla/5.0");
            if(range!=null)request.Headers.TryAddWithoutValidation("Range",range);
            var response=await HttpUtil.Client(NetworkConfig.GetProxyFor(url.Host),redirect:false,bypassProxy:url.IsLoopback).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            if((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308))return response;
            var location=response.Headers.Location;response.Dispose();
            if(location==null||hop==5)throw new IOException("HLS跳转过多或缺少地址");
            var next=new Uri(url,location);
            if(url.Scheme=="https"&&next.Scheme!="https")throw new IOException("HLS跳转降级到不安全传输");
            url=next;
        }
        throw new IOException("HLS跳转过多");
    }
    public void Dispose() { if(Interlocked.Exchange(ref _disposed,1)!=0)return;Sessions.TryRemove(_id,out _); _stop.Cancel(); _stop.Dispose(); _urls.Clear(); _tokens.Clear(); }
}
