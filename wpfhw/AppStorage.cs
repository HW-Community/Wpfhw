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
        CacheDirectory = Path.Combine(RootDirectory, "cache");
        IconCacheDirectory = Path.Combine(CacheDirectory, "icons");

        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string logDir = string.IsNullOrEmpty(localApp)
            ? RootDirectory
            : Path.Combine(localApp, "Wpfhw");
        LogFile = Path.Combine(logDir, "app.log");

        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(IconCacheDirectory);
        Directory.CreateDirectory(logDir);

        _initialized = true;
    }

    public static void Log(string message)
    {
        try
        {
            if (!_initialized) Initialize();
            string safe = (message ?? "").Replace("\r", " ").Replace("\n", " ");
            File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {safe}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
