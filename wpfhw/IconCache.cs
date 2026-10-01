using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace wpfhw;

public static class IconCache
{
    private static readonly ConcurrentDictionary<string, Task<string>> InFlight = new(StringComparer.OrdinalIgnoreCase);

    public static string? TryGetLocalPath(string? url)
    {
        if (!IsRemoteUrl(url)) return null;

        AppStorage.Initialize();
        string path = GetCachePath(url!);
        return File.Exists(path) && new FileInfo(path).Length > 0 ? path : null;
    }

    public static Task<string> GetLocalPathAsync(string url, HttpClient http, CancellationToken ct)
    {
        string? cached = TryGetLocalPath(url);
        if (cached != null) return Task.FromResult(cached);

        return InFlight.GetOrAdd(url, u => DownloadAsync(u, http, ct));
    }

    private static async Task<string> DownloadAsync(string url, HttpClient http, CancellationToken ct)
    {
        AppStorage.Initialize();
        string path = GetCachePath(url);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0)
                return path;

            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using (var remote = await response.Content.ReadAsStreamAsync(ct))
            await using (var local = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
            {
                await remote.CopyToAsync(local, ct);
            }

            File.Move(temp, path, overwrite: true);
            return path;
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
        finally
        {
            InFlight.TryRemove(url, out _);
        }
    }

    public static bool IsRemoteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCachePath(string url)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        string ext = GetExtension(url);
        return Path.Combine(AppStorage.IconCacheDirectory, hash + ext);
    }

    private static string GetExtension(string url)
    {
        try
        {
            var uri = new Uri(url);
            string ext = Path.GetExtension(uri.AbsolutePath);
            if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" or ".ico")
                return ext.ToLowerInvariant();
        }
        catch
        {
        }

        return ".png";
    }
}
