using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace wpfhw;

internal static class NativeBootstrap
{
    private const string PayloadResourceSuffix = ".Native.Wpfhw.exe";
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    public static int RunCli(string[] args)
    {
        string? nativeExe = EnsureExtracted();
        if (nativeExe is null)
        {
            AppStorage.Log("Embedded native payload is missing; CLI is unavailable.");
            return 1;
        }

        try
        {
            _ = AttachConsole(AttachParentProcess);
            var startInfo = new ProcessStartInfo
            {
                FileName = nativeExe,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return 1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            AppStorage.Log($"Run native CLI failed: {ex}");
            return 1;
        }
    }

    public static void EnsureTrayStarted()
    {
        string? nativeExe = EnsureExtracted();
        if (nativeExe is null)
        {
            AppStorage.Log("Embedded native payload is missing; tray is unavailable.");
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = nativeExe,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--tray");

            string? mainExe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(mainExe) &&
                !string.Equals(Path.GetFileName(mainExe), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.ArgumentList.Add(mainExe);
            }

            Process? process = Process.Start(startInfo);
            process?.Dispose();
        }
        catch (Exception ex)
        {
            AppStorage.Log($"Start native tray failed: {ex}");
        }
    }

    private static string? EnsureExtracted()
    {
        byte[]? payload = ReadPayload();
        if (payload is null)
        {
            return null;
        }

        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(root))
        {
            root = Path.GetTempPath();
        }

        string directory = Path.Combine(root, "Wpfhw", "native");
        try
        {
            Directory.CreateDirectory(directory);
            byte[] payloadHash = SHA256.HashData(payload);
            string hash = Convert.ToHexString(payloadHash)[..16];
            string path = Path.Combine(directory, $"Wpfhw-{hash}.exe");
            if (File.Exists(path))
            {
                if (MatchesHash(path, payloadHash))
                {
                    return path;
                }

                // 文件被篡改或损坏：先尝试删除；删不掉（例如正在运行）就换唯一文件名，
                // 绝不运行不可信内容。
                try
                {
                    File.Delete(path);
                }
                catch (Exception)
                {
                    path = Path.Combine(directory, $"Wpfhw-{hash}-{Guid.NewGuid():N}.exe");
                }
            }

            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, payload);
            try
            {
                File.Move(temp, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                File.Delete(temp);
                if (MatchesHash(path, payloadHash))
                {
                    return path;
                }

                throw;
            }

            return path;
        }
        catch (Exception ex)
        {
            AppStorage.Log($"Extract native payload failed: {ex}");
            return null;
        }
    }

    private static bool MatchesHash(string path, byte[] expectedHash)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), expectedHash);
        }
        catch
        {
            return false;
        }
    }

    private static byte[]? ReadPayload()
    {
        Assembly assembly = typeof(NativeBootstrap).Assembly;
        string? name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(PayloadResourceSuffix, StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return null;
        }

        using Stream? stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
