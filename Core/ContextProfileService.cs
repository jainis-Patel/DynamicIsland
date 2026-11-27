using System.Diagnostics;
using DynamicIsland.Services;

namespace DynamicIsland.Core;

public sealed class IslandProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Glyph { get; set; } = "\uE80F";
    public string Description { get; set; } = "";

    public string? ThemeStyle { get; set; }
    public string? IslandTheme { get; set; }
    public bool? DoNotDisturb { get; set; }
    public bool? AutoHideIsland { get; set; }
    public bool? EnableVirtualPet { get; set; }
    public bool? LowMemoryMode { get; set; }
    public bool? EnableAmbientGlow { get; set; }

    public List<string> TriggerProcessNames { get; set; } = new();
    public bool? TriggerOnBattery { get; set; }
}

public sealed class ProfileBaseSnapshot
{
    public string ThemeStyle { get; set; } = "Default";
    public string IslandTheme { get; set; } = "Ivy & Blooms";
    public bool DoNotDisturb { get; set; }
    public bool AutoHideIsland { get; set; }
    public bool EnableVirtualPet { get; set; } = true;
    public bool LowMemoryMode { get; set; }
    public bool EnableAmbientGlow { get; set; } = true;
}

/// <summary>
/// Manages named context profiles (Deep Focus, Gaming, Presenting, Default) with lossless
/// base-settings snapshot/restore and optional Choreographer-driven auto-switching.
/// </summary>
internal sealed class ContextProfileService : IDisposable
{
    public const string ChoreId = "context_profile_autoswitch";

    public static readonly IReadOnlyList<IslandProfile> BuiltInProfiles = new[]
    {
        new IslandProfile
        {
            Id = "default",
            Name = "Default",
            Glyph = "\uE80F",
            Description = "Your personal baseline settings"
        },
        new IslandProfile
        {
            Id = "focus",
            Name = "Deep Focus",
            Glyph = "\uE708",
            Description = "Do Not Disturb on, Virtual Pet paused",
            DoNotDisturb = true,
            EnableVirtualPet = false
        },
        new IslandProfile
        {
            Id = "gaming",
            Name = "Gaming / Minimal",
            Glyph = "\uE7FC",
            Description = "Auto-hide capsule, Low Memory Mode, Ambient Glow off",
            AutoHideIsland = true,
            LowMemoryMode = true,
            EnableAmbientGlow = false
        },
        new IslandProfile
        {
            Id = "presenting",
            Name = "Presenting",
            Glyph = "\uE7F4",
            Description = "DND on, Pet off · Auto-triggers on OBS, Zoom, Teams, PowerPoint",
            DoNotDisturb = true,
            EnableVirtualPet = false,
            TriggerProcessNames = new List<string> { "obs64", "Zoom", "ms-teams", "POWERPNT" }
        }
    };

    private readonly SettingsService _settingsService;
    private readonly Choreographer? _choreographer;
    private string? _lastAutoActivatedProfileId;
    private bool _disposed;

    public event Action<IslandProfile>? ProfileChanged;

    public ContextProfileService(SettingsService settingsService, Choreographer? choreographer = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _choreographer = choreographer;
        SyncChoreRegistration();
    }

