using System.Windows;
using System.Windows.Media;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 3;
    public double NotchYOffset { get; set; } = 0;
    public string DockPosition { get; set; } = "Center"; // "Left", "Center", "Right"
    public string ThemeName { get; set; } = "Solar Amber";
    public string ThemeMode { get; set; } = "System"; // "System", "Dark", "Light"
    public string ThemeStyle { get; set; } = "Default"; // "Default", "Liquid Glass"
    /// <summary>Living island nature overlay theme: "None", "Ivy &amp; Blooms", "Sakura Branch", "Blossom Tree", "Meadow".</summary>
    public string IslandTheme { get; set; } = "Ivy & Blooms";
    /// <summary>Living nature botanical growth density from 0.20 (sparse/minimal) to 1.50 (lush). Default 0.70.</summary>
    public double NatureDensity { get; set; } = 0.70;
    /// <summary>Living nature botanical scale multiplier from 0.40 (small) to 1.40 (large). Default 0.85.</summary>
    public double NatureScale { get; set; } = 0.85;
    /// <summary>Living nature cascading drop length in DIPs from 20 (tight rim hugger) to 120 (deep curtain). Default 60.0.</summary>
    public double NatureDropLength { get; set; } = 60.0;
    /// <summary>Number of fluttering butterflies from 0 (none) to 4. Default 2.</summary>
    public int NatureButterflyCount { get; set; } = 2;
    /// <summary>Living nature flower blossom frequency from 0.20 (accents only) to 1.50 (full bloom). Default 0.75.</summary>
    public double NatureBloomDensity { get; set; } = 0.75;
    /// <summary>Whether shimmering golden pollen and firefly particles are enabled.</summary>
    public bool NaturePollenEnabled { get; set; } = true;
    /// <summary>Number of drifting golden pollen motes from 0 to 30. Default 14.</summary>
    public int NaturePollenCount { get; set; } = 14;
    public string ClockStyle { get; set; } = "Minimal"; // "Minimal", "Seconds", "DateBadge", "Analog", "Neon", "Flip"
    public bool EnableAmbientGlow { get; set; } = true;
    public int MonitorIndex { get; set; } = 0;
    public string? MonitorDeviceName { get; set; }
    public bool DoNotDisturb { get; set; } = false;
    /// <summary>When true, multiple notifications from the same app are grouped/stacked into collapsible cards.</summary>
    public bool GroupNotificationsByApp { get; set; } = true;
    /// <summary>When true, Windows notification banners are dismissed after being shown in the island.</summary>
    public bool ReplaceWindowsBanners { get; set; } = true;
    public bool ReduceMotion { get; set; } = false;
    /// <summary>Gooey / liquid animation intensity from 0.0 (critically damped, no bounce) to 1.0 (ultra-gooey droplet bounce &amp; squash/stretch).</summary>
    public double LiquidIntensity { get; set; } = 0.65;
    /// <summary>Animation speed multiplier from 0.5 (slow syrupy liquid) to 1.6 (snappy).</summary>
    public double AnimationSpeed { get; set; } = 1.0;
    /// <summary>Overall Dynamic Island size scale multiplier from 0.85 (85%) to 1.25 (125%).</summary>
    public double IslandScale { get; set; } = 1.0;
    /// <summary>Pill capsule surface opacity from 0.65 (translucent dark glass) to 1.0 (true OLED pitch-black).</summary>
    public double BackdropOpacity { get; set; } = 1.0;
    /// <summary>Liquid Glass diffusion blur radius multiplier (0.2x crystal clear to 2.5x heavy frosted mist). Default 1.0.</summary>
    public double GlassBlurIntensity { get; set; } = 1.0;
    /// <summary>Liquid Glass Snell's law refraction lens thickness multiplier (0.0x no refraction to 2.0x deep prism). Default 1.0.</summary>
    public double GlassRefractionIntensity { get; set; } = 1.0;
    /// <summary>When true, displays the subtle specular rim outline around the island capsule and dock. When false, the edge border is completely hidden for a seamless blend.</summary>
    public bool ShowEdgeBorder { get; set; } = true;
    /// <summary>When true, enables the detached iOS split-droplet secondary activity bubble when two live activities run concurrently.</summary>
    public bool EnableSecondaryDroplet { get; set; } = true;
    public bool EnableDeveloperHotkeys { get; set; } = false;
    public bool EnableVolumeKeyOsd { get; set; } = false;
    public bool EnableLockKeyHud { get; set; } = true;
    public bool EnableKeyboardLayoutHud { get; set; } = true;
    public bool EnableSyncedLyrics { get; set; } = true;
    public bool ShowLyricsInCompact { get; set; } = false;
    public bool ShowNetworkSpeedInIdle { get; set; } = false;
    public bool ShowCameraLensIcon { get; set; } = true;
    public bool ShowLottieAnimation { get; set; } = true;
    public bool LowMemoryMode { get; set; } = false;
    public string VisualizerStyle { get; set; } = "ClassicBars";
    public bool HardwareNotchMode { get; set; } = false;
    public double HardwareNotchWidth { get; set; } = 188.0;
    public double HardwareNotchHeight { get; set; } = 35.0;
    public double HardwareNotchRadius { get; set; } = 12.0;
    public string HardwareNotchPreset { get; set; } = "MacBookPro16";
    public bool IsDetachedFloating { get; set; } = false;
    public double FloatingX { get; set; } = -1;
    public double FloatingY { get; set; } = -1;
    public string FloatingDockTarget { get; set; } = "None";
    public bool FollowActiveMonitor { get; set; } = false;
    public bool EnableVirtualPet { get; set; } = true;
    public string PetSpecies { get; set; } = "Cat";
    public string PetAccessory { get; set; } = "None";
    public string PetName { get; set; } = "Mochi";
    public int WellnessReminderMinutes { get; set; } = 30;
    public int PetAffectionScore { get; set; } = 25;
    public int PetWaterCount { get; set; } = 0;
    public int PetStretchCount { get; set; } = 0;
    public int PetTreatCount { get; set; } = 0;
    public bool EnableChargingPulse { get; set; } = true;
    public int LowBatteryAlertThreshold { get; set; } = 20;
    public bool AutoPowerPlanOnBattery { get; set; } = false;
    public bool AutoPowerPlanOnAc { get; set; } = false;
    public bool EnableNightLightSim { get; set; } = false;
    public int NightLightWarmthKelvin { get; set; } = 3400;
    public string? WeatherCityOverride { get; set; }
    public List<string> HiddenDockViews { get; set; } = new();
    public List<string> MutedNotificationApps { get; set; } = new();
    public Dictionary<string, uint> CustomHotkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string>? EnabledDroplets { get; set; }
    public List<string> PinnedShelfFiles { get; set; } = new();
    public string ExpandTrigger { get; set; } = "HoverSnappy";
    public string GhostModifierKey { get; set; } = "Ctrl";
    public bool EnableFastSwipeGuard { get; set; } = true;
    public bool EnableQuickTuck { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;
    public bool AutoDownloadUpdates { get; set; } = true;
    public string UpdateChannel { get; set; } = "Stable";
    public DateTime? LastUpdateCheckTime { get; set; }
    public string? SkippedVersion { get; set; }
    public DateTime? SkippedVersionDate { get; set; }
    public string? GitHubToken { get; set; }
}

