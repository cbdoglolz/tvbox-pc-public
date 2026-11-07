using System.Security.Cryptography;
using System.Text;

namespace TVBoxForWindows.Core;

/// <summary>Optional service configuration is DPAPI encrypted, stored only in the profile, never in assets.</summary>
public static class ServiceCredentials
{
    static readonly object Sync = new();
    static string FilePath => Path.Combine(AppPaths.Root, "services.credential");
    public static T Read<T>(string key, Func<T> fallback)
    {
        lock (Sync)
        {
            try { var map = ReadMap(); return map.TryGetValue(key, out var value) ? JsonUtil.Deserialize<T>(value) : fallback(); }
            catch { return fallback(); }
        }
    }
    public static void Save<T>(string key, T value)
    {
        lock (Sync)
        {
            var map = ReadMap(); map[key] = JsonUtil.Serialize(value);
            var plain = Encoding.UTF8.GetBytes(JsonUtil.Serialize(map));
            try { var encrypted = TmdbCredentials.Transform(plain, true); var temp = FilePath + ".tmp"; File.WriteAllBytes(temp, encrypted); File.Move(temp, FilePath, true); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
    }
    static Dictionary<string, string> ReadMap()
    {
        if (!File.Exists(FilePath)) return new();
        var plain = TmdbCredentials.Transform(File.ReadAllBytes(FilePath), false);
        try { return JsonUtil.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(plain)) ?? new(); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
