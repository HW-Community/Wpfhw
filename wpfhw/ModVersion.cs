using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace wpfhw;

public class ModVersion
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("version_number")]
    public string VersionNumber { get; set; } = string.Empty;

    [JsonIgnore]
    public List<ModFile> Files { get; set; } = new();

    [JsonPropertyName("files")]
    public List<ModFile>? FilesRaw
    {
        get => Files;
        set => Files = value ?? new();
    }

    [JsonIgnore]
    public List<string> GameVersions { get; set; } = new();

    [JsonPropertyName("game_versions")]
    public List<string>? GameVersionsRaw
    {
        get => GameVersions;
        set => GameVersions = value ?? new();
    }

    [JsonIgnore]
    public List<string> Loaders { get; set; } = new();

    [JsonPropertyName("loaders")]
    public List<string>? LoadersRaw
    {
        get => Loaders;
        set => Loaders = value ?? new();
    }

    [JsonPropertyName("date_published")]
    public DateTime? DatePublished { get; set; }
}

public class ModFile
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("filename")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("primary")]
    public bool IsPrimary { get; set; }
}
