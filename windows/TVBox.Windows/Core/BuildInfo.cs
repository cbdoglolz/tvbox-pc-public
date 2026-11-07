namespace TVBoxForWindows.Core;

public static class BuildInfo
{
    public const string ReleaseDate = "2026-11-07";
    public static string Version => typeof(BuildInfo).Assembly.GetName().Version?.ToString(3) ?? "未知";
}
