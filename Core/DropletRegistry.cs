using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.Core;

public enum DropletCategory
{
    Productivity,
    Media,
    System,
    Capture
}

public sealed class DropletDescriptor : INotifyPropertyChanged
{
    private bool _isEnabled = true;

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Glyph { get; init; } = "\uE7E7";
    public DropletCategory Category { get; init; }
    public ExpandedView? TargetView { get; init; }
    public string? DockPinKey { get; init; }
    public int EstimatedRamKb { get; init; }
    public bool IsDefaultEnabled { get; init; } = true;

    public Action? OnActivate { get; set; }
    public Action? OnDeactivate { get; set; }

    public string RamBadgeText => EstimatedRamKb <= 0 ? "0 KB idle" : $"~{EstimatedRamKb} KB";
    public string Title => Name;
    public string Subtitle => Description;
    public string? DockViewKey => DockPinKey;
    public string CategoryLabel => Category.ToString();
    public string RamCostText => RamBadgeText;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusForeground));
                OnPropertyChanged(nameof(ToggleBorderBrush));
                OnPropertyChanged(nameof(ToggleBackground));
                OnPropertyChanged(nameof(CardBorderBrush));
                OnPropertyChanged(nameof(CardOpacity));
            }
        }
    }

    public string StatusText => _isEnabled ? "Active" : "Off (0 RAM)";
    public Brush StatusForeground => _isEnabled ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
    public Brush ToggleBorderBrush => _isEnabled ? ThemeBrushes.GreenBorderActive : ThemeBrushes.BorderSubtle;
    public Brush ToggleBackground => _isEnabled ? ThemeBrushes.PlanSaverActiveBg : Brushes.Transparent;
    public Brush CardBorderBrush => _isEnabled ? ThemeBrushes.GreenBorderActive : ThemeBrushes.CardBorder;
    public double CardOpacity => _isEnabled ? 1.0 : 0.62;

    public string ActionLabel => Id switch
    {
        "jiggle_basket" => "Summon",
        "notes" => "Notes",
        "drop_shelf" => "Shelf",
        "clipboard_pro" => "Picker",
        "quick_calc" => "Calc",
        "voice_memo" => "Record",
        "voice_transcription" => "Transcribe",
        "element_capture" => "Capture",
        "system_hud" => "HUD",
        "peripherals" => "Devices",
        "caffeine" => "Sleep",
        "file_converter" => "Convert",
        "phone_bridge" => "Share",
        "widgets_gallery" => "Widgets",
        "quick_launcher" => "Launch",
        "termi_notch" => "Snippet",
        "window_snap" => "Snap",
        "mech_sounds" => "Sound",
        _ => "Open"
    };

    public string ActionGlyph => Id switch
    {
        "jiggle_basket" => "\uE8B7",
        "notes" => "\uE70B",
        "drop_shelf" => "\uE8F4",
        "clipboard_pro" => "\uE77F",
        "quick_calc" => "\uE8EF",
        "voice_memo" => "\uE720",
        "voice_transcription" => "\uE8D2",
        "element_capture" => "\uE924",
        "system_hud" => "\uE9D9",
        "peripherals" => "\uE702",
        "caffeine" => "\uEC32",
        "file_converter" => "\uEA90",
        "phone_bridge" => "\uE8EA",
        "widgets_gallery" => "\uE74C",
        "quick_launcher" => "\uE71D",
        "termi_notch" => "\uE756",
        "window_snap" => "\uE7C4",
        "mech_sounds" => "\uE765",
        _ => "\uE8A7"
    };

    public string CategoryGlyph => Category switch
    {
        DropletCategory.Productivity => "\uE762",
        DropletCategory.Media => "\uE8D6",
        DropletCategory.System => "\uE770",
        DropletCategory.Capture => "\uE924",
        _ => "\uE71D"
    };

    public string FeatureBadge => Id switch
    {
        "jiggle_basket" => "Floating Basket",
        "file_converter" => "File Tools",
        "element_capture" => "Screen Pin",
        "clipboard_pro" => "Clipboard Queue",
        "caffeine" => "Power Assist",
        "drop_shelf" => "Notch Tray",
        "quick_calc" => "Natural Math",
        "voice_memo" => "Audio WAV",
        "system_hud" => "Vitals Telemetry",
        "peripherals" => "Bluetooth HUD",
        "notes" => "Markdown Scratch",
        "widgets_gallery" => "Bento Dashboard",
        "quick_launcher" => "App Runner",
        "termi_notch" => "Shell Snippets",
        "window_snap" => "Smart Halves",
        "voice_transcription" => "Offline STT",
        "mech_sounds" => "Cherry/Thock",
        "phone_bridge" => "LAN Wi-Fi Share",
        _ => "Modular"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    public void RefreshThemeBindings()
    {
        OnPropertyChanged(nameof(StatusForeground));
        OnPropertyChanged(nameof(ToggleBorderBrush));
        OnPropertyChanged(nameof(ToggleBackground));
        OnPropertyChanged(nameof(CardBorderBrush));
    }
    private void OnPropertyChanged([CallerMemberName] string? prop = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

/// <summary>
/// Manages modular "Droplets" (feature extensions) inspired by macOS Droppy, but with true zero-allocation
/// lifecycle: disabled droplets tear down their background hooks/timers and hide their dock entries so
/// they consume 0 CPU and 0 active RAM.
/// </summary>
public sealed class DropletRegistry
{
    private readonly Dictionary<string, DropletDescriptor> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DropletDescriptor> _ordered = new();

    private readonly Services.SettingsService? _settings;

    public ObservableCollection<DropletDescriptor> Items { get; } = new();

    public event Action<string, bool>? DropletStateChanged;

    public DropletRegistry() : this(null) { }

    internal DropletRegistry(Services.SettingsService? settings)
    {
        _settings = settings;
        RegisterBuiltInDroplets();
        if (_settings != null)
        {
            SyncFromSettings(_settings.Current.EnabledDroplets, invokeLifecycle: false);
            _settings.PropertyChanged += prop =>
            {
                if (prop == nameof(Services.AppSettings.EnabledDroplets))
                {
                    SyncFromSettings(_settings.Current.EnabledDroplets);
                }
            };
        }
    }

    private void RegisterBuiltInDroplets()
    {
        Register(new DropletDescriptor
        {
            Id = "jiggle_basket",
            Name = "Jiggle Floating Basket",
            Description = "Shake cursor while dragging files to summon a floating drop basket anywhere",
            Glyph = "\uE8B7",
            Category = DropletCategory.Productivity,
            EstimatedRamKb = 48,
            IsDefaultEnabled = true
        });

        Register(new DropletDescriptor
        {
            Id = "file_converter",
            Name = "File Actions & PDF",
            Description = "Convert images (PNG/JPG/WebP), images→PDF, merge PDFs, compress video, unzip",
            Glyph = "\uEA90",
            Category = DropletCategory.Productivity,
            TargetView = ExpandedView.Converter,
            DockPinKey = "converter",
            EstimatedRamKb = 0,
            IsDefaultEnabled = true
        });

        Register(new DropletDescriptor
        {
            Id = "element_capture",
            Name = "Element Capture & Pin",
            Description = "Region screenshot with arrow/box/blur/text annotation, instant OCR & pin-to-screen",
            Glyph = "\uE924",
            Category = DropletCategory.Capture,
            TargetView = ExpandedView.CaptureStudio,
            DockPinKey = "capturestudio",
            EstimatedRamKb = 0,
            IsDefaultEnabled = true
        });

        Register(new DropletDescriptor
        {
            Id = "clipboard_pro",
            Name = "Clipboard Picker & Queue",
            Description = "Cursor popup picker (Ctrl+Alt+Shift+V), Plain Text paste & sequential Paste Queue",
            Glyph = "\uE77F",
            Category = DropletCategory.Productivity,
            TargetView = ExpandedView.Clipboard,
            DockPinKey = "clipboard",
            EstimatedRamKb = 64,
            IsDefaultEnabled = true
        });

        Register(new DropletDescriptor
        {
            Id = "caffeine",
            Name = "Caffeine Pro",
            Description = "Prevent sleep for 15m / 1h / ∞ / while on AC / until a target process exits",
            Glyph = "\uEC32",
            Category = DropletCategory.System,
            TargetView = ExpandedView.Battery,
            EstimatedRamKb = 16,
            IsDefaultEnabled = true
        });

        Register(new DropletDescriptor
        {
            Id = "drop_shelf",
            Name = "Notch Drop Shelf",
            Description = "Top-edge file staging tray with thumbnails, batch zip/unzip & pinned history",
            Glyph = "\uE8F4",
            Category = DropletCategory.Productivity,
            TargetView = ExpandedView.Shelf,
            DockPinKey = "shelf",
            EstimatedRamKb = 120,
            IsDefaultEnabled = true
        });

        Register(new DropletDescriptor
        {
            Id = "quick_calc",
            Name = "Quick Calc & Units",
            Description = "Natural-language math, unit/currency/base conversions & 'ans' memory",
            Glyph = "\uE8EF",
            Category = DropletCategory.Productivity,
            TargetView = ExpandedView.Calc,
            DockPinKey = "calc",
            EstimatedRamKb = 12,
            IsDefaultEnabled = true
        });

        Register(new DropletDescriptor
        {
            Id = "voice_memo",
            Name = "Voice Memo Recorder",
            Description = "One-click microphone WAV recorder with live waveform & shelf export",
            Glyph = "\uE720",
            Category = DropletCategory.Media,
            TargetView = ExpandedView.VoiceMemo,
            DockPinKey = "voicememo",
            EstimatedRamKb = 32,
            IsDefaultEnabled = true
        });

        Register(new DropletDescriptor
        {
            Id = "system_hud",
            Name = "System Vitals & RAM Buster",
            Description = "Zero-allocation CPU/RAM telemetry, top process inspector & standby RAM purge",
            Glyph = "\uE9D9",
            Category = DropletCategory.System,
            TargetView = ExpandedView.SystemHud,
            DockPinKey = "systemhud",
            EstimatedRamKb = 96,
            IsDefaultEnabled = true
        });

        Register(new DropletDescriptor
        {
            Id = "peripherals",
            Name = "AirPods & Bluetooth HUD",
            Description = "Live battery levels & connection alerts for headphones, mice & gamepads",
            Glyph = "\uE702",
            Category = DropletCategory.System,
            TargetView = ExpandedView.Peripherals,
            DockPinKey = "peripherals",
            EstimatedRamKb = 64,
            IsDefaultEnabled = true
        });

        Register(new DropletDescriptor
        {
            Id = "notes",
            Name = "Markdown Quick Notes",
            Description = "Fast offline markdown scratchpad, pinning, color tags & Drop Shelf .md export",
            Glyph = "\uE70B",
            Category = DropletCategory.Productivity,
            TargetView = ExpandedView.Notes,
            DockPinKey = "notes",
            EstimatedRamKb = 24,
            IsDefaultEnabled = true
        });

        Register(new DropletDescriptor
        {
            Id = "widgets_gallery",
            Name = "Widgets Gallery",
            Description = "Bento-style live dashboard with system vitals, battery, network and media glanceables",
            Glyph = "\uE74C",
            Category = DropletCategory.System,
            TargetView = ExpandedView.WidgetsGallery,
            DockPinKey = "widgets",
            EstimatedRamKb = 28,
            IsDefaultEnabled = true
        });

        Register(new DropletDescriptor
        {
            Id = "quick_launcher",
            Name = "Quick App & Folder Launcher",
            Description = "1-click app launcher with file drop-to-open capabilities",
            Glyph = "\uE71D",
            Category = DropletCategory.Productivity,
            TargetView = ExpandedView.Launcher,
            DockPinKey = "launcher",
            EstimatedRamKb = 20,
            IsDefaultEnabled = true
        });

        Register(new DropletDescriptor
        {
            Id = "termi_notch",
            Name = "Termi-Notch Snippets",
            Description = "Saved PowerShell/CMD snippets with live streaming output in Dynamic Island",
            Glyph = "\uE756",
            Category = DropletCategory.Productivity,
            TargetView = ExpandedView.Termi,
            DockPinKey = "termi",
            EstimatedRamKb = 32,
            IsDefaultEnabled = true
        });

        Register(new DropletDescriptor
        {
            Id = "window_snap",
            Name = "Window Snap Droplet",
            Description = "Smart multi-monitor window snapping to halves and quarters on drag",
            Glyph = "\uE7C4",
            Category = DropletCategory.Productivity,
            EstimatedRamKb = 16,
            IsDefaultEnabled = true
        });

        Register(new DropletDescriptor
        {
            Id = "voice_transcription",
            Name = "Voice Transcription",
            Description = "Offline speech-to-text recognition for voice memos with 1-click Notes export",
            Glyph = "\uE8D2",
            Category = DropletCategory.Media,
            EstimatedRamKb = 0,
            IsDefaultEnabled = true
        });

        Register(new DropletDescriptor
        {
            Id = "mech_sounds",
            Name = "Mechanical Keyboard Sounds",
            Description = "Tactile Cherry Blue, Brown Thock & Typewriter audio feedback with organic pitch jitter",
            Glyph = "\uE765",
            Category = DropletCategory.System,
            TargetView = ExpandedView.MechSounds,
            DockPinKey = "mechsounds",
            EstimatedRamKb = 40,
            IsDefaultEnabled = false
        });

        Register(new DropletDescriptor
        {
            Id = "phone_bridge",
            Name = "Phone Bridge & LAN Quickshare",
            Description = "Send & receive files and sync clipboard with any iOS/Android phone over local Wi-Fi",
            Glyph = "\uE8EA",
            Category = DropletCategory.Productivity,
            TargetView = ExpandedView.PhoneBridge,
            DockPinKey = "phonebridge",
            EstimatedRamKb = 16,
            IsDefaultEnabled = true
        });
    }

    public void Register(DropletDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _byId[descriptor.Id] = descriptor;
        _ordered.Add(descriptor);
        Items.Add(descriptor);
    }

    public void RegisterOrUpdate(DropletDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (_byId.TryGetValue(descriptor.Id, out var existing))
        {
            int idx = _ordered.IndexOf(existing);
            if (idx >= 0) _ordered[idx] = descriptor;
            int itemIdx = Items.IndexOf(existing);
            if (itemIdx >= 0) Items[itemIdx] = descriptor;
            _byId[descriptor.Id] = descriptor;
        }
        else
        {
            Register(descriptor);
        }
    }

    public bool Unregister(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (_byId.TryGetValue(id.Trim(), out var descriptor))
        {
            _byId.Remove(id.Trim());
            _ordered.Remove(descriptor);
            Items.Remove(descriptor);
            try { descriptor.OnDeactivate?.Invoke(); } catch { }
            return true;
        }
        return false;
    }

    public DropletDescriptor? Get(string id) =>
        _byId.TryGetValue(id, out var d) ? d : null;

    public bool IsEnabled(string id) =>
        _byId.TryGetValue(id, out var d) ? d.IsEnabled : true;

    public IReadOnlyList<DropletDescriptor> All => _ordered;

    public int EnabledCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _ordered.Count; i++)
            {
                if (_ordered[i].IsEnabled) count++;
            }
            return count;
        }
    }

    public int TotalActiveRamKb
    {
        get
        {
            int sum = 0;
            for (int i = 0; i < _ordered.Count; i++)
            {
                var d = _ordered[i];
                if (d.IsEnabled) sum += d.EstimatedRamKb;
            }
            return sum;
        }
    }

    /// <summary>
    /// Synchronizes droplet enabled states from persisted <c>EnabledDroplets</c> list.
    /// If <paramref name="enabledIds"/> is null (fresh installation or reset), all default-enabled droplets are enabled.
    /// If <paramref name="enabledIds"/> is an empty list, all droplets are disabled.
    /// </summary>
    public void SyncFromSettings(ICollection<string>? enabledIds, bool invokeLifecycle = true)
    {
        bool useDefaults = enabledIds == null;
        var set = enabledIds != null
            ? new HashSet<string>(enabledIds, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in _ordered)
        {
            bool shouldEnable = useDefaults ? d.IsDefaultEnabled : set.Contains(d.Id);
            bool changed = d.IsEnabled != shouldEnable;
            d.IsEnabled = shouldEnable;

            if (invokeLifecycle)
            {
                try
                {
                    if (shouldEnable) d.OnActivate?.Invoke();
                    else d.OnDeactivate?.Invoke();
                }
                catch (Exception ex)
                {
                    App.Log($"[DropletRegistry] Lifecycle callback failed for '{d.Id}': {ex.Message}");
                }
            }

            if (changed)
            {
                DropletStateChanged?.Invoke(d.Id, shouldEnable);
            }
        }
    }

    public bool SetState(string id, bool enabled) => SetEnabled(id, enabled);

    public bool SetEnabled(string id, bool enabled)
    {
        if (!_byId.TryGetValue(id, out var d)) return false;
        if (d.IsEnabled == enabled) return d.IsEnabled;

        d.IsEnabled = enabled;
        try
        {
            if (enabled) d.OnActivate?.Invoke();
            else d.OnDeactivate?.Invoke();
        }
        catch (Exception ex)
        {
            App.Log($"[DropletRegistry] SetEnabled callback failed for '{id}': {ex.Message}");
        }

        DropletStateChanged?.Invoke(id, enabled);
        if (_settings != null)
        {
            _settings.Update(s => s.EnabledDroplets = ExportEnabledIds(), nameof(Services.AppSettings.EnabledDroplets));
        }
        return d.IsEnabled;
    }

    public bool Toggle(string id)
    {
        if (!_byId.TryGetValue(id, out var d)) return false;
        return SetEnabled(id, !d.IsEnabled);
    }

    public List<string> ExportEnabledIds() =>
        _ordered.Where(d => d.IsEnabled).Select(d => d.Id).ToList();

    public void SetAll(bool enabled)
    {
        foreach (var d in _ordered)
        {
            if (d.IsEnabled != enabled)
            {
                d.IsEnabled = enabled;
                try
                {
                    if (enabled) d.OnActivate?.Invoke();
                    else d.OnDeactivate?.Invoke();
                }
                catch (Exception ex)
                {
                    App.Log($"[DropletRegistry] SetAll callback failed for '{d.Id}': {ex.Message}");
                }
                DropletStateChanged?.Invoke(d.Id, enabled);
            }
        }

        if (_settings != null)
        {
            _settings.Update(s => s.EnabledDroplets = ExportEnabledIds(), nameof(Services.AppSettings.EnabledDroplets));
        }
    }

    public void ResetToDefaults()
    {
        foreach (var d in _ordered)
        {
            bool shouldEnable = d.IsDefaultEnabled;
            if (d.IsEnabled != shouldEnable)
            {
                d.IsEnabled = shouldEnable;
                try
                {
                    if (shouldEnable) d.OnActivate?.Invoke();
                    else d.OnDeactivate?.Invoke();
                }
                catch (Exception ex)
                {
                    App.Log($"[DropletRegistry] Reset callback failed for '{d.Id}': {ex.Message}");
                }
                DropletStateChanged?.Invoke(d.Id, shouldEnable);
            }
        }

        if (_settings != null)
        {
            _settings.Update(s => s.EnabledDroplets = null, nameof(Services.AppSettings.EnabledDroplets));
        }
    }
}
