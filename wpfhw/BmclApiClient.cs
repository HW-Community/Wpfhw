using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace wpfhw;

public sealed class BmclApiClient
{
    public const string BaseUrl = "https://bmclapi2.bangbang93.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };

    private readonly HttpClient _http;

    public BmclApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<McVersionManifest> GetManifestAsync(CancellationToken ct)
    {
        foreach (string path in new[]
                 {
                     "/mc/game/version_manifest_v2.json",
                     "/mc/game/version_manifest.json"
                 })
        {
            try
            {
                string json = await _http.GetStringAsync(BaseUrl + path, ct);
                var manifest = JsonSerializer.Deserialize<McVersionManifest>(json, JsonOptions);
                if (manifest?.Versions is { Count: > 0 })
                    return manifest;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }

        throw new InvalidOperationException("无法从 BMCLAPI 获取版本列表");
    }

    public Task<string> GetVersionJsonAsync(string versionId, CancellationToken ct)
        => _http.GetStringAsync($"{BaseUrl}/version/{Uri.EscapeDataString(versionId)}/json", ct);

    public static string GetClientJarUrl(string versionId)
        => $"{BaseUrl}/version/{Uri.EscapeDataString(versionId)}/client";

    public async Task<List<ForgeBuild>> GetForgeAsync(string mcVersion, CancellationToken ct)
    {
        string json = await _http.GetStringAsync(
            $"{BaseUrl}/forge/minecraft/{Uri.EscapeDataString(mcVersion)}", ct);
        return JsonSerializer.Deserialize<List<ForgeBuild>>(json, JsonOptions) ?? new();
    }

    public static string GetForgeInstallerUrl(string mcVersion, string forgeVersion)
        => $"{BaseUrl}/forge/download?mcversion={Uri.EscapeDataString(mcVersion)}&version={Uri.EscapeDataString(forgeVersion)}&category=installer&format=jar";

    public async Task<List<FabricLoaderEntry>> GetFabricAsync(string mcVersion, CancellationToken ct)
    {
        string json = await _http.GetStringAsync(
            $"{BaseUrl}/fabric-meta/v2/versions/loader/{Uri.EscapeDataString(mcVersion)}", ct);
        return JsonSerializer.Deserialize<List<FabricLoaderEntry>>(json, JsonOptions) ?? new();
    }

    public static string GetFabricProfileUrl(string mcVersion, string loaderVersion)
        => $"{BaseUrl}/fabric-meta/v2/versions/loader/{Uri.EscapeDataString(mcVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json";

    public async Task<string> GetFabricProfileJsonAsync(string mcVersion, string loaderVersion, CancellationToken ct)
        => await _http.GetStringAsync(GetFabricProfileUrl(mcVersion, loaderVersion), ct);

    public async Task<List<NeoForgeBuild>> GetNeoForgeAsync(string mcVersion, CancellationToken ct)
    {
        string json = await _http.GetStringAsync(
            $"{BaseUrl}/neoforge/list/{Uri.EscapeDataString(mcVersion)}", ct);
        return JsonSerializer.Deserialize<List<NeoForgeBuild>>(json, JsonOptions) ?? new();
    }

    public static string GetNeoForgeInstallerUrl(string neoVersion)
        => $"{BaseUrl}/maven/net/neoforged/neoforge/{Uri.EscapeDataString(neoVersion)}/neoforge-{Uri.EscapeDataString(neoVersion)}-installer.jar";

    public async Task<List<OptiFineBuild>> GetOptiFineAsync(string mcVersion, CancellationToken ct)
    {
        string json = await _http.GetStringAsync(
            $"{BaseUrl}/optifine/{Uri.EscapeDataString(mcVersion)}", ct);
        return JsonSerializer.Deserialize<List<OptiFineBuild>>(json, JsonOptions) ?? new();
    }

    public static string GetOptiFineUrl(string mcVersion, string type, string patch)
        => $"{BaseUrl}/optifine/{Uri.EscapeDataString(mcVersion)}/{Uri.EscapeDataString(type)}/{Uri.EscapeDataString(patch)}";

    public static string MirrorUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        return url
            .Replace("https://piston-meta.mojang.com", BaseUrl, StringComparison.OrdinalIgnoreCase)
            .Replace("https://piston-data.mojang.com", BaseUrl, StringComparison.OrdinalIgnoreCase)
            .Replace("https://launchermeta.mojang.com", BaseUrl, StringComparison.OrdinalIgnoreCase)
            .Replace("https://launcher.mojang.com", BaseUrl, StringComparison.OrdinalIgnoreCase)
            .Replace("https://libraries.minecraft.net", BaseUrl + "/maven", StringComparison.OrdinalIgnoreCase)
            .Replace("https://resources.download.minecraft.net", BaseUrl + "/assets", StringComparison.OrdinalIgnoreCase)
            .Replace("https://maven.minecraftforge.net", BaseUrl + "/maven", StringComparison.OrdinalIgnoreCase)
            .Replace("https://maven.neoforged.net/releases", BaseUrl + "/maven", StringComparison.OrdinalIgnoreCase)
            .Replace("https://maven.neoforged.net", BaseUrl + "/maven", StringComparison.OrdinalIgnoreCase)
            .Replace("https://maven.fabricmc.net", BaseUrl + "/maven", StringComparison.OrdinalIgnoreCase)
            .Replace("https://meta.fabricmc.net", BaseUrl + "/fabric-meta", StringComparison.OrdinalIgnoreCase);
    }
}
