using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace OpenTaskbar.Services;

/// <summary>Looks up the latest GitHub release and applies in-place updates.</summary>
public static class UpdateChecker
{
    /// <summary>The repository whose releases are checked (the project's origin remote).</summary>
    // This fork's own releases. Pointing at upstream would let the in-place updater replace the fork with upstream builds.
    public const string Repository = "jvsleiman/barra-tarefas";

    public static string ReleasesPage => $"https://github.com/{Repository}/releases/latest";

    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

    public sealed record Release(Version Version, string Tag, string Url, string? DownloadUrl = null, long? Size = null, string? ChecksumUrl = null);

    public enum UpdateStatus { Available, UpToDate, CannotCheck }

    public sealed record UpdateCheck(UpdateStatus Status, Release? Release);

    /// <summary>
    /// Asks GitHub for the latest release. A private repository (or no network) answers 404/fails for anonymous
    /// requests: that is <see cref="UpdateStatus.CannotCheck"/>, never an error, and no token is ever embedded.
    /// </summary>
    public static async Task<UpdateCheck> CheckAsync(CancellationToken cancellation)
    {
        var (release, checkedOk) = await FindNewerAsync(cancellation);
        return !checkedOk ? new UpdateCheck(UpdateStatus.CannotCheck, null)
            : release is null ? new UpdateCheck(UpdateStatus.UpToDate, null)
            : new UpdateCheck(UpdateStatus.Available, release);
    }

