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
    Notes,
    Settings,
    Audio,
    Battery,
    VoiceMemo,
    Peripherals,
    Network,
    Notifications,
    SystemHud,
    Converter,
    Launcher,
    Termi,
    PhoneBridge,
    MechSounds,
    CaptureStudio,
    Calendar,
    QuickReply,
    WidgetsGallery,
    Pet
}

public static class ExpandedViewExtensions
{
    public static string ToKey(this ExpandedView view) => view switch
    {
        ExpandedView.Calendar => "calendar",
        ExpandedView.Shelf => "shelf",
        ExpandedView.Clipboard => "clipboard",
        ExpandedView.Calc => "calc",
        ExpandedView.Notes => "notes",
        ExpandedView.Settings => "settings",
        ExpandedView.Audio => "audio",
        ExpandedView.Battery => "battery",
        ExpandedView.VoiceMemo => "voicememo",
        ExpandedView.Peripherals => "peripherals",
        ExpandedView.Network => "network",
        ExpandedView.Notifications => "notifications",
        ExpandedView.SystemHud => "systemhud",
        ExpandedView.Converter => "converter",
        ExpandedView.Launcher => "launcher",
        ExpandedView.Termi => "termi",
        ExpandedView.PhoneBridge => "phonebridge",
        ExpandedView.MechSounds => "mechsounds",
        ExpandedView.CaptureStudio => "capturestudio",
        ExpandedView.QuickReply => "quickreply",
        ExpandedView.WidgetsGallery => "widgetsgallery",
        ExpandedView.Pet => "pet",
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
            case "calendar":
            case "tasks":
            case "schedule":
                view = ExpandedView.Calendar;
                return true;
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
            case "notes":
            case "scratchpad":
            case "note":
                view = ExpandedView.Notes;
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
            case "notifications":
            case "notifs":
                view = ExpandedView.Notifications;
                return true;
            case "systemhud":
            case "hud":
            case "cpu":
            case "performance":
            case "activity":
                view = ExpandedView.SystemHud;
                return true;
            case "converter":
            case "convert":
            case "pdf":
                view = ExpandedView.Converter;
                return true;
            case "launcher":
            case "launch":
            case "apps":
                view = ExpandedView.Launcher;
                return true;
            case "termi":
            case "terminal":
            case "terminotch":
                view = ExpandedView.Termi;
                return true;
            case "phonebridge":
            case "lan":
            case "phone":
            case "quickshare":
                view = ExpandedView.PhoneBridge;
                return true;
            case "mechsounds":
            case "mech":
            case "keyboard":
                view = ExpandedView.MechSounds;
                return true;
            case "capturestudio":
            case "capture":
            case "ocr":
            case "screenshot":
                view = ExpandedView.CaptureStudio;
                return true;
            case "quickreply":
            case "reply":
                view = ExpandedView.QuickReply;
                return true;
            case "widgetsgallery":
            case "widgets":
            case "widget":
            case "bento":
                view = ExpandedView.WidgetsGallery;
                return true;
            case "pet":
            case "companion":
            case "sanctuary":
                view = ExpandedView.Pet;
                return true;
            default:
                view = ExpandedView.Media;
                return false;
        }
    }
}
