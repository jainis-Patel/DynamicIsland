using System.IO;
using System.Windows.Threading;

namespace DynamicIsland.Services;

public sealed class DownloadInfo
{
    public string FileName { get; init; } = "";
    public string FullPath { get; init; } = "";
    public long BytesReceived { get; set; }
    public long PreviousBytes { get; set; }
    public DateTime LastPolledAt { get; set; } = DateTime.UtcNow;
    public double SpeedBytesPerSecond { get; set; }
    public string SpeedText { get; set; } = "";
    public double EstimatedPercent { get; set; }
    public DateTime StartedAt { get; } = DateTime.Now;
}

/// <summary>
/// Monitors Windows Downloads folder for active browser downloads (.crdownload, .part, .opdownload)
/// and emits live progress updates and completion events to the island.
/// Polling timer runs strictly on-demand while at least one download is active.
/// </summary>
internal sealed class DownloadTrackerService : IDisposable
{
    private readonly Dispatcher _ui;
    private FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _pollTimer;
    private readonly Dictionary<string, DownloadInfo> _activeDownloads = new(StringComparer.OrdinalIgnoreCase);
    private bool _started;

    public bool HasActiveDownloads
    {
        get { lock (_activeDownloads) return _activeDownloads.Count > 0; }
    }

    public int ActiveCount
    {
        get { lock (_activeDownloads) return _activeDownloads.Count; }
    }

    public string? LatestDownloadName
    {
        get { lock (_activeDownloads) return _activeDownloads.Values.LastOrDefault()?.FileName; }
    }

    public string? LatestDownloadSpeedText
    {
        get { lock (_activeDownloads) return _activeDownloads.Values.LastOrDefault()?.SpeedText; }
    }

    public DownloadInfo? LatestDownload
    {
        get { lock (_activeDownloads) return _activeDownloads.Values.LastOrDefault(); }
    }

    public string? LatestDownloadStatusText
    {
        get
        {
            lock (_activeDownloads)
            {
                var latest = _activeDownloads.Values.LastOrDefault();
                if (latest == null) return null;
                return FormatDownloadStatus(latest.FileName, latest.BytesReceived, latest.SpeedText);
            }
        }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 KB";
        if (bytes < 1024 * 1024)
            return $"{Math.Max(1, bytes / 1024)} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):0.0} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):0.00} GB";
    }

