namespace DynamicIsland.Core;

internal sealed record SettingsSearchEntry(
    string Title,
    string Subtitle,
    string CategoryKey,
    string CategoryLabel,
    string Glyph,
    string Keywords);

/// <summary>
/// Static zero-idle-allocation search catalog indexing every setting across all 10 Settings categories.
/// </summary>
internal static class SettingsSearchIndex
{
    public static readonly IReadOnlyList<SettingsSearchEntry> Entries = new SettingsSearchEntry[]
    {
        // Appearance
        new("Material Surface", "Classic OLED capsule or refractive Liquid Glass", "Appearance", "Appearance", "\uE790", "oled liquid glass frosted material surface"),
        new("Color Mode", "System, Dark, or Light appearance", "Appearance", "Appearance", "\uE706", "dark light system theme mode"),
        new("Accent Tint", "9 curated accent swatches or Windows system accent", "Appearance", "Appearance", "\uE790", "color accent swatch amber neon cyan emerald purple coral rose windows"),
        new("Elasticity & Morph Speed", "Spring overshoot intensity and animation tempo", "Appearance", "Appearance", "\uE945", "liquid physics spring bounce elasticity animation speed morph"),
        new("Capsule Scale & Surface Opacity", "Global size multiplier and backdrop translucency", "Appearance", "Appearance", "\uE740", "scale size opacity transparency backdrop"),
        new("Liquid Glass Optics", "Frosting blur radius and Snell's law lens refraction", "Appearance", "Appearance", "\uE781", "blur frosting refraction lens glass optics"),
        new("Rim & Motion Effects", "Edge border, ambient halo glow, reduced motion & pulse test", "Appearance", "Appearance", "\uE7E8", "edge border specular glow halo reduced motion pulse"),

        // Flora
        new("Botanical Overlay", "Disabled, Ivy & Blooms, Wild Meadow, or Winter Frost", "Themes", "Flora", "\uEC0A", "flora nature botanical ivy blooms meadow winter frost snow icicles pine holly overlay theme"),
        new("Density Profiles & Sliders", "Minimal, Subtle, Balanced, Lush, Vine & Bloom sliders", "Themes", "Flora", "\uEC0A", "density vine bloom foliage scale cascade drop"),
        new("Fluttering Companions & Pollen", "Butterfly / frost-wing count (0-4) and ambient pollen or snowflakes", "Themes", "Flora", "\uEC0A", "butterfly butterflies pollen motes particles snowflakes snow frost wings"),

        // Clock & Audio
        new("Idle Clock Face", "8 horology & digital clock styles for the idle capsule", "Clock", "Clock & Audio", "\uE823", "clock face minimal seconds date badge analog neon flip matrix jump hour"),
        new("Audio Waveform Visualizer", "Bars, Waveform, Liquid Blob, Neon Aurora, or Siri Orb", "Clock", "Clock & Audio", "\uE9D9", "visualizer audio waveform oscilloscope bars blob aurora siri orb"),
        new("Now Playing Player Disc", "Rotating Studio Vinyl LP, Full-Art Disc, or Static Squircle cover", "Clock", "Clock & Audio", "\uE958", "album art cover vinyl disc record cd circle rotating spin turntable static squircle player"),
        new("Capsule Elements", "Camera lens dot, synced LRCLIB lyrics, pill lyrics & net speed", "Clock", "Clock & Audio", "\uE8D6", "camera lens lyrics compact pill network speed idle"),
        new("Split Secondary Droplet", "Detached bubble when two live activities run at once", "Clock", "Clock & Audio", "\uF158", "secondary droplet split bubble concurrent"),

        // Modules (Droplets)
        new("Modular Droplets", "Enable/disable zero-RAM feature modules & live RAM usage", "Droplets", "Modules", "\uF158", "droplets modules extensions basket converter capture clipboard caffeine notes"),

        // Dock Strip
        new("Dock Strip Manager", "Pin or hide any of the 19 dock tools including Widgets Gallery", "Dock", "Dock Strip", "\uE90D", "dock strip pin hide widgets gallery shelf clipboard calc notes"),

        // Display & Power
        new("Target Display & Follow Cursor", "Switch monitor or auto-follow active cursor display", "Screen", "Display", "\uE7F4", "monitor screen display multi-monitor follow cursor"),
        new("Alignment, Notch & Floating PiP", "Left/Center/Right dock, hardware notch, PiP & top margin", "Screen", "Display", "\uE7F4", "left center right align top margin pip floating notch"),
        new("Hardware Notch Presets", "MacBook Pro 16, MBP 14, Air, Dell XPS, or Custom dimensions", "Screen", "Display", "\uE7F4", "notch preset macbook pro air dell xps bezel"),
        new("Night Light & Color Temperature", "Warm eye comfort filter and Kelvin warmth slider", "Screen", "Display", "\uE708", "night light warmth kelvin eye comfort"),
        new("Power Automation", "MagSafe charging pulse, low battery alert, auto saver & AC perf", "Screen", "Display", "\uE945", "battery power magsafe charging pulse alert threshold saver ac"),
        new("High-Load Droplet Alerts", "CPU & RAM high-usage capsule badge toggle and percentage thresholds", "Screen", "Display", "\uE9D9", "high load cpu ram memory alert threshold droplet performance telemetry"),
        new("Browser & Tab Passthrough", "Click Only, Hover 150/260/380ms, Fast-Swipe Guard, Ghost key & Quick Tuck", "Screen", "Display", "\uE8B0", "hover dwell fast swipe guard click only ghost modifier ctrl alt quick tuck browser tabs"),
        new("Auto-Hide & Always Visible", "Hide Dynamic Island like the Windows taskbar; hover top edge to pull back", "Screen", "Display", "\uE8B0", "auto hide autohide taskbar always visible top edge hover reveal"),
        new("Context Profiles & Auto-Switch", "Deep Focus, Gaming, Presenting presets with process auto-switching", "Screen", "Display", "\uE80F", "context profile focus gaming presenting auto switch obs zoom teams"),

        // Alerts (Notifications)
        new("Notification Center", "Inspect notification history ring buffer", "Notifications", "Alerts", "\uEA8F", "notification center history"),
        new("Do Not Disturb", "Silence incoming banner animations and alert chimes", "Notifications", "Alerts", "\uE708", "dnd do not disturb silence"),
        new("Intercept Windows Toast Banners", "Dismiss native popups and route into Dynamic Island", "Notifications", "Alerts", "\uE7E7", "windows toast banners intercept replace"),
        new("Group Notifications by App", "Stack notifications from the same application", "Notifications", "Alerts", "\uE8F4", "group stack notifications app"),
        new("Muted Apps", "View and unmute silenced notification applications", "Notifications", "Alerts", "\uE74F", "muted apps unmute silence app"),

        // Companion (Pet)
        new("Virtual Companion Profile", "Enable pet, switch species, equip gear, or rename", "Pet", "Companion", "\uEB51", "virtual pet companion cat dog bunny fox panda dragon species gear rename"),
        new("Pet Playground & Desktop Roam", "Open interactive Pet Playground or float on desktop", "Pet", "Companion", "\uE7FC", "playground roam desktop pet float"),
        new("Pet Care & Wellness Reminders", "Pet, feed treat, flip trick, hydrate, stretch & reminder interval", "Pet", "Companion", "\uEB52", "treat water stretch flip trick wellness reminder"),
        new("Pet Auto-Roam & Context Reactions", "Autonomous taskbar roaming, custom name, daily streak & smart mood reactions", "Pet", "Companion", "\uE7C4", "pet auto roam desktop frameless custom name streak context reactions focus battery"),

        // Shortcuts
        new("Global Hotkeys", "Customize Ctrl+Alt shortcuts, Backspace to clear", "Shortcuts", "Shortcuts", "\uE765", "hotkeys shortcuts keybind ctrl alt record"),
        new("Volume Keys OSD", "Trigger Island volume toast from hardware volume keys", "Shortcuts", "Shortcuts", "\uE767", "volume keys osd hardware hook"),
        new("Caps / Num / Scroll Lock HUD", "Show capsule pill indicator when lock keys toggle", "Shortcuts", "Shortcuts", "\uE765", "caps lock num lock scroll lock hud"),
        new("Keyboard Layout HUD", "Show toast when switching input language", "Shortcuts", "Shortcuts", "\uE775", "keyboard layout input language ime hud"),
        new("Developer Hotkeys", "Enable Ctrl+Alt+T test toast shortcut", "Shortcuts", "Shortcuts", "\uEC7A", "developer hotkeys debug test toast"),

        // System & Updates
        new("Launch at Windows Startup", "Automatically start Dynamic Island on sign-in", "Updates", "System", "\uE7E8", "startup boot autostart windows login"),
        new("Weather City Override", "Set fixed city name instead of IP geolocation", "Updates", "System", "\uE753", "weather city location override"),
        new("Low Memory Mode & Lottie Animations", "Software rendering fallback and vector animations toggle", "Updates", "System", "\uE945", "low memory mode software rendering lottie animation performance"),
        new("Configuration Snapshot", "Export JSON backup, import from file, or factory reset", "Updates", "System", "\uE74E", "export import backup json reset defaults"),
        new("GitHub Updates & Channels", "Check releases, switch Stable/Beta, install & auto-check", "Updates", "System", "\uE895", "update github release stable beta auto check pre-download"),
        new("GitHub API Token", "Optional Personal Access Token for 5,000 req/hr rate limit", "Updates", "System", "\uE72E", "github token pat rate limit api"),
        new("Version History & Rollback", "Browse past releases and upgrade or rollback safely", "Updates", "System", "\uE81C", "version history rollback releases changelog"),
    };

    public static IReadOnlyList<SettingsSearchEntry> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SettingsSearchEntry>();

        string[] tokens = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return Array.Empty<SettingsSearchEntry>();

        var matches = new List<SettingsSearchEntry>();
        foreach (var entry in Entries)
        {
            bool allMatch = true;
            foreach (string token in tokens)
            {
                if (!entry.Title.Contains(token, StringComparison.OrdinalIgnoreCase) &&
                    !entry.Subtitle.Contains(token, StringComparison.OrdinalIgnoreCase) &&
                    !entry.CategoryLabel.Contains(token, StringComparison.OrdinalIgnoreCase) &&
                    !entry.Keywords.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch)
            {
                matches.Add(entry);
            }
        }

        return matches;
    }
}
