using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Core;

/// <summary>
/// Pure coordinate math for Per-Monitor V2 DPI scaling, docked island placement,
/// and monitor work-area bounds clamping.
/// </summary>
internal static class WindowPlacementHelper
{
    public static int DipToPhysicalPx(double dip, double dpiScale)
    {
        double safeScale = dpiScale > 0.1 ? dpiScale : 1.0;
        return (int)Math.Round(dip * safeScale);
    }

    public static double PhysicalToDip(double physicalPx, double dpiScale)
    {
        double safeScale = dpiScale > 0.1 ? dpiScale : 1.0;
        return physicalPx / safeScale;
    }

    public static (double X, double Y) ClampToWorkArea(
        double x,
        double y,
        double width,
        double height,
        double workLeft,
        double workTop,
        double workRight,
        double workBottom,
        double margin = 8.0)
    {
        double minX = workLeft + margin;
        double maxX = Math.Max(minX, workRight - width - margin);
        double minY = workTop + margin;
        double maxY = Math.Max(minY, workBottom - height - margin);
        return (Math.Clamp(x, minX, maxX), Math.Clamp(y, minY, maxY));
    }

    public static RECT ComputeDockedWindowRectPx(
        int monLeft,
        int monTop,
        int monWidth,
        int workLeft,
        int workRight,
        double canvasWidthDip,
        double canvasHeightDip,
        double refWidthDip,
        double dpiScale,
        string? dockPosition,
        bool hardwareNotchMode,
        double notchYOffsetDip)
    {
        double scale = dpiScale > 0.1 ? dpiScale : 1.0;
        double pillMarginInsideWindow = Math.Max(0, (canvasWidthDip - refWidthDip) / 2.0);

        int widthPx = DipToPhysicalPx(canvasWidthDip, scale);
        int heightPx = DipToPhysicalPx(canvasHeightDip, scale);

        int x;
        if (string.Equals(dockPosition, "Left", StringComparison.Ordinal))
        {
            x = workLeft + DipToPhysicalPx(24 - pillMarginInsideWindow, scale);
        }
        else if (string.Equals(dockPosition, "Right", StringComparison.Ordinal))
        {
            x = workRight - widthPx - DipToPhysicalPx(24 - pillMarginInsideWindow, scale);
        }
        else
        {
            x = monLeft + (monWidth - widthPx) / 2;
        }

        int notchOffsetPx = hardwareNotchMode ? 0 : DipToPhysicalPx(notchYOffsetDip, scale);
        int y = monTop + notchOffsetPx;

        return new RECT { Left = x, Top = y, Right = x + widthPx, Bottom = y + heightPx };
    }
}
