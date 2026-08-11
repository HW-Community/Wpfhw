using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace wpfhw;

public class ModSearchHit
{
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("project_type")]
    public string ProjectType { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("icon_url")]
    public string IconUrl { get; set; } = string.Empty;

    [JsonPropertyName("downloads")]
    public long Downloads { get; set; }

    [JsonPropertyName("follows")]
    public long Followers { get; set; }

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("latest_version")]
    public string LatestVersion { get; set; } = string.Empty;

    [JsonPropertyName("license")]
    public string License { get; set; } = string.Empty;

    [JsonIgnore]
    public List<string> Categories { get; set; } = new();

    [JsonPropertyName("categories")]
    public List<string>? CategoriesRaw
    {
        get => Categories;
        set => Categories = value ?? new();
    }

    [JsonIgnore]
    public List<string> DisplayCategories { get; set; } = new();

    [JsonPropertyName("display_categories")]
    public List<string>? DisplayCategoriesRaw
    {
        get => DisplayCategories;
        set => DisplayCategories = value ?? new();
    }

    [JsonIgnore]
    public List<string> Versions { get; set; } = new();

    [JsonPropertyName("versions")]
    public List<string>? VersionsRaw
    {
        get => Versions;
        set => Versions = value ?? new();
    }

    [JsonPropertyName("date_created")]
    public string DateCreated { get; set; } = string.Empty;

    [JsonPropertyName("date_modified")]
    public string DateModified { get; set; } = string.Empty;

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

    /// <summary>
    /// 用扩展字段兜 featured_gallery 等任何 Modrinth 可能返回的字段，
    /// 避免因类型不匹配（如 null 或对象而非数组）导致整条搜索结果反序列化失败。
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }

    [JsonIgnore]
    public List<string> FeaturedGallery
    {
        get
        {
            if (ExtraFields == null || !ExtraFields.TryGetValue("featured_gallery", out var el))
                return new();
            if (el.ValueKind != JsonValueKind.Array) return new();
            var list = new List<string>();
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    list.Add(item.GetString() ?? "");
            }
            return list;
        }
    }

    [JsonIgnore]
    public string OriginalTitle { get; set; } = string.Empty;

    [JsonIgnore]
    public string OriginalDescription { get; set; } = string.Empty;

    public string DownloadsDisplay => FormatDownloads(Downloads);

    private static string FormatDownloads(long count)
    {
        if (count >= 100000000)
            return $"{count / 100000000.0:F1}亿次下载";
        if (count >= 10000)
            return $"{count / 10000.0:F1}万次下载";
        if (count >= 1000)
            return $"{count / 1000.0:F1}千次下载";
        return $"{count}次下载";
    }
}
