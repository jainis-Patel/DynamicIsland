using System.Windows;
using System.Windows.Interop;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Interop;

/// <summary>
/// Shared Win32 extended-style setup for non-activating floating tool windows
/// (prevents Alt+Tab / taskbar appearance and optional focus stealing).
/// </summary>
internal static class FloatingWindowHelper
{
    public static IntPtr ApplyToolWindowStyles(Window window, bool noActivate = true)
    {
        ArgumentNullException.ThrowIfNull(window);
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;

        long ex = GetExStyle(hwnd);
        ex |= WS_EX_TOOLWINDOW;
        if (noActivate)
        {
            ex |= WS_EX_NOACTIVATE;
        }
        ex &= ~WS_EX_APPWINDOW;
        SetExStyle(hwnd, ex);
        return hwnd;
    }
}
