using System.Runtime.InteropServices;
using System.Windows;

namespace DynamicIsland.Core;

/// <summary>
/// Resilient clipboard helper that retries transient Win32 clipboard open/lock contention (COMException).
/// </summary>
internal static class SafeClipboard
{
    public static bool TrySetText(string text, int maxAttempts = 5, int retryDelayMs = 20)
    {
        if (text == null) return false;
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (ExternalException)
            {
                if (i + 1 < maxAttempts) Thread.Sleep(retryDelayMs);
            }
            catch (Exception ex)
            {
                App.Log($"[SafeClipboard] TrySetText failed: {ex.Message}");
                return false;
            }
        }
        return false;
    }

    public static async Task<bool> TrySetTextAsync(string text, int maxAttempts = 5, int retryDelayMs = 25)
    {
        if (text == null) return false;
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (ExternalException)
            {
                if (i + 1 < maxAttempts) await Task.Delay(retryDelayMs);
            }
            catch (Exception ex)
            {
                App.Log($"[SafeClipboard] TrySetTextAsync failed: {ex.Message}");
                return false;
            }
        }
        return false;
    }
}
