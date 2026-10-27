namespace DynamicIsland.Core;

/// <summary>
/// Strongly-typed identifier for each sub-view in the Expanded Dynamic Island surface,
/// replacing error-prone magic strings across navigation, toasts, and hotkeys.
/// </summary>
public enum ExpandedView
{
    Media,
    Shelf,
    Clipboard,
    Calc,
    Settings,
    Audio,
    Battery,
    VoiceMemo,
    Peripherals,
    Network
}

public static class ExpandedViewExtensions
{
    public static string ToKey(this ExpandedView view) => view switch
    {
        ExpandedView.Shelf => "shelf",
        ExpandedView.Clipboard => "clipboard",
        ExpandedView.Calc => "calc",
        ExpandedView.Settings => "settings",
        ExpandedView.Audio => "audio",
        ExpandedView.Battery => "battery",
        ExpandedView.VoiceMemo => "voicememo",
        ExpandedView.Peripherals => "peripherals",
        ExpandedView.Network => "network",
        _ => "media"
    };

    public static bool TryParseKey(string? key, out ExpandedView view)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            view = ExpandedView.Media;
            return false;
        }

        switch (key.Trim().ToLowerInvariant())
        {
            case "media":
                view = ExpandedView.Media;
                return true;
            case "shelf":
                view = ExpandedView.Shelf;
                return true;
            case "clipboard":
                view = ExpandedView.Clipboard;
                return true;
            case "calc":
                view = ExpandedView.Calc;
                return true;
            case "settings":
                view = ExpandedView.Settings;
                return true;
            case "audio":
                view = ExpandedView.Audio;
                return true;
            case "battery":
                view = ExpandedView.Battery;
                return true;
            case "voicememo":
                view = ExpandedView.VoiceMemo;
                return true;
            case "peripherals":
                view = ExpandedView.Peripherals;
                return true;
            case "network":
            case "wifi":
                view = ExpandedView.Network;
                return true;
            default:
                view = ExpandedView.Media;
                return false;
        }
    }
}
