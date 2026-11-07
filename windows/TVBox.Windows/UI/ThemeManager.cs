using Microsoft.UI.Xaml;
using TVBoxForWindows.Core;

namespace TVBoxForWindows.UI;

public static class ThemeManager
{
    public static event Action Changed;
    public static string Brand { get; private set; } = "douban";
    public static string Mode => Setting.GetString("ui_theme", "dark");
    public static void Initialize()
    {
        Setting.InitializeAppearance();
    }
    public static ElementTheme Theme => Mode == "light" ? ElementTheme.Light : ElementTheme.Dark;
    public static void SetMode(string mode) { var next = mode == "light" ? "light" : "dark"; if (Mode == next) return; Setting.Put("ui_theme", next); Changed?.Invoke(); }
    public static void SetBrand(string brand) { Brand = brand; }
}
