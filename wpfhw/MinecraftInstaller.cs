using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

namespace wpfhw;

public sealed class MinecraftInstaller
{
    private readonly HttpClient _http;
    private readonly BmclApiClient _api;

    public MinecraftInstaller(HttpClient http)
    {
        _http = http;
        _api = new BmclApiClient(http);
    }

    public BmclApiClient Api => _api;

    public async Task InstallAsync(
        MinecraftInstallRequest request,
        IProgress<MinecraftInstallProgress> progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TargetPath))
            throw new InvalidOperationException("请选择安装目录");

        Directory.CreateDirectory(request.TargetPath);
        string mcId = SanitizeVersionSegment(request.GameVersion.Id);
        if (string.IsNullOrWhiteSpace(mcId))
            throw new InvalidOperationException("游戏版本号无效");
        string versionName = SanitizeVersionSegment(
            string.IsNullOrWhiteSpace(request.VersionName)
                ? BuildDefaultVersionName(request)
                : request.VersionName);

        if (request.Mode == McInstallMode.CoreOnly)
        {
            await InstallCoreOnlyAsync(request, versionName, progress, ct);
            return;
        }

        string minecraftRoot = ResolveMinecraftRoot(request);
        string versionsDir = Path.Combine(minecraftRoot, "versions");
        string librariesDir = Path.Combine(minecraftRoot, "libraries");
        string assetsDir = Path.Combine(minecraftRoot, "assets");
        string versionDir = ConfinePath(versionsDir, versionName);
        Directory.CreateDirectory(versionDir);
        Directory.CreateDirectory(librariesDir);
        Directory.CreateDirectory(assetsDir);

        progress.Report(new MinecraftInstallProgress("正在获取版本清单...", 2));
        string vanillaJson = await _api.GetVersionJsonAsync(mcId, ct);
        vanillaJson = BmclApiClient.MirrorText(vanillaJson);
        var vanillaNode = JsonNode.Parse(vanillaJson) as JsonObject
            ?? throw new InvalidOperationException("版本 JSON 解析失败");

        string vanillaJarPath = Path.Combine(ConfinePath(versionsDir, mcId), $"{mcId}.jar");
        if (!string.Equals(versionName, mcId, StringComparison.OrdinalIgnoreCase))
            Directory.CreateDirectory(ConfinePath(versionsDir, mcId));

        progress.Report(new MinecraftInstallProgress($"正在下载 {mcId} 核心...", 8));
        await DownloadFileAsync(
            BmclApiClient.GetClientJarUrl(mcId),
            vanillaJarPath,
            ReadSha1(vanillaNode["downloads"]?["client"]?["sha1"]),
            requireHash: true,
            ct);

        File.WriteAllText(Path.Combine(ConfinePath(versionsDir, mcId), $"{mcId}.json"), vanillaJson);

        progress.Report(new MinecraftInstallProgress("正在下载依赖库...", 20));
        await DownloadLibrariesAsync(vanillaNode, librariesDir, request.MaxConcurrency, progress, 20, 55, ct);

        progress.Report(new MinecraftInstallProgress("正在下载游戏资源...", 58));
        await DownloadAssetsAsync(vanillaNode, assetsDir, request.MaxConcurrency, progress, 58, 82, ct);

        switch (request.Loader)
        {
            case McLoaderKind.Fabric:
                await InstallFabricAsync(request, minecraftRoot, versionDir, versionName, vanillaJarPath, progress, ct);
                break;
            case McLoaderKind.Forge:
                await InstallForgeFamilyAsync(
                    request, versionDir, versionName, vanillaJarPath, librariesDir, isNeo: false, progress, ct);
                break;
            case McLoaderKind.NeoForge:
                await InstallForgeFamilyAsync(
                    request, versionDir, versionName, vanillaJarPath, librariesDir, isNeo: true, progress, ct);
                break;
            case McLoaderKind.OptiFine:
                await InstallOptiFineAsync(request, versionDir, versionName, vanillaJarPath, vanillaNode, progress, ct);
                break;
            default:
                if (!string.Equals(versionName, mcId, StringComparison.OrdinalIgnoreCase))
                {
                    CopyIfDifferent(vanillaJarPath, Path.Combine(versionDir, $"{versionName}.jar"));
                    vanillaNode["id"] = versionName;
                    File.WriteAllText(Path.Combine(versionDir, $"{versionName}.json"), vanillaNode.ToJsonString());
                }
                break;
        }

        progress.Report(new MinecraftInstallProgress("安装完成", 100));
    }

    private async Task InstallCoreOnlyAsync(
        MinecraftInstallRequest request,
        string versionName,
        IProgress<MinecraftInstallProgress> progress,
        CancellationToken ct)
    {
        string mcId = SanitizeVersionSegment(request.GameVersion.Id);
        string destDir = request.TargetPath;
        Directory.CreateDirectory(destDir);

        progress.Report(new MinecraftInstallProgress("正在获取版本清单...", 6));
        string json = BmclApiClient.MirrorText(await _api.GetVersionJsonAsync(mcId, ct));
        var node = JsonNode.Parse(json) as JsonObject;
        string? clientSha1 = ReadSha1(node?["downloads"]?["client"]?["sha1"]);

        progress.Report(new MinecraftInstallProgress("正在下载游戏核心...", 10));
        string jarPath = Path.Combine(destDir, $"{mcId}.jar");
        await DownloadFileAsync(BmclApiClient.GetClientJarUrl(mcId), jarPath, clientSha1, requireHash: true, ct);

        progress.Report(new MinecraftInstallProgress("正在保存版本清单...", 55));
        File.WriteAllText(Path.Combine(destDir, $"{mcId}.json"), json);

        if (request.Loader != McLoaderKind.Vanilla)
        {
            progress.Report(new MinecraftInstallProgress("正在下载加载器...", 70));
            string loaderPath = await DownloadLoaderArtifactAsync(request, destDir, ct);
            if (!string.IsNullOrEmpty(loaderPath))
                progress.Report(new MinecraftInstallProgress($"已保存 {Path.GetFileName(loaderPath)}", 92));
        }

        progress.Report(new MinecraftInstallProgress($"核心已保存到 {destDir}", 100));
        _ = versionName;
    }

    private async Task InstallFabricAsync(
        MinecraftInstallRequest request,
        string minecraftRoot,
        string versionDir,
        string versionName,
        string vanillaJarPath,
        IProgress<MinecraftInstallProgress> progress,
        CancellationToken ct)
    {
        progress.Report(new MinecraftInstallProgress("正在获取 Fabric 配置...", 84));
        string profileJson = BmclApiClient.MirrorText(
            await _api.GetFabricProfileJsonAsync(request.GameVersion.Id, request.LoaderVersion, ct));
        var profile = JsonNode.Parse(profileJson) as JsonObject
            ?? throw new InvalidOperationException("Fabric 配置解析失败");
        profile["id"] = versionName;
        if (profile["inheritsFrom"] == null)
            profile["inheritsFrom"] = request.GameVersion.Id;

        File.WriteAllText(Path.Combine(versionDir, $"{versionName}.json"), profile.ToJsonString());
        CopyIfDifferent(vanillaJarPath, Path.Combine(versionDir, $"{versionName}.jar"));

        progress.Report(new MinecraftInstallProgress("正在下载 Fabric 依赖...", 88));
        await DownloadLibrariesAsync(
            profile,
            Path.Combine(minecraftRoot, "libraries"),
            request.MaxConcurrency,
            progress,
            88,
            98,
            ct);
    }

    private async Task InstallForgeFamilyAsync(
        MinecraftInstallRequest request,
        string versionDir,
        string versionName,
        string vanillaJarPath,
        string librariesDir,
        bool isNeo,
        IProgress<MinecraftInstallProgress> progress,
        CancellationToken ct)
    {
        string label = isNeo ? "NeoForge" : "Forge";
        progress.Report(new MinecraftInstallProgress($"正在下载 {label} 安装器...", 84));

        string installerUrl = isNeo
            ? BmclApiClient.GetNeoForgeInstallerUrl(request.LoaderVersion)
            : BmclApiClient.GetForgeInstallerUrl(request.GameVersion.Id, request.LoaderVersion);
        string installerPath = Path.Combine(versionDir, $"{label.ToLowerInvariant()}-installer.jar");
        await DownloadFileAsync(installerUrl, installerPath, null, requireHash: false, ct);

        bool extracted = TryExtractForgeVersionJson(installerPath, versionDir, versionName, librariesDir);
        CopyIfDifferent(vanillaJarPath, Path.Combine(versionDir, $"{versionName}.jar"));
        if (!extracted)
        {
            var fallback = new JsonObject
            {
                ["id"] = versionName,
                ["inheritsFrom"] = request.GameVersion.Id,
                ["type"] = "release",
                ["mainClass"] = "net.minecraft.client.main.Main"
            };
            File.WriteAllText(Path.Combine(versionDir, $"{versionName}.json"), fallback.ToJsonString());
        }

        progress.Report(new MinecraftInstallProgress(
            extracted ? $"{label} 版本配置已写入" : $"{label} 安装器已保存到版本目录",
            96));
    }

    private async Task InstallOptiFineAsync(
        MinecraftInstallRequest request,
        string versionDir,
        string versionName,
        string vanillaJarPath,
        JsonObject vanillaNode,
        IProgress<MinecraftInstallProgress> progress,
        CancellationToken ct)
    {
        progress.Report(new MinecraftInstallProgress("正在下载 OptiFine...", 86));
        string url = BmclApiClient.GetOptiFineUrl(request.GameVersion.Id, request.LoaderExtra, request.LoaderVersion);
        string fileName = string.IsNullOrWhiteSpace(request.LoaderExtra)
            ? $"OptiFine_{request.GameVersion.Id}.jar"
            : $"OptiFine_{request.GameVersion.Id}_{request.LoaderExtra}_{request.LoaderVersion}.jar";
        string optiPath = Path.Combine(versionDir, fileName);
        await DownloadFileAsync(url, optiPath, null, requireHash: false, ct);

        CopyIfDifferent(vanillaJarPath, Path.Combine(versionDir, $"{versionName}.jar"));
        vanillaNode["id"] = versionName;
        File.WriteAllText(Path.Combine(versionDir, $"{versionName}.json"), vanillaNode.ToJsonString());
        progress.Report(new MinecraftInstallProgress("OptiFine 已放入版本目录", 96));
    }

    private async Task<string> DownloadLoaderArtifactAsync(
        MinecraftInstallRequest request, string destDir, CancellationToken ct)
    {
        string url;
        string name;
        switch (request.Loader)
        {
            case McLoaderKind.Forge:
                url = BmclApiClient.GetForgeInstallerUrl(request.GameVersion.Id, request.LoaderVersion);
                name = $"forge-{request.GameVersion.Id}-{request.LoaderVersion}-installer.jar";
                break;
            case McLoaderKind.NeoForge:
                url = BmclApiClient.GetNeoForgeInstallerUrl(request.LoaderVersion);
                name = $"neoforge-{request.LoaderVersion}-installer.jar";
                break;
            case McLoaderKind.Fabric:
                string profile = await _api.GetFabricProfileJsonAsync(
                    request.GameVersion.Id, request.LoaderVersion, ct);
                File.WriteAllText(Path.Combine(destDir, $"fabric-{request.GameVersion.Id}-{request.LoaderVersion}.json"),
                    BmclApiClient.MirrorText(profile));
                return Path.Combine(destDir, $"fabric-{request.GameVersion.Id}-{request.LoaderVersion}.json");
            case McLoaderKind.OptiFine:
                url = BmclApiClient.GetOptiFineUrl(request.GameVersion.Id, request.LoaderExtra, request.LoaderVersion);
                name = $"OptiFine_{request.GameVersion.Id}_{request.LoaderExtra}_{request.LoaderVersion}.jar";
                break;
            default:
                return "";
        }

        string path = Path.Combine(destDir, name);
        await DownloadFileAsync(url, path, null, requireHash: false, ct);
        return path;
    }

    private static bool TryExtractForgeVersionJson(
        string installerPath, string versionDir, string versionName, string librariesDir)
    {
        try
        {
            using var zip = ZipFile.OpenRead(installerPath);
            var versionEntry = zip.GetEntry("version.json");
            if (versionEntry == null) return false;

            using var stream = versionEntry.Open();
            using var reader = new StreamReader(stream);
            string json = BmclApiClient.MirrorText(reader.ReadToEnd());
            var node = JsonNode.Parse(json) as JsonObject;
            if (node == null) return false;
            node["id"] = versionName;
            File.WriteAllText(Path.Combine(versionDir, $"{versionName}.json"), node.ToJsonString());

            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith("maven/", StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.FullName.EndsWith('/')) continue;
                string relative = entry.FullName["maven/".Length..].Replace('/', Path.DirectorySeparatorChar);
                string dest = Path.Combine(librariesDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task DownloadLibrariesAsync(
        JsonObject versionNode,
        string librariesDir,
        int concurrency,
        IProgress<MinecraftInstallProgress> progress,
        double startPercent,
        double endPercent,
        CancellationToken ct)
    {
        var jobs = new List<(string Url, string Path, string? Sha1)>();
        if (versionNode["libraries"] is not JsonArray libs) return;

        foreach (var item in libs)
        {
            if (item is not JsonObject lib) continue;
            if (!LibraryAllowedOnWindows(lib)) continue;

            CollectLibraryJobs(lib, librariesDir, jobs);
        }

        await DownloadManyAsync(jobs, concurrency, progress, startPercent, endPercent, "依赖库", ct);
    }

    private static void CollectLibraryJobs(
        JsonObject lib, string librariesDir, List<(string Url, string Path, string? Sha1)> jobs)
    {
        if (lib["downloads"]?["artifact"] is JsonObject artifact)
        {
            AddJobFromArtifact(artifact, librariesDir, jobs, lib["name"]?.ToString());
        }
        else if (lib["name"] is JsonValue nameNode)
        {
            string? maven = nameNode.ToString();
            if (!string.IsNullOrWhiteSpace(maven))
            {
                string path = MavenToPath(maven);
                string url = lib["url"]?.ToString() is { Length: > 0 } rawUrl
                    ? BmclApiClient.MirrorUrl(rawUrl.TrimEnd('/') + "/" + path.Replace('\\', '/'))
                    : $"{BmclApiClient.BaseUrl}/maven/{path.Replace('\\', '/')}";
                jobs.Add((url, Path.Combine(librariesDir, path), null));
            }
        }

        if (lib["downloads"]?["classifiers"] is JsonObject classifiers)
        {
            foreach (var kv in classifiers)
            {
                if (kv.Key.Contains("natives-windows", StringComparison.OrdinalIgnoreCase)
                    && kv.Value is JsonObject nativeArtifact)
                {
                    AddJobFromArtifact(nativeArtifact, librariesDir, jobs, lib["name"]?.ToString());
                }
            }
        }
    }

    private static void AddJobFromArtifact(
        JsonObject artifact, string librariesDir, List<(string Url, string Path, string? Sha1)> jobs, string? mavenName)
    {
        string? rel = artifact["path"]?.ToString();
        if (string.IsNullOrWhiteSpace(rel) && !string.IsNullOrWhiteSpace(mavenName))
            rel = MavenToPath(mavenName);
        if (string.IsNullOrWhiteSpace(rel) || rel.Contains("..", StringComparison.Ordinal))
            return;

        string url = artifact["url"]?.ToString() ?? "";
        url = string.IsNullOrWhiteSpace(url)
            ? $"{BmclApiClient.BaseUrl}/maven/{rel.Replace('\\', '/')}"
            : BmclApiClient.MirrorUrl(url);
        jobs.Add((url, Path.Combine(librariesDir, rel.Replace('/', Path.DirectorySeparatorChar)),
            artifact["sha1"]?.ToString()));
    }

    private async Task DownloadAssetsAsync(
        JsonObject versionNode,
        string assetsDir,
        int concurrency,
        IProgress<MinecraftInstallProgress> progress,
        double startPercent,
        double endPercent,
        CancellationToken ct)
    {
        var index = versionNode["assetIndex"] as JsonObject;
        if (index == null) return;

        string indexId = SanitizeVersionSegment(index["id"]?.ToString() ?? "legacy");
        string indexUrl = BmclApiClient.MirrorUrl(index["url"]?.ToString() ?? "");
        if (string.IsNullOrWhiteSpace(indexUrl)) return;
        string? indexSha1 = index["sha1"]?.ToString();

        string indexesDir = Path.Combine(assetsDir, "indexes");
        string objectsDir = Path.Combine(assetsDir, "objects");
        Directory.CreateDirectory(indexesDir);
        Directory.CreateDirectory(objectsDir);
        string indexPath = Path.Combine(indexesDir, $"{indexId}.json");
        await DownloadFileAsync(indexUrl, indexPath, indexSha1, requireHash: !string.IsNullOrWhiteSpace(indexSha1), ct);

        string indexJson = File.ReadAllText(indexPath);
        var indexNode = JsonNode.Parse(indexJson) as JsonObject;
        if (indexNode?["objects"] is not JsonObject objects) return;

        var jobs = new List<(string Url, string Path, string? Sha1)>();
        foreach (var kv in objects)
        {
            if (kv.Value is not JsonObject obj) continue;
            string? hash = obj["hash"]?.ToString();
            if (string.IsNullOrWhiteSpace(hash) || hash.Length < 2) continue;
            string prefix = hash[..2];
            if (!hash.All(Uri.IsHexDigit) || hash.Contains("..", StringComparison.Ordinal))
                continue;
            string dest = Path.Combine(objectsDir, prefix, hash);
            jobs.Add(($"{BmclApiClient.BaseUrl}/assets/{prefix}/{hash}", dest, hash));
        }

        await DownloadManyAsync(jobs, concurrency, progress, startPercent, endPercent, "游戏资源", ct);
    }

    private async Task DownloadManyAsync(
        List<(string Url, string Path, string? Sha1)> jobs,
        int concurrency,
        IProgress<MinecraftInstallProgress> progress,
        double startPercent,
        double endPercent,
        string label,
        CancellationToken ct)
    {
        var pending = jobs
            .Where(j => !FileLooksReady(j.Path, j.Sha1))
            .DistinctBy(j => j.Path)
            .ToList();
        if (pending.Count == 0)
        {
            progress.Report(new MinecraftInstallProgress($"{label}已就绪", endPercent));
            return;
        }

        int total = pending.Count;
        int done = 0;
        using var gate = new SemaphoreSlim(Math.Clamp(concurrency, 1, 16));
        var errors = new ConcurrentBag<string>();

        await Task.WhenAll(pending.Select(async job =>
        {
            await gate.WaitAsync(ct);
            try
            {
                await DownloadFileAsync(job.Url, job.Path, job.Sha1, requireHash: job.Sha1 is { Length: > 0 }, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(job.Path)}: {ex.Message}");
            }
            finally
            {
                int current = Interlocked.Increment(ref done);
                double percent = startPercent + (endPercent - startPercent) * current / total;
                progress.Report(new MinecraftInstallProgress($"正在下载{label} {current}/{total}", percent));
                gate.Release();
            }
        }));

        if (errors.Count > total / 2)
            throw new InvalidOperationException($"{label}下载失败过多：{errors.First()}");
    }

    private async Task DownloadFileAsync(
        string url, string destPath, string? sha1, bool requireHash, CancellationToken ct)
    {
        if (requireHash && string.IsNullOrWhiteSpace(sha1))
            throw new InvalidOperationException($"缺少校验哈希：{Path.GetFileName(destPath)}");

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        if (File.Exists(destPath) && new FileInfo(destPath).Length > 0)
        {
            if (string.IsNullOrWhiteSpace(sha1) || Sha1Matches(destPath, sha1))
                return;
            File.Delete(destPath);
        }

        string temp = destPath + ".part";
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(15));

            HttpResponseMessage? response = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                response?.Dispose();
                response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                if ((int)response.StatusCode is 429 or >= 500)
                {
                    await Task.Delay(400 * (attempt + 1), timeoutCts.Token);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                break;
            }

            using (response)
            {
                if (response == null)
                    throw new InvalidOperationException("下载未返回响应");
                response.EnsureSuccessStatusCode();
                long? contentLength = response.Content.Headers.ContentLength;
                const long maxBytes = 512L * 1024 * 1024;
                if (contentLength is > maxBytes)
                    throw new InvalidOperationException($"文件过大：{Path.GetFileName(destPath)}");

                await using var remote = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
                await using var local = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                byte[] buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await remote.ReadAsync(buffer, timeoutCts.Token)) > 0)
                {
                    written += read;
                    if (written > maxBytes)
                        throw new InvalidOperationException($"文件过大：{Path.GetFileName(destPath)}");
                    await local.WriteAsync(buffer.AsMemory(0, read), timeoutCts.Token);
                }
            }

            if (!string.IsNullOrWhiteSpace(sha1) && !Sha1Matches(temp, sha1))
                throw new InvalidOperationException($"文件校验失败：{Path.GetFileName(destPath)}");

            if (File.Exists(destPath))
                File.Delete(destPath);
            File.Move(temp, destPath);
        }
        catch
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            throw;
        }
    }

    private static bool Sha1Matches(string path, string expected)
    {
        try
        {
            using var stream = File.OpenRead(path);
            byte[] hash = SHA1.HashData(stream);
            return Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool LibraryAllowedOnWindows(JsonObject lib)
    {
        if (lib["rules"] is not JsonArray rules || rules.Count == 0)
            return true;

        bool allow = false;
        foreach (var rule in rules)
        {
            if (rule is not JsonObject obj) continue;
            string action = obj["action"]?.ToString() ?? "";
            var os = obj["os"] as JsonObject;
            bool match = os == null || string.Equals(os["name"]?.ToString(), "windows", StringComparison.OrdinalIgnoreCase);
            if (!match) continue;
            allow = action == "allow";
        }
        return allow;
    }

    private static string MavenToPath(string maven)
    {
        string[] parts = maven.Split(':');
        if (parts.Length < 3) return maven.Replace('.', Path.DirectorySeparatorChar) + ".jar";
        string group = parts[0].Replace('.', Path.DirectorySeparatorChar);
        string artifact = parts[1];
        string version = parts[2];
        string classifier = parts.Length > 3 ? $"-{parts[3]}" : "";
        return Path.Combine(group, artifact, version, $"{artifact}-{version}{classifier}.jar");
    }

    private static string? ReadSha1(JsonNode? node) => node?.ToString();

    private static void CopyIfDifferent(string source, string dest)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(source, dest, overwrite: true);
    }

    private static string ResolveMinecraftRoot(MinecraftInstallRequest request)
    {
        string path = Path.GetFullPath(request.TargetPath);
        if (request.Mode == McInstallMode.CustomDirectory)
        {
            Directory.CreateDirectory(path);
            return path;
        }

        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name.Equals("versions", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(path) ?? path;
        if (name.Equals(".minecraft", StringComparison.OrdinalIgnoreCase))
            return path;
        return path;
    }

    public static string BuildDefaultVersionName(MinecraftInstallRequest request)
    {
        string id = SanitizeVersionSegment(request.GameVersion.Id);
        if (request.Loader == McLoaderKind.Vanilla)
            return id;
        string loader = SanitizeVersionSegment(request.LoaderVersion);
        return request.Loader switch
        {
            McLoaderKind.Forge => $"{id}-Forge-{loader}",
            McLoaderKind.Fabric => $"{id}-Fabric-{loader}",
            McLoaderKind.NeoForge => $"{id}-NeoForge-{loader}",
            McLoaderKind.OptiFine => $"{id}-OptiFine-{loader}",
            _ => id
        };
    }

    public static string SanitizeVersionSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("版本名不能为空");

        string name = value.Trim();
        if (name.Contains("..", StringComparison.Ordinal)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new InvalidOperationException("版本名包含非法字符");

        if (name.Length > 80)
            name = name[..80];
        return name;
    }

    private static bool FileLooksReady(string path, string? sha1)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            return false;
        return string.IsNullOrWhiteSpace(sha1) || Sha1Matches(path, sha1);
    }

    private static string ConfinePath(string root, string relative)
    {
        string fullRoot = Path.GetFullPath(root);
        string candidate = Path.GetFullPath(Path.Combine(fullRoot, relative));
        string prefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && !candidate.Equals(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("安装路径越界");
        return candidate;
    }
}
