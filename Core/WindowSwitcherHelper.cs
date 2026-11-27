using System.Diagnostics;
using System.Text;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Core;

internal readonly record struct OpenWindowEntry(IntPtr Hwnd, string Title, string ProcessName);

/// <summary>
/// Enumerates visible top-level desktop windows and focuses/restores a selected window
/// for Command Palette 2.0 (<c>win:</c> provider).
/// </summary>
internal static class WindowSwitcherHelper
{
    private const int SwRestore = 9;

    public static IReadOnlyList<OpenWindowEntry> EnumerateVisibleWindows(IntPtr excludeHwnd, int maxCount = 32)
    {
        var results = new List<OpenWindowEntry>();
        EnumWindows((hwnd, lParam) =>
        {
            if (hwnd == IntPtr.Zero || hwnd == excludeHwnd) return true;
            if (!IsWindowVisible(hwnd)) return true;

            var sb = new StringBuilder(256);
            int len = GetWindowText(hwnd, sb, sb.Capacity);
            if (len <= 0) return true;

            string title = sb.ToString().Trim();
            if (string.IsNullOrEmpty(title) || string.Equals(title, "Program Manager", StringComparison.OrdinalIgnoreCase))
                return true;

            string procName = "";
            try
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid > 0)
                {
                    using var p = Process.GetProcessById((int)pid);
                    procName = p.ProcessName;
                }
            }
            catch
            {
                procName = "";
            }

            results.Add(new OpenWindowEntry(hwnd, title, procName));
            return results.Count < maxCount;
        }, IntPtr.Zero);

        return results;
    }

    public static bool FocusWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SwRestore);
            }
            return SetForegroundWindow(hwnd);
        }
        catch
        {
            return false;
        }
    }
}
