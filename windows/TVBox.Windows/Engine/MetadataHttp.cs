using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using TVBoxForWindows.Core;
using TVBoxForWindows.Net;

namespace TVBoxForWindows.Engine;

public sealed class MetadataServiceException(string message, string code = "network") : IOException(message)
{ public string Code { get; } = code; }
public sealed class MetadataHttp
{
    readonly HttpClient _client;
    readonly Func<string> _credential;
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Gates = new();
    DateTimeOffset _blockedUntil;
    public MetadataHttp(HttpClient client = null, Func<string> credential = null) { _client = client; _credential = credential ?? TmdbCredentials.Read; }
    public async Task<string> GetAsync(string provider, string route, Dictionary<string, string> query, CancellationToken cancellation)
    {
        var token = provider == "tmdb" ? _credential() : "";
        if (provider == "tmdb" && token.Length == 0) throw new MetadataServiceException("TMDB资料为可选功能，可在设置中填写个人Key；普通点播仍可使用", "unconfigured");
        if (DateTimeOffset.UtcNow < _blockedUntil) throw new MetadataServiceException("资料服务暂时限流或不可用，请稍后再试", "backoff");
        if (route.Contains("..") || route.StartsWith('/') || route.Contains(':')) throw new ArgumentException("无效资料路径");
        var host = provider == "tmdb" ? "https://api.themoviedb.org/3/" : "https://m.douban.com/rexxar/api/v2/";
        var parameters = new Dictionary<string, string>(query);
        if (provider == "tmdb" && TmdbCredentials.IsApiKey(token)) parameters["api_key"] = token;
        var url = host + route + "?" + string.Join("&", parameters.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
        var gate = Gates.GetOrAdd(provider, _ => new(2));
        await gate.WaitAsync(cancellation);
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(12));
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.UserAgent.ParseAdd("TVBoxPC/0.4.0 Mozilla/5.0");
                    request.Headers.Accept.ParseAdd("application/json");
                    if (provider == "tmdb" && !TmdbCredentials.IsApiKey(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    if (provider == "douban")
                    {
                        request.Headers.Referrer = new Uri("https://movie.douban.com/");
                        request.Headers.TryAddWithoutValidation("Origin", "https://movie.douban.com");
                        request.Headers.Remove("User-Agent"); request.Headers.TryAddWithoutValidation("User-Agent", Setting.Ua);
                    }
                    var client = _client ?? HttpUtil.Client(NetworkConfig.GetProxyFor(new Uri(host).Host));
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    if (response.StatusCode == HttpStatusCode.Unauthorized) throw new MetadataServiceException("TMDB Key/Token无效，请在设置中检查；普通播放不受影响", "unauthorized");
                    if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                    {
                        var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromMilliseconds(500 * (1 << attempt));
                        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
                        if (attempt == 2 || delay > TimeSpan.FromSeconds(3)) { _blockedUntil = DateTimeOffset.UtcNow + (delay > TimeSpan.FromSeconds(20) ? delay : TimeSpan.FromSeconds(20)); throw new MetadataServiceException("资料服务限流或暂时故障，保留现有缓存", "backoff"); }
                        await Task.Delay(delay, cancellation); continue;
                    }
                    if (!response.IsSuccessStatusCode) throw new MetadataServiceException($"资料接口暂不可用（HTTP {(int)response.StatusCode}）", "http");
                    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                    using var bytes = new MemoryStream(); var buffer = new byte[16384];
                    while (true)
                    {
                        var read = await stream.ReadAsync(buffer, timeout.Token); if (read == 0) break;
                        if (bytes.Length + read > 4 * 1024 * 1024) throw new MetadataServiceException("资料响应过大，已停止读取", "payload");
                        bytes.Write(buffer, 0, read);
                    }
                    await Task.Delay(120, cancellation);
                    return Encoding.UTF8.GetString(bytes.ToArray());
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new MetadataServiceException("资料连接超时，普通播放仍可使用", "timeout"); }
                catch (HttpRequestException) { throw new MetadataServiceException("资料网络暂不可用，请检查网络设置或使用缓存", "network"); }
            }
            throw new MetadataServiceException("资料服务暂不可用");
        }
        finally { gate.Release(); }
    }
}
