namespace TVBoxForWindows.Models;
public static class PlaybackNetworkPolicy
{
    public static void Apply(Dictionary<string,string> options, string url, string format, bool live, bool reuse = true)
    {
        var hls = Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && (uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) || format?.Contains("hls", StringComparison.OrdinalIgnoreCase) == true || format?.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) == true);
        options["http_persistent"] = !live && hls && reuse ? "1" : "0";
        // Reconnecting a streamed HTTP response at normal HLS segment EOF can loop;
        // preserve transport reconnect while disabling that incompatible option for reused VOD sessions.
        options["reconnect_streamed"] = !live && hls && reuse ? "0" : "1";
        if (!live && hls && reuse) options["http_multiple"] = "1"; else options.Remove("http_multiple");
    }
}
