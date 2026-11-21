namespace DynamicIsland.Core;

/// <summary>
/// Centralizes Bluetooth, HID peripheral, and audio endpoint device classification and Segoe Fluent icon glyphs.
/// </summary>
internal static class DeviceClassifier
{
    private static readonly string[] AudioDeviceHints =
    {
        "airpod", "buds", "headphone", "headset", "earbud",
        "wh-", "wf-", "beats", "bose", "jbl", "freebuds", "xm4", "xm5"
    };

    public static bool IsAudioDeviceName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        foreach (string hint in AudioDeviceHints)
        {
            if (name.Contains(hint, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static (string Category, string IconGlyph) ClassifyPeripheral(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ("Peripheral", "\uE702");

        string lower = name.ToLowerInvariant();
        if (lower.Contains("mouse") || lower.Contains("trackball") || lower.Contains("master"))
            return ("Mouse", "\uE962");
        if (lower.Contains("keyboard") || lower.Contains("keychron") || lower.Contains("k380"))
            return ("Keyboard", "\uE92E");
        if (lower.Contains("controller") || lower.Contains("gamepad") || lower.Contains("xbox") || lower.Contains("dualsense"))
            return ("Controller", "\uE7FC");
        if (IsAudioDeviceName(lower) || lower.Contains("audio") || lower.Contains("speaker"))
            return ("Headset", "\uE7F6");
        if (lower.Contains("phone") || lower.Contains("iphone") || lower.Contains("pixel") || lower.Contains("galaxy") || lower.Contains("s2") || lower.Contains("s24"))
            return ("Phone", "\uE8EA");
        if (lower.Contains("pen") || lower.Contains("stylus") || lower.Contains("surface pen"))
            return ("Stylus", "\uEDC6");

        return ("Peripheral", "\uE702");
    }

    public static (string Type, string Glyph) ClassifyAudioEndpoint(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ("Speakers", "\uE7F5");

        string lower = name.ToLowerInvariant();
        if (IsAudioDeviceName(lower))
            return ("Headphones", "\uE7F6");
        if (lower.Contains("bluetooth") || lower.Contains("hands-free") || lower.Contains("wireless"))
            return ("Bluetooth", "\uE702");
        if (lower.Contains("nvidia") || lower.Contains("amd") || lower.Contains("intel") ||
            lower.Contains("hdmi") || lower.Contains("displayport") || lower.Contains("tv") ||
            lower.Contains("monitor") || lower.Contains("screen"))
            return ("Display Audio", "\uE7F4");

        return ("Speakers", "\uE7F5");
    }
}
