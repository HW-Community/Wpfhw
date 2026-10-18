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

    [JsonPropertyName("version_type")]
    public string VersionType { get; set; } = string.Empty;

    [JsonIgnore]
    public List<ModDependency> Dependencies { get; set; } = new();

    [JsonPropertyName("dependencies")]
    public List<ModDependency>? DependenciesRaw
    {
        get => Dependencies;
        set => Dependencies = value ?? new();
    }
}

public class ModDependency
{
    [JsonPropertyName("version_id")]
    public string? VersionId { get; set; }

    [JsonPropertyName("project_id")]
    public string? ProjectId { get; set; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }

    [JsonPropertyName("dependency_type")]
    public string DependencyType { get; set; } = string.Empty;

    public bool IsRequired => DependencyType == "required";
}

/// <summary>Modrinth 项目信息，用于把依赖的 project_id 解析成可展示、可跳转的条目。</summary>
public class ModProjectInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("project_type")]
    public string ProjectType { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("icon_url")]
    public string IconUrl { get; set; } = string.Empty;
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
