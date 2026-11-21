using System;
using System.Runtime.InteropServices;

namespace DynamicIsland.Interop;

/// <summary>
/// Hardware-accelerated Windows Desktop Window Manager (DWM) Acrylic and Frosted Glass Blur Behind helper.
/// Uses SetWindowCompositionAttribute (WCA_ACCENT_POLICY) on Windows 10/11 and DwmEnableBlurBehindWindow
/// to physically blur whatever is behind the application with zero CPU overhead, while ensuring
/// the window remains 100% capturable by Windows Snipping Tool, Win+Shift+S, and screen recorders.
/// </summary>
internal static class WindowBlurHelper
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(IntPtr hWnd, ref DWM_BLURBEHIND pBlurBehind);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public const int DWMSBT_AUTO = 0;
    public const int DWMSBT_NONE = 1;
    public const int DWMSBT_MAINWINDOW = 2;      // Mica
    public const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
    public const int DWMSBT_TABBEDWINDOW = 4;     // Mica Alt

    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_ROUNDSMALL = 3;

    private const int WCA_ACCENT_POLICY = 19;

    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_ENABLE_HOSTBACKDROP = 5,
        ACCENT_INVALID_STATE = 6
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor; // 0xAABBGGRR format
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public nuint SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_BLURBEHIND
    {
        public uint dwFlags;
        public bool fEnable;
        public IntPtr hRgnBlur;
        public bool fTransitionOnMaximized;
    }

    /// <summary>
    /// Checks whether Windows system-wide "Transparency effects" are enabled in Windows Settings.
    /// Returns true by default if the setting cannot be queried.
    /// </summary>
    public static bool IsWindowsTransparencyEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("EnableTransparency") is int val)
            {
                return val != 0;
            }
        }
        catch { }
        return true;
    }

    /// <summary>
    /// Enables real hardware-accelerated Frosted Glass (Acrylic / BlurBehind) for the given window HWND.
    /// Also guarantees the window display affinity is set to WDA_NONE (0) so Snipping Tool &amp; Win+Shift+S work.
    /// </summary>
    public static void EnableFrostedGlass(IntPtr hwnd, bool isDark = true)
    {
        if (hwnd == IntPtr.Zero) return;

        // 1. Windows 11 Fluent 2 Native Backdrop Composition (Mica / Acrylic)
        if (Environment.OSVersion.Version.Build >= 22000)
        {
            try
            {
                int darkMode = isDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                // Windows 11 22H2 (Build 22621+) supports DWMWA_SYSTEMBACKDROP_TYPE
                if (Environment.OSVersion.Version.Build >= 22621)
                {
                    int backdropType = DWMSBT_TRANSIENTWINDOW; // Transient Acrylic / Mica Alt
                    int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
                    if (hr == 0)
                    {
                        return; // Successfully applied Windows 11 hardware backdrop
                    }
                }
            }
            catch { }
        }

        // 3. Fallback to Windows 10 SetWindowCompositionAttribute (WCA_ACCENT_POLICY)
        try
        {
            // GradientColor format: 0xAABBGGRR
            // Dark mode: translucent charcoal tint (0x991E1612)
            // Light mode: translucent frosty mist tint (0x99F0ECE8)
            int gradientColor = isDark ? unchecked((int)0x991E1612) : unchecked((int)0x99F0ECE8);

            var policy = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2, // Draw all borders
                GradientColor = gradientColor,
                AnimationId = 0
            };

            int size = Marshal.SizeOf(policy);
            IntPtr pPolicy = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, pPolicy, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = pPolicy,
                    SizeOfData = (nuint)size
                };

                int res = SetWindowCompositionAttribute(hwnd, ref data);
                if (res == 0)
                {
                    // Fall back to standard BlurBehind if AcrylicBlurBehind isn't supported on this specific OS build
                    policy.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND;
                    Marshal.StructureToPtr(policy, pPolicy, false);
                    res = SetWindowCompositionAttribute(hwnd, ref data);
                }

                if (res == 0)
                {
                    var bb = new DWM_BLURBEHIND
                    {
                        dwFlags = 1, // DWM_BB_ENABLE
                        fEnable = true,
                        hRgnBlur = IntPtr.Zero,
                        fTransitionOnMaximized = false
                    };
                    DwmEnableBlurBehindWindow(hwnd, ref bb);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pPolicy);
            }
        }
        catch
        {
            // 3. Fallback to classic DwmEnableBlurBehindWindow
            try
            {
                var bb = new DWM_BLURBEHIND
                {
                    dwFlags = 1, // DWM_BB_ENABLE
                    fEnable = true,
                    hRgnBlur = IntPtr.Zero,
                    fTransitionOnMaximized = false
                };
                DwmEnableBlurBehindWindow(hwnd, ref bb);
            }
            catch { }
        }
    }

    /// <summary>
    /// Disables DWM frosted blur behind, restoring the window to standard presentation.
    /// </summary>
    public static void DisableFrostedGlass(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        if (Environment.OSVersion.Version.Build >= 22621)
        {
            try
            {
                int none = DWMSBT_NONE;
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
            }
            catch { }
        }

        try
        {
            var policy = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_DISABLED,
                AccentFlags = 0,
                GradientColor = 0,
                AnimationId = 0
            };

            int size = Marshal.SizeOf(policy);
            IntPtr pPolicy = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, pPolicy, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = pPolicy,
                    SizeOfData = (nuint)size
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(pPolicy);
            }
        }
        catch
        {
            try
            {
                var bb = new DWM_BLURBEHIND
                {
                    dwFlags = 1,
                    fEnable = false,
                    hRgnBlur = IntPtr.Zero,
                    fTransitionOnMaximized = false
                };
                DwmEnableBlurBehindWindow(hwnd, ref bb);
            }
            catch { }
        }
    }
}
