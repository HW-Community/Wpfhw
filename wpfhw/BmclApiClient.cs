using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace wpfhw;

public sealed class BmclApiClient
{
    public const string BaseUrl = "https://bmclapi2.bangbang93.com";
    public const string UserAgent = "HW-Community/Wpfhw (https://github.com/HW-Community/Wpfhw; Windows)";

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
        var errors = new List<Exception>();
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
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        throw new InvalidOperationException("无法从 BMCLAPI 获取版本列表",
            errors.Count == 1 ? errors[0] : new AggregateException(errors));
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

    public Task<string> GetFabricProfileJsonAsync(string mcVersion, string loaderVersion, CancellationToken ct)
        => _http.GetStringAsync(GetFabricProfileUrl(mcVersion, loaderVersion), ct);

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
        if (!Uri.TryCreate(url, UriKind.Absolute, out var source)) return url;
        if (!string.Equals(source.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(source.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            return url;

        foreach (var (from, to) in MirrorPrefixes)
        {
            if (!Uri.TryCreate(from, UriKind.Absolute, out var fromUri)) continue;
            if (!string.Equals(source.Host, fromUri.Host, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(source.Scheme, fromUri.Scheme, StringComparison.OrdinalIgnoreCase)) continue;

            string fromPath = fromUri.AbsolutePath.TrimEnd('/');
            string sourcePath = source.AbsolutePath;
            if (fromPath.Length > 0)
            {
                if (!sourcePath.StartsWith(fromPath, StringComparison.OrdinalIgnoreCase)) continue;
                if (sourcePath.Length > fromPath.Length && sourcePath[fromPath.Length] != '/') continue;
            }

            string remainder = fromPath.Length == 0
                ? sourcePath
                : sourcePath[fromPath.Length..];
            string result = to + remainder + source.Query;
            if (Uri.TryCreate(result, UriKind.Absolute, out var mirrored)
                && Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri)
                && string.Equals(mirrored.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
                return result;
        }

        return url;
    }

    public static string MirrorText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        foreach (var (from, to) in MirrorPrefixes)
        {
            int index = 0;
            while ((index = text.IndexOf(from, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = index;
                while (end < text.Length && !char.IsWhiteSpace(text[end])
                       && text[end] is not '"' and not '\'' and not ')' and not ',' and not '}' and not ']' and not '>')
                    end++;
                string original = text[index..end];
                string mirrored = MirrorUrl(original);
                if (string.Equals(mirrored, original, StringComparison.Ordinal))
                {
                    index = Math.Max(end, index + 1);
                    continue;
                }
                text = string.Concat(text.AsSpan(0, index), mirrored, text.AsSpan(end));
                index += mirrored.Length;
            }
        }
        return text;
    }

    private static readonly (string From, string To)[] MirrorPrefixes =
    {
        ("https://maven.neoforged.net/releases", BaseUrl + "/maven"),
        ("https://maven.neoforged.net", BaseUrl + "/maven"),
        ("https://maven.minecraftforge.net", BaseUrl + "/maven"),
        ("https://files.minecraftforge.net/maven", BaseUrl + "/maven"),
        ("https://maven.fabricmc.net", BaseUrl + "/maven"),
        ("https://meta.fabricmc.net", BaseUrl + "/fabric-meta"),
        ("https://libraries.minecraft.net", BaseUrl + "/maven"),
        ("https://resources.download.minecraft.net", BaseUrl + "/assets"),
        ("https://piston-meta.mojang.com", BaseUrl),
        ("https://piston-data.mojang.com", BaseUrl),
        ("https://launchermeta.mojang.com", BaseUrl),
        ("https://launcher.mojang.com", BaseUrl)
    };
}
