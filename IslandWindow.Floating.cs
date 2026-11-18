using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DynamicIsland.Core;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

public partial class IslandWindow
{
    // =================================================================
    //  Pillar 3: Detachable Floating Island (PiP) & Magnetic Docking
    // =================================================================

    private bool _isDetachedPiP;
    private bool _isPillDragging;
    private bool _isTearingOff;
    private Point _dragStartMouseScreen;
    private double _dragStartPillCenterX;
    private double _dragStartPillCenterY;
    private double _floatingCenterX = -1;
    private double _floatingCenterY = -1;
    private MagneticDockTarget _activeMagneticDock = MagneticDockTarget.None;

    public bool IsDetachedPiP => _isDetachedPiP;
    public MagneticDockTarget ActiveMagneticDock => _activeMagneticDock;

    internal void InitializeFloatingState()
    {
        _isDetachedPiP = _settingsService?.Current.IsDetachedFloating ?? false;
        _floatingCenterX = _settingsService?.Current.FloatingX ?? -1;
        _floatingCenterY = _settingsService?.Current.FloatingY ?? -1;
    }

    internal void StartPillDrag(Point mouseScreenPoint)
    {
        _dragStartMouseScreen = mouseScreenPoint;
        _dragStartPillCenterX = IslandCenterDipX;
        _dragStartPillCenterY = IslandTopDipY + (Pill?.Height ?? 30.0) / 2.0;
        _isPillDragging = false;
        _isTearingOff = false;
        _activeMagneticDock = MagneticDockTarget.None;
    }

    internal void ProcessPillDrag(Point currentMouseScreenPoint)
    {
        double scale = DpiScaleValue;
        double deltaX = (currentMouseScreenPoint.X - _dragStartMouseScreen.X) / scale;
        double deltaY = (currentMouseScreenPoint.Y - _dragStartMouseScreen.Y) / scale;
        double dist = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

        // 1. If currently docked at the top bezel: evaluate tear-off threshold
        if (!_isDetachedPiP)
        {
            if (deltaY > 0 && deltaY >= MagneticDockingEngine.TearOffThreshold)
            {
                _isDetachedPiP = true;
                _isTearingOff = true;
                _isPillDragging = true;
                TactileFeedback.PlaySnap();

                _floatingCenterX = _dragStartPillCenterX + deltaX;
                _floatingCenterY = _dragStartPillCenterY + deltaY;

                ApplyGeometry();
                MoveFloatingWindow(_floatingCenterX, _floatingCenterY);

                _machine.PushToast(new ToastContent(
                    "\uE7C4",
                    "Detached Floating Island",
                    "PiP Mode Active \u00B7 Drag anywhere on screen",
                    ThemeBrushes.BlueColor,
                    TimeSpan.FromSeconds(1.8),
                    Priority: ToastPriority.Feedback));
            }
            return;
        }

        // 2. If already in Detached PiP mode: handle continuous drag and magnetic snapping
        if (dist > 5.0)
        {
            _isPillDragging = true;
            double rawTargetX = _dragStartPillCenterX + deltaX;
            double rawTargetY = _dragStartPillCenterY + deltaY;

            GetActiveMonitorWorkAndBounds(out var monBounds, out var workArea);

            double pillW = Pill?.ActualWidth > 0 ? Pill.ActualWidth : 180.0;
            double pillH = Pill?.ActualHeight > 0 ? Pill.ActualHeight : 34.0;
            double notchOffset = _settingsService?.Current.NotchYOffset ?? 0;

            var (target, snappedPos) = MagneticDockingEngine.CalculateMagneticSnap(
                new Point(rawTargetX, rawTargetY),
                monBounds,
                workArea,
                pillW,
                pillH,
                notchOffset);

            if (target != _activeMagneticDock)
            {
                if (target != MagneticDockTarget.None)
                {
                    TactileFeedback.PlayTick();
                }
                _activeMagneticDock = target;
            }

            MoveFloatingWindow(snappedPos.X, snappedPos.Y);
        }
    }

    internal void EndPillDrag()
    {
        if (!_isPillDragging && !_isTearingOff)
            return;

        _isPillDragging = false;
        _isTearingOff = false;

        // If user dragged to the top bezel magnetic zone: re-dock!
        if (_activeMagneticDock == MagneticDockTarget.TopCenter)
        {
            ReDockToBezel();
            return;
        }

        // Settle into floating PiP position or corner dock
        _isDetachedPiP = true;
        _settingsService?.Update(s =>
        {
            s.IsDetachedFloating = true;
            s.FloatingX = _floatingCenterX;
            s.FloatingY = _floatingCenterY;
            s.FloatingDockTarget = _activeMagneticDock.ToString();
        }, nameof(AppSettings.IsDetachedFloating));

        TactileFeedback.PlaySnap();

        string targetLabel = _activeMagneticDock switch
        {
            MagneticDockTarget.TaskbarTray => "Docked above Taskbar Tray",
            MagneticDockTarget.BottomLeft => "Docked at Bottom-Left Corner",
            MagneticDockTarget.TopRight => "Docked at Top-Right Corner",
            MagneticDockTarget.TopLeft => "Docked at Top-Left Corner",
            _ => "Floating PiP Island \u00B7 Double-click to re-dock"
        };

        _machine.PushToast(new ToastContent(
            "\uE7C4",
            "Floating Island (PiP)",
            targetLabel,
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(1.8),
            Priority: ToastPriority.Feedback));
    }

