using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TVBoxForWindows.Core;

public static class TmdbCredentials
{
    public static string CredentialPath => Path.Combine(AppPaths.Root, "tmdb.credential");
    public static bool HasCredential => Read().Length > 0;
    public static bool IsApiKey(string value) => Regex.IsMatch(value ?? "", "^[a-fA-F0-9]{32}$");
    public static bool IsValidFormat(string value) => IsApiKey(value) || Regex.IsMatch(value ?? "", "^[A-Za-z0-9_-]{10,}\\.[A-Za-z0-9_-]{10,}\\.[A-Za-z0-9_-]{10,}$");
    public static void Save(string value)
    {
        value = value.Trim(); if (!IsValidFormat(value)) throw new ArgumentException("请输入个人API Key（32位）或API Read Access Token");
        var bytes = Encoding.UTF8.GetBytes(value);
        try { var encrypted = Transform(bytes, true); var temp = CredentialPath + ".tmp"; File.WriteAllBytes(temp, encrypted); File.Move(temp, CredentialPath, true); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public static string Read()
    {
        try
        {
            if (!File.Exists(CredentialPath)) return "";
            var plain = Transform(File.ReadAllBytes(CredentialPath), false);
            try { return Encoding.UTF8.GetString(plain); } finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch { return ""; }
    }
    public static void Clear() { if (File.Exists(CredentialPath)) File.Delete(CredentialPath); }
    internal static byte[] Transform(byte[] data, bool protect)
    {
        var input = new Blob { Count = data.Length, Data = Marshal.AllocHGlobal(data.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            var ok = protect ? CryptProtectData(ref input, "TVBox PC optional TMDB", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) :
                CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException("Windows安全存储不可用");
            var result = new byte[output.Count]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            for (var i = 0; i < input.Count; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) { if (!protect) for (var i = 0; i < output.Count; i++) Marshal.WriteByte(output.Data, i, 0); LocalFree(output.Data); }
        }
    }
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Count; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
}
public static class OptionalMetadataHint
{
    public static bool ShouldShow(bool hasCredential, bool userRequested, bool seen, bool hidden) => !hasCredential && userRequested && !seen && !hidden;
    public static bool TryMarkShown(bool userRequested)
    {
        if (!ShouldShow(TmdbCredentials.HasCredential, userRequested, Setting.GetBool("tmdb_hint_seen"), Setting.GetBool("tmdb_hint_hidden"))) return false;
        Setting.Put("tmdb_hint_seen", true); return true;
    }
    public static void DismissForever() { Setting.Put("tmdb_hint_hidden", true); Setting.Put("tmdb_hint_seen", true); }
}
