using System.Runtime.InteropServices;
using System.Windows.Threading;
using DynamicIsland.Interop;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

public enum SnapZone
{
    None,
    LeftHalf,
    RightHalf,
    TopHalf,
    BottomHalf,
    TopLeftQuarter,
    TopRightQuarter,
    BottomLeftQuarter,
    BottomRightQuarter,
    Maximize
}

/// <summary>
/// Window Snap Droplet: Provides macOS/Droppy-style layout snapping when moving windows.
/// Uses WinEvent hooks (EVENT_SYSTEM_MOVESIZESTART / END) to detect window drags with 0% idle CPU.
/// The hook is only registered while the droplet is enabled in DropletRegistry.
/// </summary>
internal sealed class WindowSnapService : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly Func<IntPtr> _islandHwndProvider;
    private readonly Func<bool> _isEnabledProvider;

    private IntPtr _hWinEventHook;
    private WinEventDelegate? _winEventDelegate;
    private IntPtr _activeMovingHwnd;
    private bool _disposed;

    public event Action<IntPtr, SnapZone>? WindowSnapped;

    public WindowSnapService(
        Dispatcher ui,
        Func<IntPtr> islandHwndProvider,
        Func<bool> isEnabledProvider)
    {
        _ui = ui;
        _islandHwndProvider = islandHwndProvider;
        _isEnabledProvider = isEnabledProvider;
    }

    public void Start()
    {
        if (_isEnabledProvider())
        {
            RegisterHook();
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled && _hWinEventHook == IntPtr.Zero)
        {
            RegisterHook();
        }
        else if (!enabled && _hWinEventHook != IntPtr.Zero)
        {
            UnregisterHook();
        }
    }

    private void RegisterHook()
    {
        if (_hWinEventHook != IntPtr.Zero) return;

        try
        {
            _winEventDelegate = OnWinEvent;
            _hWinEventHook = SetWinEventHook(
                EVENT_SYSTEM_MOVESIZESTART,
                EVENT_SYSTEM_MOVESIZEEND,
                IntPtr.Zero,
                _winEventDelegate,
                0, 0,
                WINEVENT_OUTOFCONTEXT);
        }
        catch (Exception ex)
        {
            App.Log($"[WindowSnapService] SetWinEventHook failed: {ex.Message}");
        }
    }

    private void UnregisterHook()
    {
        if (_hWinEventHook != IntPtr.Zero)
        {
            try { UnhookWinEvent(_hWinEventHook); } catch { }
            _hWinEventHook = IntPtr.Zero;
            _winEventDelegate = null;
        }
    }

    private void OnWinEvent(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero || hwnd == _islandHwndProvider()) return;

        if (eventType == EVENT_SYSTEM_MOVESIZESTART)
        {
            _activeMovingHwnd = hwnd;
        }
        else if (eventType == EVENT_SYSTEM_MOVESIZEEND)
        {
            if (_activeMovingHwnd == hwnd)
            {
                _activeMovingHwnd = IntPtr.Zero;
                _ui.InvokeAsync(() => ProcessDrop(hwnd));
            }
        }
    }

    private void ProcessDrop(IntPtr hwnd)
    {
        if (!GetCursorPos(out var pt)) return;

        IntPtr hMon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        if (hMon == IntPtr.Zero) return;

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMon, ref mi)) return;

        var zone = DetermineSnapZone(pt, mi.rcWork);
        if (zone != SnapZone.None)
        {
            SnapWindow(hwnd, zone, mi.rcWork);
            WindowSnapped?.Invoke(hwnd, zone);
        }
    }

    public static SnapZone DetermineSnapZone(POINT cursorPt, RECT workArea)
    {
        int x = cursorPt.X;
        int y = cursorPt.Y;

        // Top edge: Maximize or Top Quarters
        int topThreshold = workArea.Top + 16;
        int bottomThreshold = workArea.Bottom - 16;
        int leftThreshold = workArea.Left + 16;
        int rightThreshold = workArea.Right - 16;

        // Top-left corner
        if (x <= leftThreshold && y <= topThreshold) return SnapZone.TopLeftQuarter;
        // Top-right corner
        if (x >= rightThreshold && y <= topThreshold) return SnapZone.TopRightQuarter;
        // Bottom-left corner
        if (x <= leftThreshold && y >= bottomThreshold) return SnapZone.BottomLeftQuarter;
        // Bottom-right corner
        if (x >= rightThreshold && y >= bottomThreshold) return SnapZone.BottomRightQuarter;

        // Screen Top Edge center -> Maximize
        if (y <= topThreshold) return SnapZone.Maximize;

        // Left Edge -> Left Half
        if (x <= leftThreshold) return SnapZone.LeftHalf;

        // Right Edge -> Right Half
        if (x >= rightThreshold) return SnapZone.RightHalf;

        return SnapZone.None;
    }

    public static RECT CalculateSnappedRect(RECT workArea, SnapZone zone)
    {
        int w = workArea.Width;
        int h = workArea.Height;
        int halfW = w / 2;
        int halfH = h / 2;

        return zone switch
        {
            SnapZone.LeftHalf => new RECT
            {
                Left = workArea.Left,
                Top = workArea.Top,
                Right = workArea.Left + halfW,
                Bottom = workArea.Bottom
            },
            SnapZone.RightHalf => new RECT
            {
                Left = workArea.Left + halfW,
                Top = workArea.Top,
                Right = workArea.Right,
                Bottom = workArea.Bottom
            },
            SnapZone.TopHalf => new RECT
            {
                Left = workArea.Left,
                Top = workArea.Top,
                Right = workArea.Right,
                Bottom = workArea.Top + halfH
            },
            SnapZone.BottomHalf => new RECT
            {
                Left = workArea.Left,
                Top = workArea.Top + halfH,
                Right = workArea.Right,
                Bottom = workArea.Bottom
            },
            SnapZone.TopLeftQuarter => new RECT
            {
                Left = workArea.Left,
                Top = workArea.Top,
                Right = workArea.Left + halfW,
                Bottom = workArea.Top + halfH
            },
            SnapZone.TopRightQuarter => new RECT
            {
                Left = workArea.Left + halfW,
                Top = workArea.Top,
                Right = workArea.Right,
                Bottom = workArea.Top + halfH
            },
            SnapZone.BottomLeftQuarter => new RECT
            {
                Left = workArea.Left,
                Top = workArea.Top + halfH,
                Right = workArea.Left + halfW,
                Bottom = workArea.Bottom
            },
            SnapZone.BottomRightQuarter => new RECT
            {
                Left = workArea.Left + halfW,
                Top = workArea.Top + halfH,
                Right = workArea.Right,
                Bottom = workArea.Bottom
            },
            SnapZone.Maximize => workArea,
            _ => workArea
        };
    }

    public static bool SnapWindow(IntPtr hwnd, SnapZone zone, RECT workArea)
    {
        if (hwnd == IntPtr.Zero || zone == SnapZone.None) return false;

        try
        {
            if (IsZoomed(hwnd) && zone != SnapZone.Maximize)
            {
                ShowWindow(hwnd, SW_RESTORE);
            }

            var target = CalculateSnappedRect(workArea, zone);
            return SetWindowPos(
                hwnd,
                IntPtr.Zero,
                target.Left,
                target.Top,
                target.Width,
                target.Height,
                SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch (Exception ex)
        {
            App.Log($"[WindowSnapService] SnapWindow failed: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterHook();
    }
}
