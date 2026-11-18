using System.Windows;

namespace DynamicIsland.Core;

public enum MagneticDockTarget
{
    None,
    TopCenter,
    TaskbarTray,
    BottomLeft,
    TopLeft,
    TopRight
}

/// <summary>
/// Physics engine for Picture-in-Picture (PiP) detached floating island,
/// tear-off drag thresholds, and magnetic edge/corner snapping with spring elasticity.
/// Zero-idle-CPU: pure analytical math with 0 heap allocations per step.
/// </summary>
public static class MagneticDockingEngine
{
    public const double TearOffThreshold = 35.0;
    public const double MagneticSnapRadius = 85.0;

    /// <summary>
    /// Calculates the magnetic dock target and snapped screen center coordinates.
    /// </summary>
    public static (MagneticDockTarget Target, Point SnappedPosition) CalculateMagneticSnap(
        Point rawCenter,
        Rect monitorBounds,
        Rect workArea,
        double pillWidth,
        double pillHeight,
        double notchYOffset = 0)
    {
        // 1. Top-Center (Docked to Screen Bezel)
        Point topCenter = new(
            monitorBounds.Left + monitorBounds.Width / 2.0,
            monitorBounds.Top + notchYOffset + pillHeight / 2.0);

        double distTopX = Math.Abs(rawCenter.X - topCenter.X);
        double distTopY = Math.Abs(rawCenter.Y - topCenter.Y);
        bool isNearHorizontalCenter = distTopX < (monitorBounds.Width * 0.25);

        if (isNearHorizontalCenter)
        {
            double distTop = Math.Sqrt((distTopX * 0.70) * (distTopX * 0.70) + distTopY * distTopY);
            if (distTop < MagneticSnapRadius * 1.35 || rawCenter.Y <= monitorBounds.Top + 48.0)
            {
                return (MagneticDockTarget.TopCenter, topCenter);
            }
        }

        // 2. Taskbar Notification Tray (Bottom-Right Corner)
        Point taskbarTray = new(
            workArea.Right - (pillWidth / 2.0) - 24.0,
            workArea.Bottom - (pillHeight / 2.0) - 12.0);

        double distTray = (rawCenter - taskbarTray).Length;
        if (distTray < MagneticSnapRadius)
        {
            return (MagneticDockTarget.TaskbarTray, taskbarTray);
        }

        // 3. Bottom-Left Corner
        Point bottomLeft = new(
            workArea.Left + (pillWidth / 2.0) + 24.0,
            workArea.Bottom - (pillHeight / 2.0) - 12.0);

        double distBottomLeft = (rawCenter - bottomLeft).Length;
        if (distBottomLeft < MagneticSnapRadius)
        {
            return (MagneticDockTarget.BottomLeft, bottomLeft);
        }

        // 4. Top-Right Corner
        Point topRight = new(
            workArea.Right - (pillWidth / 2.0) - 24.0,
            workArea.Top + (pillHeight / 2.0) + 20.0);

        double distTopRight = (rawCenter - topRight).Length;
        if (distTopRight < MagneticSnapRadius)
        {
            return (MagneticDockTarget.TopRight, topRight);
        }

        // 5. Top-Left Corner
        Point topLeft = new(
            workArea.Left + (pillWidth / 2.0) + 24.0,
            workArea.Top + (pillHeight / 2.0) + 20.0);

        double distTopLeft = (rawCenter - topLeft).Length;
        if (distTopLeft < MagneticSnapRadius)
        {
            return (MagneticDockTarget.TopLeft, topLeft);
        }

        // 6. Free Floating clamped within work area
        double clampedX = Math.Clamp(
            rawCenter.X,
            workArea.Left + (pillWidth / 2.0) + 8.0,
            workArea.Right - (pillWidth / 2.0) - 8.0);

        double clampedY = Math.Clamp(
            rawCenter.Y,
            workArea.Top + (pillHeight / 2.0) + 8.0,
            workArea.Bottom - (pillHeight / 2.0) - 8.0);

        return (MagneticDockTarget.None, new Point(clampedX, clampedY));
    }

    /// <summary>
    /// Computes the effective corner radii based on docked bezel mode vs detached PiP floating mode.
    /// In detached PiP mode, all 4 corners are fully rounded (symmetric capsule).
    /// </summary>
    public static (double TopRadius, double BottomRadius) ComputeCornerRadii(
        double effectiveRadius,
        bool isDetached,
        bool hardwareNotch,
        double notchYOffset,
        double liquidIntensity,
        double motionEnergy)
    {
        double bottomR = effectiveRadius;
        if (isDetached)
        {
            return (effectiveRadius, effectiveRadius);
        }

        if (hardwareNotch)
        {
            return (0.0, bottomR);
        }

        double topR = Math.Clamp(
            (notchYOffset * 1.5) + (effectiveRadius * 0.18 * liquidIntensity * motionEnergy),
            0.0,
            effectiveRadius);

        return (topR, bottomR);
    }

    /// <summary>
    /// Synthesizes SVG Path geometry for concave outward bezel fillet ears.
    /// </summary>
    public static string GenerateNotchEarSvgPath(double radius, bool isLeft)
    {
        double r = Math.Max(2.0, radius);
        string rStr = r.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        return isLeft
            ? $"M 0,0 L {rStr},0 L {rStr},{rStr} A {rStr},{rStr} 0 0 1 0,0 Z"
            : $"M 0,{rStr} L 0,0 L {rStr},0 A {rStr},{rStr} 0 0 1 0,{rStr} Z";
    }
}
