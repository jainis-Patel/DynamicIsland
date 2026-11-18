using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
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
    private const int HotkeyNetwork = 11;
    private const int HotkeyNotifications = 12;
    private const int HotkeySettings = 13;
    private const int HotkeyMonitor = 14;
    private const int HotkeySystemHud = 15;
    private const int HotkeyQuickPaste = 16;
    private const int HotkeyCapture = 17;
    private const int HotkeyNotes = 18;
    private const int HotkeyTuck = 19;
    private const int HotkeyDock = 20;
    private const int HotkeyMicMute = 21;
    private const int HotkeyPromptApprove = 22;
    private const int HotkeyPromptReject = 23;
    private const int HotkeyToggleFloat = 24;

    private static readonly (int Id, string KeyName, uint DefaultVk)[] HotKeyBindings =
    {
        (HotkeyStopwatch, "Stopwatch", 0x41),         // VK_A
        (HotkeyToast, "TestToast", 0x54),             // VK_T
        (HotkeyQuit, "Quit", 0x51),                   // VK_Q
        (HotkeyMedia, "MediaPlayPause", 0x20),        // VK_SPACE
        (HotkeyClipboard, "Clipboard", 0x56),         // VK_V
        (HotkeyCalc, "Calculator", 0x43),             // VK_C
        (HotkeyAudio, "AudioDevices", 0x4F),          // VK_O
        (HotkeyPower, "BatteryPower", 0x50),          // VK_P
        (HotkeyRecord, "VoiceMemo", 0x52),            // VK_R
        (HotkeyPeripherals, "Peripherals", 0x44),     // VK_D
        (HotkeyNetwork, "Network", 0x57),             // VK_W
        (HotkeyNotifications, "Notifications", 0x4E), // VK_N
        (HotkeySettings, "Settings", 0x53),           // VK_S
        (HotkeyMonitor, "SwitchMonitor", 0x4D),       // VK_M (Ctrl+Alt+M)
        (HotkeySystemHud, "SystemHud", 0x48),         // VK_H (Ctrl+Alt+H)
        (HotkeyQuickPaste, "QuickPaste", 0x42),       // VK_B (Ctrl+Alt+B)
        (HotkeyCapture, "ElementCapture", 0x58),      // VK_X (Ctrl+Alt+X)
        (HotkeyNotes, "QuickNotes", 0x4A),           // VK_J (Ctrl+Alt+J)
        (HotkeyTuck, "TuckIsland", 0x28),             // VK_DOWN (Ctrl+Alt+Down)
        (HotkeyDock, "CycleDock", 0x5A),              // VK_Z (Ctrl+Alt+Z)
        (HotkeyMicMute, "MicrophoneMute", 0x4B),      // VK_K (Ctrl+Alt+K)
        (HotkeyPromptApprove, "PromptApprove", 0x59), // VK_Y (Ctrl+Alt+Y)
        (HotkeyPromptReject, "PromptReject", 0x55),   // VK_U (Ctrl+Alt+U)
        (HotkeyToggleFloat, "ToggleFloating", 0x46),  // VK_F (Ctrl+Alt+F)
    };

    private readonly Dictionary<string, bool> _hotkeyRegistrationStatus = new(StringComparer.OrdinalIgnoreCase);
    private IntPtr _hwnd;
    private IntPtr _llKeyboardHook;
    private LowLevelKeyboardProc? _llKeyboardProc;
    private IntPtr _cachedTargetMonitor;
    private int _cachedMonitorIndex = -1;
    private string? _cachedMonitorDeviceName;
    private double _dpiScale = 1.0;
    private uint _expandMsg;
    private uint _quitMsg;
    private RECT _windowRectPx;

    public IReadOnlyDictionary<string, bool> HotkeyRegistrationStatus => _hotkeyRegistrationStatus;

    protected override async void OnSourceInitialized(EventArgs e)
    {
        try
        {
            App.Log("IslandWindow OnSourceInitialized starting...");
            base.OnSourceInitialized(e);

            _hwnd = new WindowInteropHelper(this).Handle;
            App.Log($"HWND acquired: {_hwnd}");
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
            RegisterTouchpadGestures(_hwnd);

            _expandMsg = RegisterWindowMessage("DYNAMIC_ISLAND_EXPAND");
            _quitMsg = RegisterWindowMessage("DYNAMIC_ISLAND_QUIT");

            long ex = GetExStyle(_hwnd);
            ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            ex &= ~WS_EX_APPWINDOW;
            SetExStyle(_hwnd, ex);
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            _dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            PositionWindow();

            // Disable any window-level DWM blur: IslandWindow is a 680x580 canvas and DWM blur on _hwnd
            // renders a large 680x580 square behind the morphing capsule.
            DynamicIsland.Interop.WindowBlurHelper.DisableFrostedGlass(_hwnd);

            DynamicIsland.LiquidGlass.GlassManager.Instance.InitializeDefaultBackdrop(_hwnd);
            DynamicIsland.LiquidGlass.GlassManager.Instance.FrameReady += (_, _) =>
            {
                // P0 perf: Only re-render glass while springs are animating (_rendering).
                // When the pill is at rest the glass bitmap is already up-to-date;
                // dispatching here every capture frame (30-60 fps) was the #1 idle-CPU offender.
                if (ThemeManager.IsLiquidGlass && _machine.State != IslandState.Hidden && _rendering)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        UpdatePillLiquidGlass(Pill.ActualWidth > 0 ? Pill.ActualWidth : 148, Pill.ActualHeight > 0 ? Pill.ActualHeight : 30, 15.0f, _dropletOffset?.Value ?? 0);
                    }, DispatcherPriority.Background);
                }
            };

            ChangeWindowMessageFilter(WM_DROPFILES, MSGFLT_ADD);
            ChangeWindowMessageFilter(WM_COPYDATA, MSGFLT_ADD);
            ChangeWindowMessageFilter(WM_COPYGLOBALDATA, MSGFLT_ADD);
            if (_quitMsg != 0) ChangeWindowMessageFilter(_quitMsg, MSGFLT_ADD);
            if (_expandMsg != 0) ChangeWindowMessageFilter(_expandMsg, MSGFLT_ADD);

            RegisterConfiguredHotKeys();

            App.Log("Starting monitors...");
            _cursor.Start();
            _fullscreen.Start();
            _bluetoothMonitor.Start();
            _privacyService.Start();
            _downloadService.Start();
            _peripheralService.Start();
            _networkService.Start();
            _weatherService.Start();
            _calendarService.Start();
            _brightnessService.InitializeAsync();
            _clipboardService.Attach(_hwnd);
            _jiggleBasketService.Start();
            _windowSnapService.Start();
            if (_dropletRegistry.IsEnabled("mech_sounds"))
            {
                _keyboardSoundService.SetEnabled(true);
            }
            UpdateDropletsUI();
            _ = _notificationListener.InitializeAsync();
            UpdateVolumeKeyHook();

            _trayIcon = new TrayIconService(
                _hwnd,
                onExpand: () => Dispatcher.InvokeAsync(() => _machine.ExpandNow()),
                onAudioSwitch: () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.Audio)),
                onPower: () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.Battery)),
                onVoiceMemo: () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.VoiceMemo)),
                onPeripherals: () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.Peripherals)),
                onSettings: () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.Settings)),
                onTestToast: () => Dispatcher.InvokeAsync(ShowNextDemoToast),
                onToggleStopwatch: () => Dispatcher.InvokeAsync(ToggleStopwatch),
                onExit: () => Dispatcher.InvokeAsync(() => Application.Current.Shutdown()),
                onSystemHud: () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.SystemHud)),
                onShelf: () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.Shelf)),
                onClipboard: () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.Clipboard)),
                onNotes: () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.Notes)),
                isCaffeineEnabled: () => _isCaffeineActive,
                setCaffeineEnabled: enabled => Dispatcher.InvokeAsync(() =>
                {
                    _isCaffeineActive = enabled;
                    ApplyCaffeineMode(enabled);
                }),
                isDndEnabled: () => _settingsService.Current.DoNotDisturb,
                setDndEnabled: enabled => Dispatcher.InvokeAsync(() => SetDoNotDisturb(enabled)),
                isReplaceBannersEnabled: () => _settingsService.Current.ReplaceWindowsBanners,
                setReplaceBannersEnabled: enabled => Dispatcher.InvokeAsync(() =>
                    _settingsService.Update(s => s.ReplaceWindowsBanners = enabled, nameof(AppSettings.ReplaceWindowsBanners))),
                isNetworkSpeedEnabled: () => _settingsService.Current.ShowNetworkSpeedInIdle,
                setNetworkSpeedEnabled: enabled => Dispatcher.InvokeAsync(() =>
                    _settingsService.Update(s => s.ShowNetworkSpeedInIdle = enabled, nameof(AppSettings.ShowNetworkSpeedInIdle))),
                getThemeMode: () => _settingsService.Current.ThemeMode,
                setThemeMode: mode => Dispatcher.InvokeAsync(() =>
                    _settingsService.Update(s => s.ThemeMode = mode, nameof(AppSettings.ThemeMode))),
                isEdgeBorderEnabled: () => _settingsService.Current.ShowEdgeBorder,
                setEdgeBorderEnabled: enabled => Dispatcher.InvokeAsync(() =>
                    _settingsService.Update(s => s.ShowEdgeBorder = enabled, nameof(AppSettings.ShowEdgeBorder))),
                getThemeStyle: () => _settingsService.Current.ThemeStyle,
                setThemeStyle: style => Dispatcher.InvokeAsync(() =>
                    _settingsService.Update(s => s.ThemeStyle = style, nameof(AppSettings.ThemeStyle))),
                getIslandTheme: () => _settingsService.Current.IslandTheme,
                setIslandTheme: theme => Dispatcher.InvokeAsync(() =>
                    _settingsService.Update(s => s.IslandTheme = theme, nameof(AppSettings.IslandTheme))));

            await _mediaService.InitializeAsync();
            UpdateSpectrumActivity();
            App.Log("MediaService initialized.");

            _machine.PushToast(new ToastContent(
                "\uE7E7",
                "Dynamic Island",
                "Ready · Hover top-center or click to expand",
                ThemeBrushes.BlueColor,
                TimeSpan.FromSeconds(3.5),
                TargetView: null,
                Priority: ToastPriority.Low));

            // Windows Startup Display-Settle Schedule:
            // External monitors (HDMI/DisplayPort) and DPI handshakes often settle 1-3s after user logon.
            // Re-evaluating monitor bounds and z-order ensures the island aligns to the physical display.
            _ = Task.Delay(1500).ContinueWith(_ => Dispatcher.InvokeAsync(() =>
            {
                _cachedTargetMonitor = IntPtr.Zero;
                PositionWindow();
                ReassertTopmost();
            }));
            _ = Task.Delay(4000).ContinueWith(_ => Dispatcher.InvokeAsync(() =>
            {
                _cachedTargetMonitor = IntPtr.Zero;
                PositionWindow();
                ReassertTopmost();
            }));
            _ = Task.Delay(5500).ContinueWith(_ => Dispatcher.InvokeAsync(() =>
            {
                if (_machine.State == IslandState.Idle && !_pointerInside)
                {
                    IslandMemoryManager.ExecuteConservativeTrim();
                }
            }, DispatcherPriority.ApplicationIdle));

            RegisterSystemEvents();
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
                bool isInteractiveInput = _currentExpandedView is ExpandedView.Calc
                                              or ExpandedView.Clipboard
                                              or ExpandedView.Notes
                                              or ExpandedView.Launcher
                                              or ExpandedView.Termi
                                              or ExpandedView.Converter
                                              or ExpandedView.Calendar
                                              or ExpandedView.QuickReply
                                           || _recordingHotkeyAction != null;
                return new IntPtr(isInteractiveInput && _machine.State == IslandState.Expanded
                    ? MA_ACTIVATE
                    : MA_NOACTIVATE);

            case WM_NCHITTEST:
            {
                short screenX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                short screenY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
                handled = true;

                if (_machine.State == IslandState.Hidden || _isSessionLocked || _isTucked)
                {
                    return new IntPtr(HTTRANSPARENT);
                }

                if (IsGhostModifierDown())
                {
                    NotifyGhostPassthroughHover(true);
                    return new IntPtr(HTTRANSPARENT);
                }
                else if (_isGhostVisualActive)
                {
                    NotifyGhostPassthroughHover(false);
                }

                if (IsPointInIslandGeometry(screenX, screenY))
                {
                    return new IntPtr(HTCLIENT);
                }

                return new IntPtr(HTTRANSPARENT);
            }

            case WM_MOUSEWHEEL:
            {
                short delta = unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF));
                HandleMouseWheelDelta(delta);
                handled = true;
                break;
            }

            case WM_MOUSEHWHEEL:
            {
                short tilt = unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF));
                bool isMediaActive = _mediaService.Current != null && !string.IsNullOrWhiteSpace(_mediaService.Current.Title);
                bool canSeek = _mediaService.Current?.CanSeek ?? false;
                var res = _touchpadProcessor.ProcessHorizontalScroll(tilt, Environment.TickCount64, isMediaActive, canSeek, _machine.State == IslandState.Expanded);
                if (res.Kind != GestureActionKind.None)
                {
                    ExecuteGestureResult(res);
                }
                handled = true;
                break;
            }

            case WM_GESTURE:
            {
                var gi = new GESTUREINFO { cbSize = Marshal.SizeOf<GESTUREINFO>() };
                if (GetGestureInfo(lParam, ref gi))
                {
                    try
                    {
                        bool isMediaActive = _mediaService.Current != null && !string.IsNullOrWhiteSpace(_mediaService.Current.Title);
                        bool canSeek = _mediaService.Current?.CanSeek ?? false;
                        var res = _touchpadProcessor.ProcessWin32Gesture(ref gi, _machine.State, isMediaActive, canSeek);
                        if (res.Kind != GestureActionKind.None)
                        {
                            ExecuteGestureResult(res);
                            handled = true;
                        }
                    }
                    finally
                    {
                        CloseGestureInfoHandle(lParam);
                    }
                }
                break;
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
                _cachedTargetMonitor = IntPtr.Zero;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(PositionWindow));
                break;

            case WM_DPICHANGED:
            {
                short newDpiX = unchecked((short)(wParam.ToInt64() & 0xFFFF));
                if (newDpiX > 0)
                {
                    _dpiScale = newDpiX / 96.0;
                }
                _cachedTargetMonitor = IntPtr.Zero;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(PositionWindow));
                handled = true;
                break;
            }

            case WM_SETTINGCHANGE:
                if (wParam.ToInt32() == SPI_SETWORKAREA)
                {
                    _cachedTargetMonitor = IntPtr.Zero;
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(PositionWindow));
                }
                if (string.Equals(_settingsService?.Current.ThemeMode, "System", StringComparison.OrdinalIgnoreCase))
                {
                    if (ThemeManager.CheckSystemThemeChanged())
                    {
                        ApplyTheme(_settingsService!.Current.ThemeName);
                    }
                }
                break;

            case WM_THEMECHANGED:
                if (string.Equals(_settingsService?.Current.ThemeMode, "System", StringComparison.OrdinalIgnoreCase))
                {
                    if (ThemeManager.CheckSystemThemeChanged())
                    {
                        ApplyTheme(_settingsService!.Current.ThemeName);
                    }
                }
                break;

            case WM_INPUTLANGCHANGE:
                if (_settingsService?.Current.EnableKeyboardLayoutHud ?? true)
                {
                    OnInputLanguageChanged(lParam);
                }
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }

    // ── Windows Multi-Touch & Precision Touchpad Gesture Integration ──
    private void RegisterTouchpadGestures(IntPtr hWnd)
    {
        try
        {
            var configs = new[]
            {
                new GESTURECONFIG
                {
                    dwID = 0, // All gestures (Zoom, Pan, Rotate, Two-Finger Tap)
                    dwWant = GC_ALLGESTURES,
                    dwBlock = 0
                }
            };
            SetGestureConfig(hWnd, 0, (uint)configs.Length, configs, (uint)Marshal.SizeOf<GESTURECONFIG>());
            App.Log("[Gesture] Windows Multi-Touch & Precision Touchpad gestures registered successfully.");
        }
        catch (Exception ex)
        {
            App.Log($"[Gesture] SetGestureConfig warning: {ex.Message}");
        }
    }

    private void ExecuteGestureResult(GestureResult res)
    {
        switch (res.Kind)
        {
            case GestureActionKind.NextTrack:
                _mediaService.Next();
                TriggerLiquidImpulse(120, -20, 10);
                TactileFeedback.PlaySnap();
                break;

            case GestureActionKind.PreviousTrack:
                _mediaService.Previous();
                TriggerLiquidImpulse(-120, -20, 10);
                TactileFeedback.PlaySnap();
                break;

            case GestureActionKind.Scrub:
                ScrubActiveMedia(res.ScrubOffset);
                TactileFeedback.PlayTick();
                break;

            case GestureActionKind.ExpandPull:
                if (_machine.State != IslandState.Expanded)
                {
                    _machine.ExpandNow();
                    TriggerLiquidImpulse(20, Math.Min(res.Velocity, 320), 10);
                    TactileFeedback.PlaySnap();
                }
                break;

            case GestureActionKind.CollapsePinch:
                if (_machine.State == IslandState.Expanded)
                {
                    _machine.CollapseNow();
                    TriggerLiquidImpulse(-20, -Math.Min(res.Velocity, 320), -10);
                    TactileFeedback.PlaySnap();
                }
                break;

            case GestureActionKind.ScrollContent:
                HandleMouseWheelDelta((int)-res.Delta);
                break;
        }
    }

    private void ScrubActiveMedia(TimeSpan offset)
    {
        var media = _mediaService.Current;
        if (media == null || !media.CanSeek || media.Duration <= TimeSpan.Zero) return;

        var currentPos = media.GetPosition(DateTimeOffset.Now);
        var newPos = currentPos + offset;
        if (newPos < TimeSpan.Zero) newPos = TimeSpan.Zero;
        if (newPos > media.Duration) newPos = media.Duration;

        _mediaService.Seek(newPos);

        string sign = offset >= TimeSpan.Zero ? "+" : "-";
        string deltaStr = $"{sign}{Math.Abs(offset.TotalSeconds):F0}s";
        string posStr = $"{(int)newPos.TotalMinutes}:{newPos.Seconds:D2} / {(int)media.Duration.TotalMinutes}:{media.Duration.Seconds:D2}";

        _machine.PushToast(new ToastContent(
            "\uE768",
            $"Seek {deltaStr}",
            posStr,
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(1.5),
            Priority: ToastPriority.Feedback,
            Category: "media_scrub"));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _dpiScale = newDpi.DpiScaleX;
        Width = CanvasWidthDip;
        Height = CanvasHeightDip;
        _cachedTargetMonitor = IntPtr.Zero;
        PositionWindow();
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
        if (_settingsService?.Current.FollowActiveMonitor == true)
        {
            if (GetCursorPos(out var pt))
            {
                var curMon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
                if (curMon != IntPtr.Zero)
                {
                    _cachedTargetMonitor = curMon;
                    return curMon;
                }
            }
        }

        int idx = _settingsService?.Current.MonitorIndex ?? 0;
        string? devName = _settingsService?.Current.MonitorDeviceName;
        if (_cachedTargetMonitor != IntPtr.Zero &&
            _cachedMonitorIndex == idx &&
            string.Equals(_cachedMonitorDeviceName, devName, StringComparison.OrdinalIgnoreCase))
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(_cachedTargetMonitor, ref mi))
            {
                // If a specific device name is required, ensure the cached monitor actually matches it
                // (prevents latching onto a fallback monitor when external displays connect late after boot)
                if (string.IsNullOrWhiteSpace(devName) ||
                    string.Equals(GetMonitorDeviceName(_cachedTargetMonitor), devName, StringComparison.OrdinalIgnoreCase))
                {
                    return _cachedTargetMonitor;
                }
            }
            _cachedTargetMonitor = IntPtr.Zero;
        }

        var monitors = GetAvailableMonitors();
        _cachedMonitorIndex = idx;
        _cachedMonitorDeviceName = devName;

        // 1. Prefer matching the stable Win32 monitor device name (e.g. "\\.\DISPLAY2") across dock/undock events
        if (!string.IsNullOrWhiteSpace(devName))
        {
            foreach (var hMon in monitors)
            {
                if (string.Equals(GetMonitorDeviceName(hMon), devName, StringComparison.OrdinalIgnoreCase))
                {
                    _cachedTargetMonitor = hMon;
                    return _cachedTargetMonitor;
                }
            }
        }

        // 2. Fallback to MonitorIndex or primary monitor
        _cachedTargetMonitor = (idx >= 0 && idx < monitors.Count)
            ? monitors[idx]
            : MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY);
        return _cachedTargetMonitor;
    }

    private void PositionWindow()
    {
        if (_hwnd == IntPtr.Zero) return;

        Width = CanvasWidthDip;
        Height = CanvasHeightDip;

        var monitor = GetTargetMonitor();
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        double scale = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0
            ? dpiX / 96.0
            : _dpiScale;
        _dpiScale = scale;

        string dock = _settingsService?.Current.DockPosition ?? "Center";
        double pillMarginInsideWindow = Math.Max(0, (CanvasWidthDip - 484) / 2.0);

        int widthPx = (int)Math.Round(CanvasWidthDip * scale);
        int heightPx = (int)Math.Round(CanvasHeightDip * scale);
        int monLeft = info.rcMonitor.Left;
        int monTop = info.rcMonitor.Top;
        int monWidth = info.rcMonitor.Width;

        int x;
        int y;
        bool isDetached = _isDetachedPiP || (_settingsService?.Current.IsDetachedFloating ?? false);

        if (isDetached && _floatingCenterX > 0 && _floatingCenterY > 0)
        {
            x = (int)Math.Round((_floatingCenterX - (CanvasWidthDip / 2.0)) * scale);
            y = (int)Math.Round((_floatingCenterY - (Pill?.Height ?? 30.0) / 2.0) * scale);
        }
        else
        {
            if (dock == "Left")
                x = info.rcWork.Left + (int)Math.Round((24 - pillMarginInsideWindow) * scale);
            else if (dock == "Right")
                x = info.rcWork.Right - widthPx - (int)Math.Round((24 - pillMarginInsideWindow) * scale);
            else
                x = monLeft + (monWidth - widthPx) / 2;

            bool hardwareNotch = _settingsService?.Current.HardwareNotchMode ?? false;
            int notchOffsetPx = hardwareNotch ? 0 : (int)Math.Round((_settingsService?.Current.NotchYOffset ?? 0) * scale);
            y = monTop + notchOffsetPx;
        }

        _windowRectPx = new RECT { Left = x, Top = y, Right = x + widthPx, Bottom = y + heightPx };

        // Always enforce the exact physical canvas dimensions for the target monitor's DPI scale
        // so moving across displays never leaves the window size frozen or clipped.
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, widthPx, heightPx, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private IntPtr _lastForegroundHwnd;
    private bool _isSessionLocked;

    private void ReassertTopmost()
    {
        if (_hwnd == IntPtr.Zero || _machine.State == IslandState.Hidden || _isSessionLocked) return;
        IntPtr fg = GetForegroundWindow();
        if (fg != IntPtr.Zero && fg == _lastForegroundHwnd && _machine.State == IslandState.Idle)
            return;
        _lastForegroundHwnd = fg;
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
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

    private bool _isGhostVisualActive;
    private bool _isTucked;
    private DispatcherTimer? _tuckRestoreTimer;

    internal bool IsTucked => _isTucked;

    internal bool IsGhostModifierDown()
    {
        string? mod = _settingsService?.Current.GhostModifierKey;
        if (string.Equals(mod, "Ctrl", StringComparison.OrdinalIgnoreCase))
            return (GetKeyState(VK_CONTROL) & 0x8000) != 0;
        if (string.Equals(mod, "Alt", StringComparison.OrdinalIgnoreCase))
            return (GetKeyState(VK_MENU) & 0x8000) != 0;
        if (string.Equals(mod, "Shift", StringComparison.OrdinalIgnoreCase))
            return (GetKeyState(VK_SHIFT) & 0x8000) != 0;
        return false;
    }

    internal void NotifyGhostPassthroughHover(bool ghost)
    {
        if (ghost == _isGhostVisualActive) return;
        _isGhostVisualActive = ghost;
        Dispatcher.InvokeAsync(() =>
        {
            if (Root != null)
            {
                var anim = new System.Windows.Media.Animation.DoubleAnimation(
                    ghost ? 0.18 : 1.0,
                    TimeSpan.FromMilliseconds(ghost ? 80 : 180))
                {
                    EasingFunction = ghost ? SharedQuarticOut : SharedCubicOut
                };
                Root.BeginAnimation(OpacityProperty, anim);
            }
        });
    }

    internal void TuckIsland(TimeSpan? duration = null)
    {
        if (_isTucked || _hwnd == IntPtr.Zero) return;
        _isTucked = true;

        if (_machine.State == IslandState.Expanded)
        {
            _machine.CollapseNow();
        }

        var slideUp = new System.Windows.Media.Animation.DoubleAnimation(-36.0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = SharedPanelBackOut
        };
        RootTranslateTransform?.BeginAnimation(TranslateTransform.YProperty, slideUp);

        var fade = new System.Windows.Media.Animation.DoubleAnimation(0.2, TimeSpan.FromMilliseconds(180));
        Root?.BeginAnimation(OpacityProperty, fade);

        _tuckRestoreTimer?.Stop();
        _tuckRestoreTimer = new DispatcherTimer { Interval = duration ?? TimeSpan.FromSeconds(5) };
        _tuckRestoreTimer.Tick += (_, _) =>
        {
            _tuckRestoreTimer.Stop();
            UntuckIsland();
        };
        _tuckRestoreTimer.Start();
    }

    internal void UntuckIsland()
    {
        if (!_isTucked) return;
        _isTucked = false;
        _tuckRestoreTimer?.Stop();

        var slideDown = new System.Windows.Media.Animation.DoubleAnimation(0.0, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = SharedPanelBackOut
        };
        RootTranslateTransform?.BeginAnimation(TranslateTransform.YProperty, slideDown);

        var fade = new System.Windows.Media.Animation.DoubleAnimation(1.0, TimeSpan.FromMilliseconds(240));
        Root?.BeginAnimation(OpacityProperty, fade);

        TriggerLiquidImpulse(180, 70, 15);
    }

    internal void ToggleTuck()
    {
        if (_isTucked) UntuckIsland();
        else TuckIsland();
    }

    internal void CycleDockPosition()
    {
        string current = _settingsService?.Current.DockPosition ?? "Center";
        string next = current switch
        {
            "Center" => "Right",
            "Right" => "Left",
            _ => "Center"
        };
        _settingsService?.Update(s => s.DockPosition = next, nameof(AppSettings.DockPosition));
        PositionWindow();
        TriggerLiquidImpulse(240, 120, 20);

        _machine.PushToast(new ToastContent(
            "\uE7F4",
            "Dock Position",
            $"Aligned to {next}",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(1.8),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "dock_position"));
    }

    internal bool IsPointInIslandGeometry(int screenX, int screenY)
    {
        if (_hwnd == IntPtr.Zero || _machine.State == IslandState.Hidden || _isSessionLocked || _isTucked)
            return false;

        if (_windowRectPx.Width <= 0 && GetWindowRect(_hwnd, out var currentRect) && currentRect.Width > 0)
        {
            _windowRectPx = currentRect;
        }

        if (_windowRectPx.Width <= 0) return false;

        double relX = (screenX - _windowRectPx.Left) / _dpiScale;
        double relY = (screenY - _windowRectPx.Top) / _dpiScale;

        double scaleFactor = _settingsService?.Current.IslandScale ?? 1.0;
        if (Math.Abs(scaleFactor - 1.0) > 0.001 && scaleFactor > 0.1)
        {
            relX = (CanvasWidthDip * 0.5) + (relX - (CanvasWidthDip * 0.5)) / scaleFactor;
            relY = relY / scaleFactor;
        }

        var shape = GetTargetShape(_machine.State);
        double w = _machine.State == IslandState.Expanded
            ? Math.Max(shape.Width, 484.0)
            : (Pill != null && Pill.ActualWidth > 0 ? Pill.ActualWidth : Math.Max(shape.Width, _width.Value));
        double h = Pill != null && Pill.ActualHeight > 0 ? Pill.ActualHeight : Math.Max(shape.Height, _height.Value);

        double radius = _appliedRadius > 0 ? _appliedRadius : Math.Clamp(_radius.Value, 0, Math.Min(w, h) / 2.0);
        if (radius <= 0) radius = Math.Min(w, h) / 2.0;

        double pillLeft = (CanvasWidthDip - w) / 2.0;
        double pillRight = pillLeft + w;
        double pillTop = 0.0;
        double pillBottom = h;

        // 1. Primary pill capsule
        if (IsPointInRoundedRect(relX, relY, pillLeft, pillTop, pillRight, pillBottom, radius))
        {
            return true;
        }

        // 2. Secondary split droplet (if active)
        if (_dropletOffset.Value > 0.05 && SecondaryDroplet != null && SecondaryDroplet.Visibility == Visibility.Visible)
        {
            double dropletDiameter = SecondaryDroplet.Width > 0 ? SecondaryDroplet.Width : 30.0;
            double dropR = dropletDiameter / 2.0;
            double dropCenterX = (CanvasWidthDip / 2.0) + (SecondaryDropletTranslate?.X ?? 0.0);
            double dropCenterY = (SecondaryDropletTranslate?.Y ?? 0.0) + dropR;

            double ddx = relX - dropCenterX;
            double ddy = relY - dropCenterY;
            if ((ddx * ddx + ddy * ddy) <= (dropR * dropR))
            {
                return true;
            }
        }

        // 3. Floating dock host in Expanded state
        if (_machine.State == IslandState.Expanded && FloatingDockHost != null && FloatingDockHost.Visibility == Visibility.Visible)
        {
            double dockW = FloatingDockHost.ActualWidth > 0 ? FloatingDockHost.ActualWidth : 320.0;
            double dockH = FloatingDockHost.ActualHeight > 0 ? FloatingDockHost.ActualHeight : 40.0;
            double dockLeft = (CanvasWidthDip - dockW) / 2.0;
            double dockRight = dockLeft + dockW;
            double dockTop = FloatingDockTranslate?.Y ?? (h + 4.0);
            double dockBottom = dockTop + dockH;
            double dockRadius = FloatingDockHost.CornerRadius.TopLeft > 0 ? FloatingDockHost.CornerRadius.TopLeft : 20.0;

            if (IsPointInRoundedRect(relX, relY, dockLeft, dockTop, dockRight, dockBottom, dockRadius))
            {
                return true;
            }
        }

        // 4. Scampering pet
        if (_isPetScampering)
        {
            var pet = _machine.State == IslandState.Idle ? IdlePetCompanion : CompactPetCompanion;
            if (pet != null && pet.Visibility == Visibility.Visible)
            {
                double petW = pet.ActualWidth > 0 ? pet.ActualWidth : 28.0;
                double petH = pet.ActualHeight > 0 ? pet.ActualHeight : 24.0;
                double petLeft = pillLeft - petW - 2.0;
                double petTop = (h - petH) / 2.0;
                if (relX >= petLeft && relX <= pillLeft && relY >= petTop && relY <= petTop + petH)
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static bool IsPointInRoundedRect(double x, double y, double left, double top, double right, double bottom, double radius)
    {
        if (x < left || x > right || y < top || y > bottom)
            return false;

        double maxR = Math.Min((right - left) / 2.0, (bottom - top) / 2.0);
        radius = Math.Clamp(radius, 0, maxR);
        if (radius <= 0.5) return true;

        if (x < left + radius && y < top + radius)
        {
            double dx = x - (left + radius);
            double dy = y - (top + radius);
            return (dx * dx + dy * dy) <= (radius * radius);
        }
        if (x > right - radius && y < top + radius)
        {
            double dx = x - (right - radius);
            double dy = y - (top + radius);
            return (dx * dx + dy * dy) <= (radius * radius);
        }
        if (x < left + radius && y > bottom - radius)
        {
            double dx = x - (left + radius);
            double dy = y - (bottom - radius);
            return (dx * dx + dy * dy) <= (radius * radius);
        }
        if (x > right - radius && y > bottom - radius)
        {
            double dx = x - (right - radius);
            double dy = y - (bottom - radius);
            return (dx * dx + dy * dy) <= (radius * radius);
        }

        return true;
    }

    private RECT? GetHotZone()
    {
        if (_hwnd == IntPtr.Zero || _machine.State == IslandState.Hidden || _isSessionLocked || _isTucked) return null;

        if (_windowRectPx.Width <= 0 && GetWindowRect(_hwnd, out var currentRect) && currentRect.Width > 0)
        {
            _windowRectPx = currentRect;
        }

        if (_windowRectPx.Width <= 0) return null;

        var shape = GetTargetShape(_machine.State);
        double scaleFactor = _settingsService?.Current.IslandScale ?? 1.0;
        double effectiveW = Math.Max(shape.Width, _width.Value) * scaleFactor;
        double effectiveH = Math.Max(shape.Height, _height.Value) * scaleFactor;

        if (_machine.State == IslandState.Expanded)
        {
            effectiveW = Math.Max(effectiveW, 484.0 * scaleFactor);
        }

        int halfWidth = (int)Math.Round((effectiveW / 2 + HoverSlopDip) * _dpiScale);
        int centerX = _windowRectPx.Left + _windowRectPx.Width / 2;
        int dropletExtraRight = _dropletOffset.Value > 0.05
            ? (int)Math.Round(48 * scaleFactor * _dpiScale)
            : 0;

        int scamperExtraLeft = _isPetScampering ? (int)Math.Round(28 * scaleFactor * _dpiScale) : 0;
        int scamperExtraBottom = _isPetScampering ? (int)Math.Round(12 * scaleFactor * _dpiScale) : 0;

        int dockExtraPx = (_machine.State == IslandState.Expanded)
            ? (int)Math.Round((4 + 40 + 26) * scaleFactor * _dpiScale)
            : 0;

        return new RECT
        {
            Left = centerX - halfWidth - scamperExtraLeft,
            Right = centerX + halfWidth + dropletExtraRight,
            Top = _windowRectPx.Top - 6,
            Bottom = _windowRectPx.Top + (int)Math.Round((effectiveH + HoverSlopDip) * _dpiScale) + scamperExtraBottom + dockExtraPx,
        };
    }

    private void RegisterConfiguredHotKeys()
    {
        if (_hwnd == IntPtr.Zero) return;
        UnregisterAllHotKeys();

        bool allowDevHotkeys = _settingsService?.Current.EnableDeveloperHotkeys ?? false;
#if DEBUG
        allowDevHotkeys = true;
#endif

        var custom = _settingsService?.Current.CustomHotkeys;
        foreach (var (id, keyName, defaultVk) in HotKeyBindings)
        {
            if (id == HotkeyToast && !allowDevHotkeys)
            {
                continue;
            }

            uint vk = defaultVk;
            if (custom != null && custom.TryGetValue(keyName, out uint customVk))
            {
                vk = customVk;
            }

            // Setting a custom VK of 0 disables that hotkey
            if (vk == 0)
            {
                _hotkeyRegistrationStatus[keyName] = false;
                continue;
            }

            bool ok = RegisterHotKey(_hwnd, id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, vk);
            _hotkeyRegistrationStatus[keyName] = ok;
            if (!ok)
            {
                App.Log($"[HotKey] '{keyName}' (VK=0x{vk:X2}) unavailable (Win32 error {Marshal.GetLastWin32Error()}).");
            }
        }
    }

    private void UnregisterAllHotKeys()
    {
        UninstallVolumeKeyHook();
        if (_hwnd == IntPtr.Zero) return;
        foreach (var (id, _, _) in HotKeyBindings)
        {
            UnregisterHotKey(_hwnd, id);
        }
    }

    private void UpdateVolumeKeyHook()
    {
        bool enable = (_settingsService?.Current.EnableVolumeKeyOsd ?? false) ||
                      (_settingsService?.Current.EnableLockKeyHud ?? true);
        if (enable && _llKeyboardHook == IntPtr.Zero)
        {
            try
            {
                _llKeyboardProc = LowLevelKeyboardCallback;
                IntPtr hMod = GetModuleHandle(null);
                _llKeyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _llKeyboardProc, hMod, 0);
            }
            catch (Exception ex)
            {
                App.Log($"[VolumeHook] SetWindowsHookEx failed: {ex.Message}");
            }
        }
        else if (!enable && _llKeyboardHook != IntPtr.Zero)
        {
            UninstallVolumeKeyHook();
        }
    }

    private void UninstallVolumeKeyHook()
    {
        if (_llKeyboardHook != IntPtr.Zero)
        {
            try { UnhookWindowsHookEx(_llKeyboardHook); } catch { }
            _llKeyboardHook = IntPtr.Zero;
        }
    }

    private IntPtr LowLevelKeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (vkCode is VK_VOLUME_UP or VK_VOLUME_DOWN or VK_VOLUME_MUTE)
                {
                    _ = Dispatcher.InvokeAsync(async () =>
                    {
                        await Task.Delay(35);
                        if (_machine.State != IslandState.Expanded && (_settingsService?.Current.EnableVolumeKeyOsd ?? false))
                        {
                            OnVolumeChanged(_volumeService.CurrentVolume, _volumeService.IsMuted);
                        }
                    });
                }
            }
            else if (msg is WM_KEYUP or WM_SYSKEYUP)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (vkCode is VK_CAPITAL or VK_NUMLOCK or VK_SCROLL && (_settingsService?.Current.EnableLockKeyHud ?? true))
                {
                    _ = Dispatcher.InvokeAsync(async () =>
                    {
                        await Task.Delay(15);
                        OnLockKeyToggled(vkCode);
                    });
                }
            }
        }
        return CallNextHookEx(_llKeyboardHook, nCode, wParam, lParam);
    }

    private void OnLockKeyToggled(int vkCode)
    {
        short state = GetKeyState(vkCode);
        bool isOn = (state & 0x0001) != 0;

        string title;
        string subtitle;
        string glyph;
        Color color = isOn ? Color.FromRgb(0x30, 0xD1, 0x58) : Color.FromRgb(0x8E, 0x8E, 0x93);

        switch (vkCode)
        {
            case VK_CAPITAL:
                title = isOn ? "Caps Lock On" : "Caps Lock Off";
                subtitle = isOn ? "UPPERCASE active" : "lowercase active";
                glyph = "\uE8B0";
                break;
            case VK_NUMLOCK:
                title = isOn ? "Num Lock On" : "Num Lock Off";
                subtitle = isOn ? "Keypad numbers enabled" : "Keypad navigation active";
                glyph = "\uE8EF";
                break;
            case VK_SCROLL:
                title = isOn ? "Scroll Lock On" : "Scroll Lock Off";
                subtitle = isOn ? "Scrolling locked" : "Scrolling normal";
                glyph = "\uE74B";
                break;
            default:
                return;
        }

        _machine.PushToast(new ToastContent(
            glyph,
            title,
            subtitle,
            color,
            TimeSpan.FromSeconds(1.8),
            Priority: ToastPriority.Feedback,
            Category: "hud_lock"));
    }

    private void OnInputLanguageChanged(IntPtr hkl)
    {
        try
        {
            ushort langId = unchecked((ushort)(hkl.ToInt64() & 0xFFFF));
            if (langId == 0) return;

            var culture = System.Globalization.CultureInfo.GetCultureInfo(langId);
            string code = culture.TwoLetterISOLanguageName.ToUpperInvariant();
            string name = culture.NativeName;

            _machine.PushToast(new ToastContent(
                "\uE774",
                $"{code} \u00B7 {culture.EnglishName}",
                name,
                Color.FromRgb(0x0A, 0x84, 0xFF),
                TimeSpan.FromSeconds(2.0),
                Priority: ToastPriority.Feedback,
                Category: "hud_keyboard"));
        }
        catch
        {
            // Ignore unrecognized language identifier
        }
    }

    private void OnHotKey(int id)
    {
        switch (id)
        {
            case HotkeyStopwatch: ToggleStopwatch(); break;
            case HotkeyToast: ShowNextDemoToast(); break;
            case HotkeyMedia: _mediaService.TogglePlayPause(); break;
            case HotkeyClipboard: SwitchExpandedView(ExpandedView.Clipboard); break;
            case HotkeyCalc: SwitchExpandedView(ExpandedView.Calc); break;
            case HotkeyAudio: SwitchExpandedView(ExpandedView.Audio); break;
            case HotkeyPower: SwitchExpandedView(ExpandedView.Battery); break;
            case HotkeyRecord: ToggleVoiceRecording(); break;
            case HotkeyPeripherals: SwitchExpandedView(ExpandedView.Peripherals); break;
            case HotkeyNetwork: SwitchExpandedView(ExpandedView.Network); break;
            case HotkeyNotifications: SwitchExpandedView(ExpandedView.Notifications); break;
            case HotkeySettings: SwitchExpandedView(ExpandedView.Settings); break;
            case HotkeySystemHud: SwitchExpandedView(ExpandedView.SystemHud); break;
            case HotkeyQuickPaste:
                if (_dropletRegistry.IsEnabled("clipboard_pro"))
                    SummonClipboardPopupWindow();
                break;
            case HotkeyCapture:
                if (_dropletRegistry.IsEnabled("element_capture"))
                    LaunchElementCaptureOverlay();
                break;
            case HotkeyNotes:
                if (_dropletRegistry.IsEnabled("notes"))
                    SwitchExpandedView(ExpandedView.Notes);
                break;
            case HotkeyTuck: ToggleTuck(); break;
            case HotkeyDock: CycleDockPosition(); break;
            case HotkeyMonitor: SwitchToNextMonitor(); break;
            case HotkeyMicMute: ToggleMicMute(); break;
            case HotkeyPromptApprove: TriggerActivePromptDecision("Approve"); break;
            case HotkeyPromptReject: TriggerActivePromptDecision("Reject"); break;
            case HotkeyToggleFloat: ToggleFloatingMode(); break;
            case HotkeyQuit: Application.Current.Shutdown(); break;
        }
    }

    internal void SwitchToNextMonitor()
    {
        var monitors = GetAvailableMonitors();
        int count = Math.Max(1, monitors.Count);
        int nextIdx = (_settingsService.Current.MonitorIndex + 1) % count;
        string? devName = (nextIdx >= 0 && nextIdx < monitors.Count)
            ? GetMonitorDeviceName(monitors[nextIdx])
            : null;

        _cachedTargetMonitor = IntPtr.Zero;
        _settingsService.Update(s =>
        {
            s.MonitorIndex = nextIdx;
            s.MonitorDeviceName = devName;
        }, nameof(AppSettings.MonitorIndex));

        PositionWindow();
        UpdateMonitorUI();
        TriggerLiquidImpulse(320, 160, 30);

        string label = $"Display {nextIdx + 1} of {count}";
        if (nextIdx >= 0 && nextIdx < monitors.Count)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitors[nextIdx], ref info))
            {
                label = $"{info.rcMonitor.Width}×{info.rcMonitor.Height} (Screen {nextIdx + 1})";
            }
        }

        _machine.PushToast(new ToastContent(
            "\uE7F4",
            "Switched Display",
            label,
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "monitor"));
    }

    private void RegisterSystemEvents()
    {
        try
        {
            SystemEvents.DisplaySettingsChanged += OnSystemDisplaySettingsChanged;
            SystemEvents.SessionSwitch += OnSystemSessionSwitch;
            SystemEvents.PowerModeChanged += OnSystemPowerModeChanged;
        }
        catch (Exception ex)
        {
            App.Log($"[SystemEvents] Hook failed: {ex.Message}");
        }
    }

    internal void UnregisterSystemEvents()
    {
        try
        {
            SystemEvents.DisplaySettingsChanged -= OnSystemDisplaySettingsChanged;
            SystemEvents.SessionSwitch -= OnSystemSessionSwitch;
            SystemEvents.PowerModeChanged -= OnSystemPowerModeChanged;
        }
        catch { }
    }

    private void OnSystemDisplaySettingsChanged(object? sender, EventArgs e)
    {
        App.Log("[SystemEvents] DisplaySettingsChanged triggered.");
        _cachedTargetMonitor = IntPtr.Zero;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            PositionWindow();
            ReassertTopmost();
        }));
    }

    private void OnSystemSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        App.Log($"[SystemEvents] SessionSwitch: {e.Reason}");
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect)
        {
            _isSessionLocked = true;
            Dispatcher.InvokeAsync(() =>
            {
                _clockTimer.Stop();
                _topmostTimer.Stop();
                _cursor.SetSuspended(true);
                _networkService.SetSuspended(true);
                _privacyService.SetSuspended(true);
                _peripheralService.SetSuspended(true);
                _systemPerfService.SetFullscreenSuspended(true);
                IslandMemoryManager.ExecuteConservativeTrim();
            });
            return;
        }

        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.SessionRemoteControl)
        {
            _isSessionLocked = false;
            _cachedTargetMonitor = IntPtr.Zero;
            Dispatcher.InvokeAsync(() =>
            {
                _clockTimer.Start();
                _topmostTimer.Start();
                _cursor.SetSuspended(false);
                _networkService.SetSuspended(false);
                _privacyService.SetSuspended(false);
                _peripheralService.SetSuspended(false);
                _systemPerfService.SetFullscreenSuspended(false);
                UpdateClockAndTimeline();
            });
            _ = Task.Delay(400).ContinueWith(_ => Dispatcher.InvokeAsync(() =>
            {
                _cachedTargetMonitor = IntPtr.Zero;
                PositionWindow();
                ReassertTopmost();
            }));
            _ = Task.Delay(1500).ContinueWith(_ => Dispatcher.InvokeAsync(() =>
            {
                _cachedTargetMonitor = IntPtr.Zero;
                PositionWindow();
                ReassertTopmost();
            }));
        }
    }

    private void OnSystemPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        App.Log($"[SystemEvents] PowerModeChanged: {e.Mode}");
        if (e.Mode == PowerModes.Resume)
        {
            _cachedTargetMonitor = IntPtr.Zero;
            _ = Task.Delay(1000).ContinueWith(_ => Dispatcher.InvokeAsync(() =>
            {
                _cachedTargetMonitor = IntPtr.Zero;
                PositionWindow();
                ReassertTopmost();
            }));
            _ = Task.Delay(3000).ContinueWith(_ => Dispatcher.InvokeAsync(() =>
            {
                _cachedTargetMonitor = IntPtr.Zero;
                PositionWindow();
                ReassertTopmost();
            }));
        }
    }
}
