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
    private IntPtr _hwnd;
    private uint _expandMsg;
    private uint _quitMsg;
    private void OnGlassManagerFrameReady(object? sender, EventArgs e)
    {
        // P0 perf: Only re-render glass while springs are animating (_rendering).
        // When the pill is at rest the glass bitmap is already up-to-date;
        // dispatching here every capture frame (30-60 fps) was the #1 idle-CPU offender.
        if (!_isClosing && ThemeManager.IsLiquidGlass && _machine.State != IslandState.Hidden && _rendering)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (!_isClosing)
                {
                    UpdatePillLiquidGlass(Pill.ActualWidth > 0 ? Pill.ActualWidth : 148, Pill.ActualHeight > 0 ? Pill.ActualHeight : 30, 15.0f, _dropletOffset?.Value ?? 0);
                }
            }, DispatcherPriority.Background);
        }
    }

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
            EvaluateAutoHideState(immediate: true);

            // Disable any window-level DWM blur: IslandWindow is a 680x580 canvas and DWM blur on _hwnd
            // renders a large 680x580 square behind the morphing capsule.
            DynamicIsland.Interop.WindowBlurHelper.DisableFrostedGlass(_hwnd);

            DynamicIsland.LiquidGlass.GlassManager.Instance.InitializeDefaultBackdrop(_hwnd);
            DynamicIsland.LiquidGlass.GlassManager.Instance.FrameReady += OnGlassManagerFrameReady;

            ChangeWindowMessageFilter(WM_DROPFILES, MSGFLT_ADD);
            ChangeWindowMessageFilter(WM_COPYDATA, MSGFLT_ADD);
            ChangeWindowMessageFilter(WM_COPYGLOBALDATA, MSGFLT_ADD);
            if (_quitMsg != 0) ChangeWindowMessageFilter(_quitMsg, MSGFLT_ADD);
            if (_expandMsg != 0) ChangeWindowMessageFilter(_expandMsg, MSGFLT_ADD);

            RegisterConfiguredHotKeys();

            App.Log("Starting core monitors...");
            _cursor.Start();
            _fullscreen.Start();
            _privacyService.Start();
            _clipboardService.Attach(_hwnd);
            _jiggleBasketService.Start();
            _windowSnapService.Start();
            if (_dropletRegistry.IsEnabled("mech_sounds"))
            {
                _keyboardSoundService.SetEnabled(true);
            }
            UpdateDropletsUI();
            UpdateVolumeKeyHook();

            // Defer non-urgent background pollers until after initial frame render
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (_isClosing) return;
                _bluetoothMonitor.Start();
                _downloadService.Start();
                _peripheralService.Start();
                _networkService.Start();
                _weatherService.Start();
                _calendarService.Start();
                _brightnessService.InitializeAsync();
                _notificationListener.InitializeAsync().FireAndForget("Startup.Notifications");
            }, DispatcherPriority.ApplicationIdle);

            _trayIcon = new TrayIconService(
                _hwnd,
                new TrayIconOptions
                {
                    OnExpand = () => Dispatcher.InvokeAsync(() => _machine.ExpandNow()),
                    OnAudioSwitch = () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.Audio)),
                    OnPower = () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.Battery)),
                    OnVoiceMemo = () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.VoiceMemo)),
                    OnPeripherals = () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.Peripherals)),
                    OnSettings = () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.Settings)),
                    OnTestToast = () => Dispatcher.InvokeAsync(ShowNextDemoToast),
                    OnToggleStopwatch = () => Dispatcher.InvokeAsync(ToggleStopwatch),
                    OnExit = () => Dispatcher.InvokeAsync(() => Application.Current.Shutdown()),
                    OnSystemHud = () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.SystemHud)),
                    OnShelf = () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.Shelf)),
                    OnClipboard = () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.Clipboard)),
                    OnNotes = () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.Notes)),
                    OnCalc = () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.Calc)),
                    OnMirror = () => Dispatcher.InvokeAsync(() => SwitchExpandedView(ExpandedView.Mirror)),
                    IsCaffeineEnabled = () => _isCaffeineActive,
                    SetCaffeineEnabled = enabled => Dispatcher.InvokeAsync(() =>
                    {
                        _isCaffeineActive = enabled;
                        ApplyCaffeineMode(enabled);
                    }),
                    IsDndEnabled = () => _settingsService.Current.DoNotDisturb,
                    SetDndEnabled = enabled => Dispatcher.InvokeAsync(() => SetDoNotDisturb(enabled)),
                    IsReplaceBannersEnabled = () => _settingsService.Current.ReplaceWindowsBanners,
                    SetReplaceBannersEnabled = enabled => Dispatcher.InvokeAsync(() =>
                        _settingsService.Update(s => s.ReplaceWindowsBanners = enabled, nameof(AppSettings.ReplaceWindowsBanners))),
                    IsNetworkSpeedEnabled = () => _settingsService.Current.ShowNetworkSpeedInIdle,
                    SetNetworkSpeedEnabled = enabled => Dispatcher.InvokeAsync(() =>
                        _settingsService.Update(s => s.ShowNetworkSpeedInIdle = enabled, nameof(AppSettings.ShowNetworkSpeedInIdle))),
                    GetThemeMode = () => _settingsService.Current.ThemeMode,
                    SetThemeMode = mode => Dispatcher.InvokeAsync(() =>
                        _settingsService.Update(s => s.ThemeMode = mode, nameof(AppSettings.ThemeMode))),
                    IsEdgeBorderEnabled = () => _settingsService.Current.ShowEdgeBorder,
                    SetEdgeBorderEnabled = enabled => Dispatcher.InvokeAsync(() =>
                        _settingsService.Update(s => s.ShowEdgeBorder = enabled, nameof(AppSettings.ShowEdgeBorder))),
                    GetThemeStyle = () => _settingsService.Current.ThemeStyle,
                    SetThemeStyle = style => Dispatcher.InvokeAsync(() =>
                        _settingsService.Update(s => s.ThemeStyle = style, nameof(AppSettings.ThemeStyle))),
                    GetIslandTheme = () => _settingsService.Current.IslandTheme,
                    SetIslandTheme = theme => Dispatcher.InvokeAsync(() =>
                        _settingsService.Update(s => s.IslandTheme = theme, nameof(AppSettings.IslandTheme)))
                });

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
                                              or ExpandedView.Settings
                                           || _recordingHotkeyAction != null;
                return new IntPtr(isInteractiveInput && _machine.State == IslandState.Expanded
                    ? MA_ACTIVATE
                    : MA_NOACTIVATE);

            case WM_NCHITTEST:
            {
                short screenX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                short screenY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
                handled = true;

                if (_machine.State == IslandState.Hidden || _isSessionLocked || _isTucked || _isAutoHidden)
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
                StopRendering();
                UpdateNatureThemeTimerState();
                _clockTimer.Stop();
                SetTopmostReassertEnabled(false);
                _cursor.SetSuspended(true);
                _fullscreen.SetSuspended(true);
                _networkService.SetSuspended(true);
                _privacyService.SetSuspended(true);
                _peripheralService.SetSuspended(true);
                _choreographer.SetSuspended(true);
                _systemPerfService.SetFullscreenSuspended(true);
                _notificationListener.SetSuspended(true);
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
                SetTopmostReassertEnabled(true);
                _cursor.SetSuspended(false);
                _fullscreen.SetSuspended(false);
                _networkService.SetSuspended(false);
                _privacyService.SetSuspended(false);
                _peripheralService.SetSuspended(false);
                _choreographer.SetSuspended(false);
                _systemPerfService.SetFullscreenSuspended(false);
                _notificationListener.SetSuspended(false);
                UpdateClockAndTimeline();
                UpdateNatureThemeTimerState();
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
