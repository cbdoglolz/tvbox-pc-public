// Asset-loading tests must not touch the network. Any accidental HTTP call fails.
namespace TVBoxForWindows.Net
{
    internal static class HttpUtil
    {
        internal static readonly Dictionary<string, byte[]> Responses = new();
        static readonly System.Net.Http.HttpClient ProbeClient = new(new ProbeHandler());
        public static System.Net.Http.HttpClient Client(string proxyUrl = null) => ProbeClient;
        public static Task<string> GetString(string url) => throw new InvalidOperationException("Unexpected extension request");
        public static Task<GuardResponse> Get(string url, int timeoutMs) =>
            throw new InvalidOperationException("Unexpected HTTP during bundled asset load: " + url);
    }
    internal class ProbeHandler : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken ct)
        {
            var response = new System.Net.Http.HttpResponseMessage(ResponsesContain(request, out var bytes) ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.NotFound)
            { RequestMessage = request, Content = new System.Net.Http.ByteArrayContent(bytes ?? Array.Empty<byte>()) };
            return Task.FromResult(response);
        }
        static bool ResponsesContain(System.Net.Http.HttpRequestMessage request, out byte[] bytes) => HttpUtil.Responses.TryGetValue(request.RequestUri!.AbsoluteUri, out bytes);
    }
    internal static class NetworkConfig { public static string GetProxyFor(string host) => null; }
    internal class GuardResponse
    {
        public int Code => 200;
        public string FinalUrl => "";
        public string Text() => "";
    }
}
namespace TVBoxForWindows.Core
{
    internal static class Sniffer { public static bool IsVideoFormat(string url) => url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) || url.Contains(".mp4", StringComparison.OrdinalIgnoreCase); }
    internal static class UrlUtil
    {
        public static string Resolve(string url, string relative) => new Uri(new Uri(url), relative).AbsoluteUri;
    }
}
