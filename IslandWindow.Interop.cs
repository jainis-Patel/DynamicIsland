using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

public partial class IslandWindow
{
    /// <summary>Extra hover margin around the pill so the tiny idle bar is easy to hit.</summary>
    private const double HoverSlopDip = 8;

    private const int HotkeyStopwatch = 1;
    private const int HotkeyToast = 2;
    private const int HotkeyQuit = 3;
    private const int HotkeyMedia = 4;
    private const int HotkeyClipboard = 5;
    private const int HotkeyCalc = 6;
    private const int HotkeyAudio = 7;
    private const int HotkeyPower = 8;
    private const int HotkeyRecord = 9;
    private const int HotkeyPeripherals = 10;

    private static readonly (int Id, uint Vk)[] HotKeyBindings =
    {
        (HotkeyStopwatch, 0x41),   // VK_A
        (HotkeyToast, 0x54),       // VK_T
        (HotkeyQuit, 0x51),        // VK_Q
        (HotkeyMedia, 0x20),       // VK_SPACE
        (HotkeyClipboard, 0x56),   // VK_V
        (HotkeyCalc, 0x43),        // VK_C
        (HotkeyAudio, 0x4F),       // VK_O
        (HotkeyPower, 0x50),       // VK_P
        (HotkeyRecord, 0x52),      // VK_R
        (HotkeyPeripherals, 0x44), // VK_D
    };

    private IntPtr _hwnd;
    private IntPtr _cachedTargetMonitor;
    private int _cachedMonitorIndex = -1;
    private double _dpiScale = 1.0;
    private uint _expandMsg;
    private uint _quitMsg;
    private RECT _windowRectPx;

    protected override async void OnSourceInitialized(EventArgs e)
    {
        try
        {
            App.Log("IslandWindow OnSourceInitialized starting...");
            base.OnSourceInitialized(e);

            _hwnd = new WindowInteropHelper(this).Handle;
            App.Log($"HWND acquired: {_hwnd}");
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

            _expandMsg = RegisterWindowMessage("DYNAMIC_ISLAND_EXPAND");
            _quitMsg = RegisterWindowMessage("DYNAMIC_ISLAND_QUIT");

            long ex = GetExStyle(_hwnd);
            ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            ex &= ~WS_EX_APPWINDOW;
            SetExStyle(_hwnd, ex);
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);

            _dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            PositionWindow();

            ChangeWindowMessageFilter(WM_DROPFILES, MSGFLT_ADD);
            ChangeWindowMessageFilter(WM_COPYDATA, MSGFLT_ADD);
            ChangeWindowMessageFilter(WM_COPYGLOBALDATA, MSGFLT_ADD);

            foreach (var (id, vk) in HotKeyBindings)
            {
                TryRegisterHotKey(id, vk);
            }

            App.Log("Starting monitors...");
            _cursor.Start();
            _fullscreen.Start();
            _bluetoothMonitor.Start();
            _privacyService.Start();
            _downloadService.Start();
            _peripheralService.Start();
            _clipboardService.Attach(_hwnd);
            _ = _notificationListener.InitializeAsync();

            _trayIcon = new TrayIconService(
                _hwnd,
                onExpand: () => Dispatcher.InvokeAsync(() => _machine.ExpandNow()),
                onAudioSwitch: () => Dispatcher.InvokeAsync(ToggleAudioView),
                onPower: () => Dispatcher.InvokeAsync(ToggleBatteryView),
                onVoiceMemo: () => Dispatcher.InvokeAsync(ToggleVoiceMemoView),
                onPeripherals: () => Dispatcher.InvokeAsync(TogglePeripheralsView),
                onSettings: () => Dispatcher.InvokeAsync(ToggleSettingsView),
                onTestToast: () => Dispatcher.InvokeAsync(ShowNextDemoToast),
                onToggleStopwatch: () => Dispatcher.InvokeAsync(ToggleStopwatch),
                onExit: () => Dispatcher.InvokeAsync(() => Application.Current.Shutdown()));

            await _mediaService.InitializeAsync();
            UpdateSpectrumActivity();
            App.Log("MediaService initialized.");

            _machine.PushToast(new ToastContent("\uE7E7", "Dynamic Island", "Ready · Hover top-center or click to expand", ThemeBrushes.BlueColor, TimeSpan.FromSeconds(3.5)));
        }
        catch (Exception ex)
        {
            App.Log($"Exception in OnSourceInitialized: {ex}");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_quitMsg != 0 && (uint)msg == _quitMsg)
        {
            handled = true;
            Application.Current.Shutdown();
            return IntPtr.Zero;
        }

