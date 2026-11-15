using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
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
    private Timer? _dailyTimer;
    private bool _disposed;

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
        _client = client ?? new GitHubReleaseClient();
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

        _dailyTimer = new Timer(async _ =>
        {
            if (_settingsService.Current.AutoCheckUpdates)
            {
                await CheckForUpdatesAsync(silent: true).ConfigureAwait(false);
            }
        }, null, initialDelay, TimeSpan.FromHours(24));
    }

    /// <summary>
    /// Queries GitHub for updates, applies channel filtering (Stable vs Beta), and compares SemVer.
    /// </summary>
    public async Task<UpdateManifest?> CheckForUpdatesAsync(bool silent = false, bool force = false, CancellationToken ct = default)
    {
        SetStatus(UpdateStatus.Checking, "Checking GitHub for updates...");
        LastErrorMessage = null;

        try
        {
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
                // Verify user hasn't chosen to skip this version during silent background checks
                if (silent && string.Equals(_settingsService.Current.SkippedVersion, manifest.Version, StringComparison.OrdinalIgnoreCase))
                {
                    SetStatus(UpdateStatus.UpToDate, $"Skipped version {manifest.Version}.");
                    return null;
                }

                SetStatus(UpdateStatus.UpdateAvailable, $"Update {manifest.Version} is available.");
                Dispatch(() => UpdateAvailable?.Invoke(manifest));
                return manifest;
            }
            else
            {
                SetStatus(UpdateStatus.UpToDate, $"Dynamic Island {CurrentVersionString} is up to date.");
                return null;
            }
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            SetStatus(UpdateStatus.Failed, $"Update check failed: {ex.Message}");
            App.Log($"[UpdateManager] Check failed: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Downloads the update package into %LocalAppData%\Temp\DynamicIsland\Updates
    /// and verifies cryptographic SHA-256 integrity.
    /// </summary>
    public async Task<bool> DownloadUpdateAsync(CancellationToken ct = default)
    {
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

        string tempDir = Path.Combine(Path.GetTempPath(), "DynamicIsland", "Updates");
        Directory.CreateDirectory(tempDir);

        string ext = Path.GetExtension(asset.Url);
        if (string.IsNullOrEmpty(ext)) ext = isInstaller ? ".exe" : ".zip";
        string targetFile = Path.Combine(tempDir, $"DynamicIsland-{LatestManifest.Version}{ext}");

        SetStatus(UpdateStatus.Downloading, "Downloading update package...");

        var progress = new Progress<UpdateProgressInfo>(info =>
        {
            Dispatch(() => DownloadProgressChanged?.Invoke(info));
        });

        try
        {
            await _client.DownloadFileWithProgressAsync(asset.Url, targetFile, progress, ct).ConfigureAwait(false);

            // Step 2: Cryptographic SHA-256 integrity verification
            SetStatus(UpdateStatus.Verifying, "Verifying package integrity (SHA-256)...");

            if (!string.IsNullOrWhiteSpace(asset.Sha256))
            {
                bool matches = await UpdateIntegrityVerifier.VerifyFileAsync(targetFile, asset.Sha256, ct).ConfigureAwait(false);
                if (!matches)
                {
                    UpdateIntegrityVerifier.TryCleanFile(targetFile);
                    LastErrorMessage = "SHA-256 checksum mismatch. Downloaded package was discarded for security.";
                    SetStatus(UpdateStatus.Failed, LastErrorMessage);
                    App.Log($"[UpdateManager] SHA-256 mismatch for {targetFile}");
                    return false;
                }
            }

            DownloadedPackagePath = targetFile;
            IsInstallerPackage = isInstaller;

            SetStatus(UpdateStatus.ReadyToInstall, $"Dynamic Island {LatestManifest.Version} is ready to install.");
            return true;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            UpdateIntegrityVerifier.TryCleanFile(targetFile);
            LastErrorMessage = "Release package has not been published to GitHub yet (404 Not Found).";
            SetStatus(UpdateStatus.Failed, LastErrorMessage);
            App.Log($"[UpdateManager] Download 404: {asset.Url}");
            return false;
        }
        catch (Exception ex)
        {
            UpdateIntegrityVerifier.TryCleanFile(targetFile);
            LastErrorMessage = ex.Message;
            SetStatus(UpdateStatus.Failed, $"Download failed: {ex.Message}");
            App.Log($"[UpdateManager] Download failed: {ex}");
            return false;
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

        SetStatus(UpdateStatus.Installing, "Launching updater and restarting Dynamic Island...");
        return UpdateHandoffEngine.ExecuteHandoff(DownloadedPackagePath, IsInstallerPackage);
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
    /// Skips notifications for the current available version.
    /// </summary>
    public void SkipCurrentVersion()
    {
        if (LatestManifest != null && !string.IsNullOrWhiteSpace(LatestManifest.Version))
        {
            _settingsService.Update(s => s.SkippedVersion = LatestManifest.Version, nameof(AppSettings.SkippedVersion));
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _dailyTimer?.Dispose();
        _dailyTimer = null;
        _client.Dispose();
    }
}
