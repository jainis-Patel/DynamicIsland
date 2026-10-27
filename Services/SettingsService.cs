using System.Windows;
using System.Windows.Media;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 2;
    public double NotchYOffset { get; set; } = 0;
    public string DockPosition { get; set; } = "Center"; // "Left", "Center", "Right"
    public string ThemeName { get; set; } = "Solar Amber";
    public bool EnableAmbientGlow { get; set; } = true;
    public int MonitorIndex { get; set; } = 0;
    public string? MonitorDeviceName { get; set; }
    public bool DoNotDisturb { get; set; } = false;
    public bool ReduceMotion { get; set; } = false;
    public bool EnableDeveloperHotkeys { get; set; } = false;
    public List<string> MutedNotificationApps { get; set; } = new();
    public Dictionary<string, uint> CustomHotkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record ThemePalette(string Name, Color Accent, Color Glow);

/// <summary>
/// Manages persistent application settings (Notch Y-offset, screen edge docking, accent themes, ambient glow,
/// stable monitor device ID, DND, and hotkeys) saved atomically to a local JSON file with debounced disk writes.
/// </summary>
internal sealed class SettingsService
{
    private readonly string _settingsFile;

    public static readonly IReadOnlyList<ThemePalette> AvailableThemes = new[]
    {
        new ThemePalette("Solar Amber", Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0xFF, 0x9F, 0x0A)),
        new ThemePalette("Apple Neon", Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0x0A, 0x84, 0xFF)),
        new ThemePalette("Cyberpunk Cyan", Color.FromRgb(0x00, 0xF0, 0xFF), Color.FromRgb(0x00, 0xC8, 0xFF)),
        new ThemePalette("Emerald Green", Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0x30, 0xD1, 0x58)),
        new ThemePalette("Monochrome Frost", Color.FromRgb(0xE5, 0xE5, 0xEA), Color.FromRgb(0x8E, 0x8E, 0x93)),
        new ThemePalette("Purple Haze", Color.FromRgb(0xBF, 0x5A, 0xF2), Color.FromRgb(0xAF, 0x52, 0xDE)),
        new ThemePalette("Windows Accent", Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0x0A, 0x84, 0xFF)),
    };

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
        settings.MutedNotificationApps ??= new List<string>();
        settings.CustomHotkeys ??= new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        if (settings.SchemaVersion < 2)
        {
            settings.SchemaVersion = 2;
        }
        return settings;
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
