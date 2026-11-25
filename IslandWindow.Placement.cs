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
    private IntPtr _cachedTargetMonitor;
    private int _cachedMonitorIndex = -1;
    private string? _cachedMonitorDeviceName;
    private double _dpiScale = 1.0;
    private RECT _windowRectPx;
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
        double refWidth = _machine.State == IslandState.Expanded
            ? GetTargetShape(IslandState.Expanded).Width
            : 484.0;

        bool isDetached = _isDetachedPiP || (_settingsService?.Current.IsDetachedFloating ?? false);
        RECT targetRect;

        if (isDetached && !double.IsNaN(_floatingCenterX) && !double.IsNaN(_floatingCenterY))
        {
            int widthPx = WindowPlacementHelper.DipToPhysicalPx(CanvasWidthDip, scale);
            int heightPx = WindowPlacementHelper.DipToPhysicalPx(CanvasHeightDip, scale);
            int x = WindowPlacementHelper.DipToPhysicalPx(_floatingCenterX - (CanvasWidthDip / 2.0), scale);
            int y = WindowPlacementHelper.DipToPhysicalPx(_floatingCenterY - (Pill?.Height ?? 30.0) / 2.0, scale);
            targetRect = new RECT { Left = x, Top = y, Right = x + widthPx, Bottom = y + heightPx };
        }
        else
        {
            targetRect = WindowPlacementHelper.ComputeDockedWindowRectPx(
                info.rcMonitor.Left,
                info.rcMonitor.Top,
                info.rcMonitor.Width,
                info.rcWork.Left,
                info.rcWork.Right,
                CanvasWidthDip,
                CanvasHeightDip,
                refWidth,
                scale,
                dock,
                _settingsService?.Current.HardwareNotchMode ?? false,
                _settingsService?.Current.NotchYOffset ?? 0);
        }

        _windowRectPx = targetRect;

        // Always enforce the exact physical canvas dimensions for the target monitor's DPI scale
        // so moving across displays never leaves the window size frozen or clipped.
        SetWindowPos(_hwnd, HWND_TOPMOST, targetRect.Left, targetRect.Top, targetRect.Width, targetRect.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    internal void RepositionIfEdgeDocked()
    {
        bool isDetached = _isDetachedPiP || (_settingsService?.Current.IsDetachedFloating ?? false);
        if (isDetached) return;
        string dock = _settingsService?.Current.DockPosition ?? "Center";
        if (dock == "Left" || dock == "Right")
        {
            PositionWindow();
        }
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
        EvaluateAutoHideState();
    }

    private bool _isAutoHidden;
    private DispatcherTimer? _autoHideDelayTimer;
    internal bool IsAutoHidden => _isAutoHidden;

    internal void EvaluateAutoHideState(bool immediate = false)
    {
        if (_hwnd == IntPtr.Zero || _isClosing || _isTucked) return;

        bool autoHideEnabled = _settingsService?.Current.AutoHideIsland ?? false;
        if (!autoHideEnabled || _isDetachedPiP || _machine.State == IslandState.Hidden)
        {
            _autoHideDelayTimer?.Stop();
            if (_isAutoHidden)
            {
                SetAutoHidden(false, immediate);
            }
            return;
        }

        bool shouldReveal =
            _pointerInside ||
            ShouldHoldExpandedOpen ||
            _machine.State is IslandState.Expanded or IslandState.Toast;

        if (shouldReveal)
        {
            _autoHideDelayTimer?.Stop();
            if (_isAutoHidden)
            {
                SetAutoHidden(false, immediate);
            }
            return;
        }

        if (_isAutoHidden) return;

        if (immediate)
        {
            _autoHideDelayTimer?.Stop();
            SetAutoHidden(true, immediate: true);
            return;
        }

        _autoHideDelayTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        _autoHideDelayTimer.Stop();
        _autoHideDelayTimer.Tick -= OnAutoHideDelayTick;
        _autoHideDelayTimer.Tick += OnAutoHideDelayTick;
        _autoHideDelayTimer.Start();
    }

    private void OnAutoHideDelayTick(object? sender, EventArgs e)
    {
        _autoHideDelayTimer?.Stop();
        if (_isTucked || _isClosing || _isDetachedPiP) return;
        bool autoHideEnabled = _settingsService?.Current.AutoHideIsland ?? false;
        if (!autoHideEnabled) return;

        bool shouldReveal =
            _pointerInside ||
            ShouldHoldExpandedOpen ||
            _machine.State is IslandState.Expanded or IslandState.Toast;

        if (!shouldReveal)
        {
            SetAutoHidden(true, immediate: false);
        }
    }

    private void SetAutoHidden(bool hidden, bool immediate)
    {
        if (_isAutoHidden == hidden) return;
        _isAutoHidden = hidden;

        double targetY = hidden ? -48.0 : 0.0;
        double targetOpacity = hidden ? 0.0 : 1.0;

        if (immediate)
        {
            RootTranslateTransform?.BeginAnimation(TranslateTransform.YProperty, null);
            if (RootTranslateTransform != null) RootTranslateTransform.Y = targetY;
            Root?.BeginAnimation(OpacityProperty, null);
            if (Root != null) Root.Opacity = targetOpacity;
            return;
        }

        var slide = new System.Windows.Media.Animation.DoubleAnimation(
            targetY,
            TimeSpan.FromMilliseconds(hidden ? 210 : 250))
        {
            EasingFunction = SharedPanelBackOut
        };
        RootTranslateTransform?.BeginAnimation(TranslateTransform.YProperty, slide);

        var fade = new System.Windows.Media.Animation.DoubleAnimation(
            targetOpacity,
            TimeSpan.FromMilliseconds(hidden ? 170 : 200));
        Root?.BeginAnimation(OpacityProperty, fade);

        if (!hidden)
        {
            TriggerLiquidImpulse(140, 50, 10);
        }
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

        // 2. Secondary split droplet / high-load capsule (if active)
        if (_dropletOffset.Value > 0.05 && SecondaryDroplet != null && SecondaryDroplet.Visibility == Visibility.Visible)
        {
            double dropW = SecondaryDroplet.Width > 0 ? SecondaryDroplet.Width : 32.0;
            double dropH = SecondaryDroplet.Height > 0 ? SecondaryDroplet.Height : 32.0;
            double dropCenterX = (CanvasWidthDip / 2.0) + (SecondaryDropletTranslate?.X ?? 0.0);
            double dropTop = SecondaryDropletTranslate?.Y ?? 0.0;

            if (IsPointInRoundedRect(relX, relY, dropCenterX - dropW / 2.0, dropTop, dropCenterX + dropW / 2.0, dropTop + dropH, dropH / 2.0))
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

        if (_isAutoHidden)
        {
            int stripTop = _windowRectPx.Top - (int)Math.Round(12 * _dpiScale);
            int stripBottom = _windowRectPx.Top + (int)Math.Round(10 * _dpiScale);
            return new RECT
            {
                Left = centerX - halfWidth,
                Right = centerX + halfWidth,
                Top = stripTop,
                Bottom = stripBottom,
            };
        }

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

}
