using System.Text.Json.Serialization;

namespace wpfhw;

public enum McLoaderKind
{
    Vanilla,
    Forge,
    Fabric,
    NeoForge,
    OptiFine
}

public enum McInstallMode
{
    CoreOnly,
    VersionsFolder,
    CustomDirectory
}

public class McVersionManifest
{
    [JsonPropertyName("latest")]
    public McLatestVersions Latest { get; set; } = new();

    [JsonPropertyName("versions")]
    public List<McVersionInfo> Versions { get; set; } = new();
}

public class McLatestVersions
{
    [JsonPropertyName("release")]
    public string Release { get; set; } = "";

    [JsonPropertyName("snapshot")]
    public string Snapshot { get; set; } = "";
}

public class McVersionInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("releaseTime")]
    public DateTime ReleaseTime { get; set; }

    public string TypeLabel => Type switch
    {
        "release" => "正式版",
        "snapshot" => "快照",
        _ => "远古"
    };

    public string TimeDisplay => ReleaseTime == default
        ? ""
        : ReleaseTime.ToLocalTime().ToString("yyyy-MM-dd");

    public bool IsRelease => Type == "release";
    public bool IsSnapshot => Type == "snapshot";
    public bool IsLegacy => !IsRelease && !IsSnapshot;
}

public class ForgeBuild
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("mcversion")]
    public string McVersion { get; set; } = "";

    [JsonPropertyName("modified")]
    public DateTime? Modified { get; set; }

    [JsonPropertyName("build")]
    public long Build { get; set; }

    public string Display => Version;
}

public class FabricLoaderEntry
{
    [JsonPropertyName("loader")]
    public FabricLoaderInfo Loader { get; set; } = new();
}

public class FabricLoaderInfo
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("stable")]
    public bool Stable { get; set; }

    public string Display => Stable ? $"{Version}  稳定" : Version;
}

public class NeoForgeBuild
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("mcversion")]
    public string McVersion { get; set; } = "";

    [JsonPropertyName("rawVersion")]
    public string RawVersion { get; set; } = "";

    [JsonPropertyName("installerPath")]
    public string InstallerPath { get; set; } = "";

    public string Display => Version;
}

public class OptiFineBuild
{
    [JsonPropertyName("mcversion")]
    public string McVersion { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("patch")]
    public string Patch { get; set; } = "";

    [JsonPropertyName("filename")]
    public string FileName { get; set; } = "";

    public string Display => string.IsNullOrWhiteSpace(FileName)
        ? $"{Type}_{Patch}"
        : FileName;

    public bool IsPreview =>
        FileName.Contains("preview", StringComparison.OrdinalIgnoreCase)
        || Patch.Contains("pre", StringComparison.OrdinalIgnoreCase);
}

public class MinecraftInstallRequest
{
    public McVersionInfo GameVersion { get; set; } = new();
    public McLoaderKind Loader { get; set; }
    public string LoaderVersion { get; set; } = "";
    public string LoaderExtra { get; set; } = "";
    public McInstallMode Mode { get; set; }
    public string TargetPath { get; set; } = "";
    public string VersionName { get; set; } = "";
    public int MaxConcurrency { get; set; } = 3;
    public bool OpenFolderWhenDone { get; set; }
}

public sealed record MinecraftInstallProgress(string Message, double Percent);

public sealed record LoaderOption(string Value, string Extra, string Display);
