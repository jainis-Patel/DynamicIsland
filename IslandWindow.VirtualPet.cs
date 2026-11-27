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
using DynamicIsland.Views;
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

    internal void FloatingPiPToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleFloatingMode();
    }

    internal void NotchPresetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string preset })
        {
            string normalized = SettingsService.NormalizeHardwareNotchPreset(preset);
            var (w, h, r) = SettingsService.GetNotchPresetDimensions(normalized);
            _settingsService.Update(s =>
            {
                s.HardwareNotchPreset = normalized;
                s.HardwareNotchWidth = w;
                s.HardwareNotchHeight = h;
                s.HardwareNotchRadius = r;
            }, nameof(AppSettings.HardwareNotchPreset));

            _machine.PushToast(new ToastContent(
                "\uE7F4",
                "Notch Preset",
                $"{normalized} ({w:0} \u00D7 {h:0} px)",
                ThemeBrushes.BlueColor,
                TimeSpan.FromSeconds(1.8),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "notch"));
        }
    }

    private void UpdateNotchBezelUI()
    {
        bool notchOn = _settingsService?.Current?.HardwareNotchMode ?? false;
        bool isDetached = _isDetachedPiP || (_settingsService?.Current.IsDetachedFloating ?? false);
        var dp = SettingsContainer?.DisplayPowerPanel;
        if (dp == null) return;

        if (dp.NotchBezelToggleSwitch != null)
            dp.NotchBezelToggleSwitch.IsChecked = notchOn;

        if (dp.FloatingPiPToggleSwitch != null)
            dp.FloatingPiPToggleSwitch.IsChecked = isDetached;

        if (dp.NotchPresetsGroup != null)
            dp.NotchPresetsGroup.Visibility = notchOn ? Visibility.Visible : Visibility.Collapsed;

        UpdateNotchPresetUI();

        if (dp.NotchOffsetSlider != null)
        {
            dp.NotchOffsetSlider.IsEnabled = !notchOn && !isDetached;
            dp.NotchOffsetSlider.Opacity = (notchOn || isDetached) ? 0.42 : 1.0;
        }
        if (dp.NotchOffsetText != null)
        {
            dp.NotchOffsetText.Text = isDetached ? "PiP Free" : (notchOn ? "Locked (Notch)" : $"{_settingsService?.Current?.NotchYOffset ?? 0} px");
        }
    }

    private void UpdateNotchPresetUI()
    {
        var dp = SettingsContainer?.DisplayPowerPanel;
        if (dp?.NotchPresetMBP16Btn == null) return;

        string current = SettingsService.NormalizeHardwareNotchPreset(_settingsService?.Current?.HardwareNotchPreset);
        var accent = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var defaultBg = (Brush)FindResource("CardSurfaceBrush");

        StyleAccentToggleButton(dp.NotchPresetMBP16Btn, current == "MacBookPro16", accent, defaultBg, defaultBorder, dp.NotchPresetMBP16Text);
        StyleAccentToggleButton(dp.NotchPresetMBP14Btn, current == "MacBookPro14", accent, defaultBg, defaultBorder, dp.NotchPresetMBP14Text);
        StyleAccentToggleButton(dp.NotchPresetAirBtn, current == "MacBookAir", accent, defaultBg, defaultBorder, dp.NotchPresetAirText);
        StyleAccentToggleButton(dp.NotchPresetXpsBtn, current == "DellXps", accent, defaultBg, defaultBorder, dp.NotchPresetXpsText);
        StyleAccentToggleButton(dp.NotchPresetCustomBtn, current == "Custom", accent, defaultBg, defaultBorder, dp.NotchPresetCustomText);
    }

    private void UpdateFollowMonitorUI()
    {
        bool followOn = _settingsService?.Current?.FollowActiveMonitor ?? false;
        var dp = SettingsContainer?.DisplayPowerPanel;
        if (dp?.FollowMonitorToggleSwitch != null)
        {
            dp.FollowMonitorToggleSwitch.IsChecked = followOn;
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

        if (e.ClickCount >= 2)
        {
            _machine.ExpandNow();
            SwitchExpandedView(ExpandedView.Pet, true);
            e.Handled = true;
            return;
        }

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
        UpdatePetSettingsUI();
        EnsureRendering();
        e.Handled = true;
    }

    private void PetCompanion_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (_petService == null) return;
        e.Handled = true;
        _petService.PerformTrick();
        TriggerLiquidImpulse(220, -90, 16);
        IdlePetCompanion?.TriggerTrick();
        CompactPetCompanion?.TriggerTrick();
        SettingsHeroPet?.TriggerTrick();
        UpdatePetSettingsUI();
        EnsureRendering();
    }

    private PetFloatWindow? _petFloatWindow;

    internal void TogglePetFloatWindow()
    {
        if (_petService == null) return;

        if (_petFloatWindow != null && _petFloatWindow.IsLoaded)
        {
            _petFloatWindow.Close();
            _petFloatWindow = null;
            _machine.PushToast(new ToastContent(
                "\uE708",
                "Desktop Roam Ended",
                $"{_petService.PetName} returned to Dynamic Island capsule",
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "pet",
                Priority: ToastPriority.Feedback));
            return;
        }

        _petFloatWindow = new PetFloatWindow(_petService, this);
        _petFloatWindow.Closed += (_, _) => _petFloatWindow = null;
        _petFloatWindow.Show();

        _machine.PushToast(new ToastContent(
            "\uE7C4",
            "Desktop Roam Active",
            $"{_petService.PetName} is now roaming your desktop! Drag anywhere.",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(2.5),
            TargetView: null,
            Priority: ToastPriority.Feedback));
    }

    internal void OpenPetPlaygroundBtn_Click(object sender, RoutedEventArgs e)
    {
        _machine.ExpandNow();
        SwitchExpandedView(ExpandedView.Pet, true);
    }

    internal void RoamDesktopBtn_Click(object sender, RoutedEventArgs e)
    {
        TogglePetFloatWindow();
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

    internal void PetSpeciesPickBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string species } || _petService == null) return;
        _petService.SetSpecies(species);
        IdlePetCompanion?.PetClickImpulse();
        CompactPetCompanion?.PetClickImpulse();
        SettingsHeroPet?.PetClickImpulse();
        UpdatePetSettingsUI();
        EnsureRendering();
        _machine.PushToast(new ToastContent(
            "\uE708",
            "Pet Companion Species",
            $"Switched to {FormatSpeciesLabel(_petService.PetSpecies)}",
            ThemeBrushes.PinkColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "pet"));
    }

    internal void PetRenderStylePickBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string style } || _petService == null) return;
        _petService.SetRenderStyle(style);
        IdlePetCompanion?.PetClickImpulse();
        CompactPetCompanion?.PetClickImpulse();
        SettingsHeroPet?.PetClickImpulse();
        _petFloatWindow?.RefreshUI();
        PetSanctuaryContainer?.RefreshUI();
        UpdatePetSettingsUI();
        EnsureRendering();
        bool is3D = string.Equals(_petService.PetRenderStyle, "Realistic3D", StringComparison.OrdinalIgnoreCase);
        _machine.PushToast(new ToastContent(
            "\uE708",
            "Pet Visual Style",
            is3D ? "Switched to 3D Realistic" : "Switched to 2D Classic (Backup)",
            ThemeBrushes.AccentColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "pet"));
    }

    internal void PetAccessoryPickBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string accessory } || _petService == null) return;
        _petService.SetAccessory(accessory);
        IdlePetCompanion?.PetClickImpulse();
        CompactPetCompanion?.PetClickImpulse();
        SettingsHeroPet?.PetClickImpulse();
        UpdatePetSettingsUI();
        EnsureRendering();
        _machine.PushToast(new ToastContent(
            "\uE734",
            "Pet Wardrobe",
            $"Equipped {FormatAccessoryLabel(_petService.PetAccessory)} on {_petService.PetName}",
            ThemeBrushes.PurpleColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "pet"));
    }

    internal void PetCustomNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            CommitCustomPetName(tb.Text);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    internal void PetCustomNameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            CommitCustomPetName(tb.Text);
        }
    }

    private void CommitCustomPetName(string? rawName)
    {
        if (_petService == null) return;
        string clean = SettingsService.SanitizePetName(rawName);
        if (string.Equals(clean, _petService.PetName, StringComparison.Ordinal)) return;

        string saved = _petService.SetPetName(clean);
        IdlePetCompanion?.PetClickImpulse();
        CompactPetCompanion?.PetClickImpulse();
        SettingsHeroPet?.PetClickImpulse();
        UpdatePetSettingsUI();
        EnsureRendering();
        _machine.PushToast(new ToastContent(
            "\uE70F",
            "Companion Renamed",
            $"Your companion is now named {saved}!",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "pet"));
    }

    internal void PetAutoRoamToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        _petService?.ToggleAutoRoam();
        UpdatePetSettingsUI();
    }

    internal void PetContextToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        _petService?.ToggleContextReactions();
        UpdatePetSettingsUI();
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
        _petService.Pet();
        IdlePetCompanion?.PetClickImpulse();
        CompactPetCompanion?.PetClickImpulse();
        SettingsHeroPet?.PetClickImpulse();
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
        "Dog" => "Dog",
        "Bunny" => "Bunny",
        "Fox" => "Fox",
        "Panda" => "Panda",
        "Dragon" => "Dragon",
        _ => "Cat"
    };

    private static string FormatAccessoryLabel(string accessory) => accessory switch
    {
        "Crown" => "Crown",
        "WizardHat" => "Wizard",
        "Headphones" => "DJ",
        "Sunglasses" => "Shades",
        "Halo" => "Halo",
        "Sprout" => "Sprout",
        _ => "None"
    };

    private void UpdatePetSettingsUI()
    {
        if (_petService == null) return;
        bool enabled = _petService.IsEnabled;
        string species = _petService.PetSpecies;
        string accessory = _petService.PetAccessory;
        string renderStyle = _petService.PetRenderStyle;
        string name = _petService.PetName;
        int affection = _petService.AffectionScore;
        int interval = _petService.ReminderIntervalMinutes;

        var pp = SettingsContainer?.PetPanel;
        if (pp != null)
        {
            if (pp.PetToggleSwitch != null)
                pp.PetToggleSwitch.IsChecked = enabled;

            if (pp.PetAutoRoamToggleSwitch != null)
                pp.PetAutoRoamToggleSwitch.IsChecked = _petService.AutoRoamEnabled;

            if (pp.PetContextToggleSwitch != null)
                pp.PetContextToggleSwitch.IsChecked = _petService.ContextReactionsEnabled;

            if (pp.PetCustomNameBox != null && !pp.PetCustomNameBox.IsKeyboardFocusWithin)
                pp.PetCustomNameBox.Text = name;

            if (pp.PetNameBtnText != null)
                pp.PetNameBtnText.Text = "🎲";

            if (pp.PetSettingsStreakBadge != null && pp.PetSettingsStreakText != null)
            {
                int streak = _petService.CareStreakDays;
                pp.PetSettingsStreakBadge.Visibility = streak >= 1 ? Visibility.Visible : Visibility.Collapsed;
                pp.PetSettingsStreakText.Text = $"🔥 {streak}d";
            }

            if (pp.PetSpeciesCatBtn != null)
            {
                var accent = (Brush)Resources["AccentBrush"];
                var defaultBorder = (Brush)FindResource("CardBorderBrush");
                var activeBg = ThemeBrushes.PillToggleActiveBg;
                var defaultBg = (Brush)FindResource("CardSurfaceBrush");

                StyleCardOptionButton(pp.PetStyle3DBtn, renderStyle == "Realistic3D", accent, activeBg, defaultBg, defaultBorder);
                StyleCardOptionButton(pp.PetStyle2DBtn, renderStyle == "Classic2D", accent, activeBg, defaultBg, defaultBorder);

                StyleCardOptionButton(pp.PetSpeciesCatBtn, species == "Cat", accent, activeBg, defaultBg, defaultBorder);
                StyleCardOptionButton(pp.PetSpeciesDogBtn, species == "Dog", accent, activeBg, defaultBg, defaultBorder);
                StyleCardOptionButton(pp.PetSpeciesBunnyBtn, species == "Bunny", accent, activeBg, defaultBg, defaultBorder);
                StyleCardOptionButton(pp.PetSpeciesFoxBtn, species == "Fox", accent, activeBg, defaultBg, defaultBorder);
                StyleCardOptionButton(pp.PetSpeciesPandaBtn, species == "Panda", accent, activeBg, defaultBg, defaultBorder);
                StyleCardOptionButton(pp.PetSpeciesDragonBtn, species == "Dragon", accent, activeBg, defaultBg, defaultBorder);

                StyleCardOptionButton(pp.PetAccNoneBtn, accessory == "None", accent, activeBg, defaultBg, defaultBorder);
                StyleCardOptionButton(pp.PetAccCrownBtn, accessory == "Crown", accent, activeBg, defaultBg, defaultBorder);
                StyleCardOptionButton(pp.PetAccWizardBtn, accessory == "WizardHat", accent, activeBg, defaultBg, defaultBorder);
                StyleCardOptionButton(pp.PetAccHeadphonesBtn, accessory == "Headphones", accent, activeBg, defaultBg, defaultBorder);
                StyleCardOptionButton(pp.PetAccSunglassesBtn, accessory == "Sunglasses", accent, activeBg, defaultBg, defaultBorder);
                StyleCardOptionButton(pp.PetAccHaloBtn, accessory == "Halo", accent, activeBg, defaultBg, defaultBorder);
                StyleCardOptionButton(pp.PetAccSproutBtn, accessory == "Sprout", accent, activeBg, defaultBg, defaultBorder);
            }

            if (pp.PetStatusSummaryText != null)
                pp.PetStatusSummaryText.Text = $"{name} \u00B7 {affection} XP";

            if (pp.PetTreatText != null)
                pp.PetTreatText.Text = $"Treat: {_petService.TreatCount}";

            if (pp.PetHydrateText != null)
                pp.PetHydrateText.Text = $"Water: {_petService.WaterCount}";

            if (pp.PetStretchText != null)
                pp.PetStretchText.Text = $"Stretch: {_petService.StretchCount}";

            if (pp.PetReminderCycleText != null)
                pp.PetReminderCycleText.Text = interval > 0 ? $"Remind: {interval}m" : "Remind: Off";
        }

        if (IdlePetCompanion != null)
        {
            IdlePetCompanion.RenderStyle = renderStyle;
            IdlePetCompanion.Species = species;
            IdlePetCompanion.Accessory = accessory;
            IdlePetCompanion.Visibility = enabled && _machine.State == IslandState.Idle
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        if (CompactPetCompanion != null)
        {
            CompactPetCompanion.RenderStyle = renderStyle;
            CompactPetCompanion.Species = species;
            CompactPetCompanion.Accessory = accessory;
            CompactPetCompanion.Visibility = enabled && _machine.State == IslandState.Compact
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // Hero Showcase Card updates
        if (pp?.SettingsHeroPet != null)
        {
            pp.SettingsHeroPet.RenderStyle = renderStyle;
            pp.SettingsHeroPet.Species = species;
            pp.SettingsHeroPet.Accessory = accessory;
            pp.SettingsHeroPet.Mood = _petService.CurrentMood;
            pp.SettingsHeroPet.PetName = name;
            pp.SettingsHeroPet.Affection = affection;
        }

        if (pp?.SettingsHeroPetName != null)
            pp.SettingsHeroPetName.Text = name;

        if (pp != null && (pp.SettingsHeroPetRank != null || pp.SettingsHeroPetScore != null || pp.SettingsHeroPetProgressBar != null))
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

            if (pp.SettingsHeroPetRank != null) pp.SettingsHeroPetRank.Text = rankTitle;
            if (pp.SettingsHeroPetScore != null) pp.SettingsHeroPetScore.Text = $"{affection} / {nextThresh} XP";
            if (pp.SettingsHeroPetProgressBar != null)
            {
                double frac = Math.Clamp((double)(affection - prevThresh) / Math.Max(1, nextThresh - prevThresh), 0.05, 1.0);
                pp.SettingsHeroPetProgressBar.Width = frac * 110.0;
            }
        }

        if (pp?.SettingsHeroPetWellnessMini != null)
        {
            pp.SettingsHeroPetWellnessMini.Text = $"Treat {_petService.TreatCount} \u00B7 Water {_petService.WaterCount} \u00B7 Stretch {_petService.StretchCount}";
        }

        if (pp?.SettingsHeroPetSub != null)
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
            pp.SettingsHeroPetSub.Text = accessory != "None"
                ? $"{speciesDesc} \u00B7 Wearing {FormatAccessoryLabel(accessory)}"
                : speciesDesc;
        }
    }
}
