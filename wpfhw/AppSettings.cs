using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace wpfhw;

/// <summary>主题模式：浅色 / 深色 / 跟随系统</summary>
public enum AppThemeMode
{
    Light,
    Dark,
    System
}

public class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static AppSettings()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public string DownloadPath { get; set; } = "";
    public string LastProjectType { get; set; } = "mod";
    public double WindowWidth { get; set; } = 1080;
    public double WindowHeight { get; set; } = 720;

    /// <summary>主题模式，默认跟随系统</summary>
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;

    /// <summary>最大并发下载线程数（1-16），默认 3</summary>
    public int MaxDownloadThreads { get; set; } = 3;

    /// <summary>下载时将按资源类型（mod / resourcepack 等）创建子文件夹</summary>
    public bool CreateTypeSubfolder { get; set; }

    /// <summary>目标文件已存在时直接覆盖；否则自动重命名</summary>
    public bool OverwriteExistingFiles { get; set; }

    /// <summary>下载完成后自动打开所在文件夹</summary>
    public bool OpenFolderAfterDownload { get; set; }

    /// <summary>点击下载后跳过确认页，直接开始下载</summary>
    public bool SkipDownloadConfirm { get; set; }

    public static AppSettings Load()
    {
        AppStorage.Initialize();

        try
        {
            if (File.Exists(AppStorage.SettingsFile))
            {
                string json = File.ReadAllText(AppStorage.SettingsFile);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    loaded.MaxDownloadThreads = Math.Clamp(loaded.MaxDownloadThreads, 1, 16);
                    if (!Enum.IsDefined(loaded.ThemeMode))
                        loaded.ThemeMode = AppThemeMode.System;
                    return loaded;
                }
            }
        }
        catch
        {
        }

        return new AppSettings();
    }

    public void Save()
    {
        AppStorage.Initialize();

        try
        {
            string json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(AppStorage.SettingsFile, json);
        }
        catch
        {
        }
    }
}
