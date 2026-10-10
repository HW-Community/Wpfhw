using System.IO;

namespace wpfhw;

public static class AppStorage
{
    public static string RootDirectory { get; private set; } = "";
    public static string SettingsFile { get; private set; } = "";
    public static string LogFile { get; private set; } = "";
    public static string CacheDirectory { get; private set; } = "";
    public static string IconCacheDirectory { get; private set; } = "";

    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;

        RootDirectory = Path.Combine(AppContext.BaseDirectory, ".wpfhw");
        SettingsFile = Path.Combine(RootDirectory, "settings.json");
        LogFile = Path.Combine(RootDirectory, "app.log");
        CacheDirectory = Path.Combine(RootDirectory, "cache");
        IconCacheDirectory = Path.Combine(CacheDirectory, "icons");

        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(IconCacheDirectory);

        _initialized = true;
    }

    public static void Log(string message)
    {
        try
        {
            Initialize();
            File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