    public static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec <= 0) return "";
        if (bytesPerSec < 1024 * 1024)
            return $"\u2193 {bytesPerSec / 1024.0:0} KB/s";
        return $"\u2193 {bytesPerSec / (1024.0 * 1024.0):0.0} MB/s";
    }

    public static string FormatDownloadStatus(string fileName, long bytesReceived, string? speedText = null)
    {
        string clean = string.IsNullOrWhiteSpace(fileName) ? "Downloading" : fileName.Trim();
        string bytesFormatted = FormatBytes(bytesReceived);
        if (!string.IsNullOrWhiteSpace(speedText))
        {
            return $"{bytesFormatted} \u00B7 {clean} \u00B7 {speedText}";
        }
        if (bytesReceived <= 0) return clean;
        return $"{bytesFormatted} \u00B7 {clean}";
    }

    public static void OpenDownloadsFolder()
    {
        try
        {
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloads))
            {
                System.Diagnostics.Process.Start("explorer.exe", downloads);
            }
        }
        catch { }
    }

    public event Action? ActiveDownloadsChanged;
    public event Action<string, string>? DownloadCompleted;

    public DownloadTrackerService(Dispatcher ui)
    {
        _ui = ui;
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background, _ui)
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _pollTimer.Tick += (_, _) => PollDownloads();

        try
        {
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloads))
            {
                _watcher = new FileSystemWatcher(downloads)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };

                _watcher.Created += OnFileEvent;
                _watcher.Changed += OnFileEvent;
                _watcher.Deleted += OnFileDeleted;
                _watcher.Renamed += OnFileRenamed;
            }
        }
        catch { }
    }

    public void Start()
    {
        _started = true;
        UpdateTimerState();
    }

    public void Stop()
    {
        _started = false;
        _pollTimer.Stop();
    }

    private void UpdateTimerState()
    {
        bool shouldRun = _started && HasActiveDownloads;
        if (shouldRun && !_pollTimer.IsEnabled)
            _pollTimer.Start();
        else if (!shouldRun && _pollTimer.IsEnabled)
            _pollTimer.Stop();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        string ext = Path.GetExtension(e.FullPath);
        if (IsDownloadExt(ext))
        {
            bool added = false;
            lock (_activeDownloads)
            {
                string cleanName = CleanDownloadFileName(e.Name ?? "file");
                if (!_activeDownloads.ContainsKey(e.FullPath))
                {
                    _activeDownloads[e.FullPath] = new DownloadInfo { FileName = cleanName, FullPath = e.FullPath };
                    added = true;
                }
            }

            if (added)
            {
                _ui.InvokeAsync(() =>
                {
                    UpdateTimerState();
                    ActiveDownloadsChanged?.Invoke();
                });
            }
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        bool removed;
        lock (_activeDownloads)
        {
            removed = _activeDownloads.Remove(e.FullPath);
        }

        if (removed)
        {
            _ui.InvokeAsync(() =>
            {
                UpdateTimerState();
                ActiveDownloadsChanged?.Invoke();
            });
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        string oldExt = Path.GetExtension(e.OldFullPath);
        string newExt = Path.GetExtension(e.FullPath);

        if (IsDownloadExt(oldExt) && !IsDownloadExt(newExt))
        {
            lock (_activeDownloads)
            {
                _activeDownloads.Remove(e.OldFullPath);
            }

            _ui.InvokeAsync(() =>
            {
                UpdateTimerState();
                ActiveDownloadsChanged?.Invoke();
                DownloadCompleted?.Invoke(e.Name ?? "Downloaded File", e.FullPath);
            });
        }
    }

    private void PollDownloads()
    {
        List<string>? keysToRemove = null;
        bool bytesChanged = false;
        lock (_activeDownloads)
        {
            foreach (var kvp in _activeDownloads)
            {
                try
                {
                    var file = new FileInfo(kvp.Key);
                    if (!file.Exists)
                    {
                        keysToRemove ??= new List<string>();
                        keysToRemove.Add(kvp.Key);
                        continue;
                    }

                    long len = file.Length;
                    if (len != kvp.Value.BytesReceived)
                    {
                        DateTime now = DateTime.UtcNow;
                        double sec = (now - kvp.Value.LastPolledAt).TotalSeconds;
                        if (sec >= 0.4)
                        {
                            long diff = len - kvp.Value.PreviousBytes;
                            if (diff >= 0)
                            {
                                kvp.Value.SpeedBytesPerSecond = diff / sec;
                                kvp.Value.SpeedText = FormatSpeed(kvp.Value.SpeedBytesPerSecond);
                            }
                            kvp.Value.PreviousBytes = len;
                            kvp.Value.LastPolledAt = now;
                        }

                        kvp.Value.BytesReceived = len;
                        bytesChanged = true;
                    }
                }
                catch
                {
                    // File might be locked by browser
                }
            }

            if (keysToRemove != null)
            {
                foreach (var k in keysToRemove)
                {
                    _activeDownloads.Remove(k);
                }
            }
        }

        if (keysToRemove is { Count: > 0 })
        {
            UpdateTimerState();
            ActiveDownloadsChanged?.Invoke();
        }
        else if (bytesChanged)
        {
            ActiveDownloadsChanged?.Invoke();
        }
    }

    private static bool IsDownloadExt(string ext) =>
        string.Equals(ext, ".crdownload", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ext, ".part", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ext, ".opdownload", StringComparison.OrdinalIgnoreCase);

    private static string CleanDownloadFileName(string name)
    {
        string withoutExt = Path.GetFileNameWithoutExtension(name);
        return string.IsNullOrWhiteSpace(withoutExt) ? name : withoutExt;
    }

    public void Dispose()
    {
        _started = false;
        _pollTimer.Stop();
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
    }
}
