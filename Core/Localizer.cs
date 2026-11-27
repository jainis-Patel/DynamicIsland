using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace DynamicIsland.Core;

/// <summary>
/// Lightweight ResourceDictionary-backed string lookup helper for Dynamic Island UI &amp; toasts,
/// with support for external XAML language packs (%LocalAppData%\DynamicIsland\lang\*.xaml).
/// </summary>
internal static class Localizer
{
    private static readonly ConcurrentDictionary<string, string> Overrides = new(StringComparer.Ordinal);

    public static string Get(string key, string fallback)
    {
        if (string.IsNullOrWhiteSpace(key)) return fallback;

        if (Overrides.TryGetValue(key, out string? overridden) && !string.IsNullOrEmpty(overridden))
        {
            return overridden;
        }

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

    public static bool TryLoadExternalLanguagePack(string xamlFilePath)
    {
        if (string.IsNullOrWhiteSpace(xamlFilePath) || !File.Exists(xamlFilePath))
            return false;

        try
        {
            using var fs = File.OpenRead(xamlFilePath);
            if (XamlReader.Load(fs) is not ResourceDictionary dict)
                return false;

            int loaded = 0;
            foreach (object? keyObj in dict.Keys)
            {
                if (keyObj is string key && dict[keyObj] is string val && !string.IsNullOrEmpty(val))
                {
                    Overrides[key] = val;
                    loaded++;
                }
            }

            if (Application.Current?.Resources?.MergedDictionaries is { } merged && loaded > 0)
            {
                merged.Add(dict);
            }

            return loaded > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void ClearOverrides() => Overrides.Clear();
}
