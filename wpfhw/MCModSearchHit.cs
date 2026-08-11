using System.Text.RegularExpressions;

namespace wpfhw;

/// <summary>
/// MC百科（mcmod.cn）搜索结果项，通过解析搜索页 HTML 得到。
/// </summary>
public class MCModSearchHit
{
    /// <summary>MC百科页面链接，如 https://www.mcmod.cn/class/2.html</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>MC百科站内 ID</summary>
    public string McModId { get; set; } = string.Empty;

    /// <summary>原始标题，如 [IC2] 工业时代2 (Industrial Craft 2)</summary>
    public string RawTitle { get; set; } = string.Empty;

    /// <summary>展示用标题（已去掉 em 高亮标签）</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>从标题括号中提取的英文名，用于去 Modrinth 搜索，如 Industrial Craft 2</summary>
    public string EnglishName { get; set; } = string.Empty;

    /// <summary>描述文本</summary>
    public string Description { get; set; } = string.Empty;

    public string SourceDisplay => "来源：MC百科";

    public string DownloadsDisplay => SourceDisplay;

    public string IconUrl => "";

    /// <summary>从标题中提取括号内的英文名。标题常见格式：[缩写] 中文名 (English Name)</summary>
    public static string ExtractEnglishName(string title)
    {
        // 优先匹配最后的 (xxx) 或 （xxx）
        var match = Regex.Match(title, @"[（(]\s*([^（）()]+?)\s*[）)]\s*$");
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }
        return "";
    }

    /// <summary>从 MC百科 URL 中提取数字 ID，如 class/2.html -> 2</summary>
    public static string ExtractIdFromUrl(string url)
    {
        var match = Regex.Match(url, @"/(\d+)\.html");
        return match.Success ? match.Groups[1].Value : "";
    }
}
