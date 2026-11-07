using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TVBoxForWindows.Core;

public record ReleaseDownload(string Version, string Url, string Sha256, long Size, string Page);
public record StagedUpdate(string Version, string Directory, string PackageHash);

/// <summary>Only public, stable x64 releases; no access to user credentials or source subscriptions.</summary>
public sealed class ReleaseUpdate(HttpClient client = null)
{
    public const string Repository = "cbdoglolz/tvbox-pc-public";
    public const string ReleasePage = "https://github.com/" + Repository + "/releases/latest";
    const long MaximumDownload = 1024L * 1024 * 1024;
    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(20) };
    readonly HttpClient _client = client ?? Client;

    public async Task<ReleaseDownload> CheckAsync(string currentVersion, CancellationToken ct)
    {
        using var response = await SendAsync("https://api.github.com/repos/" + Repository + "/releases/latest", ct);
        using var doc = JsonDocument.Parse(await ReadLimited(response.Content, 1024 * 1024, ct));
        var release = doc.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$") || !System.Version.TryParse(tag[1..], out var next)
            || !System.Version.TryParse(currentVersion, out var current) || next <= current) return null;
        var name = $"TVBox-x64-{tag[1..]}.zip";
        var assets = release.GetProperty("assets").EnumerateArray().ToList();
        var package = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == name);
        if (package.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("新版本尚未提供x64便携包");
        var size = package.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaximumDownload) throw new InvalidDataException("更新包大小无效");
        var hash = package.TryGetProperty("digest", out var digest) ? digest.GetString()?.Replace("sha256:", "") : null;
        if (!Regex.IsMatch(hash ?? "", "^[a-fA-F0-9]{64}$"))
        {
            var sums = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == "RELEASE-SHA256SUMS.txt");
            if (sums.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("新版本缺少SHA-256校验信息");
            using var sumResponse = await SendAsync(AssetUrl(sums, tag), ct);
            var text = System.Text.Encoding.UTF8.GetString(await ReadLimited(sumResponse.Content, 64 * 1024, ct));
            hash = Regex.Match(text, @"(?m)^([a-fA-F0-9]{64})\s+" + Regex.Escape(name) + @"\r?$" ).Groups[1].Value;
        }
        if (!Regex.IsMatch(hash ?? "", "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("更新包SHA-256无效");
        return new(tag[1..], AssetUrl(package, tag), hash.ToLowerInvariant(), size, "https://github.com/" + Repository + "/releases/tag/" + tag);
    }

    static string AssetUrl(JsonElement asset, string tag)
    {
        var url = asset.GetProperty("browser_download_url").GetString() ?? "";
        if (!url.StartsWith("https://github.com/" + Repository + "/releases/download/" + tag + "/", StringComparison.Ordinal)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidDataException("更新附件地址不属于本项目");
        return url;
    }

    async Task<HttpResponseMessage> SendAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("TVBoxPC-Updater/1.0");
        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        try
        {
            response.EnsureSuccessStatusCode();
            var final = response.RequestMessage?.RequestUri ?? request.RequestUri;
            if (final.Scheme != "https" || final.Host is not ("github.com" or "api.github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("更新下载跳转到非GitHub地址");
            return response;
        }
        catch { response.Dispose(); throw; }
    }

    public async Task<StagedUpdate> DownloadAsync(ReleaseDownload release, string updateRoot, IProgress<double> progress, CancellationToken ct)
    {
        if (!Regex.IsMatch(release.Version, @"^\d+\.\d+\.\d+$")) throw new InvalidDataException("无效更新版本");
        var directory = Path.Combine(Path.GetFullPath(updateRoot), release.Version + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var archive = Path.Combine(directory, "package.zip");
        using var response = await SendAsync(release.Url, ct);
        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[65536]; long total = 0; int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                total += read; if (total > release.Size || total > MaximumDownload) throw new InvalidDataException("更新包大小与声明不符");
                sha.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), ct);
                progress?.Report(total * 100d / release.Size);
            }
            if (total != release.Size || !Convert.ToHexString(sha.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新包校验失败，未修改当前程序");
        }
        var staged = Path.Combine(directory, "files");
        await Task.Run(() => ExtractCleanPackage(archive, staged, ct), ct);
        var result = new StagedUpdate(release.Version, staged, release.Sha256);
        DurableJsonFile.Write(Path.Combine(directory, "ready.json"), JsonUtil.Serialize(result));
        return result;
    }

    public static void ExtractCleanPackage(string archive, string destination, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count is < 1 or > 10000) throw new InvalidDataException("更新包文件数无效");
        var prefix = zip.Entries[0].FullName.Replace('\\', '/').Split('/')[0];
        prefix = Regex.IsMatch(prefix, @"^TVBox-x64-\d+\.\d+\.\d+$") ? prefix + "/" : "";
        string Relative(ZipArchiveEntry entry)
        {
            var path = entry.FullName.Replace('\\', '/');
            if (prefix.Length == 0) return path;
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("更新包根目录不一致");
            return path[prefix.Length..];
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var name = Relative(entry);
            if (name.Length == 0 || name.EndsWith('/')) continue;
            if (!AllowedPackagePath(name) || !names.Add(name) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("更新包包含不允许的文件");
            expanded += entry.Length;
            if (expanded > 3L * 1024 * 1024 * 1024) throw new InvalidDataException("更新包展开过大");
        }
        foreach (var required in new[] { "TVBox.exe", "libs/TVBox.dll", "libs/TVBox.runtimeconfig.json", "LICENSE" })
            if (!names.Contains(required)) throw new InvalidDataException("更新包结构不完整");
        if (!names.Any(n => n.StartsWith("runtime/host/fxr/", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("更新包缺少私有运行库");
        Directory.CreateDirectory(destination);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested(); if (entry.FullName.EndsWith('/')) continue;
            var target = Path.GetFullPath(Path.Combine(destination, Relative(entry)));
            if (!target.StartsWith(Path.GetFullPath(destination).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新路径越界");
            Directory.CreateDirectory(Path.GetDirectoryName(target)); entry.ExtractToFile(target, false);
        }
    }

    public static bool AllowedPackagePath(string name)
    {
        if (name.Contains(':') || name.StartsWith('/') || name.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.'))) return false;
        var file = Path.GetFileName(name).ToLowerInvariant();
        if (file.Contains(".credential") || Regex.IsMatch(file, @"\.(?:sqlite3?|db)(?:$|-(?:wal|shm)$|\.bak$)") || file.EndsWith(".log") || file.EndsWith(".pdb") || file.EndsWith(".pfx") || file.EndsWith(".p12") || file.EndsWith(".key") || file is "prefs.json" or "history.json" or "configs.json" or "keep.json" or "app.log" or "tokens.json" or "cookies.json" or "credentials.json" or "source-statistics.json") return false;
        var top = name.Split('/')[0];
        return name == "TVBox.exe" || name == "LICENSE" || name is "README.md" or "THIRD-PARTY-NOTICES.md"
            || top is "assets" or "libs" or "ffmpeg" or "node" or "runtime" or "locales" or "docs";
    }

    static async Task<byte[]> ReadLimited(HttpContent content, int limit, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct); using var output = new MemoryStream(); var buffer = new byte[16384]; int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0) { if (output.Length + read > limit) throw new InvalidDataException("更新元数据过大"); output.Write(buffer, 0, read); }
        return output.ToArray();
    }
}