public sealed record ThemePalette(string Name, Color Accent, Color Glow, Color? LightAccent = null, Color? LightGlow = null)
{
    public Color GetAccent(bool isDark) => isDark ? Accent : (LightAccent ?? Accent);
    public Color GetGlow(bool isDark) => isDark ? Glow : (LightGlow ?? Glow);
}

/// <summary>
/// Manages persistent application settings (Notch Y-offset, screen edge docking, accent themes, ambient glow,
/// stable monitor device ID, DND, and hotkeys) saved atomically to a local JSON file with debounced disk writes.
/// </summary>
internal sealed class SettingsService
{
    private readonly string _settingsFile;

    public static readonly IReadOnlyList<ThemePalette> AvailableThemes = new[]
    {
        new ThemePalette("Solar Amber", Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0xD9, 0x77, 0x06), Color.FromRgb(0xD9, 0x77, 0x06)),
        new ThemePalette("Apple Neon", Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0x00, 0x7A, 0xFF), Color.FromRgb(0x00, 0x7A, 0xFF)),
        new ThemePalette("Cyberpunk Cyan", Color.FromRgb(0x00, 0xF0, 0xFF), Color.FromRgb(0x00, 0xC8, 0xFF), Color.FromRgb(0x00, 0x82, 0xC8), Color.FromRgb(0x00, 0x82, 0xC8)),
        new ThemePalette("Emerald Green", Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0x24, 0x8A, 0x3D), Color.FromRgb(0x24, 0x8A, 0x3D)),
        new ThemePalette("Monochrome Frost", Color.FromRgb(0xE5, 0xE5, 0xEA), Color.FromRgb(0x8E, 0x8E, 0x93), Color.FromRgb(0x48, 0x48, 0x4A), Color.FromRgb(0x8E, 0x8E, 0x93)),
        new ThemePalette("Purple Haze", Color.FromRgb(0xBF, 0x5A, 0xF2), Color.FromRgb(0xAF, 0x52, 0xDE), Color.FromRgb(0x89, 0x44, 0xAB), Color.FromRgb(0x89, 0x44, 0xAB)),
        new ThemePalette("Coral Sunset", Color.FromRgb(0xFF, 0x64, 0x42), Color.FromRgb(0xFF, 0x45, 0x3A), Color.FromRgb(0xD7, 0x36, 0x1E), Color.FromRgb(0xD7, 0x36, 0x1E)),
        new ThemePalette("Rose Velvet", Color.FromRgb(0xFF, 0x37, 0x5F), Color.FromRgb(0xFF, 0x33, 0x66), Color.FromRgb(0xD8, 0x1B, 0x60), Color.FromRgb(0xD8, 0x1B, 0x60)),
        new ThemePalette("Windows Accent", Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0x0A, 0x84, 0xFF)),
    };

    public static readonly IReadOnlyList<string> AvailableClockStyles = new[]
    {
        "Minimal",
        "Seconds",
        "DateBadge",
        "Analog",
        "Neon",
        "Flip",
        "Matrix",
        "JumpHour",
    };

    public static string NormalizeClockStyle(string? style)
    {
        if (string.IsNullOrWhiteSpace(style)) return "Minimal";
        string trimmed = style.Trim();
        if (string.Equals(trimmed, "Flip", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "RetroFlip", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "FlipClock", StringComparison.OrdinalIgnoreCase))
            return "Flip";
        if (string.Equals(trimmed, "Neon", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "NeonGlow", StringComparison.OrdinalIgnoreCase))
            return "Neon";
        if (string.Equals(trimmed, "Date", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "DateBadge", StringComparison.OrdinalIgnoreCase))
            return "DateBadge";
        if (string.Equals(trimmed, "Analog", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "MicroAnalog", StringComparison.OrdinalIgnoreCase))
            return "Analog";
        if (string.Equals(trimmed, "Seconds", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Second", StringComparison.OrdinalIgnoreCase))
            return "Seconds";
        if (string.Equals(trimmed, "Matrix", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "FieldMatrix", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Telemetry", StringComparison.OrdinalIgnoreCase))
            return "Matrix";
        if (string.Equals(trimmed, "JumpHour", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Jump", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Guichet", StringComparison.OrdinalIgnoreCase))
            return "JumpHour";
        return AvailableClockStyles.FirstOrDefault(s => string.Equals(s, trimmed, StringComparison.OrdinalIgnoreCase)) ?? "Minimal";
    }

    public static readonly IReadOnlyList<string> AvailableVisualizerStyles = new[]
    {
        "ClassicBars",
        "Waveform",
        "LiquidBlob",
        "NeonAurora",
        "SiriOrb",
    };

    public static string NormalizeVisualizerStyle(string? style)
    {
        if (string.IsNullOrWhiteSpace(style)) return "ClassicBars";
        string trimmed = style.Trim();
        if (string.Equals(trimmed, "Bars", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "ClassicBars", StringComparison.OrdinalIgnoreCase))
            return "ClassicBars";
        if (string.Equals(trimmed, "Wave", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Waveform", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Oscilloscope", StringComparison.OrdinalIgnoreCase))
            return "Waveform";
        if (string.Equals(trimmed, "Blob", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "LiquidBlob", StringComparison.OrdinalIgnoreCase))
            return "LiquidBlob";
        if (string.Equals(trimmed, "Aurora", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "NeonAurora", StringComparison.OrdinalIgnoreCase))
            return "NeonAurora";
        if (string.Equals(trimmed, "Siri", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "SiriOrb", StringComparison.OrdinalIgnoreCase))
            return "SiriOrb";
        return AvailableVisualizerStyles.FirstOrDefault(s => string.Equals(s, trimmed, StringComparison.OrdinalIgnoreCase)) ?? "ClassicBars";
    }

    public static readonly IReadOnlyList<string> AvailableDockPositions = new[]
    {
        "Left",
        "Center",
        "Right",
    };

    public static string NormalizeDockPosition(string? pos) =>
        AvailableDockPositions.FirstOrDefault(d => string.Equals(d, pos?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Center";

    public static readonly IReadOnlyList<string> AvailableHardwareNotchPresets = new[]
    {
        "MacBookPro16",
        "MacBookPro14",
        "MacBookAir",
        "DellXps",
        "Custom"
    };

    public static string NormalizeHardwareNotchPreset(string? preset) =>
        AvailableHardwareNotchPresets.FirstOrDefault(p => string.Equals(p, preset?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "MacBookPro16";

    public static (double Width, double Height, double Radius) GetNotchPresetDimensions(string preset) =>
        NormalizeHardwareNotchPreset(preset) switch
        {
            "MacBookPro14" => (172.0, 32.0, 11.0),
            "MacBookAir" => (160.0, 30.0, 10.0),
            "DellXps" => (140.0, 26.0, 8.0),
            "Custom" => (180.0, 34.0, 12.0),
            _ => (188.0, 35.0, 12.0)
        };

    public static string NormalizeThemeName(string? theme) =>
        string.IsNullOrWhiteSpace(theme) ? "Solar Amber" : theme.Trim();

    public static readonly IReadOnlyList<string> AvailableThemeModes = new[]
    {
        "System",
        "Dark",
        "Light",
    };

    public static string NormalizeThemeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return "System";
        string trimmed = mode.Trim();
        if (string.Equals(trimmed, "Dark", StringComparison.OrdinalIgnoreCase))
            return "Dark";
        if (string.Equals(trimmed, "Light", StringComparison.OrdinalIgnoreCase))
            return "Light";
        return "System";
    }

    public static readonly IReadOnlyList<string> AvailableThemeStyles = new[]
    {
        "Default",
        "Liquid Glass",
    };

    public static string NormalizeThemeStyle(string? style)
    {
        if (string.IsNullOrWhiteSpace(style)) return "Default";
        string trimmed = style.Trim();
        if (string.Equals(trimmed, "Liquid Glass", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "LiquidGlass", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Liquid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Glass", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Frosted", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Frosted Glass", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "FrostedGlass", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Frost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Mac", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "macOS", StringComparison.OrdinalIgnoreCase))
        {
            return "Liquid Glass";
        }
        return "Default";
    }

    public static readonly IReadOnlyList<string> AvailableIslandThemes = new[]
    {
        "None",
        "Ivy & Blooms",
        "Meadow",
    };

    public static string NormalizeIslandTheme(string? theme)
    {
        if (string.IsNullOrWhiteSpace(theme)) return "Ivy & Blooms";
        string trimmed = theme.Trim();
        if (string.Equals(trimmed, "None", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Off", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Disabled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Clean", StringComparison.OrdinalIgnoreCase))
            return "None";
        if (string.Equals(trimmed, "Meadow", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Flower Meadow", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Grass", StringComparison.OrdinalIgnoreCase))
            return "Meadow";

        // "Ivy & Blooms", "Leaves", "Leaves & Blooms", or legacy "Sakura" / "Tree" all map to the refined botanical leaves theme
        return "Ivy & Blooms";
    }

    public static readonly IReadOnlyList<string> AvailablePetSpecies = new[]
    {
        "Cat",
        "Dog",
        "Bunny",
        "Fox",
        "Panda",
        "Dragon",
    };

    public static string NormalizePetSpecies(string? species) =>
        AvailablePetSpecies.FirstOrDefault(s => string.Equals(s, species?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Cat";

    public static readonly IReadOnlyList<string> AvailablePetAccessories = new[]
    {
        "None",
        "Crown",
        "WizardHat",
        "Headphones",
        "Sunglasses",
        "Halo",
        "Sprout",
    };

    public static string NormalizePetAccessory(string? accessory) =>
        AvailablePetAccessories.FirstOrDefault(a => string.Equals(a, accessory?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "None";

    public static string NormalizeUpdateChannel(string? channel) =>
        string.Equals(channel?.Trim(), "Beta", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(channel?.Trim(), "Canary", StringComparison.OrdinalIgnoreCase)
            ? "Beta"
            : "Stable";

    private CancellationTokenSource? _saveDebounceCts;
    private readonly object _saveLock = new();

    public AppSettings Current { get; private set; }

    public event Action? Changed;
    public event Action<string?>? PropertyChanged;

    public SettingsService(string? customSettingsFile = null)
    {
        _settingsFile = customSettingsFile ?? AppPaths.SettingsFile;
        Current = Load(_settingsFile);
    }

    public static ThemePalette ResolveTheme(string? themeName)
    {
        if (string.Equals(themeName, "Windows Accent", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var glass = SystemParameters.WindowGlassColor;
                if (glass.A > 0 && (glass.R > 10 || glass.G > 10 || glass.B > 10))
                {
                    var accent = Color.FromRgb(glass.R, glass.G, glass.B);
                    return new ThemePalette("Windows Accent", accent, accent);
                }
            }
            catch
            {
                // Fallback to preset if SystemParameters is unavailable
            }
        }

        return AvailableThemes.FirstOrDefault(t => string.Equals(t.Name, themeName, StringComparison.OrdinalIgnoreCase))
               ?? AvailableThemes[0];
    }

    public void Update(Action<AppSettings> updater, string? propertyName = null)
    {
        lock (_saveLock)
        {
            updater(Current);
        }
        ScheduleSave();
        PropertyChanged?.Invoke(propertyName);
        Changed?.Invoke();
    }

    private static AppSettings Load(string path)
    {
        var settings = AtomicJsonStore.Load(path, () => new AppSettings());
        settings.HiddenDockViews ??= new List<string>();
        settings.MutedNotificationApps ??= new List<string>();
        settings.CustomHotkeys ??= new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        settings.PinnedShelfFiles ??= new List<string>();
        settings.LiquidIntensity = Math.Clamp(settings.LiquidIntensity, 0.0, 1.0);
        settings.AnimationSpeed = settings.AnimationSpeed <= 0.1 ? 1.0 : Math.Clamp(settings.AnimationSpeed, 0.5, 1.6);
        settings.IslandScale = settings.IslandScale <= 0.1 ? 1.0 : Math.Clamp(settings.IslandScale, 0.85, 1.25);
        settings.BackdropOpacity = settings.BackdropOpacity <= 0.1 ? 1.0 : Math.Clamp(settings.BackdropOpacity, 0.65, 1.0);
        settings.ClockStyle = NormalizeClockStyle(settings.ClockStyle);
        settings.VisualizerStyle = NormalizeVisualizerStyle(settings.VisualizerStyle);
        settings.DockPosition = NormalizeDockPosition(settings.DockPosition);
        settings.ThemeName = NormalizeThemeName(settings.ThemeName);
        settings.ThemeMode = NormalizeThemeMode(settings.ThemeMode);
        settings.ThemeStyle = NormalizeThemeStyle(settings.ThemeStyle);
        settings.IslandTheme = NormalizeIslandTheme(settings.IslandTheme);
        settings.PetSpecies = NormalizePetSpecies(settings.PetSpecies);
        settings.PetAccessory = NormalizePetAccessory(settings.PetAccessory);
        settings.UpdateChannel = NormalizeUpdateChannel(settings.UpdateChannel);
        if (settings.SchemaVersion < 3)
        {
            settings.SchemaVersion = 3;
        }
        return settings;
    }

    public void ResetToDefaults()
    {
        Update(s =>
        {
            s.SchemaVersion = 3;
            s.NotchYOffset = 0;
            s.DockPosition = "Center";
            s.ThemeName = "Solar Amber";
            s.ThemeMode = "System";
            s.ThemeStyle = "Default";
            s.IslandTheme = "Ivy & Blooms";
            s.ClockStyle = "Minimal";
            s.EnableAmbientGlow = true;
            s.MonitorIndex = 0;
            s.MonitorDeviceName = null;
            s.DoNotDisturb = false;
            s.ReplaceWindowsBanners = true;
            s.ReduceMotion = false;
            s.LiquidIntensity = 0.65;
            s.AnimationSpeed = 1.0;
            s.IslandScale = 1.0;
            s.BackdropOpacity = 1.0;
            s.ShowEdgeBorder = true;
            s.EnableSecondaryDroplet = true;
            s.EnableDeveloperHotkeys = false;
            s.EnableVolumeKeyOsd = false;
            s.EnableSyncedLyrics = true;
            s.ShowLyricsInCompact = false;
            s.ShowNetworkSpeedInIdle = false;
            s.ShowCameraLensIcon = true;
            s.ShowLottieAnimation = true;
            s.LowMemoryMode = false;
            s.VisualizerStyle = "ClassicBars";
            s.HardwareNotchMode = false;
            s.HardwareNotchWidth = 188.0;
            s.HardwareNotchHeight = 35.0;
            s.HardwareNotchRadius = 12.0;
            s.HardwareNotchPreset = "MacBookPro16";
            s.IsDetachedFloating = false;
            s.FloatingX = -1;
            s.FloatingY = -1;
            s.FloatingDockTarget = "None";
            s.FollowActiveMonitor = false;
            s.EnableVirtualPet = true;
            s.PetSpecies = "Cat";
            s.PetAccessory = "None";
            s.PetName = "Mochi";
            s.WellnessReminderMinutes = 30;
            s.PetAffectionScore = 25;
            s.PetWaterCount = 0;
            s.PetStretchCount = 0;
            s.PetTreatCount = 0;
            s.EnableChargingPulse = true;
            s.LowBatteryAlertThreshold = 20;
            s.AutoPowerPlanOnBattery = false;
            s.AutoPowerPlanOnAc = false;
            s.EnableNightLightSim = false;
            s.NightLightWarmthKelvin = 3400;
            s.GroupNotificationsByApp = true;
            s.WeatherCityOverride = null;
            s.HiddenDockViews.Clear();
            s.MutedNotificationApps.Clear();
            s.CustomHotkeys.Clear();
            s.EnabledDroplets = null;
            s.PinnedShelfFiles.Clear();
        });
    }

    public bool ExportToFile(string filePath)
    {
        return AtomicJsonStore.Save(filePath, Current, _saveLock);
    }

    public bool ImportFromFile(string filePath)
    {
        try
        {
            var imported = Load(filePath);
            lock (_saveLock)
            {
                Current = imported;
            }
            SaveNow();
            PropertyChanged?.Invoke(null);
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[SettingsService] ImportFromFile failed: {ex.Message}");
            return false;
        }
    }

    private void ScheduleSave()
    {
        CancellationToken token;
        lock (_saveLock)
        {
            _saveDebounceCts?.Cancel();
            _saveDebounceCts?.Dispose();
            _saveDebounceCts = new CancellationTokenSource();
            token = _saveDebounceCts.Token;
        }

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, token).ConfigureAwait(false);
                SaveNow();
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    public void SaveNow()
    {
        AtomicJsonStore.Save(_settingsFile, Current, _saveLock);
    }
}
