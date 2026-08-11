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

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = new();

    [JsonPropertyName("display_categories")]
    public List<string> DisplayCategories { get; set; } = new();

    [JsonPropertyName("versions")]
    public List<string> Versions { get; set; } = new();

    [JsonPropertyName("date_created")]
    public string DateCreated { get; set; } = string.Empty;

    [JsonPropertyName("date_modified")]
    public string DateModified { get; set; } = string.Empty;

    [JsonPropertyName("game_versions")]
    public List<string> GameVersions { get; set; } = new();

    [JsonPropertyName("loaders")]
    public List<string> Loaders { get; set; } = new();

    [JsonPropertyName("featured_gallery")]
    public List<string> FeaturedGallery { get; set; } = new();

    /// <summary>应用中译后，保留 Modrinth 原始英文标题（可选）</summary>
    [JsonIgnore]
    public string OriginalTitle { get; set; } = string.Empty;

    /// <summary>应用中译后，保留 Modrinth 原始英文描述（可选）</summary>
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
