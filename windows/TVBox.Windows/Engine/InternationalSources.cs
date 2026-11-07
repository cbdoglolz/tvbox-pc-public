using System.Diagnostics;
using System.Text.Json;
using TVBoxForWindows.Core;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Engine;

public static class InternationalSources
{
    public const int SiteType = 91;
    public static IReadOnlyList<Site> Sites => new[] {
        new Site { Key = "nuvio_4khdhub", Name = "4KHDHub · 国际", Type = SiteType, Searchable = 0, Api = "https://4khdhub.click", Timeout = 50 },
        new Site { Key = "nuvio_anizone", Name = "AniZone · 动画", Type = SiteType, Searchable = 0, Api = "https://anizone.to", Timeout = 50 },
        new Site { Key = "nuvio_vixsrc", Name = "Vixsrc · 国际", Type = SiteType, Searchable = 0, Api = "https://vixsrc.to", Timeout = 50 }
    };
    public static string PreferredTitle(CatalogItem work) => !string.IsNullOrWhiteSpace(work.OriginalTitle) ? work.OriginalTitle : work.Title;
    public static async Task<List<PlaybackCandidate>> FindAsync(Site site, CatalogItem work, string episodeName, bool quality, CancellationToken ct)
    {
        if (!Sites.Any(s => s.Key == site.Key) || WorkMediaService.TmdbId(work) is not int id) throw new InvalidOperationException("国际来源需要可靠TMDB匹配，可在详情页校正");
        var ep = EpisodeIdentity.Parse(episodeName).Number;
        var season = work.SelectedSeason ?? Math.Max(1, CatalogIdentity.Season(work)); if (work.Kind == "tv" && ep <= 0) ep = 1;
        var title = PreferredTitle(work);
        if (title.Any(c => c > 127) && TmdbCredentials.HasCredential) {
            var key = work.Kind + ":" + id; var cache = MetadataStore.Instance.Read<string>("english-source-title", key);
            if (cache?.IsFresh == true) title = cache.Value;
            else try { using var english = JsonDocument.Parse(await new MetadataHttp().GetAsync("tmdb", work.Kind + "/" + id, new() { ["language"] = "en-US" }, ct));
                var resolved = CatalogMetadataParser.Text(english.RootElement, work.Kind == "tv" ? "name" : "title");
                if (resolved.Length > 0) { title = resolved; MetadataStore.Instance.Write("english-source-title", key, title, TimeSpan.FromDays(7)); }
            } catch (OperationCanceledException) { throw; } catch { /* Existing title remains usable for ID-based providers. */ }
        }
        var input = JsonSerializer.Serialize(new { tmdbId = id, kind = work.Kind, title, originalTitle = work.OriginalTitle, year = work.Year, imdb = work.ExternalIds.Imdb, season, episode = ep });
        if (!NodeRuntime.EnsureNode()) throw new IOException("内置Node运行库未就绪");
        var start = new ProcessStartInfo(NodeRuntime.NodePath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(AppPaths.InstallRoot, "assets", "providers", "run-provider.mjs")); start.ArgumentList.Add(site.Key[6..]);
        using var process = Process.Start(start) ?? throw new IOException("无法启动国际源适配器");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(50));
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token); var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try {
            await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token); process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token); var output = await outputTask; var error = await errorTask;
            if (output.Length > 1024 * 1024 || process.ExitCode != 0) {
                var safe = System.Text.RegularExpressions.Regex.Replace(error, @"https?://[^\s]+", "<remote-url>").Replace("\r", " ").Replace("\n", " ");
                Logger.D("InternationalSources", $"helper exit={process.ExitCode}; " + safe[..Math.Min(safe.Length, 600)]);
                throw new IOException("国际源适配器没有返回有效结果");
            }
            using (var diagnostic = JsonDocument.Parse(output)) if (diagnostic.RootElement.TryGetProperty("streams", out var raw)) Logger.D("InternationalSources", $"adapter={site.Key}, raw-streams={raw.GetArrayLength()}");
            return await ProbeStreamsAsync(output, site, work, ep, quality, ct);
        }
        finally { if (!process.HasExited) { try { process.Kill(); } catch { } } }
    }
    public static async Task<List<PlaybackCandidate>> ProbeStreamsAsync(string output, Site site, CatalogItem work, int episode, bool quality, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(output); var candidates = new List<PlaybackCandidate>();
        if (!doc.RootElement.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array) return candidates;
        // A tiny header response is not sustained throughput. In speed mode prefer the
        // provider's smaller declared encodes, then verify bytes; never start a 50GB remux just because it was listed first.
        static long SizeHint(JsonElement stream) { var text = CatalogMetadataParser.Text(stream, "title"); var m = System.Text.RegularExpressions.Regex.Match(text, @"([\d.]+)\s*(GB|MB|TB)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase); return m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? (long)(number * (m.Groups[2].Value.ToUpperInvariant() == "TB" ? 1e12 : m.Groups[2].Value.ToUpperInvariant() == "GB" ? 1e9 : 1e6)) : long.MaxValue; }
        static int HeightHint(JsonElement stream) { var m = System.Text.RegularExpressions.Regex.Match(CatalogMetadataParser.Text(stream, "quality"), @"(\d{3,4})"); return m.Success && int.TryParse(m.Groups[1].Value, out var height) ? height : 0; }
        var ordered = quality ? streams.EnumerateArray().OrderByDescending(HeightHint).ThenBy(SizeHint) : streams.EnumerateArray().OrderBy(SizeHint).ThenBy(HeightHint);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(45));
        using var slots = new SemaphoreSlim(4);
        async Task<PlaybackCandidate> ProbeOne(JsonElement stream)
        {
            var url = CatalogMetadataParser.Text(stream, "url"); if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0) return null;
            url = uri.AbsoluteUri.Replace("[", "%5B").Replace("]", "%5D");
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["User-Agent"] = "Mozilla/5.0" };
            if (stream.TryGetProperty("headers", out var supplied) && supplied.ValueKind == JsonValueKind.Object) foreach (var h in supplied.EnumerateObject()) if (h.Name is "Referer" or "Origin" or "User-Agent" or "Accept" && !h.Value.ToString().Contains('\r') && !h.Value.ToString().Contains('\n')) headers[h.Name] = h.Value.ToString();
            var playbackSite = new Site { Key = site.Key, Name = site.Name, Type = SiteType, Searchable = 0, Api = site.Api, Header = headers };
            var flag = new VodFlag { SourceSite = playbackSite, SourceVodId = work.Id, OriginalFlag = CatalogMetadataParser.Text(stream, "name"), Flag = site.Name + " · " + CatalogMetadataParser.Text(stream, "title") };
            var timer = Stopwatch.StartNew();
            bool playable;
            try { playable = await MediaProbe.CheckAsync(url, headers, budget.Token, (resolved, height, bitrate) => { url = resolved; flag.ProbeHeight = height; flag.ProbeBitrate = bitrate; }, quality); }
            catch (Exception) when (!ct.IsCancellationRequested) { return null; }
            if (!playable) return null;
            flag.IsMediaVerified = true; flag.ProbeLatencyMs = timer.ElapsedMilliseconds;
            var entry = new Episode { Name = work.Kind == "tv" ? "第" + episode.ToString("00") + "集" : "正片", Url = url };
            if (stream.TryGetProperty("subtitles", out var subtitles) && subtitles.ValueKind == JsonValueKind.Array) foreach (var sub in subtitles.EnumerateArray().Take(20)) { var link = CatalogMetadataParser.Text(sub, "url"); if (Uri.TryCreate(link, UriKind.Absolute, out var subUri) && subUri.Scheme is "http" or "https") entry.Subtitles.Add(new Sub { Name = CatalogMetadataParser.Text(sub, "name"), Lang = CatalogMetadataParser.Text(sub, "language"), Url = link }); }
            flag.Episodes.Add(entry); return new(playbackSite, new Vod { Id = work.Id, Name = work.Title, Pic = work.Poster }, flag);
        }
        var results = new System.Collections.Concurrent.ConcurrentDictionary<int, PlaybackCandidate>();
        var jobs = ordered.Take(16).Select((stream, index) => Task.Run(async () => {
            var entered = false;
            try {
                await slots.WaitAsync(budget.Token); entered = true;
                var candidate = await ProbeOne(stream);
                if (candidate != null) { results[index] = candidate; if (results.Count >= 3) budget.Cancel(); }
            } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            finally { if (entered) slots.Release(); }
        }, ct)).ToArray();
        await Task.WhenAll(jobs); ct.ThrowIfCancellationRequested();
        return results.OrderBy(x => x.Key).Take(3).Select(x => x.Value).ToList();
    }
}
