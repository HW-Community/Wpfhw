using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace wpfhw;

public static class ThemeManager
{
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
    public const string KeyAccentSoft = "ThemeAccentSoft";
    public const string KeyBorder = "ThemeBorder";
    public const string KeyScrollTrack = "ThemeScrollTrack";
    public const string KeyScrollThumb = "ThemeScrollThumb";
    public const string KeyScrollThumbHover = "ThemeScrollThumbHover";

    private static readonly (string Key, Color Light, Color Dark)[] Palette =
    {
        (KeyWindowBackground,     Color.FromRgb(0xF6, 0xF7, 0xF9), Color.FromRgb(0x12, 0x12, 0x16)),
        (KeyCardBackground,       Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x1C, 0x1C, 0x22)),
        (KeyCardAltBackground,    Color.FromRgb(0xEE, 0xF0, 0xF4), Color.FromRgb(0x27, 0x27, 0x2E)),
        (KeyCardPressed,          Color.FromRgb(0xDD, 0xE1, 0xE8), Color.FromRgb(0x32, 0x32, 0x3A)),
        (KeyPrimaryText,          Color.FromRgb(0x18, 0x18, 0x1B), Color.FromRgb(0xF4, 0xF4, 0xF5)),
        (KeySecondaryText,        Color.FromRgb(0x52, 0x52, 0x5B), Color.FromRgb(0xA1, 0xA1, 0xAA)),
        (KeyTertiaryText,         Color.FromRgb(0xA1, 0xA1, 0xAA), Color.FromRgb(0x71, 0x71, 0x7A)),
        (KeyInputBackground,      Color.FromRgb(0xF4, 0xF5, 0xF7), Color.FromRgb(0x27, 0x27, 0x2E)),
        (KeyInputFocusBackground, Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x32, 0x32, 0x3A)),
        (KeyItemHover,            Color.FromRgb(0xF0, 0xF4, 0xFF), Color.FromRgb(0x24, 0x2A, 0x3A)),
        (KeyItemSelected,         Color.FromRgb(0xE4, 0xED, 0xFF), Color.FromRgb(0x1E, 0x2A, 0x44)),
        (KeyAccent,               Color.FromRgb(0x25, 0x63, 0xEB), Color.FromRgb(0x60, 0xA5, 0xFA)),
        (KeyAccentHover,          Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0x93, 0xC5, 0xFD)),
        (KeyAccentPressed,        Color.FromRgb(0x1D, 0x4E, 0xD8), Color.FromRgb(0x3B, 0x82, 0xF6)),
        (KeyAccentSoft,           Color.FromArgb(0x33, 0x25, 0x63, 0xEB), Color.FromArgb(0x40, 0x60, 0xA5, 0xFA)),
        (KeyBorder,               Color.FromRgb(0xE4, 0xE7, 0xEC), Color.FromRgb(0x2E, 0x2E, 0x38)),
        (KeyScrollTrack,          Color.FromArgb(0x00, 0x00, 0x00, 0x00), Color.FromArgb(0x00, 0x00, 0x00, 0x00)),
        (KeyScrollThumb,          Color.FromRgb(0xC5, 0xCA, 0xD3), Color.FromRgb(0x3F, 0x3F, 0x46)),
        (KeyScrollThumbHover,     Color.FromRgb(0x8B, 0x93, 0xA0), Color.FromRgb(0xA1, 0xA1, 0xAA))
    };

    public static void ApplyTheme(AppThemeMode mode)
    {
        bool isDark = mode == AppThemeMode.Dark || (mode == AppThemeMode.System && IsSystemDark());
        Apply(isDark);
    }

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
            resources[key] = new SolidColorBrush(isDark ? dark : light);
    }
}