        if (_expandMsg != 0 && (uint)msg == _expandMsg)
        {
            _machine.ExpandNow();
            _machine.PushToast(new ToastContent("\uE7E7", "Dynamic Island", "Already running · Here I am!", ThemeBrushes.BlueColor, TimeSpan.FromSeconds(3.5)));
            handled = true;
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case TrayIconService.WM_TRAYICON:
                _trayIcon?.HandleMessage(lParam);
                handled = true;
                break;

            case WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(_currentExpandedView == "calc" && _machine.State == IslandState.Expanded
                    ? MA_ACTIVATE
                    : MA_NOACTIVATE);

            case WM_NCHITTEST:
            {
                short screenX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                short screenY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
                var hot = GetHotZone();
                handled = true;
                if (hot.HasValue && hot.Value.Contains(new POINT { X = screenX, Y = screenY }))
                {
                    return new IntPtr(HTCLIENT);
                }
                return new IntPtr(HTTRANSPARENT);
            }

            case WM_HOTKEY:
                OnHotKey(wParam.ToInt32());
                handled = true;
                break;

            case WM_CLIPBOARDUPDATE:
                _clipboardService.HandleClipboardUpdate();
                handled = true;
                break;

            case WM_DISPLAYCHANGE:
            case WM_SETTINGCHANGE when wParam.ToInt32() == SPI_SETWORKAREA:
                _cachedTargetMonitor = IntPtr.Zero;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(PositionWindow));
                break;
        }
        return IntPtr.Zero;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _dpiScale = newDpi.DpiScaleX;
        _cachedTargetMonitor = IntPtr.Zero;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(PositionWindow));
    }

    internal static List<IntPtr> GetAvailableMonitors()
    {
        var list = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref RECT _, IntPtr _) =>
        {
            list.Add(hMon);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    internal IntPtr GetTargetMonitor()
    {
        int idx = _settingsService?.Current.MonitorIndex ?? 0;
        if (_cachedTargetMonitor != IntPtr.Zero && _cachedMonitorIndex == idx)
        {
            return _cachedTargetMonitor;
        }

        var monitors = GetAvailableMonitors();
        _cachedMonitorIndex = idx;
        _cachedTargetMonitor = (idx >= 0 && idx < monitors.Count)
            ? monitors[idx]
            : MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY);
        return _cachedTargetMonitor;
    }

    private void PositionWindow()
    {
        if (_hwnd == IntPtr.Zero) return;

        var monitor = GetTargetMonitor();
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        double scale = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0
            ? dpiX / 96.0
            : _dpiScale;
        _dpiScale = scale;

        double leftDip;
        string dock = _settingsService?.Current.DockPosition ?? "Center";
        if (dock == "Left")
            leftDip = (info.rcWork.Left + 24 * scale) / scale;
        else if (dock == "Right")
            leftDip = (info.rcWork.Right - Width * scale - 24 * scale) / scale;
        else
            leftDip = (info.rcMonitor.Left + (info.rcMonitor.Width - Width * scale) / 2) / scale;

        double topDip = (info.rcWork.Top / scale) + (_settingsService?.Current.NotchYOffset ?? 0);

        Left = leftDip;
        Top = topDip;

        int widthPx = (int)Math.Round(Width * scale);
        int heightPx = (int)Math.Round(Height * scale);
        int x = (int)Math.Round(leftDip * scale);
        int y = (int)Math.Round(topDip * scale);

        _windowRectPx = new RECT { Left = x, Top = y, Right = x + widthPx, Bottom = y + heightPx };
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, widthPx, heightPx, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private void ReassertTopmost()
    {
        if (_hwnd == IntPtr.Zero || _machine.State == IslandState.Hidden) return;
        if ((GetExStyle(_hwnd) & WS_EX_TOPMOST) == 0)
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
    }

    private void SetKeyboardFocusable(bool focusable)
    {
        Focusable = focusable;
        if (_hwnd == IntPtr.Zero) return;

        long ex = GetExStyle(_hwnd);
        if (focusable)
        {
            ex &= ~WS_EX_NOACTIVATE;
            SetExStyle(_hwnd, ex);
            SetForegroundWindow(_hwnd);
            Activate();
        }
        else
        {
            if ((ex & WS_EX_NOACTIVATE) == 0)
            {
                ex |= WS_EX_NOACTIVATE;
                SetExStyle(_hwnd, ex);
            }
        }
    }

    private RECT? GetHotZone()
    {
        if (_hwnd == IntPtr.Zero || _machine.State == IslandState.Hidden) return null;
        if (_windowRectPx.Width <= 0) return null;

        var shape = Shapes[_machine.State];
        double effectiveW = Math.Max(shape.Width, _width.Value);
        double effectiveH = Math.Max(shape.Height, _height.Value);
        int halfWidth = (int)Math.Round((effectiveW / 2 + HoverSlopDip) * _dpiScale);
        int centerX = _windowRectPx.Left + _windowRectPx.Width / 2;

        return new RECT
        {
            Left = centerX - halfWidth,
            Right = centerX + halfWidth,
            Top = _windowRectPx.Top - 2,
            Bottom = _windowRectPx.Top + (int)Math.Round((effectiveH + HoverSlopDip) * _dpiScale),
        };
    }

    private void TryRegisterHotKey(int id, uint vk)
    {
        if (!RegisterHotKey(_hwnd, id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, vk))
            Debug.WriteLine($"[Island] Hotkey {id} unavailable (Win32 error {Marshal.GetLastWin32Error()}).");
    }

    private void UnregisterAllHotKeys()
    {
        if (_hwnd == IntPtr.Zero) return;
        foreach (var (id, _) in HotKeyBindings)
        {
            UnregisterHotKey(_hwnd, id);
        }
    }

    private void OnHotKey(int id)
    {
        switch (id)
        {
            case HotkeyStopwatch: ToggleStopwatch(); break;
            case HotkeyToast: ShowNextDemoToast(); break;
            case HotkeyMedia: _mediaService.TogglePlayPause(); break;
            case HotkeyClipboard: ToggleClipboardView(); break;
            case HotkeyCalc: ToggleCalcView(); break;
            case HotkeyAudio: ToggleAudioView(); break;
            case HotkeyPower: ToggleBatteryView(); break;
            case HotkeyRecord: ToggleVoiceRecording(); break;
            case HotkeyPeripherals: TogglePeripheralsView(); break;
            case HotkeyQuit: Application.Current.Shutdown(); break;
        }
    }
}