    internal void ToggleFloatingMode()
    {
        if (_isDetachedPiP)
        {
            ReDockToBezel();
        }
        else
        {
            DetachToFloatingCenter();
        }
    }

    internal void DetachToFloatingCenter()
    {
        GetActiveMonitorWorkAndBounds(out var monBounds, out var workArea);

        _isDetachedPiP = true;
        _floatingCenterX = monBounds.Left + (monBounds.Width / 2.0);
        _floatingCenterY = monBounds.Top + 140.0;
        _activeMagneticDock = MagneticDockTarget.None;

        _settingsService?.Update(s =>
        {
            s.IsDetachedFloating = true;
            s.FloatingX = _floatingCenterX;
            s.FloatingY = _floatingCenterY;
            s.FloatingDockTarget = "None";
        }, nameof(AppSettings.IsDetachedFloating));

        TactileFeedback.PlaySnap();
        ApplyGeometry();
        MoveFloatingWindow(_floatingCenterX, _floatingCenterY);

        _machine.PushToast(new ToastContent(
            "\uE7C4",
            "Detached Floating Island",
            "PiP Mode Active \u00B7 Double-click to re-dock",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(2.0),
            Priority: ToastPriority.Feedback));
    }

    internal void ReDockToBezel()
    {
        _isDetachedPiP = false;
        _activeMagneticDock = MagneticDockTarget.TopCenter;

        _settingsService?.Update(s =>
        {
            s.IsDetachedFloating = false;
            s.FloatingDockTarget = "TopCenter";
        }, nameof(AppSettings.IsDetachedFloating));

        TactileFeedback.PlaySnap();
        PositionWindow();
        ApplyGeometry();
        EnsureRendering();

        _machine.PushToast(new ToastContent(
            "\uE7C4",
            "Dynamic Island",
            "Re-docked to Top Bezel",
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(1.6),
            Priority: ToastPriority.Feedback));
    }

    internal void MoveFloatingWindow(double screenCenterX, double screenCenterY)
    {
        if (_hwnd == IntPtr.Zero) return;
        _floatingCenterX = screenCenterX;
        _floatingCenterY = screenCenterY;

        double scale = DpiScaleValue;
        int winX = (int)Math.Round((screenCenterX - (CanvasWidthDip / 2.0)) * scale);
        int winY = (int)Math.Round((screenCenterY - (Pill?.Height ?? 30.0) / 2.0) * scale);
        int widthPx = (int)Math.Round(CanvasWidthDip * scale);
        int heightPx = (int)Math.Round(CanvasHeightDip * scale);

        _windowRectPx = new RECT { Left = winX, Top = winY, Right = winX + widthPx, Bottom = winY + heightPx };
        SetWindowPos(_hwnd, HWND_TOPMOST, winX, winY, widthPx, heightPx, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private void GetActiveMonitorWorkAndBounds(out Rect monitorBounds, out Rect workArea)
    {
        var monitor = GetTargetMonitor();
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(monitor, ref info))
        {
            double scale = DpiScaleValue;
            monitorBounds = new Rect(
                info.rcMonitor.Left / scale,
                info.rcMonitor.Top / scale,
                info.rcMonitor.Width / scale,
                info.rcMonitor.Height / scale);

            workArea = new Rect(
                info.rcWork.Left / scale,
                info.rcWork.Top / scale,
                info.rcWork.Width / scale,
                info.rcWork.Height / scale);
        }
        else
        {
            monitorBounds = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
            workArea = new Rect(0, 0, SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        }
    }

    internal void UpdateNotchEarGeometry(double radius)
    {
        if (NotchLeftEar == null || NotchRightEar == null) return;
        double r = Math.Max(2.0, radius);

        NotchLeftEar.Width = r;
        NotchLeftEar.Height = r;
        NotchLeftEar.Margin = new Thickness(-r, 0, 0, 0);
        NotchLeftEar.Data = Geometry.Parse(MagneticDockingEngine.GenerateNotchEarSvgPath(r, isLeft: true));

        NotchRightEar.Width = r;
        NotchRightEar.Height = r;
        NotchRightEar.Margin = new Thickness(0, 0, -r, 0);
        NotchRightEar.Data = Geometry.Parse(MagneticDockingEngine.GenerateNotchEarSvgPath(r, isLeft: false));
    }
}
