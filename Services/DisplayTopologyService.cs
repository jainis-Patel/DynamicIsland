using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DynamicIsland.Interop;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

public sealed record MonitorCardInfo(
    int Index,
    IntPtr Handle,
    string DeviceName,
    string FriendlyName,
    int Width,
    int Height,
    int RefreshRateHz,
    int DpiScalePercent,
    bool IsPrimary,
    bool IsActive)
{
    public string ResolutionText => $"{Width} × {Height}";
    public string RefreshRateText => RefreshRateHz > 0 ? $"{RefreshRateHz} Hz" : "60 Hz";
    public string SummaryText => $"{ResolutionText} @ {RefreshRateText}";
    public string BadgeText => IsPrimary ? (IsActive ? "Primary • Active" : "Primary") : (IsActive ? "Active" : "");
}

/// <summary>
/// Service that discovers connected monitors, retrieves hardware display modes (resolution,
/// refresh rate in Hz, bits per pixel, primary status) via Win32 EnumDisplaySettings,
/// and manages instant hardware Night Light / warm candlelight gamma curves.
/// </summary>
internal static class DisplayTopologyService
{
    private static readonly ushort[] OriginalRamp = new ushort[768];
    private static bool _originalRampSaved;

    public static List<MonitorCardInfo> QueryConnectedMonitors(IntPtr activeMonitor)
    {
        var result = new List<MonitorCardInfo>();
        var handles = new List<IntPtr>();
        var edidNames = TryGetEdidFriendlyNames();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref RECT _, IntPtr _) =>
        {
            handles.Add(hMon);
            return true;
        }, IntPtr.Zero);

        for (int i = 0; i < handles.Count; i++)
        {
            IntPtr hMon = handles[i];
            var ex = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfoEx(hMon, ref ex)) continue;

            string devName = ex.szDevice ?? $"DISPLAY{i + 1}";
            bool isPrimary = (ex.dwFlags & 1) != 0; // MONITORINFOF_PRIMARY
            bool isActive = (hMon == activeMonitor);

            // Query current resolution and refresh rate via Win32 EnumDisplaySettingsW
            int width = Math.Abs(ex.rcMonitor.Right - ex.rcMonitor.Left);
            int height = Math.Abs(ex.rcMonitor.Bottom - ex.rcMonitor.Top);
            int refreshRate = 60;

            var dm = new DEVMODEW { dmSize = (short)Marshal.SizeOf<DEVMODEW>() };
            if (EnumDisplaySettingsW(devName, ENUM_CURRENT_SETTINGS, ref dm))
            {
                if (dm.dmPelsWidth > 0 && dm.dmPelsHeight > 0)
                {
                    width = dm.dmPelsWidth;
                    height = dm.dmPelsHeight;
                }
                if (dm.dmDisplayFrequency > 0)
                {
                    refreshRate = dm.dmDisplayFrequency;
                }
            }

            // DPI scale percentage
            int dpiPercent = 100;
            if (GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
            {
                dpiPercent = (int)Math.Round((dpiX / 96.0) * 100);
            }

            // Friendly name (prefer hardware EDID name via QueryDisplayConfig when available)
            string friendly;
            if (edidNames.TryGetValue(devName, out string? edidName) && !string.IsNullOrWhiteSpace(edidName))
            {
                friendly = isPrimary ? $"{edidName} (Main)" : edidName;
            }
            else
            {
                friendly = isPrimary
                    ? $"Display {i + 1} (Main)"
                    : $"Display {i + 1} (External)";
            }

            result.Add(new MonitorCardInfo(
                Index: i,
                Handle: hMon,
                DeviceName: devName,
                FriendlyName: friendly,
                Width: width,
                Height: height,
                RefreshRateHz: refreshRate,
                DpiScalePercent: dpiPercent,
                IsPrimary: isPrimary,
                IsActive: isActive));
        }

        return result;
    }

    private static Dictionary<string, string> TryGetEdidFriendlyNames()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0 ||
                pathCount == 0)
            {
                return map;
            }

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
            {
                return map;
            }

            uint sourceSize = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
            uint targetSize = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();

            for (int i = 0; i < pathCount; i++)
            {
                var src = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = sourceSize,
                        adapterId = paths[i].sourceInfo.adapterId,
                        id = paths[i].sourceInfo.id
                    }
                };

                if (DisplayConfigGetDeviceInfo(ref src) != 0 || string.IsNullOrWhiteSpace(src.viewGdiDeviceName))
                    continue;

                var tgt = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = targetSize,
                        adapterId = paths[i].targetInfo.adapterId,
                        id = paths[i].targetInfo.id
                    }
                };

                if (DisplayConfigGetDeviceInfo(ref tgt) == 0 && !string.IsNullOrWhiteSpace(tgt.monitorFriendlyDeviceName))
                {
                    map[src.viewGdiDeviceName] = tgt.monitorFriendlyDeviceName.Trim();
                }
            }
        }
        catch { }
        return map;
    }

    /// <summary>
    /// Applies or resets a smooth hardware GDI gamma ramp for Night Light (blue-light filter).
    /// </summary>
    public static bool SetNightLight(bool enabled, int warmthKelvin)
    {
        IntPtr hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return false;

        try
        {
            if (!_originalRampSaved)
            {
                GCHandle pinOrig = GCHandle.Alloc(OriginalRamp, GCHandleType.Pinned);
                try
                {
                    if (GetDeviceGammaRamp(hdc, pinOrig.AddrOfPinnedObject()))
                    {
                        _originalRampSaved = true;
                    }
                }
                finally
                {
                    pinOrig.Free();
                }
            }

            ushort[] ramp = new ushort[768];

            if (!enabled)
            {
                // Restore original or standard linear ramp
                if (_originalRampSaved)
                {
                    Array.Copy(OriginalRamp, ramp, 768);
                }
                else
                {
                    for (int i = 0; i < 256; i++)
                    {
                        ushort val = (ushort)Math.Min(65535, i * 257);
                        ramp[i] = val;
                        ramp[i + 256] = val;
                        ramp[i + 512] = val;
                    }
                }
            }
            else
            {
                // Compute warm daylight/candlelight color temperature
                // Kelvin range 2200K (very warm amber) to 5000K (mild warm)
                int kelvin = Math.Clamp(warmthKelvin, 2000, 6000);
                double t = (kelvin - 2000.0) / 4000.0; // 0 = warm amber, 1 = neutral

                double redFactor = 1.0;
                double greenFactor = Math.Clamp(0.78 + (0.22 * t), 0.75, 1.0);
                double blueFactor = Math.Clamp(0.50 + (0.50 * t), 0.50, 1.0);

                for (int i = 0; i < 256; i++)
                {
                    double baseVal = i * 257.0;
                    ramp[i] = (ushort)Math.Clamp(baseVal * redFactor, 0, 65535);
                    ramp[i + 256] = (ushort)Math.Clamp(baseVal * greenFactor, 0, 65535);
                    ramp[i + 512] = (ushort)Math.Clamp(baseVal * blueFactor, 0, 65535);
                }
            }

            GCHandle pin = GCHandle.Alloc(ramp, GCHandleType.Pinned);
            try
            {
                return SetDeviceGammaRamp(hdc, pin.AddrOfPinnedObject());
            }
            finally
            {
                pin.Free();
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }
}
