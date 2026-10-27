using System.IO;
using System.Text.Json;
using System.Windows.Media;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

public sealed class AppSettings
{
    public double NotchYOffset { get; set; } = 0;
    public string DockPosition { get; set; } = "Center"; // "Left", "Center", "Right"
    public string ThemeName { get; set; } = "Solar Amber";
    public bool EnableAmbientGlow { get; set; } = true;
    public int MonitorIndex { get; set; } = 0;
}

public sealed record ThemePalette(string Name, Color Accent, Color Glow);

/// <summary>
/// Manages persistent application settings (Notch Y-offset, screen edge docking, accent themes, ambient glow)
/// saved to a local JSON file with debounced background disk writes.
/// </summary>
internal sealed class SettingsService
{
    private static readonly string SettingsFile = AppPaths.SettingsFile;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static readonly IReadOnlyList<ThemePalette> AvailableThemes = new[]
    {
        new ThemePalette("Solar Amber", Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0xFF, 0x9F, 0x0A)),
        new ThemePalette("Apple Neon", Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0x0A, 0x84, 0xFF)),
        new ThemePalette("Cyberpunk Cyan", Color.FromRgb(0x00, 0xF0, 0xFF), Color.FromRgb(0x00, 0xC8, 0xFF)),
        new ThemePalette("Emerald Green", Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0x30, 0xD1, 0x58)),
        new ThemePalette("Monochrome Frost", Color.FromRgb(0xE5, 0xE5, 0xEA), Color.FromRgb(0x8E, 0x8E, 0x93)),
        new ThemePalette("Purple Haze", Color.FromRgb(0xBF, 0x5A, 0xF2), Color.FromRgb(0xAF, 0x52, 0xDE)),
    };

    private CancellationTokenSource? _saveDebounceCts;
    private readonly object _saveLock = new();

    public AppSettings Current { get; private set; }

    public event Action? Changed;

    public SettingsService()
    {
        Current = Load();
    }

    public void Update(Action<AppSettings> updater)
    {
        updater(Current);
        ScheduleSave();
        Changed?.Invoke();
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                string json = File.ReadAllText(SettingsFile);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null) return loaded;
            }
        }
        catch { }
        return new AppSettings();
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
        try
        {
            string json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(SettingsFile, json);
        }
        catch { }
    }
}
