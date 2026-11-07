using System.Security.Cryptography;
using System.Text;

namespace TVBoxForWindows.Core;

public static class ImageByteCache
{
    public static string PathFor(string directory, string key) => Path.Combine(directory, Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(key ?? ""))).ToLowerInvariant());
    public static bool LooksLikeImage(byte[] bytes) => bytes is { Length: >= 8 } &&
        (bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) || bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 || Encoding.ASCII.GetString(bytes, 0, 6) is "GIF87a" or "GIF89a" || bytes[0] == 66 && bytes[1] == 77 || bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP");
    public static async Task<byte[]> ReadAsync(string directory, string key)
    {
        var file = PathFor(directory, key);
        try {
            if (!File.Exists(file)) return null;
            var bytes = await File.ReadAllBytesAsync(file);
            if (!LooksLikeImage(bytes)) { File.Delete(file); return null; }
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromHours(1)) File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
            return bytes;
        } catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; }
    }
    public static void Invalidate(string directory, string key) { try { File.Delete(PathFor(directory, key)); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    public static async Task WriteAsync(string directory, string key, byte[] decodedImageBytes)
    {
        if (!LooksLikeImage(decodedImageBytes)) return;
        Directory.CreateDirectory(directory); var file = PathFor(directory, key);
        if (File.Exists(file)) return;
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temp, decodedImageBytes); File.Move(temp, file, true); }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } }
    }
}
