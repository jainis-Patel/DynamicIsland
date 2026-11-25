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
    private void UpdateHaloUI()
    {
        var ap = SettingsContainer?.AppearancePanel;
        if (ap?.HaloToggleSwitch != null)
        {
            ap.HaloToggleSwitch.IsChecked = _settingsService.Current.EnableAmbientGlow;
        }
    }

    internal void ApplyEdgeBorder(bool showBorder)
    {
        if (PillStroke != null)
        {
            PillStroke.Visibility = showBorder ? Visibility.Visible : Visibility.Collapsed;
            PillStroke.BorderThickness = showBorder ? new Thickness(1, 0, 1, 1) : new Thickness(0);
        }
        if (FloatingDockHost != null)
        {
            FloatingDockHost.BorderThickness = showBorder ? new Thickness(1) : new Thickness(0);
        }
        if (SecondaryDroplet != null)
        {
            SecondaryDroplet.BorderThickness = showBorder ? new Thickness(1, 0, 1, 1) : new Thickness(0);
        }
    }

    private void UpdateEdgeBorderUI()
    {
        if (_settingsService == null) return;
        bool enabled = _settingsService.Current.ShowEdgeBorder;
        var ap = SettingsContainer?.AppearancePanel;
        if (ap?.EdgeBorderToggleSwitch != null)
        {
            ap.EdgeBorderToggleSwitch.IsChecked = enabled;
        }
        if (ap?.EdgeBorderSubtext != null)
        {
            ap.EdgeBorderSubtext.Text = enabled ? "Subtle edge outline visible around island & dock" : "Edge border hidden for seamless blend";
        }
    }

    private void UpdateSecondaryDropletUI()
    {
        if (_settingsService == null) return;
        bool enabled = _settingsService.Current.EnableSecondaryDroplet;
        var cp = SettingsContainer?.ClockAudioPanel;
        if (cp?.SecondaryDropletToggleSwitch != null)
        {
            cp.SecondaryDropletToggleSwitch.IsChecked = enabled;
        }
        if (cp?.SecondaryDropletSubtext != null)
        {
            cp.SecondaryDropletSubtext.Text = enabled ? "Detached circular bubble when two activities run at once" : "Single unified capsule only (detached bubble off)";
        }
    }

    private void UpdateDndUI()
    {
        bool dnd = _settingsService.Current.DoNotDisturb;
        var np = SettingsContainer?.NotificationsPanel;
        if (np?.SettingsDndToggleSwitch != null)
        {
            np.SettingsDndToggleSwitch.IsChecked = dnd;
        }
        UpdateNotificationsDndUI();
        UpdateNotificationsEmptyStateOnDndChange();
    }

    private void UpdateBannerModeUI()
    {
        var np = SettingsContainer?.NotificationsPanel;
        if (np?.ReplaceBannersToggleSwitch != null)
        {
            np.ReplaceBannersToggleSwitch.IsChecked = _settingsService.Current.ReplaceWindowsBanners;
        }
    }

    private void UpdateMotionModeUI()
    {
        var ap = SettingsContainer?.AppearancePanel;
        if (ap?.MotionModeText == null) return;
        bool reduced = _settingsService.Current.ReduceMotion;
        ap.MotionModeText.Text = reduced ? "Reduced" : "Liquid";
        ap.MotionModeText.Foreground = reduced ? ThemeBrushes.Amber : ThemeBrushes.Green;
    }

    private void UpdateStartupUI()
    {
        var su = SettingsContainer?.SystemUpdatesPanel;
        if (su?.StartupToggleSwitch != null)
        {
            su.StartupToggleSwitch.IsChecked = StartupService.IsStartupEnabled();
        }
    }

    private void UpdateMonitorUI()
    {
        var dp = SettingsContainer?.DisplayPowerPanel;
        if (dp?.MonitorSwitchText == null) return;
        var monitors = GetAvailableMonitors();
        int idx = Math.Clamp(_settingsService.Current.MonitorIndex, 0, Math.Max(0, monitors.Count - 1));
        dp.MonitorSwitchText.Text = $"Screen {idx + 1}/{Math.Max(1, monitors.Count)}";

        if (dp.MonitorDetailsText != null)
        {
            var topo = DisplayTopologyService.QueryConnectedMonitors(GetTargetMonitor());
            if (idx >= 0 && idx < topo.Count)
            {
                var mon = topo[idx];
                dp.MonitorDetailsText.Text = $"{mon.FriendlyName}: {mon.SummaryText}{(mon.IsPrimary ? " \u2022 Primary" : "")}";
            }
            else
            {
                dp.MonitorDetailsText.Text = $"Display {idx + 1}: Active \u2022 Island Docked";
            }
        }
    }

    internal void MonitorSwitchBtn_Click(object sender, RoutedEventArgs e)
    {
        SwitchToNextMonitor();
    }

    internal void BannerModeBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.ReplaceWindowsBanners;
        _settingsService.Update(s => s.ReplaceWindowsBanners = next, nameof(AppSettings.ReplaceWindowsBanners));
        _machine.PushToast(new ToastContent(
            "\uE7E7",
            next ? "Island-Only Notifications" : "Island + Windows Banners",
            next ? "Native Windows popups dismissed after island toast" : "Native Windows popups remain in Action Center",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(1.8),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "banners"));
    }

    internal void MotionModeBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.ReduceMotion;
        _settingsService.Update(s => s.ReduceMotion = next, nameof(AppSettings.ReduceMotion));
        if (!next)
        {
            TriggerLiquidImpulse(320, -140, 35);
        }
    }

    internal void StartupToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !StartupService.IsStartupEnabled();
        StartupService.SetStartup(next);
        UpdateStartupUI();
        _machine.PushToast(new ToastContent(
            next ? "\uE7E8" : "\uE711",
            "Windows Startup",
            next ? "Dynamic Island will launch at sign-in" : "Launch at sign-in disabled",
            next ? ThemeBrushes.GreenColor : ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "startup"));
    }

    private void UpdateVolumeOsdUI()
    {
        var sp = SettingsContainer?.ShortcutsPanel;
        if (sp == null) return;
        if (sp.VolumeOsdToggleSwitch != null)
            sp.VolumeOsdToggleSwitch.IsChecked = _settingsService.Current.EnableVolumeKeyOsd;
        if (sp.LockKeyHudToggleSwitch != null)
            sp.LockKeyHudToggleSwitch.IsChecked = _settingsService.Current.EnableLockKeyHud;
        if (sp.KeyboardLayoutHudToggleSwitch != null)
            sp.KeyboardLayoutHudToggleSwitch.IsChecked = _settingsService.Current.EnableKeyboardLayoutHud;
        if (sp.DevHotkeysToggleSwitch != null)
            sp.DevHotkeysToggleSwitch.IsChecked = _settingsService.Current.EnableDeveloperHotkeys;
    }

    internal void VolumeOsdBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.EnableVolumeKeyOsd;
        _settingsService.Update(s => s.EnableVolumeKeyOsd = next, nameof(AppSettings.EnableVolumeKeyOsd));
        _machine.PushToast(new ToastContent(
            next ? "\uE767" : "\uE74F",
            "Volume Keys OSD",
            next ? "Hardware volume keys trigger island toast" : "Standard Windows volume OSD restored",
            next ? ThemeBrushes.GreenColor : ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "volhook"));
    }

    internal void LockKeyHudToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.EnableLockKeyHud;
        _settingsService.Update(s => s.EnableLockKeyHud = next, nameof(AppSettings.EnableLockKeyHud));
        _machine.PushToast(new ToastContent(
            "\uE765",
            "Lock Key HUD",
            next ? "Caps, Num & Scroll Lock toasts enabled" : "Lock key toasts disabled",
            next ? ThemeBrushes.GreenColor : ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(1.8),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "lockhud"));
    }

    internal void KeyboardLayoutHudToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.EnableKeyboardLayoutHud;
        _settingsService.Update(s => s.EnableKeyboardLayoutHud = next, nameof(AppSettings.EnableKeyboardLayoutHud));
        _machine.PushToast(new ToastContent(
            "\uE775",
            "Keyboard Layout HUD",
            next ? "Input language switch indicator enabled" : "Input language switch indicator disabled",
            next ? ThemeBrushes.GreenColor : ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(1.8),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "layouthud"));
    }

    internal void DevHotkeysToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.EnableDeveloperHotkeys;
        _settingsService.Update(s => s.EnableDeveloperHotkeys = next, nameof(AppSettings.EnableDeveloperHotkeys));
        _machine.PushToast(new ToastContent(
            "\uEC7A",
            "Developer Hotkeys",
            next ? "Ctrl+Alt+T test toast shortcut active" : "Developer hotkeys disabled",
            next ? ThemeBrushes.GreenColor : ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(1.8),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "devhotkeys"));
    }

    internal void LowMemoryModeToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.LowMemoryMode;
        _settingsService.Update(s => s.LowMemoryMode = next, nameof(AppSettings.LowMemoryMode));
        _machine.PushToast(new ToastContent(
            "\uE945",
            "Low Memory Mode",
            next ? "Software rendering will apply on next launch" : "Hardware GPU rendering enabled on launch",
            next ? ThemeBrushes.AmberColor : ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(2.2),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "lowmem"));
    }

    internal void LottieAnimToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.ShowLottieAnimation;
        _settingsService.Update(s => s.ShowLottieAnimation = next, nameof(AppSettings.ShowLottieAnimation));
        _machine.PushToast(new ToastContent(
            "\uE790",
            "Vector Animations",
            next ? "Decorative animations enabled" : "Decorative animations disabled",
            next ? ThemeBrushes.GreenColor : ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(1.8),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "lottie"));
    }

    internal void WeatherCityBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitWeatherCity();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    internal void WeatherCityBox_LostFocus(object sender, RoutedEventArgs e) => CommitWeatherCity();

    private void CommitWeatherCity()
    {
        var box = SettingsContainer?.SystemUpdatesPanel?.WeatherCityBox;
        if (box == null) return;
        string? city = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
        if (!string.Equals(_settingsService.Current.WeatherCityOverride, city, StringComparison.Ordinal))
        {
            _settingsService.Update(s => s.WeatherCityOverride = city, nameof(AppSettings.WeatherCityOverride));
            _machine.PushToast(new ToastContent(
                "\uE753",
                "Weather Location",
                string.IsNullOrEmpty(city) ? "Using automatic IP geolocation" : $"Weather locked to {city}",
                ThemeBrushes.BlueColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "weather"));
        }
    }

    private void UpdateSystemSettingsUI()
    {
        var su = SettingsContainer?.SystemUpdatesPanel;
        if (su == null) return;
        var cur = _settingsService.Current;
        if (su.LowMemoryModeToggleSwitch != null) su.LowMemoryModeToggleSwitch.IsChecked = cur.LowMemoryMode;
        if (su.LottieAnimToggleSwitch != null) su.LottieAnimToggleSwitch.IsChecked = cur.ShowLottieAnimation;
        if (su.WeatherCityBox != null && !su.WeatherCityBox.IsFocused && !su.WeatherCityBox.IsKeyboardFocused)
        {
            string city = cur.WeatherCityOverride ?? "";
            if (!string.Equals(su.WeatherCityBox.Text, city, StringComparison.Ordinal))
            {
                su.WeatherCityBox.Text = city;
            }
        }
    }

    internal void HotkeyRecordBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string action })
        {
            if (_recordingHotkeyAction == action)
            {
                _recordingHotkeyAction = null;
            }
            else
            {
                _recordingHotkeyAction = action;
            }
            UpdateHotkeysUI();
        }
    }

    private void UpdateHotkeysUI()
    {
        var sp = SettingsContainer?.ShortcutsPanel;
        if (sp?.HotkeyBindClipboardText == null) return;
        var custom = _settingsService.Current.CustomHotkeys;

        void ApplyKeycap(string action, string label, uint defaultVk, Button? btn, TextBlock? text)
        {
            if (text == null) return;
            if (_recordingHotkeyAction == action)
            {
                text.Text = "Press key...";
                if (btn != null)
                {
                    btn.BorderBrush = (Brush)Resources["AccentBrush"];
                    btn.ToolTip = "Press a key to bind, Backspace/Delete to clear, or Esc to cancel";
                }
                return;
            }

            uint vk = (custom != null && custom.TryGetValue(action, out uint c)) ? c : defaultVk;
            if (vk == 0)
            {
                text.Text = $"{label}: Off";
                if (btn != null)
                {
                    btn.BorderBrush = (Brush)FindResource("IslandButtonBorderBrush");
                    btn.ToolTip = $"{action} shortcut disabled · Click to bind";
                }
                return;
            }

            Key k = KeyInterop.KeyFromVirtualKey((int)vk);
            bool conflict = _hotkeyRegistrationStatus.TryGetValue(action, out bool ok) && !ok;
            text.Text = conflict ? $"{label}: {k} (!)" : $"{label}: {k}";
            if (btn != null)
            {
                btn.BorderBrush = conflict ? ThemeBrushes.Amber : (Brush)FindResource("IslandButtonBorderBrush");
                btn.ToolTip = conflict
                    ? $"Ctrl+Alt+{k} could not be registered (already in use by Windows or another app)"
                    : $"Ctrl+Alt+{k} · Click to rebind or Backspace to clear";
            }
        }

        ApplyKeycap("Clipboard", "Clip", 0x56, sp.HotkeyBindClipboardBtn, sp.HotkeyBindClipboardText);
        ApplyKeycap("Calculator", "Calc", 0x43, sp.HotkeyBindCalcBtn, sp.HotkeyBindCalcText);
        ApplyKeycap("AudioDevices", "Audio", 0x4F, sp.HotkeyBindAudioBtn, sp.HotkeyBindAudioText);
        ApplyKeycap("Network", "Wi-Fi", 0x57, sp.HotkeyBindWifiBtn, sp.HotkeyBindWifiText);
        ApplyKeycap("Stopwatch", "Timer", 0x41, sp.HotkeyBindTimerBtn, sp.HotkeyBindTimerText);
        ApplyKeycap("Notifications", "Alerts", 0x4E, sp.HotkeyBindNotifBtn, sp.HotkeyBindNotifText);
        ApplyKeycap("SystemHud", "HUD", 0x48, sp.HotkeyBindHudBtn, sp.HotkeyBindHudText);
        ApplyKeycap("VoiceMemo", "Memo", 0x52, sp.HotkeyBindMemoBtn, sp.HotkeyBindMemoText);
        ApplyKeycap("Peripherals", "BT", 0x44, sp.HotkeyBindDevicesBtn, sp.HotkeyBindDevicesText);
        ApplyKeycap("BatteryPower", "Power", 0x50, sp.HotkeyBindPowerBtn, sp.HotkeyBindPowerText);
        ApplyKeycap("Settings", "Cfg", 0x53, sp.HotkeyBindSettingsBtn, sp.HotkeyBindSettingsText);
        UpdateVolumeOsdUI();
    }

    internal void SettingsTrimRamBtn_Click(object sender, RoutedEventArgs e)
    {
        long beforeMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
        Core.IslandMemoryManager.ExecuteConservativeTrim();
        long afterMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
        long freed = Math.Max(0, beforeMb - afterMb);
        UpdateSettingsHeaderRamBadge();
        TriggerLiquidImpulse(24, -8, 3);
        _machine.PushToast(new ToastContent(
            "\uE74D",
            "Working Set Purged",
            $"Trimmed {freed} MB · Current RAM: {afterMb} MB",
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "ram_purge"));
    }

    internal void SettingsOpenDataBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", Core.AppPaths.DataDirectory) { UseShellExecute = true });
            TriggerLiquidImpulse(18, -6, 2);
        }
        catch (Exception ex)
        {
            App.Log($"[Settings] Failed to open data folder: {ex.Message}");
        }
    }
    internal void HaloToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.EnableAmbientGlow;
        _settingsService.Update(s => s.EnableAmbientGlow = next, nameof(AppSettings.EnableAmbientGlow));
        _machine.PushToast(new ToastContent(
            "\uE713",
            "Ambient Backlight",
            next ? "Halo Glow Enabled" : "Halo Glow Disabled",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(1.5),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "halo"));
    }

    internal void EdgeBorderToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        bool next = !_settingsService.Current.ShowEdgeBorder;
        _settingsService.Update(s => s.ShowEdgeBorder = next, nameof(AppSettings.ShowEdgeBorder));
        _machine.PushToast(new ToastContent(
            next ? "\uE73E" : "\uE711",
            "Island Edge Border",
            next ? "Edge border outline enabled" : "Edge border outline removed",
            next ? ThemeBrushes.GreenColor : ThemeBrushes.AmberColor,
            TimeSpan.FromSeconds(1.5),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "edgeborder"));
    }

    internal void SecondaryDropletToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        bool next = !_settingsService.Current.EnableSecondaryDroplet;
        _settingsService.Update(s => s.EnableSecondaryDroplet = next, nameof(AppSettings.EnableSecondaryDroplet));
        _machine.PushToast(new ToastContent(
            next ? "\uE73E" : "\uE711",
            "Split Secondary Droplet",
            next ? "Secondary activity droplet bubble enabled" : "Secondary activity droplet bubble disabled",
            next ? ThemeBrushes.GreenColor : ThemeBrushes.AmberColor,
            TimeSpan.FromSeconds(1.5),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "secondarydroplet"));
    }

    internal void DndToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        SetDoNotDisturb(!_settingsService.Current.DoNotDisturb);
    }

    private void SetDoNotDisturb(bool enabled)
    {
        _settingsService.Update(s => s.DoNotDisturb = enabled, nameof(AppSettings.DoNotDisturb));
        UpdateNotifSettingsUI();
        UpdateNotificationsDndUI();
        OnNotificationHistoryChanged();
        _machine.PushToast(new ToastContent(
            "\uE708",
            enabled ? "Do Not Disturb On" : "Do Not Disturb Off",
            enabled ? "Incoming notification toasts silenced" : "Notification toasts enabled",
            enabled ? ThemeBrushes.AmberColor : ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(1.8),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "dnd"));
    }

    private void UpdateNotifSettingsUI()
    {
        var np = SettingsContainer?.NotificationsPanel;
        if (np == null) return;

        bool dnd = _settingsService?.Current.DoNotDisturb ?? false;
        if (np.SettingsDndToggleSwitch != null)
            np.SettingsDndToggleSwitch.IsChecked = dnd;

        bool replace = _settingsService?.Current.ReplaceWindowsBanners ?? true;
        if (np.ReplaceBannersToggleSwitch != null)
            np.ReplaceBannersToggleSwitch.IsChecked = replace;

        bool groupByApp = _settingsService?.Current.GroupNotificationsByApp ?? true;
        if (np.GroupNotifsToggleSwitch != null)
            np.GroupNotifsToggleSwitch.IsChecked = groupByApp;

        var muted = _settingsService?.Current.MutedNotificationApps;
        int mutedCount = muted?.Count ?? 0;
        if (np.MutedAppsCountText != null)
            np.MutedAppsCountText.Text = $"{mutedCount} muted";
        if (np.MutedAppsEmptyText != null)
            np.MutedAppsEmptyText.Visibility = mutedCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (np.MutedAppsItemsControl != null)
            np.MutedAppsItemsControl.ItemsSource = muted?.ToList();
    }

    internal void GroupNotifsToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        bool next = !_settingsService.Current.GroupNotificationsByApp;
        _settingsService.Update(s => s.GroupNotificationsByApp = next, nameof(AppSettings.GroupNotificationsByApp));
        OnNotificationHistoryChanged();
        _machine.PushToast(new ToastContent(
            "\uE8F4",
            "Notification Grouping",
            next ? "Notifications stacked by application" : "Chronological individual cards",
            next ? ThemeBrushes.GreenColor : ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(1.8),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "notifgroup"));
    }

    internal void UnmuteAppBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        if (sender is FrameworkElement { Tag: string appName } && !string.IsNullOrWhiteSpace(appName))
        {
            _settingsService.Update(s =>
            {
                s.MutedNotificationApps.RemoveAll(a => string.Equals(a, appName, StringComparison.OrdinalIgnoreCase));
            }, nameof(AppSettings.MutedNotificationApps));
            UpdateMutedAppsBadgeUI();
            _machine.PushToast(new ToastContent(
                "\uE767",
                "App Unmuted",
                $"{appName} notifications restored",
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(1.8),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "notifmute"));
        }
    }

    internal void ReplaceBannersToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        bool next = !_settingsService.Current.ReplaceWindowsBanners;
        _settingsService.Update(s => s.ReplaceWindowsBanners = next, nameof(AppSettings.ReplaceWindowsBanners));
        UpdateNotifSettingsUI();
        _machine.PushToast(new ToastContent(
            next ? "\uE73E" : "\uE711",
            "Windows Banners",
            next ? "Native Windows banners silenced & captured into Island" : "Native Windows banners allowed",
            next ? ThemeBrushes.GreenColor : ThemeBrushes.AmberColor,
            TimeSpan.FromSeconds(1.5),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "banners"));
    }

    internal void OpenNotifCenterBtn_Click(object sender, RoutedEventArgs e)
    {
        SwitchExpandedView(ExpandedView.Notifications, true);
    }

}
