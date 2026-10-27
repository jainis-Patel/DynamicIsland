using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// Manages Windows CurrentUser Run registry key registration so the user can toggle
/// "Start with Windows" from either the Settings panel or the system tray context menu.
/// </summary>
internal static class StartupService
{
    private const string StartupRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "DynamicIsland";

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRunKey, writable: false);
            return key?.GetValue(AppName) != null;
        }
        catch (Exception ex)
        {
            App.Log($"[StartupService] IsStartupEnabled check failed: {ex.Message}");
            return false;
        }
    }

    public static bool SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRunKey, writable: true);
            if (key == null) return false;

            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;

            if (enable)
                key.SetValue(AppName, $"\"{exe}\"");
            else
                key.DeleteValue(AppName, throwOnMissingValue: false);

            App.Log($"[StartupService] Run at Windows Startup set to: {enable}");
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[StartupService] SetStartup failed: {ex.Message}");
            return false;
        }
    }
}
