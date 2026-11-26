using System.Windows;

namespace DynamicIsland.Core;

/// <summary>
/// Lightweight ResourceDictionary-backed string lookup helper for Dynamic Island UI &amp; toasts.
/// </summary>
internal static class Localizer
{
    public static string Get(string key, string fallback)
    {
        if (string.IsNullOrWhiteSpace(key)) return fallback;
        try
        {
            if (Application.Current != null &&
                Application.Current.TryFindResource(key) is string value &&
                !string.IsNullOrEmpty(value))
            {
                return value;
            }
        }
        catch
        {
            // Fallback when running headless (e.g. --self-test)
        }
        return fallback;
    }
}