    public IReadOnlyList<IslandProfile> GetAllProfiles()
    {
        var list = new List<IslandProfile>(BuiltInProfiles);
        var custom = _settingsService.Current.CustomProfiles;
        if (custom != null && custom.Count > 0)
        {
            foreach (var p in custom)
            {
                if (!string.IsNullOrWhiteSpace(p.Id) &&
                    !list.Any(b => string.Equals(b.Id, p.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(p);
                }
            }
        }
        return list;
    }

    public IslandProfile GetActiveProfile()
    {
        string id = _settingsService.Current.ActiveProfileId ?? "default";
        return GetAllProfiles().FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? BuiltInProfiles[0];
    }

    public IslandProfile? ApplyProfile(string profileId, bool isAutoSwitch = false)
    {
        var target = GetAllProfiles().FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
        if (target == null) return null;

        _settingsService.Update(s =>
        {
            string prevId = s.ActiveProfileId ?? "default";
            if (string.Equals(target.Id, "default", StringComparison.OrdinalIgnoreCase))
            {
                if (s.ProfileBaseSnapshot != null)
                {
                    s.ThemeStyle = s.ProfileBaseSnapshot.ThemeStyle;
                    s.IslandTheme = s.ProfileBaseSnapshot.IslandTheme;
                    s.DoNotDisturb = s.ProfileBaseSnapshot.DoNotDisturb;
                    s.AutoHideIsland = s.ProfileBaseSnapshot.AutoHideIsland;
                    s.EnableVirtualPet = s.ProfileBaseSnapshot.EnableVirtualPet;
                    s.LowMemoryMode = s.ProfileBaseSnapshot.LowMemoryMode;
                    s.EnableAmbientGlow = s.ProfileBaseSnapshot.EnableAmbientGlow;
                    s.ProfileBaseSnapshot = null;
                }
                s.ActiveProfileId = "default";
                return;
            }

            // Snapshot baseline only when transitioning out of "default"
            if (string.Equals(prevId, "default", StringComparison.OrdinalIgnoreCase) && s.ProfileBaseSnapshot == null)
            {
                s.ProfileBaseSnapshot = new ProfileBaseSnapshot
                {
                    ThemeStyle = s.ThemeStyle,
                    IslandTheme = s.IslandTheme,
                    DoNotDisturb = s.DoNotDisturb,
                    AutoHideIsland = s.AutoHideIsland,
                    EnableVirtualPet = s.EnableVirtualPet,
                    LowMemoryMode = s.LowMemoryMode,
                    EnableAmbientGlow = s.EnableAmbientGlow
                };
            }
            else if (s.ProfileBaseSnapshot != null)
            {
                // Restore baseline first before applying the new non-default profile overrides
                s.ThemeStyle = s.ProfileBaseSnapshot.ThemeStyle;
                s.IslandTheme = s.ProfileBaseSnapshot.IslandTheme;
                s.DoNotDisturb = s.ProfileBaseSnapshot.DoNotDisturb;
                s.AutoHideIsland = s.ProfileBaseSnapshot.AutoHideIsland;
                s.EnableVirtualPet = s.ProfileBaseSnapshot.EnableVirtualPet;
                s.LowMemoryMode = s.ProfileBaseSnapshot.LowMemoryMode;
                s.EnableAmbientGlow = s.ProfileBaseSnapshot.EnableAmbientGlow;
            }

            if (!string.IsNullOrWhiteSpace(target.ThemeStyle)) s.ThemeStyle = target.ThemeStyle;
            if (!string.IsNullOrWhiteSpace(target.IslandTheme)) s.IslandTheme = target.IslandTheme;
            if (target.DoNotDisturb.HasValue) s.DoNotDisturb = target.DoNotDisturb.Value;
            if (target.AutoHideIsland.HasValue) s.AutoHideIsland = target.AutoHideIsland.Value;
            if (target.EnableVirtualPet.HasValue) s.EnableVirtualPet = target.EnableVirtualPet.Value;
            if (target.LowMemoryMode.HasValue) s.LowMemoryMode = target.LowMemoryMode.Value;
            if (target.EnableAmbientGlow.HasValue) s.EnableAmbientGlow = target.EnableAmbientGlow.Value;

            s.ActiveProfileId = target.Id;
        }, nameof(AppSettings.ActiveProfileId));

        _lastAutoActivatedProfileId = isAutoSwitch ? target.Id : null;
        ProfileChanged?.Invoke(target);
        return target;
    }

    public IslandProfile SaveCurrentAsCustomProfile(
        string name,
        IEnumerable<string>? triggerProcesses = null,
        bool? triggerOnBattery = null)
    {
        string cleanName = string.IsNullOrWhiteSpace(name) ? "Custom" : name.Trim();
        string id = string.Concat(cleanName.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == '_'));
        if (string.IsNullOrEmpty(id)) id = "custom";

        var cur = _settingsService.Current;
        var profile = new IslandProfile
        {
            Id = id,
            Name = cleanName,
            Glyph = "\uE734",
            Description = "User-saved custom profile",
            ThemeStyle = cur.ThemeStyle,
            IslandTheme = cur.IslandTheme,
            DoNotDisturb = cur.DoNotDisturb,
            AutoHideIsland = cur.AutoHideIsland,
            EnableVirtualPet = cur.EnableVirtualPet,
            LowMemoryMode = cur.LowMemoryMode,
            EnableAmbientGlow = cur.EnableAmbientGlow,
            TriggerProcessNames = triggerProcesses?.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
            TriggerOnBattery = triggerOnBattery
        };

        _settingsService.Update(s =>
        {
            s.CustomProfiles ??= new List<IslandProfile>();
            s.CustomProfiles.RemoveAll(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
            s.CustomProfiles.Add(profile);
            s.ActiveProfileId = profile.Id;
        }, nameof(AppSettings.CustomProfiles));

        ProfileChanged?.Invoke(profile);
        return profile;
    }

    public bool RemoveCustomProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId)) return false;
        bool removed = false;
        _settingsService.Update(s =>
        {
            if (s.CustomProfiles == null) return;
            removed = s.CustomProfiles.RemoveAll(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed && string.Equals(s.ActiveProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            {
                s.ActiveProfileId = "default";
            }
        }, nameof(AppSettings.CustomProfiles));
        return removed;
    }

    public string? MatchProfileForContext(IReadOnlyCollection<string> runningProcessNames, bool isOnBattery)
    {
        foreach (var profile in GetAllProfiles())
        {
            if (string.Equals(profile.Id, "default", StringComparison.OrdinalIgnoreCase)) continue;

            if (profile.TriggerProcessNames.Count > 0)
            {
                foreach (var trigger in profile.TriggerProcessNames)
                {
                    if (string.IsNullOrWhiteSpace(trigger)) continue;
                    foreach (var proc in runningProcessNames)
                    {
                        if (string.Equals(proc, trigger, StringComparison.OrdinalIgnoreCase))
                        {
                            return profile.Id;
                        }
                    }
                }
            }

            if (profile.TriggerOnBattery.HasValue && profile.TriggerOnBattery.Value == isOnBattery)
            {
                return profile.Id;
            }
        }
        return null;
    }

    public void SyncChoreRegistration()
    {
        if (_disposed || _choreographer == null) return;
        if (_settingsService.Current.EnableAutoProfileSwitching)
        {
            _choreographer.Register(ChoreId, TickAutoSwitch, intervalSeconds: 5, PowerSensitivity.Relaxed);
        }
        else
        {
            _choreographer.Unregister(ChoreId);
        }
    }

    private void TickAutoSwitch()
    {
        if (_disposed || !_settingsService.Current.EnableAutoProfileSwitching) return;

        var procNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var procs = Process.GetProcesses();
        try
        {
            foreach (var p in procs)
            {
                try { procNames.Add(p.ProcessName); } catch { }
            }
        }
        finally
        {
            foreach (var p in procs)
            {
                try { p.Dispose(); } catch { }
            }
        }

        bool onBattery = Interop.NativeMethods.GetSystemPowerStatus(out var sps) && sps.ACLineStatus == 0;
        string? matched = MatchProfileForContext(procNames, onBattery);
        string currentId = _settingsService.Current.ActiveProfileId ?? "default";

        if (!string.IsNullOrEmpty(matched))
        {
            if (!string.Equals(currentId, matched, StringComparison.OrdinalIgnoreCase))
            {
                ApplyProfile(matched, isAutoSwitch: true);
            }
        }
        else if (!string.IsNullOrEmpty(_lastAutoActivatedProfileId) &&
                 string.Equals(currentId, _lastAutoActivatedProfileId, StringComparison.OrdinalIgnoreCase))
        {
            ApplyProfile("default", isAutoSwitch: false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _choreographer?.Unregister(ChoreId);
    }
}
