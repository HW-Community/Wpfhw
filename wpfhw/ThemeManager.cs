using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace wpfhw;

/// <summary>
/// 主题管理器：维护一组共享的 SolidColorBrush 资源，
/// 通过修改 Color 属性实现浅色 / 深色 / 跟随系统主题的切换。
/// </summary>
public static class ThemeManager
{
    // 资源 key（与 App.xaml 中定义的一致）
    public const string KeyWindowBackground = "ThemeWindowBackground";
    public const string KeyCardBackground = "ThemeCardBackground";
    public const string KeyCardAltBackground = "ThemeCardAltBackground";
    public const string KeyCardPressed = "ThemeCardPressed";
    public const string KeyPrimaryText = "ThemePrimaryText";
    public const string KeySecondaryText = "ThemeSecondaryText";
    public const string KeyTertiaryText = "ThemeTertiaryText";
    public const string KeyInputBackground = "ThemeInputBackground";
    public const string KeyInputFocusBackground = "ThemeInputFocusBackground";
    public const string KeyItemHover = "ThemeItemHover";
    public const string KeyItemSelected = "ThemeItemSelected";
    public const string KeyAccent = "ThemeAccent";
    public const string KeyAccentHover = "ThemeAccentHover";
    public const string KeyAccentPressed = "ThemeAccentPressed";

    private static readonly (string Key, Color Light, Color Dark)[] Palette =
    {
        (KeyWindowBackground,   Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x1C, 0x1C, 0x1E)),
        (KeyCardBackground,     Color.FromRgb(0xF2, 0xF2, 0xF7), Color.FromRgb(0x2C, 0x2C, 0x2E)),
        (KeyCardAltBackground,  Color.FromRgb(0xE5, 0xE5, 0xEA), Color.FromRgb(0x3A, 0x3A, 0x3C)),
        (KeyCardPressed,        Color.FromRgb(0xD1, 0xD1, 0xD6), Color.FromRgb(0x48, 0x48, 0x4A)),
        (KeyPrimaryText,        Color.FromRgb(0x33, 0x33, 0x33), Color.FromRgb(0xFF, 0xFF, 0xFF)),
        (KeySecondaryText,      Color.FromRgb(0x66, 0x66, 0x66), Color.FromRgb(0xEB, 0xEB, 0xF5)),
        (KeyTertiaryText,       Color.FromRgb(0x99, 0x99, 0x99), Color.FromRgb(0x8E, 0x8E, 0x93)),
        (KeyInputBackground,    Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x3A, 0x3A, 0x3C)),
        (KeyInputFocusBackground, Color.FromRgb(0xF5, 0xF5, 0xF5), Color.FromRgb(0x48, 0x48, 0x4A)),
        (KeyItemHover,          Color.FromRgb(0xF2, 0xF2, 0xF7), Color.FromRgb(0x3A, 0x3A, 0x3C)),
        (KeyItemSelected,       Color.FromRgb(0xE5, 0xE5, 0xEA), Color.FromRgb(0x48, 0x48, 0x4A)),
        (KeyAccent,             Color.FromRgb(0x00, 0x7A, 0xFF), Color.FromRgb(0x0A, 0x84, 0xFF)),
        (KeyAccentHover,        Color.FromRgb(0x33, 0x95, 0xFF), Color.FromRgb(0x40, 0x9C, 0xFF)),
        (KeyAccentPressed,      Color.FromRgb(0x00, 0x56, 0xB3), Color.FromRgb(0x00, 0x60, 0xCC))
    };

    /// <summary>应用指定主题模式。</summary>
    public static void ApplyTheme(ThemeMode mode)
    {
        bool isDark = mode == ThemeMode.Dark || (mode == ThemeMode.System && IsSystemDark());
        Apply(isDark);
    }

    /// <summary>读取 Windows 系统是否为深色模式。</summary>
    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value)
                return value == 0;
        }
        catch
        {
        }
        return false;
    }

    private static void Apply(bool isDark)
    {
        var resources = Application.Current.Resources;
        foreach (var (key, light, dark) in Palette)
        {
            Color color = isDark ? dark : light;
            // 直接替换资源为新的 Brush（避免已冻结的 Brush 无法改色）
            resources[key] = new SolidColorBrush(color);
        }
    }
}
