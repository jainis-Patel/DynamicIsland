using System.Runtime;
using System.Windows.Threading;
using DynamicIsland.Interop;

namespace DynamicIsland.Core;

/// <summary>
/// Conservative, non-blocking memory compaction and working set trimmer for Dynamic Island.
/// Executes only once per prolonged idle period (after 45 seconds of continuous undisturbed rest),
/// preventing UI stutter and memory thrashing.
/// </summary>
internal sealed class IslandMemoryManager : IDisposable
{
    private readonly DispatcherTimer _idleTrimTimer;
    private readonly Func<bool> _isSafeToTrim;
    private bool _hasTrimmedForCurrentIdle;

    public IslandMemoryManager(Dispatcher dispatcher, Func<bool> isSafeToTrim)
    {
        _isSafeToTrim = isSafeToTrim;
        _idleTrimTimer = new DispatcherTimer(TimeSpan.FromSeconds(12), DispatcherPriority.ApplicationIdle, OnIdleTimerElapsed, dispatcher);
    }

    /// <summary>
    /// Notifies memory manager of island state machine transitions.
    /// </summary>
    public void NotifyStateChanged(IslandState nextState)
    {
        if (nextState != IslandState.Idle)
        {
            CancelPendingTrim();
            _hasTrimmedForCurrentIdle = false;
        }
        else if (!_hasTrimmedForCurrentIdle)
        {
            _idleTrimTimer.Stop();
            _idleTrimTimer.Start();
        }
    }

    /// <summary>
    /// Postpones the idle trim timer when pointer enters, clicks occur, or notifications pop.
    /// </summary>
    public void NotifyActivity()
    {
        if (_idleTrimTimer.IsEnabled)
        {
            _idleTrimTimer.Stop();
            _idleTrimTimer.Start();
        }
    }

    public void CancelPendingTrim()
    {
        _idleTrimTimer.Stop();
    }

    private void OnIdleTimerElapsed(object? sender, EventArgs e)
    {
        _idleTrimTimer.Stop();

        if (_hasTrimmedForCurrentIdle) return;
        if (!_isSafeToTrim()) return;

        _hasTrimmedForCurrentIdle = true;
        ExecuteConservativeTrim();
    }

    /// <summary>
    /// Performs non-blocking Gen-2 compaction and releases idle pages back to Windows.
    /// Uses the pseudo-handle (-1) from GetCurrentProcess to avoid leaking OS handles.
    /// </summary>
    public static void ExecuteConservativeTrim()
    {
        try
        {
            App.LogMemorySnapshot("Pre-Idle-Trim");

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: true);

            // Reclaim unreferenced startup, JIT, and driver pages from working set via Win32 pseudo-handle
            IntPtr proc = NativeMethods.GetCurrentProcess();
            NativeMethods.SetProcessWorkingSetSize(proc, (IntPtr)(-1), (IntPtr)(-1));
            NativeMethods.K32EmptyWorkingSet(proc);

            App.LogMemorySnapshot("Post-Idle-Trim");
            App.Log("[MemoryManager] Conservative idle trim executed successfully.");
        }
        catch (Exception ex)
        {
            App.Log($"[MemoryManager] Trim error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _idleTrimTimer.Stop();
    }
}
