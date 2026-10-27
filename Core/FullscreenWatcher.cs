using System.Runtime.InteropServices;
using System.Windows.Threading;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Core;

/// <summary>
/// Detects exclusive DirectX/D3D fullscreen games as well as borderless-fullscreen apps/videos
/// on the island's target monitor to auto-hide the island.
/// </summary>
internal sealed class FullscreenWatcher : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Func<(IntPtr IslandHwnd, IntPtr Monitor)> _getContext;
    private bool _fullscreen;

    public event Action<bool>? Changed;

    public FullscreenWatcher(Func<(IntPtr IslandHwnd, IntPtr Monitor)> getContext)
    {
        _getContext = getContext;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        _timer.Start();
        OnTick(null, EventArgs.Empty);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        bool fullscreen = IsFullscreenActive();
        if (fullscreen == _fullscreen) return;
        _fullscreen = fullscreen;
        App.Log($"[FullscreenWatcher] Fullscreen state changed: {fullscreen}");
        Changed?.Invoke(fullscreen);
    }

    private bool IsFullscreenActive()
    {
        // 1. Check exclusive D3D fullscreen or presentation mode via Shell notification state
        if (SHQueryUserNotificationState(out var state) == 0 &&
            state is QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
                  or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE)
        {
            return true;
        }

        // 2. Check if the foreground window is borderless-fullscreen on our target monitor
        var (islandHwnd, targetMonitor) = _getContext();
        if (targetMonitor == IntPtr.Zero) return false;

        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == islandHwnd || fg == GetDesktopWindow() || fg == GetShellWindow())
            return false;

        // Ignore explorer desktop/worker windows (which have WS_EX_TOOLWINDOW or don't match monitor)
        long exStyle = GetExStyle(fg);
        if ((exStyle & WS_EX_TOOLWINDOW) != 0) return false;

        if (MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST) != targetMonitor)
            return false;

        var monInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(targetMonitor, ref monInfo)) return false;

        if (!GetWindowRect(fg, out var winRect)) return false;

        // A borderless fullscreen window covers the entire physical monitor bounds (rcMonitor),
        // whereas a normal maximized window only covers rcWork (leaving the taskbar visible).
        bool coversMonitor =
            winRect.Left <= monInfo.rcMonitor.Left &&
            winRect.Top <= monInfo.rcMonitor.Top &&
            winRect.Right >= monInfo.rcMonitor.Right &&
            winRect.Bottom >= monInfo.rcMonitor.Bottom;

        // If the taskbar reduces rcWork, a maximized window won't cover rcMonitor;
        // if the taskbar is auto-hidden (rcWork == rcMonitor), check that the window has no caption/thickframe.
        if (!coversMonitor) return false;

        bool workEqualsMonitor =
            monInfo.rcWork.Left == monInfo.rcMonitor.Left &&
            monInfo.rcWork.Top == monInfo.rcMonitor.Top &&
            monInfo.rcWork.Right == monInfo.rcMonitor.Right &&
            monInfo.rcWork.Bottom == monInfo.rcMonitor.Bottom;

        return !workEqualsMonitor;
    }

    public void Dispose() => _timer.Stop();
}
