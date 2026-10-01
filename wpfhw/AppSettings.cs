using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace wpfhw;

public class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string DownloadPath { get; set; } = "";
    public string LastProjectType { get; set; } = "mod";
    public double WindowWidth { get; set; } = 1080;
    public double WindowHeight { get; set; } = 720;

    public static AppSettings Load()
    {
        AppStorage.Initialize();

        try
        {
            if (File.Exists(AppStorage.SettingsFile))
            {
                string json = File.ReadAllText(AppStorage.SettingsFile);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null) return loaded;
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
