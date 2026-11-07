using TVBoxForWindows.Models;

namespace TVBoxForWindows.Core;

/// <summary>Stable local subscription identity keeps favorites and history across upgrades.</summary>
public static class BuiltInSources
{
    public const string ConfigUrl = "assets://catalog/default.json";
    public const string ConfigName = "内置影视源";

    public static bool IsSelected => string.Equals(Setting.ConfigVod, ConfigUrl, StringComparison.OrdinalIgnoreCase);

    public static ConfigRecord ResolveStartup()
    {
        var saved = Stores.ResolveConfig(Setting.ConfigVod, 0);
        if (saved != null) return saved;
        var bundled = Stores.FindConfig(ConfigUrl, 0);
        bundled.Name = ConfigName;
        return bundled;
    }
}
