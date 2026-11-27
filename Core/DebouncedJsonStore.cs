using System.IO;
using System.Text.Json;

namespace DynamicIsland.Core;

/// <summary>
/// Thread-safe debounced atomic JSON persistence helper.
/// Coalesces rapid mutations on a background timer and synchronously flushes pending state on Dispose.
/// </summary>
internal sealed class DebouncedJsonStore<T> : IDisposable
{
    private readonly string _filePath;
    private readonly Func<T> _snapshotProvider;
    private readonly JsonSerializerOptions? _options;
    private readonly int _delayMs;
    private readonly object _timerLock = new();
    private readonly object _fileLock = new();
    private readonly object _flushLock = new();
    private readonly Timer _timer;

    private bool _dirty;
    private bool _disposed;

    public DebouncedJsonStore(
        string filePath,
        Func<T> snapshotProvider,
        int delayMs = 300,
        JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _delayMs = Math.Max(25, delayMs);
        _options = options;
        _timer = new Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void ScheduleSave()
    {
        lock (_timerLock)
        {
            if (_disposed) return;
            _dirty = true;
            _timer.Change(_delayMs, Timeout.Infinite);
        }
    }

    public void Flush()
    {
        FlushCore(requireExistingDir: false);
    }

    public void FlushNow()
    {
        lock (_timerLock)
        {
            if (_disposed) return;
            _dirty = true;
        }
        FlushCore(requireExistingDir: false);
    }

    private void OnTimerTick(object? _)
    {
        FlushCore(requireExistingDir: true);
    }

    private void FlushCore(bool requireExistingDir)
    {
        lock (_flushLock)
        {
            lock (_timerLock)
            {
                if (!_dirty) return;
                _dirty = false;
                try { _timer.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
            }

            if (requireExistingDir)
            {
                string? dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    return;
                }
            }

            try
            {
                T snapshot = _snapshotProvider();
                AtomicJsonStore.Save(_filePath, snapshot, _fileLock, _options);
            }
            catch (Exception ex)
            {
                App.Log($"[DebouncedJsonStore] Flush failed for '{_filePath}': {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        bool wasDirty;
        lock (_timerLock)
        {
            if (_disposed) return;
            _disposed = true;
            wasDirty = _dirty;
            _dirty = false;
            try { _timer.Dispose(); } catch { }
        }

        lock (_flushLock)
        {
            bool stillDirty;
            lock (_timerLock)
            {
                stillDirty = wasDirty || _dirty;
                _dirty = false;
            }

            if (stillDirty)
            {
                try
                {
                    T snapshot = _snapshotProvider();
                    AtomicJsonStore.Save(_filePath, snapshot, _fileLock, _options);
                }
                catch (Exception ex)
                {
                    App.Log($"[DebouncedJsonStore] Dispose flush failed for '{_filePath}': {ex.Message}");
                }
            }
        }
    }
}
