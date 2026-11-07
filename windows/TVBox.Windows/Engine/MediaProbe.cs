using System.Net.Http;
using TVBoxForWindows.Core;
using TVBoxForWindows.Net;

namespace TVBoxForWindows.Engine;

/// <summary>Reads actual media bytes; HTTP 200 alone is not proof of a playable source.</summary>
public static class MediaProbe
{
    public static async Task<bool> CheckAsync(string url, Dictionary<string, string> headers, CancellationToken cancellation,
        Action<string, int, long> variantSelected = null, bool highQuality = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        var ct = timeout.Token;
        var encryptedHls = false;
        for (var depth = 0; depth < 4; depth++)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            foreach (var header in headers ?? new())
                if (!header.Key.Equals("Range", StringComparison.OrdinalIgnoreCase)) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (!request.Headers.Contains("User-Agent")) request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            // Servers may ignore ranges. Read a bounded prefix and dispose the response either way.
            request.Headers.TryAddWithoutValidation("Range", "bytes=0-65535");
            using var response = await HttpUtil.Client(NetworkConfig.GetProxyFor(uri.Host)).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return false;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var bytes = new byte[65536];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count), ct);
                if (read == 0) break;
                count += read;
                // A TS/MP4 prefix suffices; playlists need a larger prefix for variant selection.
                if (count >= 4096 && bytes[0] != '#') break;
            }
            if (count == 0) return false;
            var text = System.Text.Encoding.UTF8.GetString(bytes, 0, count).TrimStart('\uFEFF', ' ', '\r', '\n');
            if (text.StartsWith("#EXTM3U", StringComparison.Ordinal))
            {
                encryptedHls |= text.Contains("METHOD=AES-128", StringComparison.OrdinalIgnoreCase);
                var variants = HlsVariants.Parse(text);
                var selected = highQuality ? variants.OrderByDescending(v => v.Height).ThenByDescending(v => v.Bitrate).FirstOrDefault() :
                    variants.OrderBy(v => v.Bitrate > 0 ? v.Bitrate : long.MaxValue).FirstOrDefault();
                var next = selected?.Url ?? text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
                if (next == null) return false;
                url = new Uri(response.RequestMessage?.RequestUri ?? uri, next).AbsoluteUri;
                if (selected != null) variantSelected?.Invoke(url, selected.Height, selected.Bitrate);
                continue;
            }
            if (text.StartsWith('<') || text.StartsWith('{')) return false;
            // MPEG-TS, common ISO BMFF boxes, or Matroska. Reject disguised HTML/JSON.
            return encryptedHls && count >= 16 || Enumerable.Range(0, Math.Max(0, Math.Min(count - 376, 4096)))
                       .Any(i => bytes[i] == 0x47 && bytes[i + 188] == 0x47 && bytes[i + 376] == 0x47) ||
                   (count > 8 && (System.Text.Encoding.ASCII.GetString(bytes, 4, 4) is "ftyp" or "styp" or "moof" or "sidx" or "moov")) ||
                   (count > 4 && bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3);
        }
        return false;
    }
}

public record HlsVariant(string Url, int Height, long Bitrate);
public static class HlsVariants
{
    public static List<HlsVariant> Parse(string text)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).ToArray();
        var variants = new List<HlsVariant>();
        for (var i = 0; i + 1 < lines.Length; i++)
        {
            if (!lines[i].StartsWith("#EXT-X-STREAM-INF:")) continue;
            var height = System.Text.RegularExpressions.Regex.Match(lines[i], @"RESOLUTION=\d+x(\d+)").Groups[1].Value;
            var bitrate = System.Text.RegularExpressions.Regex.Match(lines[i], @"(?:^|[:,])BANDWIDTH=(\d+)").Groups[1].Value;
            var next = lines.Skip(i + 1).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
            if (next != null) variants.Add(new(next, int.TryParse(height, out var h) ? h : 0, long.TryParse(bitrate, out var b) ? b : 0));
        }
        return variants;
    }
}
