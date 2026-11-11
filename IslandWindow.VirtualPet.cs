using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

public partial class IslandWindow
{

    internal void NotchBezelToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool cur = _settingsService.Current.HardwareNotchMode;
        bool next = !cur;
        _settingsService.Update(s => s.HardwareNotchMode = next, nameof(AppSettings.HardwareNotchMode));
        _machine.PushToast(new ToastContent(
            next ? "\uE7F4" : "\uE711",
            "Hardware Notch Mode",
            next ? "Physical notch simulated (bezel ears enabled)" : "Standard floating dynamic island",
            next ? ThemeBrushes.GreenColor : ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "notch"));
    }

    internal void FollowMonitorToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool cur = _settingsService.Current.FollowActiveMonitor;
        bool next = !cur;
        _settingsService.Update(s => s.FollowActiveMonitor = next, nameof(AppSettings.FollowActiveMonitor));
        _machine.PushToast(new ToastContent(
            next ? "\uE7F4" : "\uE711",
            "Follow Active Monitor",
            next ? "Island snaps to monitor with active mouse cursor" : "Island fixed to assigned monitor",
            next ? ThemeBrushes.GreenColor : ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "followmon"));
    }

    private void UpdateNotchBezelUI()
    {
        bool notchOn = _settingsService?.Current?.HardwareNotchMode ?? false;
        if (NotchBezelToggleText != null)
        {
            NotchBezelToggleText.Text = notchOn ? "Notch On" : "Notch Off";
            NotchBezelToggleText.Foreground = notchOn ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
        }
        if (NotchBezelToggleBtn != null)
        {
            NotchBezelToggleBtn.BorderBrush = notchOn
                ? ThemeBrushes.GreenBorderActive
                : Brushes.Transparent;
        }
        if (NotchOffsetSlider != null)
        {
            NotchOffsetSlider.IsEnabled = !notchOn;
            NotchOffsetSlider.Opacity = notchOn ? 0.42 : 1.0;
        }
        if (NotchOffsetText != null)
        {
            NotchOffsetText.Text = notchOn ? "Locked (Notch)" : $"{_settingsService?.Current?.NotchYOffset ?? 0} px";
        }
    }

    private void UpdateFollowMonitorUI()
    {
        bool followOn = _settingsService?.Current?.FollowActiveMonitor ?? false;
        if (FollowMonitorToggleText != null)
        {
            FollowMonitorToggleText.Text = followOn ? "Follow Mon On" : "Follow Mon Off";
            FollowMonitorToggleText.Foreground = followOn ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
        }
        if (FollowMonitorToggleBtn != null)
        {
            FollowMonitorToggleBtn.BorderBrush = followOn
                ? ThemeBrushes.GreenBorderActive
                : Brushes.Transparent;
        }
    }

    private void OnCursorMonitorChanged(IntPtr newMonitor)
    {
        if (!_settingsService.Current.FollowActiveMonitor || newMonitor == IntPtr.Zero) return;

        var monitors = GetAvailableMonitors();
        int targetIdx = monitors.IndexOf(newMonitor);
        string? devName = GetMonitorDeviceName(newMonitor);
        if (targetIdx >= 0 && (targetIdx != _settingsService.Current.MonitorIndex ||
            !string.Equals(devName, _settingsService.Current.MonitorDeviceName, StringComparison.OrdinalIgnoreCase)))
        {
            _cachedTargetMonitor = IntPtr.Zero;
            _settingsService.Update(s =>
            {
                s.MonitorIndex = targetIdx;
                s.MonitorDeviceName = devName;
            }, nameof(AppSettings.MonitorIndex));

            PositionWindow();
            UpdateMonitorUI();
            TriggerLiquidImpulse(80, 25, 8);
        }
    }

    private void OnPetWellnessAlert(WellnessAlert alert)
    {
        _machine.PushToast(new ToastContent(
            alert.Glyph,
            alert.Title,
            alert.Message,
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(5.5),
            TargetView: null,
            Priority: ToastPriority.System));
        UpdatePetSettingsUI();
    }

    private int _petClickStreak;
    private double _lastPetClickSeconds;

    internal void PetCompanion_Click(object sender, MouseButtonEventArgs e)
    {
        if (_petService == null) return;
        string msg = _petService.Pet();
        TriggerLiquidImpulse(180, 80, 12);
        IdlePetCompanion?.PetClickImpulse();
        CompactPetCompanion?.PetClickImpulse();
        SettingsHeroPet?.PetClickImpulse();

        double nowSec = DateTime.UtcNow.TimeOfDay.TotalSeconds;
        if (nowSec - _lastPetClickSeconds > 2.5) _petClickStreak = 0;
        _lastPetClickSeconds = nowSec;
        _petClickStreak++;

        if (_petClickStreak >= 3 || (_petService.AffectionScore % 25 == 0 && _petService.AffectionScore > 25))
        {
            TriggerPetScamper();
            _petClickStreak = 0;
        }
        else
        {
            _machine.PushToast(new ToastContent(
                "\uE76E", // Heart
                _petService.PetName,
                msg,
                ThemeBrushes.PinkColor,
                TimeSpan.FromSeconds(2.5),
                TargetView: null,
                Priority: ToastPriority.Feedback));
        }
        UpdatePetSettingsUI();
        EnsureRendering();
        e.Handled = true;
    }

    private void PetCompanion_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (_petService == null) return;
        e.Handled = true;
        string msg = _petService.PerformTrick();
        TriggerLiquidImpulse(220, -90, 16);
        IdlePetCompanion?.TriggerTrick();
        CompactPetCompanion?.TriggerTrick();
        SettingsHeroPet?.TriggerTrick();
        UpdatePetSettingsUI();
        EnsureRendering();
        _machine.PushToast(new ToastContent(
            "\uE734", // Star / Trick
            $"{_petService.PetName} Flip!",
            msg,
            ThemeBrushes.AmberColor,
            TimeSpan.FromSeconds(2.5),
            TargetView: null,
            Priority: ToastPriority.Feedback,
            Category: "pet"));
    }

    internal void PetToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        _petService?.ToggleEnabled();
        UpdatePetSettingsUI();
        ApplyGeometry();
        EnsureRendering();
        bool on = _petService?.IsEnabled ?? false;
        _machine.PushToast(new ToastContent(
            on ? "\uE708" : "\uE711",
            "Virtual Pet Companion",
            on ? "Companion active in dynamic island" : "Companion hidden",
            on ? ThemeBrushes.GreenColor : ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "pet"));
    }

    internal void PetSpeciesBtn_Click(object sender, RoutedEventArgs e)
    {
        _petService?.CycleSpecies();
        IdlePetCompanion?.PetClickImpulse();
        CompactPetCompanion?.PetClickImpulse();
        SettingsHeroPet?.PetClickImpulse();
        UpdatePetSettingsUI();
        EnsureRendering();
        string species = _petService?.PetSpecies ?? "Cat";
        string icon = FormatSpeciesLabel(species);
        _machine.PushToast(new ToastContent(
            "\uE708",
            "Pet Companion Species",
            $"Switched to {icon}",
            ThemeBrushes.PinkColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "pet"));
    }

    internal void PetAccessoryBtn_Click(object sender, RoutedEventArgs e)
    {
        _petService?.CycleAccessory();
        IdlePetCompanion?.PetClickImpulse();
        CompactPetCompanion?.PetClickImpulse();
        SettingsHeroPet?.PetClickImpulse();
        UpdatePetSettingsUI();
        EnsureRendering();
        string acc = _petService?.PetAccessory ?? "None";
        string label = FormatAccessoryLabel(acc);
        _machine.PushToast(new ToastContent(
            "\uE734",
            "Pet Wardrobe",
            $"Equipped {label} on {_petService?.PetName ?? "Mochi"}",
            ThemeBrushes.PurpleColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "pet"));
    }

    internal void PetNameBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_petService == null) return;
        string newName = _petService.CyclePetName();
        IdlePetCompanion?.PetClickImpulse();
        CompactPetCompanion?.PetClickImpulse();
        SettingsHeroPet?.PetClickImpulse();
        UpdatePetSettingsUI();
        EnsureRendering();
        _machine.PushToast(new ToastContent(
            "\uE70F",
            "Companion Renamed",
            $"Your companion is now named {newName}!",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "pet"));
    }

    internal void PetTreatBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_petService == null) return;
        string msg = _petService.FeedTreat();
        IdlePetCompanion?.PetClickImpulse();
        CompactPetCompanion?.PetClickImpulse();
        SettingsHeroPet?.PetClickImpulse();
        UpdatePetSettingsUI();
        EnsureRendering();
        _machine.PushToast(new ToastContent(
            "\uE76E",
            "Yummy Treat!",
            msg,
            ThemeBrushes.AmberColor,
            TimeSpan.FromSeconds(2.6),
            TargetView: null,
            Priority: ToastPriority.Feedback,
            Category: "pet"));
    }

    internal void PetTrickBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_petService == null) return;
        string msg = _petService.PerformTrick();
        IdlePetCompanion?.TriggerTrick();
        CompactPetCompanion?.TriggerTrick();
        SettingsHeroPet?.TriggerTrick();
        UpdatePetSettingsUI();
        EnsureRendering();
        _machine.PushToast(new ToastContent(
            "\uE734",
            "Acrobatic Trick!",
            msg,
            ThemeBrushes.AmberColor,
            TimeSpan.FromSeconds(2.6),
            TargetView: null,
            Priority: ToastPriority.Feedback,
            Category: "pet"));
    }

    internal void PetInteractBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_petService == null) return;
        string msg = _petService.Pet();
        IdlePetCompanion?.PetClickImpulse();
        CompactPetCompanion?.PetClickImpulse();
        SettingsHeroPet?.PetClickImpulse();
        _machine.PushToast(new ToastContent(
            "\uE76E",
            _petService.PetName,
            msg,
            ThemeBrushes.PinkColor,
            TimeSpan.FromSeconds(3.0),
            TargetView: null,
            Priority: ToastPriority.Feedback));
        UpdatePetSettingsUI();
        EnsureRendering();
    }

    internal void PetHydrateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_petService == null) return;
        string msg = _petService.LogWater();
        _machine.PushToast(new ToastContent(
            "\uE958",
            "Hydration Logged!",
            msg,
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(3.0),
            TargetView: null,
            Priority: ToastPriority.Feedback));
        UpdatePetSettingsUI();
        EnsureRendering();
    }

    internal void PetStretchBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_petService == null) return;
        string msg = _petService.LogStretch();
        _machine.PushToast(new ToastContent(
            "\uE7C9",
            "Stretch Completed!",
            msg,
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(3.0),
            TargetView: null,
            Priority: ToastPriority.Feedback));
        UpdatePetSettingsUI();
        EnsureRendering();
    }

    internal void PetReminderCycleBtn_Click(object sender, RoutedEventArgs e)
    {
        _petService?.CycleReminderInterval();
        UpdatePetSettingsUI();
    }

    private static string FormatSpeciesLabel(string species) => species switch
    {
        "Dog" => "\U0001F436 Dog",
        "Bunny" => "\U0001F430 Bunny",
        "Fox" => "\U0001F98A Fox",
        "Panda" => "\U0001F43C Panda",
        "Dragon" => "\U0001F432 Dragon",
        _ => "\U0001F431 Cat"
    };

    private static string FormatAccessoryLabel(string accessory) => accessory switch
    {
        "Crown" => "\U0001F451 Crown",
        "WizardHat" => "\U0001F9D9 Wizard",
        "Headphones" => "\U0001F3A7 DJ",
        "Sunglasses" => "\U0001F60E Shades",
        "Halo" => "\U0001F607 Halo",
        "Sprout" => "\U0001F331 Sprout",
        _ => "\U0001F3A9 None"
    };

    private static string GetSpeciesTreatIcon(string species) => species switch
    {
        "Dog" => "\U0001F9B4",
        "Bunny" => "\U0001F955",
        "Fox" => "\U0001FAD0",
        "Panda" => "\U0001F38B",
        "Dragon" => "\u2728",
        _ => "\U0001F363"
    };

    private void UpdatePetSettingsUI()
    {
        if (_petService == null) return;
        bool enabled = _petService.IsEnabled;
        string species = _petService.PetSpecies;
        string accessory = _petService.PetAccessory;
        string name = _petService.PetName;
        int affection = _petService.AffectionScore;
        int interval = _petService.ReminderIntervalMinutes;

        if (PetToggleText != null)
        {
            PetToggleText.Text = enabled ? "Pet On" : "Pet Off";
            PetToggleText.Foreground = enabled ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
        }
        if (PetToggleBtn != null)
        {
            PetToggleBtn.BorderBrush = enabled
                ? ThemeBrushes.GreenBorderActive
                : Brushes.Transparent;
        }

        if (PetSpeciesText != null)
        {
            PetSpeciesText.Text = FormatSpeciesLabel(species);
        }

        if (PetAccessoryText != null)
        {
            PetAccessoryText.Text = FormatAccessoryLabel(accessory);
        }

        if (PetNameBtnText != null)
        {
            PetNameBtnText.Text = $"\u270F\uFE0F {name}";
        }

        if (PetStatusSummaryText != null)
        {
            PetStatusSummaryText.Text = $"{name} \u00B7 {affection} \u2764\uFE0F";
        }

        string treatIcon = GetSpeciesTreatIcon(species);
        if (PetTreatText != null)
        {
            PetTreatText.Text = $"{treatIcon} Treat: {_petService.TreatCount}";
        }

        if (PetHydrateText != null)
        {
            PetHydrateText.Text = $"\U0001F4A7 Water: {_petService.WaterCount}";
        }

        if (PetStretchText != null)
        {
            PetStretchText.Text = $"\U0001F9D8 Stretch: {_petService.StretchCount}";
        }

        if (PetReminderCycleText != null)
        {
            PetReminderCycleText.Text = interval > 0 ? $"Remind: {interval}m" : "Remind: Off";
        }

        if (IdlePetCompanion != null)
        {
            IdlePetCompanion.Species = species;
            IdlePetCompanion.Accessory = accessory;
            IdlePetCompanion.Visibility = enabled && _machine.State == IslandState.Idle
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        if (CompactPetCompanion != null)
        {
            CompactPetCompanion.Species = species;
            CompactPetCompanion.Accessory = accessory;
            CompactPetCompanion.Visibility = enabled && _machine.State == IslandState.Compact
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // Hero Showcase Card updates
        if (SettingsHeroPet != null)
        {
            SettingsHeroPet.Species = species;
            SettingsHeroPet.Accessory = accessory;
            SettingsHeroPet.Mood = _petService.CurrentMood;
            SettingsHeroPet.PetName = name;
            SettingsHeroPet.Affection = affection;
        }

        if (SettingsHeroPetName != null)
        {
            SettingsHeroPetName.Text = name;
        }

        if (SettingsHeroPetRank != null || SettingsHeroPetScore != null || SettingsHeroPetProgressBar != null)
        {
            string rankTitle;
            int prevThresh, nextThresh;
            if (affection < 50)
            {
                rankTitle = "Level 1 \u00B7 Curious Stray";
                prevThresh = 0;
                nextThresh = 50;
            }
            else if (affection < 150)
            {
                rankTitle = "Level 2 \u00B7 Playful Friend";
                prevThresh = 50;
                nextThresh = 150;
            }
            else if (affection < 300)
            {
                rankTitle = "Level 3 \u00B7 Devoted Bestie";
                prevThresh = 150;
                nextThresh = 300;
            }
            else if (affection < 500)
            {
                rankTitle = "Level 4 \u00B7 Soulmate Companion";
                prevThresh = 300;
                nextThresh = 500;
            }
            else if (affection < 1000)
            {
                rankTitle = "Level 5 \u00B7 Island Legend";
                prevThresh = 500;
                nextThresh = 1000;
            }
            else
            {
                rankTitle = "Level 6 \u00B7 Mythic Guardian";
                prevThresh = 1000;
                nextThresh = 2000;
            }

            if (SettingsHeroPetRank != null) SettingsHeroPetRank.Text = rankTitle;
            if (SettingsHeroPetScore != null) SettingsHeroPetScore.Text = $"{affection} / {nextThresh} \u2764";
            if (SettingsHeroPetProgressBar != null)
            {
                double frac = Math.Clamp((double)(affection - prevThresh) / Math.Max(1, nextThresh - prevThresh), 0.05, 1.0);
                SettingsHeroPetProgressBar.Width = frac * 110.0;
            }
        }

        if (SettingsHeroPetWellnessMini != null)
        {
            SettingsHeroPetWellnessMini.Text = $"{treatIcon} {_petService.TreatCount}  \U0001F4A7 {_petService.WaterCount}  \U0001F9D8 {_petService.StretchCount}";
        }

        if (SettingsHeroPetSub != null)
        {
            string speciesDesc = species switch
            {
                "Dog" => "Loyal Shiba Inu Companion",
                "Bunny" => "Gentle Marshmallow Bunny",
                "Fox" => "Mystical Kitsune Spirit Fox",
                "Panda" => "Cozy Bamboo Giant Panda",
                "Dragon" => "Celestial Baby Sky Dragon",
                _ => "Playful Ginger Tabby Cat"
            };
            SettingsHeroPetSub.Text = accessory != "None"
                ? $"{speciesDesc} \u00B7 Wearing {FormatAccessoryLabel(accessory)}"
                : speciesDesc;
        }
    }
}