    /// <summary>First 64-hex token of a .sha256 file ("hash  OpenTaskbar.exe" or just the hash).</summary>
    public static string? ParseChecksum(string content)
    {
        var token = content.Split((char[])[' ', '\t', '\r', '\n', '*'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return token is { Length: 64 } && token.All(Uri.IsHexDigit) ? token.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Checks a downloaded OpenTaskbar.exe before installing it: a Windows executable whose version resource says
    /// OpenTaskbar and is newer than the running version. Returns the version, or the UiText key of the problem.
    /// </summary>
    public static (Version? Version, string? ErrorKey) ValidateLocalFile(string path, Version currentVersion)
    {
        if (!File.Exists(path)) return (null, "UpdateFileInvalid");
        try
        {
            var header = new byte[2];
            using (var stream = File.OpenRead(path)) _ = stream.Read(header, 0, 2);
            if (header[0] != 0x4D || header[1] != 0x5A) return (null, "UpdateFileInvalid");
            var info = FileVersionInfo.GetVersionInfo(path);
            var isOpenTaskbar = new[] { info.ProductName, info.OriginalFilename, info.InternalName, info.FileDescription }
                .Any(value => value?.Contains("OpenTaskbar", StringComparison.OrdinalIgnoreCase) == true);
            if (!isOpenTaskbar) return (null, "UpdateFileNotOpenTaskbar");
            var version = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
            return IsNewer(version, currentVersion) ? (version, null) : (version, "UpdateFileNotNewer");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, "UpdateFileInvalid");
        }
    }

    public static bool IsNewer(Version candidate, Version current) => Normalize(candidate) > Normalize(current);

    /// <summary>Installs a validated local OpenTaskbar.exe through the same replace-and-restart path as downloads.</summary>
    public static string? ApplyFromFile(string path)
    {
        var (_, errorKey) = ValidateLocalFile(path, CurrentVersion);
        if (errorKey is not null) return UiText.Get(errorKey);
        var targetPath = GetExecutablePath();
        if (string.IsNullOrEmpty(targetPath)) return UiText.Get("UpdateErrorNoPath");
        if (string.Equals(Path.GetFileName(targetPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase)) return UiText.Get("UpdateErrorDevBuild");
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "OpenTaskbar", "update");
            Directory.CreateDirectory(tempDir);
            var tempExe = Path.Combine(tempDir, "OpenTaskbar.exe");
            File.Copy(path, tempExe, overwrite: true);
            AppLog.Info($"Installing update from {path} to {targetPath}...");
            LaunchUpdaterAndRestart(tempExe, targetPath);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            AppLog.Error($"Local update failed: {ex.Message}");
            return UiText.Get("UpdateFailed");
        }
    }

    /// <summary>The newer release (null when up to date); <c>Checked</c> is false when GitHub could not be reached.</summary>
    public static async Task<(Release? Release, bool Checked)> FindNewerAsync(CancellationToken cancellation)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OpenTaskbar", CurrentVersion.ToString(3)));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await client.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", cancellation);
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Info($"Update check: GitHub returned {(int)response.StatusCode}.");
                return (null, false);
            }

            var json = await response.Content.ReadAsStringAsync(cancellation);
            return (ParseRelease(json, CurrentVersion), true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException
            or TimeoutException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            AppLog.Info($"Update check failed: {ex.Message}");
            return (null, false);
        }
    }

    /// <summary>Parses release json and returns a newer Release if available, or null.</summary>
    public static Release? ParseRelease(string json, Version currentVersion)
    {
        using var document = JsonDocument.Parse(json);
        var tag = document.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? string.Empty : string.Empty;
        var url = document.RootElement.TryGetProperty("html_url", out var html) ? html.GetString() ?? ReleasesPage : ReleasesPage;
        if (!Version.TryParse(tag.TrimStart('v', 'V').Split('-', '+')[0], out var version)) return null;

        string? downloadUrl = null;
        string? checksumUrl = null;
        long? size = null;
        if (document.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.Equals(name, "OpenTaskbar.exe.sha256", StringComparison.OrdinalIgnoreCase))
                {
                    checksumUrl = asset.TryGetProperty("browser_download_url", out var sumUrl) ? sumUrl.GetString() : null;
                    continue;
                }
                if (string.Equals(name, "OpenTaskbar.exe", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.TryGetProperty("browser_download_url", out var bdu) ? bdu.GetString() : null;
                    size = asset.TryGetProperty("size", out var sz) ? sz.GetInt64() : null;
                }
                if (downloadUrl is null && name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
                {
                    downloadUrl = asset.TryGetProperty("browser_download_url", out var bdu) ? bdu.GetString() : null;
                    size = asset.TryGetProperty("size", out var sz) ? sz.GetInt64() : null;
                }
            }
        }

        return Normalize(version) > Normalize(currentVersion) ? new Release(version, tag, url, downloadUrl, size, checksumUrl) : null;
    }

    /// <summary>Finds the currently running executable path on disk.</summary>
    public static string? GetExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
            return Path.GetFullPath(processPath);

        var modulePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(modulePath) && File.Exists(modulePath))
            return Path.GetFullPath(modulePath);

        return null;
    }

    /// <summary>
    /// Downloads the updated binary from the release asset, validates it, and executes the replacement script before shutdown.
    /// </summary>
    public static async Task<(bool Success, string? Error)> DownloadAndApplyAsync(
        Release release,
        CancellationToken cancellation = default)
    {
        var targetPath = GetExecutablePath();
        if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
        {
            return (false, UiText.Get("UpdateErrorNoPath"));
        }

        if (string.Equals(Path.GetFileName(targetPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return (false, UiText.Get("UpdateErrorDevBuild"));
        }

        if (string.IsNullOrEmpty(release.DownloadUrl))
        {
            ShellShortcuts.OpenUri(release.Url);
            return (false, UiText.Get("UpdateErrorNoAsset"));
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "OpenTaskbar", "update");
        Directory.CreateDirectory(tempDir);
        var tempExe = Path.Combine(tempDir, "OpenTaskbar.exe");
        var partFile = tempExe + ".part";

        try
        {
            if (File.Exists(partFile)) File.Delete(partFile);

            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OpenTaskbar", CurrentVersion.ToString(3)));

            using var response = await client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (!response.IsSuccessStatusCode)
            {
                return (false, UiText.Format("UpdateErrorDownload", (int)response.StatusCode));
            }

            await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellation))
            await using (var fileStream = new FileStream(partFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await contentStream.CopyToAsync(fileStream, cancellation);
            }

            var fileInfo = new FileInfo(partFile);
            if (fileInfo.Length < 1_000_000)
            {
                TryDelete(partFile);
                return (false, UiText.Get("UpdateFileInvalid"));
            }

            if (release.Size.HasValue && release.Size.Value > 0 && fileInfo.Length != release.Size.Value)
            {
                TryDelete(partFile);
                return (false, UiText.Get("UpdateFileInvalid"));
            }

            byte[] header = new byte[2];
            using (var fs = File.OpenRead(partFile))
            {
                _ = fs.Read(header, 0, 2);
            }
            if (header[0] != 0x4D || header[1] != 0x5A) // 'M', 'Z'
            {
                TryDelete(partFile);
                return (false, UiText.Get("UpdateFileInvalid"));
            }

            // The release publishes OpenTaskbar.exe.sha256; refuse a download that does not match it.
            if (!string.IsNullOrEmpty(release.ChecksumUrl))
            {
                var expected = ParseChecksum(await client.GetStringAsync(release.ChecksumUrl, cancellation));
                string actual;
                await using (var stream = File.OpenRead(partFile))
                    actual = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellation)).ToLowerInvariant();
                if (expected is null || actual != expected)
                {
                    TryDelete(partFile);
                    AppLog.Warning($"Update checksum mismatch: expected {expected}, got {actual}.");
                    return (false, UiText.Get("UpdateErrorChecksum"));
                }
            }

            if (File.Exists(tempExe)) File.Delete(tempExe);
            File.Move(partFile, tempExe);

            AppLog.Info($"Downloaded update to {tempExe}. Applying to {targetPath}...");
            LaunchUpdaterAndRestart(tempExe, targetPath);
            return (true, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            TryDelete(partFile);
            AppLog.Error($"Update download/apply error: {ex.Message}");
            return (false, ex.Message);
        }
    }

    internal static string GenerateUpdaterScript(string tempExe, string targetExe)
    {
        return $"""
        @echo off
        set "SRC={tempExe}"
        set "DST={targetExe}"
        set ATTEMPTS=0
        :wait_loop
        timeout /t 1 /nobreak >nul
        copy /y "%SRC%" "%DST%" >nul 2>&1
        if not errorlevel 1 goto launch
        set /a ATTEMPTS+=1
        if %ATTEMPTS% lss 20 goto wait_loop
        goto cleanup

        :launch
        del /f /q "%SRC%" >nul 2>&1
        start "" "%DST%"

        :cleanup
        del /f /q "%~f0" >nul 2>&1
        """;
    }

    internal static void LaunchUpdaterAndRestart(string tempExe, string targetPath)
    {
        var targetDir = Path.GetDirectoryName(targetPath) ?? AppContext.BaseDirectory;
        var scriptPath = Path.Combine(Path.GetDirectoryName(tempExe)!, "apply-update.cmd");
        var script = GenerateUpdaterScript(tempExe, targetPath);
        File.WriteAllText(scriptPath, script);

        var requiresElevation = !IsDirectoryWritable(targetDir);
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = requiresElevation,
            CreateNoWindow = !requiresElevation,
            WindowStyle = requiresElevation ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden,
            WorkingDirectory = targetDir
        };

        if (requiresElevation)
        {
            startInfo.Verb = "runas";
            startInfo.Arguments = $"/c \"\"{scriptPath}\"\"";
        }
        else
        {
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(scriptPath);
        }

        Process.Start(startInfo);

        if (Application.Current is { } app)
        {
            app.Dispatcher.BeginInvoke(() => app.Shutdown(0));
        }
        else
        {
            Environment.Exit(0);
        }
    }

    private static bool IsDirectoryWritable(string dirPath)
    {
        try
        {
            var testFile = Path.Combine(dirPath, $"_update_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
}
