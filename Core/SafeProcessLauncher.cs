using System.Diagnostics;
using System.IO;

namespace DynamicIsland.Core;

/// <summary>
/// Validates URI schemes and disposes Process handles when launching external browsers, Settings pages, or folders.
/// </summary>
internal static class SafeProcessLauncher
{
    private static readonly HashSet<string> AllowedShellSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        "ms-settings",
        "ms-availablenetworks",
        "outlookcal",
        "mailto",
        "spotify"
    };

    public static bool TryOpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        string trimmed = url.Trim();
        if (trimmed.Contains('\r') || trimmed.Contains('\n') || trimmed.Contains('\0'))
            return false;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            App.Log($"[SafeProcessLauncher] Blocked non-HTTP(S) URL scheme: {uri.Scheme}");
            return false;
        }

        return StartShellTarget(uri.AbsoluteUri);
    }

    public static bool TryOpenUri(string? uriText, IReadOnlySet<string> allowedSchemes)
    {
        if (string.IsNullOrWhiteSpace(uriText)) return false;
        string trimmed = uriText.Trim();
        if (trimmed.Contains('\r') || trimmed.Contains('\n') || trimmed.Contains('\0'))
            return false;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            !allowedSchemes.Contains(uri.Scheme))
        {
            App.Log($"[SafeProcessLauncher] Blocked unsafe URI scheme: '{trimmed}'");
            return false;
        }

        return StartShellTarget(uri.AbsoluteUri);
    }

    public static bool TryOpenShellUri(string? uriText)
    {
        if (string.IsNullOrWhiteSpace(uriText)) return false;
        string trimmed = uriText.Trim();
        if (trimmed.Contains('\r') || trimmed.Contains('\n') || trimmed.Contains('\0'))
            return false;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            !AllowedShellSchemes.Contains(uri.Scheme))
        {
            App.Log($"[SafeProcessLauncher] Blocked unsafe shell URI: '{trimmed}'");
            return false;
        }

        return StartShellTarget(trimmed);
    }

    public static bool TryOpenFolder(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return false;

        try
        {
            var psi = new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = false
            };
            psi.ArgumentList.Add(folderPath);
            using var proc = Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[SafeProcessLauncher] Failed to open folder '{folderPath}': {ex.Message}");
            return false;
        }
    }

    private static bool StartShellTarget(string target)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[SafeProcessLauncher] Failed to launch '{target}': {ex.Message}");
            return false;
        }
    }
}
