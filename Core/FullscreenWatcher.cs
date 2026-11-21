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
    private bool _suspended;

    public event Action<bool>? Changed;

    public FullscreenWatcher(Func<(IntPtr IslandHwnd, IntPtr Monitor)> getContext)
    {
        _getContext = getContext;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        if (_suspended) return;
        _timer.Start();
        OnTick(null, EventArgs.Empty);
    }

    public void SetSuspended(bool suspended)
    {
        if (_suspended == suspended) return;
        _suspended = suspended;
        if (suspended)
        {
            _timer.Stop();
        }
        else
        {
            _timer.Start();
            OnTick(null, EventArgs.Empty);
        }
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
        var (islandHwnd, targetMonitor) = _getContext();
        if (targetMonitor == IntPtr.Zero) return false;

        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == islandHwnd || fg == GetDesktopWindow() || fg == GetShellWindow())
            return false;

        // Ignore explorer desktop/worker windows (which have WS_EX_TOOLWINDOW or don't match monitor)
        long exStyle = GetExStyle(fg);
        if ((exStyle & WS_EX_TOOLWINDOW) != 0) return false;

        // Foreground window MUST be on the island's target monitor (prevents secondary monitor apps/games from hiding this island)
        if (MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST) != targetMonitor)
            return false;

        var monInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(targetMonitor, ref monInfo)) return false;

        if (!GetWindowRect(fg, out var winRect)) return false;

        long winStyle = GetStyle(fg);

        // 1. Check if the foreground window is borderless-fullscreen on our target monitor
        if (IsWindowBorderlessFullscreen(winRect, monInfo, winStyle))
            return true;

        // 2. Secondary check: Direct3D exclusive fullscreen or presentation mode via Shell notification state.
        // Gated strictly to when the foreground window on OUR monitor covers >= 90% of screen dimensions,
        // preventing background DirectX overlays (e.g. Discord, Game Bar) from falsely suppressing the island.
        if (SHQueryUserNotificationState(out var state) == 0 &&
            state is QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
                  or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE)
        {
            int fgWidth = winRect.Right - winRect.Left;
            int fgHeight = winRect.Bottom - winRect.Top;
            int monWidth = monInfo.rcMonitor.Right - monInfo.rcMonitor.Left;
            int monHeight = monInfo.rcMonitor.Bottom - monInfo.rcMonitor.Top;

            if (fgWidth >= monWidth * 0.9 && fgHeight >= monHeight * 0.9)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a window rectangle and Win32 style represent a borderless-fullscreen window
    /// covering the entire physical monitor. Handles both standard taskbars (where rcWork &lt; rcMonitor)
    /// and auto-hidden taskbars (where rcWork == rcMonitor, verified via absence of WS_CAPTION / WS_THICKFRAME).
    /// </summary>
    internal static bool IsWindowBorderlessFullscreen(RECT winRect, MONITORINFO monInfo, long windowStyle)
    {
        bool coversMonitor =
            winRect.Left <= monInfo.rcMonitor.Left &&
            winRect.Top <= monInfo.rcMonitor.Top &&
            winRect.Right >= monInfo.rcMonitor.Right &&
            winRect.Bottom >= monInfo.rcMonitor.Bottom;

        if (!coversMonitor) return false;

        bool workEqualsMonitor =
            monInfo.rcWork.Left == monInfo.rcMonitor.Left &&
            monInfo.rcWork.Top == monInfo.rcMonitor.Top &&
            monInfo.rcWork.Right == monInfo.rcMonitor.Right &&
            monInfo.rcWork.Bottom == monInfo.rcMonitor.Bottom;

        if (!workEqualsMonitor)
        {
            return true;
        }

        // When the taskbar is auto-hidden (rcWork == rcMonitor), a normal maximized window still carries
        // WS_CAPTION or WS_THICKFRAME, whereas a true borderless-fullscreen game/video strips both.
        return (windowStyle & (WS_CAPTION | WS_THICKFRAME)) == 0;
    }

    public void Dispose() => _timer.Stop();
}
