using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

/// <summary>
/// Core update manager service coordinating update checks, channel filtering,
/// tamper-resistant background downloads, and seamless in-place installation handoffs.
/// Adheres to near-zero idle CPU rules by avoiding active polling.
/// </summary>
internal sealed class UpdateManagerService : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly GitHubReleaseClient _client;
    private readonly Dispatcher? _dispatcher;
    private readonly SemaphoreSlim _opLock = new(1, 1);
    private Timer? _dailyTimer;
    private volatile bool _disposed;

    public string CurrentVersionString { get; }
    public SemVersion CurrentVersion { get; }

    public UpdateStatus Status { get; private set; } = UpdateStatus.Idle;
    public UpdateManifest? LatestManifest { get; private set; }
    public string? LastErrorMessage { get; private set; }
    public string? DownloadedPackagePath { get; private set; }
    public bool IsInstallerPackage { get; private set; }
    public IReadOnlyList<ReleaseHistoryItem> ReleaseHistory { get; private set; } = Array.Empty<ReleaseHistoryItem>();

    public event Action<UpdateStatus, string?>? StatusChanged;
    public event Action<UpdateProgressInfo>? DownloadProgressChanged;
    public event Action<UpdateManifest>? UpdateAvailable;

    public UpdateManagerService(
        SettingsService settingsService,
        GitHubReleaseClient? client = null,
        Dispatcher? dispatcher = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _dispatcher = dispatcher;

        // Extract version from executing assembly, fallback to InstallerService.AppVersion
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var ver = asm.GetName().Version;
        CurrentVersionString = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : InstallerService.AppVersion;

        if (!SemVersion.TryParse(CurrentVersionString, out var parsed))
        {
            parsed = new SemVersion(0, 4, 0);
        }
        CurrentVersion = parsed;

        _client = client ?? new GitHubReleaseClient(appVersion: CurrentVersionString, gitHubToken: _settingsService.Current.GitHubToken);
    }

    /// <summary>
    /// Configures or clears the GitHub Personal Access Token for higher rate limits.
    /// </summary>
    public void SetGitHubToken(string? token)
    {
        _client.SetToken(token);
    }

    /// <summary>
    /// Starts background scheduled checks (every 24 hours).
    /// Defers the initial check by 15 seconds to ensure app startup remains instantaneous.
    /// </summary>
    public void StartBackgroundWorker()
    {
        if (_dailyTimer != null) return;

        // Check if 24 hours have elapsed since the last check
        TimeSpan initialDelay = TimeSpan.FromSeconds(15);
        var lastCheck = _settingsService.Current.LastUpdateCheckTime;
        if (lastCheck.HasValue)
        {
            var elapsed = DateTime.UtcNow - lastCheck.Value;
            if (elapsed < TimeSpan.FromHours(24))
            {
                initialDelay = TimeSpan.FromHours(24) - elapsed;
                if (initialDelay < TimeSpan.FromMinutes(5))
                {
                    initialDelay = TimeSpan.FromMinutes(5);
                }
            }
        }

        _dailyTimer = new Timer(_ => _ = RunScheduledCheckAsync(), null, initialDelay, TimeSpan.FromHours(24));
    }

    private async Task RunScheduledCheckAsync()
    {
        if (_disposed) return;
        try
        {
            if (_settingsService.Current.AutoCheckUpdates)
            {
                await CheckForUpdatesAsync(silent: true).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            App.Log($"[UpdateManager] Scheduled update check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Queries GitHub for updates, applies channel filtering (Stable vs Beta), and compares SemVer.
    /// </summary>
    public async Task<UpdateManifest?> CheckForUpdatesAsync(bool silent = false, bool force = false, CancellationToken ct = default)
    {
        if (_disposed) return null;

        bool shouldAutoDownload = false;
        UpdateManifest? resultManifest = null;

        await _opLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed) return null;

            bool alreadyDownloaded = Status == UpdateStatus.ReadyToInstall &&
                                     !string.IsNullOrEmpty(DownloadedPackagePath) &&
                                     File.Exists(DownloadedPackagePath);
            string? downloadedVersion = LatestManifest?.Version;

            SetStatus(UpdateStatus.Checking, "Checking GitHub for updates...");
            LastErrorMessage = null;

            if (force)
            {
                _client.ClearETagCache();
            }

            var manifest = await _client.FetchManifestAsync(ct: ct).ConfigureAwait(false);

            Dispatch(() =>
            {
                _settingsService.Update(s => s.LastUpdateCheckTime = DateTime.UtcNow, nameof(AppSettings.LastUpdateCheckTime));
            });

            if (manifest == null)
            {
                // Manifest unchanged (304 Not Modified) or up to date
                if (LatestManifest != null && LatestManifest.ParsedVersion > CurrentVersion)
                {
                    manifest = LatestManifest;
                }
                else
                {
                    SetStatus(UpdateStatus.UpToDate, $"Dynamic Island {CurrentVersionString} is up to date.");
                    return null;
                }
            }

            LatestManifest = manifest;

            // Channel filtering: Stable channel ignores pre-release builds
            bool userWantsBeta = string.Equals(_settingsService.Current.UpdateChannel, "Beta", StringComparison.OrdinalIgnoreCase);
            bool isReleaseBeta = manifest.ParsedVersion.IsPreRelease || manifest.ParsedChannel == UpdateChannel.Beta;

            if (!userWantsBeta && isReleaseBeta)
            {
                SetStatus(UpdateStatus.UpToDate, "Dynamic Island (Stable) is up to date.");
                return null;
            }

            // Compare version against currently executing binary
            if (manifest.ParsedVersion > CurrentVersion)
            {
                // Verify user hasn't chosen to skip this version during silent background checks (critical updates cannot be skipped)
                if (silent && !manifest.IsCritical && string.Equals(_settingsService.Current.SkippedVersion, manifest.Version, StringComparison.OrdinalIgnoreCase))
                {
                    var skipDate = _settingsService.Current.SkippedVersionDate;
                    if (skipDate.HasValue && (DateTime.UtcNow - skipDate.Value) < TimeSpan.FromDays(7))
                    {
                        SetStatus(UpdateStatus.UpToDate, $"Skipped version {manifest.Version}.");
                        return null;
                    }

                    // 7 days elapsed: reset skip state so user is reminded
                    Dispatch(() =>
                    {
                        _settingsService.Update(s =>
                        {
                            s.SkippedVersion = null;
                            s.SkippedVersionDate = null;
                        }, nameof(AppSettings.SkippedVersion));
                    });
                }

                if (alreadyDownloaded && string.Equals(downloadedVersion, manifest.Version, StringComparison.OrdinalIgnoreCase))
                {
                    SetStatus(UpdateStatus.ReadyToInstall, $"Dynamic Island {manifest.Version} is ready to install.");
                    return manifest;
                }

                SetStatus(UpdateStatus.UpdateAvailable, manifest.IsCritical
                    ? $"Critical security update {manifest.Version} is available!"
                    : $"Update {manifest.Version} is available.");
                Dispatch(() => UpdateAvailable?.Invoke(manifest));

                shouldAutoDownload = _settingsService.Current.AutoDownloadUpdates;
                resultManifest = manifest;
            }
            else
            {
                SetStatus(UpdateStatus.UpToDate, $"Dynamic Island {CurrentVersionString} is up to date.");
                return null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastErrorMessage = ex.Message;
            SetStatus(UpdateStatus.Failed, $"Update check failed: {ex.Message}");
            App.Log($"[UpdateManager] Check failed: {ex}");
            return null;
        }
        finally
        {
            _opLock.Release();
        }

        if (shouldAutoDownload && resultManifest != null && !_disposed)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await DownloadUpdateAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    App.Log($"[UpdateManager] Background auto-download failed: {ex.Message}");
                }
            }, ct);
        }

        return resultManifest;
    }

    /// <summary>
    /// Downloads the update package into %LocalAppData%\Temp\DynamicIsland\Updates
    /// and verifies cryptographic SHA-256 integrity.
    /// </summary>
    public async Task<bool> DownloadUpdateAsync(CancellationToken ct = default)
    {
        if (_disposed) return false;

        await _opLock.WaitAsync(ct).ConfigureAwait(false);
        string? targetFile = null;
        try
        {
            if (_disposed) return false;

            if (Status == UpdateStatus.ReadyToInstall &&
                !string.IsNullOrEmpty(DownloadedPackagePath) &&
                File.Exists(DownloadedPackagePath))
            {
                return true;
            }

            if (LatestManifest == null)
            {
                SetStatus(UpdateStatus.Failed, "No update package available to download.");
                return false;
            }

            // Determine best download asset
            UpdateDownloadAsset? asset = null;
            bool isInstaller = true;

            if (LatestManifest.Downloads.Installer != null && !string.IsNullOrWhiteSpace(LatestManifest.Downloads.Installer.Url))
            {
                asset = LatestManifest.Downloads.Installer;
                isInstaller = true;
            }
            else if (LatestManifest.Downloads.Portable != null && !string.IsNullOrWhiteSpace(LatestManifest.Downloads.Portable.Url))
            {
                asset = LatestManifest.Downloads.Portable;
                isInstaller = false;
            }

            if (asset == null || string.IsNullOrWhiteSpace(asset.Url))
            {
                SetStatus(UpdateStatus.Failed, "No valid download URLs found in release manifest.");
                return false;
            }

            if (!GitHubReleaseClient.IsTrustedGitHubUrl(asset.Url))
            {
                LastErrorMessage = "Refused untrusted download URL outside GitHub.";
                SetStatus(UpdateStatus.Failed, LastErrorMessage);
                App.Log($"[UpdateManager] Untrusted download URL blocked: {asset.Url}");
                return false;
            }

            if (string.IsNullOrWhiteSpace(asset.Sha256))
            {
                LastErrorMessage = "Missing cryptographic SHA-256 checksum. Update aborted for security.";
                SetStatus(UpdateStatus.Failed, LastErrorMessage);
                App.Log($"[UpdateManager] Update blocked because SHA-256 checksum is missing for {LatestManifest.Version}");
                return false;
            }

            string tempDir = Path.GetFullPath(ResolveDownloadDirectory(asset.SizeBytes));
            Directory.CreateDirectory(tempDir);

            string uriPath = new Uri(asset.Url).AbsolutePath;
            string ext = Path.GetExtension(uriPath);
            if (string.IsNullOrEmpty(ext)) ext = isInstaller ? ".exe" : ".zip";

            string safeVersion = Regex.Replace(LatestManifest.Version ?? "unknown", @"[^0-9A-Za-z._-]", "_");
            if (string.IsNullOrWhiteSpace(safeVersion)) safeVersion = "unknown";

            string filePrefix = isInstaller ? "DynamicIsland-Setup" : "DynamicIsland-Portable";
            targetFile = Path.GetFullPath(Path.Combine(tempDir, $"{filePrefix}-{safeVersion}{ext}"));
            string tempDirPrefix = tempDir.EndsWith(Path.DirectorySeparatorChar) ? tempDir : tempDir + Path.DirectorySeparatorChar;
            if (!targetFile.StartsWith(tempDirPrefix, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(UpdateStatus.Failed, "Invalid update target file path.");
                return false;
            }

            UpdateIntegrityVerifier.TryCleanFile(targetFile);
            SetStatus(UpdateStatus.Downloading, "Downloading update package...");

            var progress = new Progress<UpdateProgressInfo>(info =>
            {
                Dispatch(() => DownloadProgressChanged?.Invoke(info));
            });

            await _client.DownloadFileWithProgressAsync(asset.Url, targetFile, progress, ct).ConfigureAwait(false);

            // Step 2: Cryptographic SHA-256 integrity verification (fail closed)
            SetStatus(UpdateStatus.Verifying, "Verifying package integrity (SHA-256)...");

            bool matches = await UpdateIntegrityVerifier.VerifyFileAsync(targetFile, asset.Sha256, ct).ConfigureAwait(false);
            if (!matches)
            {
                UpdateIntegrityVerifier.TryCleanFile(targetFile);
                LastErrorMessage = "SHA-256 checksum mismatch. Downloaded package was discarded for security.";
                SetStatus(UpdateStatus.Failed, LastErrorMessage);
                App.Log($"[UpdateManager] SHA-256 mismatch for {targetFile}");
                return false;
            }

            DownloadedPackagePath = targetFile;
            IsInstallerPackage = isInstaller;

            SetStatus(UpdateStatus.ReadyToInstall, $"Dynamic Island {LatestManifest.Version} is ready to install.");
            return true;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            if (targetFile != null) UpdateIntegrityVerifier.TryCleanFile(targetFile);
            LastErrorMessage = "Release package has not been published to GitHub yet (404 Not Found).";
            SetStatus(UpdateStatus.Failed, LastErrorMessage);
            _client.ClearETagCache();
            LatestManifest = null;
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (targetFile != null) UpdateIntegrityVerifier.TryCleanFile(targetFile);
            LastErrorMessage = ex.Message;
            SetStatus(UpdateStatus.Failed, $"Download failed: {ex.Message}");
            App.Log($"[UpdateManager] Download failed: {ex}");
            return false;
        }
        finally
        {
            _opLock.Release();
        }
    }

    /// <summary>
    /// Executes the update handoff and seamlessly restarts Dynamic Island.
    /// </summary>
    public bool ApplyUpdateAndRestart()
    {
        if (string.IsNullOrEmpty(DownloadedPackagePath) || !File.Exists(DownloadedPackagePath))
        {
            SetStatus(UpdateStatus.Failed, "No verified update package ready to install.");
            return false;
        }

        string handoffPath = DownloadedPackagePath;
        if (!IsInstallerPackage && DownloadedPackagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            string? extractedExe = ExtractPortableBinarySafe(DownloadedPackagePath);
            if (string.IsNullOrEmpty(extractedExe))
            {
                SetStatus(UpdateStatus.Failed, "Failed to extract DynamicIsland.exe from portable update archive.");
                return false;
            }
            handoffPath = extractedExe;
        }

        SetStatus(UpdateStatus.Installing, "Launching updater and restarting Dynamic Island...");
        return UpdateHandoffEngine.ExecuteHandoff(handoffPath, IsInstallerPackage);
    }

    private static string? ExtractPortableBinarySafe(string zipPath)
    {
        const int MaxEntries = 5000;
        const long MaxTotalBytes = 1024L * 1024 * 1024; // 1 GB cap

        string parentDir = Path.GetDirectoryName(zipPath) ?? Path.GetTempPath();
        string stagingDir = Path.GetFullPath(Path.Combine(parentDir, $"staged_{Guid.NewGuid():N}"));
        string stagingPrefix = stagingDir.EndsWith(Path.DirectorySeparatorChar)
            ? stagingDir
            : stagingDir + Path.DirectorySeparatorChar;

        try
        {
            Directory.CreateDirectory(stagingDir);
            using var archive = ZipFile.OpenRead(zipPath);

            int entryCount = 0;
            long totalExtracted = 0;
            byte[] buffer = new byte[64 * 1024];
            string? foundExe = null;

            foreach (var entry in archive.Entries)
            {
                if (++entryCount > MaxEntries)
                    throw new InvalidOperationException("Portable archive exceeds maximum entry limit.");

                if (string.IsNullOrEmpty(entry.Name))
                    continue; // Directory entry

                string fullDest = Path.GetFullPath(Path.Combine(stagingDir, entry.FullName));
                if (!fullDest.StartsWith(stagingPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Blocked Zip Slip entry: {entry.FullName}");

                string? entryDir = Path.GetDirectoryName(fullDest);
                if (!string.IsNullOrEmpty(entryDir))
                    Directory.CreateDirectory(entryDir);

                using var src = entry.Open();
                using var dst = new FileStream(fullDest, FileMode.Create, FileAccess.Write, FileShare.None);
                int read;
                while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    totalExtracted += read;
                    if (totalExtracted > MaxTotalBytes)
                        throw new InvalidOperationException("Portable archive exceeds maximum uncompressed size limit.");
                    dst.Write(buffer, 0, read);
                }

                if (string.Equals(Path.GetFileName(fullDest), "DynamicIsland.exe", StringComparison.OrdinalIgnoreCase))
                {
                    foundExe ??= fullDest;
                }
            }

            if (foundExe == null)
            {
                TryDeleteDirectory(stagingDir);
            }
            return foundExe;
        }
        catch (Exception ex)
        {
            App.Log($"[UpdateManager] Portable archive extraction failed: {ex.Message}");
            TryDeleteDirectory(stagingDir);
            return null;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    /// <summary>
    /// Fetches the recent release history for the in-app Version Control & Rollback manager.
    /// </summary>
    public async Task<IReadOnlyList<ReleaseHistoryItem>> FetchVersionHistoryAsync(CancellationToken ct = default)
    {
        try
        {
            var history = await _client.FetchReleaseHistoryAsync(ct: ct).ConfigureAwait(false);
            ReleaseHistory = history;
            return history;
        }
        catch (Exception ex)
        {
            App.Log($"[UpdateManager] Failed to fetch version history: {ex.Message}");
            return Array.Empty<ReleaseHistoryItem>();
        }
    }

    /// <summary>
    /// Resolves the expected cryptographic SHA-256 for a historical release item.
    /// </summary>
    public Task<string?> FetchSha256ForReleaseAsync(ReleaseHistoryItem item, string targetFileName, CancellationToken ct = default)
    {
        return _client.FetchExpectedSha256Async(item, targetFileName, ct);
    }

    /// <summary>
    /// Skips notifications for the current available version for 7 days (unless it is a critical security update).
    /// </summary>
    public void SkipCurrentVersion()
    {
        if (LatestManifest != null && !string.IsNullOrWhiteSpace(LatestManifest.Version))
        {
            if (LatestManifest.IsCritical)
            {
                // Critical updates cannot be skipped
                return;
            }

            _settingsService.Update(s =>
            {
                s.SkippedVersion = LatestManifest.Version;
                s.SkippedVersionDate = DateTime.UtcNow;
            }, nameof(AppSettings.SkippedVersion));

            SetStatus(UpdateStatus.UpToDate, $"Skipped version {LatestManifest.Version}.");
        }
    }

    private void SetStatus(UpdateStatus status, string message)
    {
        Status = status;
        Dispatch(() => StatusChanged?.Invoke(status, message));
    }

    private void Dispatch(Action action)
    {
        if (_dispatcher != null && !_dispatcher.CheckAccess())
        {
            _dispatcher.InvokeAsync(action);
        }
        else
        {
            action();
        }
    }

    /// <summary>
    /// Resolves the destination directory for update downloads.
    /// Checks available disk space and automatically falls back to secondary fixed drives (e.g. D:, E:)
    /// if the system %TEMP% drive (typically C:) is constrained.
    /// </summary>
    private static string ResolveDownloadDirectory(long estimatedBytes)
    {
        long requiredSpace = (estimatedBytes > 0 ? estimatedBytes : 75L * 1024 * 1024) + (50L * 1024 * 1024);
        string preferredTemp = Path.Combine(Path.GetTempPath(), "DynamicIsland", "Updates");

        try
        {
            string? driveRoot = Path.GetPathRoot(preferredTemp);
            if (!string.IsNullOrEmpty(driveRoot))
            {
                var drive = new DriveInfo(driveRoot);
                if (drive.IsReady && drive.AvailableFreeSpace >= requiredSpace)
                {
                    return preferredTemp;
                }
            }

            // Fallback: search for any fixed drive with plenty of space (e.g. D:, E:)
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.IsReady && d.DriveType == DriveType.Fixed && d.AvailableFreeSpace >= requiredSpace)
                {
                    return Path.Combine(d.RootDirectory.FullName, "DynamicIsland", "Updates");
                }
            }
        }
        catch
        {
            // Fallback to preferred temp on query failure
        }

        return preferredTemp;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _dailyTimer?.Dispose();
        _dailyTimer = null;
        _opLock.Dispose();
        _client.Dispose();
    }
}
