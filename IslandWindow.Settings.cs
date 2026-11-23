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
    // =================================================================
    //  Customization Settings, Liquid Physics & Laptop Notch Offset
    // =================================================================

    private bool _isApplyingSettings;

    private void ApplySettings()
    {
        if (_isApplyingSettings) return;
        _isApplyingSettings = true;
        try
        {
            var cur = _settingsService.Current;
            var appPanel = SettingsContainer.AppearancePanel;
            var dispPanel = SettingsContainer.DisplayPowerPanel;

            if (dispPanel.NotchOffsetSlider != null) dispPanel.NotchOffsetSlider.Value = cur.NotchYOffset;
            if (dispPanel.NotchOffsetText != null) dispPanel.NotchOffsetText.Text = $"{cur.NotchYOffset} px";

            double liquidPct = Math.Round(cur.LiquidIntensity * 100);
            if (appPanel.LiquidIntensitySlider != null) appPanel.LiquidIntensitySlider.Value = liquidPct;
            if (appPanel.LiquidIntensityText != null) appPanel.LiquidIntensityText.Text = $"{liquidPct}%";

            double speedPct = Math.Round(cur.AnimationSpeed * 100);
            if (appPanel.AnimationSpeedSlider != null) appPanel.AnimationSpeedSlider.Value = speedPct;
            if (appPanel.AnimationSpeedText != null) appPanel.AnimationSpeedText.Text = $"{speedPct}%";

            double scalePct = Math.Round(cur.IslandScale * 100);
            if (appPanel.IslandScaleSlider != null) appPanel.IslandScaleSlider.Value = scalePct;
            if (appPanel.IslandScaleText != null) appPanel.IslandScaleText.Text = $"{scalePct}%";
            if (RootScaleTransform != null)
            {
                RootScaleTransform.ScaleX = cur.IslandScale;
                RootScaleTransform.ScaleY = cur.IslandScale;
            }

            double opacityPct = Math.Round(cur.BackdropOpacity * 100);
            if (appPanel.BackdropOpacitySlider != null) appPanel.BackdropOpacitySlider.Value = opacityPct;
            if (appPanel.BackdropOpacityText != null) appPanel.BackdropOpacityText.Text = $"{opacityPct}%";
            if (PillBackground != null) PillBackground.Opacity = Math.Clamp(cur.BackdropOpacity, 0.65, 1.0);

            double blurPct = Math.Round(cur.GlassBlurIntensity * 100);
            if (appPanel.GlassBlurSlider != null) appPanel.GlassBlurSlider.Value = blurPct;
            if (appPanel.GlassBlurText != null) appPanel.GlassBlurText.Text = $"{blurPct}%";
            DynamicIsland.Core.LiquidGlassTheme.GlobalBlurScale = (float)cur.GlassBlurIntensity;

            double refPct = Math.Round(cur.GlassRefractionIntensity * 100);
            if (appPanel.GlassRefractionSlider != null) appPanel.GlassRefractionSlider.Value = refPct;
            if (appPanel.GlassRefractionText != null) appPanel.GlassRefractionText.Text = $"{refPct}%";
            DynamicIsland.Core.LiquidGlassTheme.GlobalThicknessScale = (float)cur.GlassRefractionIntensity;

            UpdateGlassOpticsAvailabilityUI();

            ThemeManager.SetThemeMode(SettingsService.NormalizeThemeMode(cur.ThemeMode));
            ThemeManager.SetThemeStyle(SettingsService.NormalizeThemeStyle(cur.ThemeStyle));
            ApplyTheme(cur.ThemeName);
            UpdateDockButtonsUI();
            UpdateExpandTriggerUI();
            UpdateFastSwipeGuardUI();
            UpdateGhostModifierUI();
            UpdateQuickTuckUI();
            UpdateHaloUI();
            UpdateDndUI();
            UpdateBannerModeUI();
            UpdateMotionModeUI();
            UpdateStartupUI();
            UpdateVolumeOsdUI();
            UpdateHotkeysUI();
            UpdateDockVisibilityUI();
            UpdateMonitorUI();
            UpdateNotchBezelUI();
            UpdateFollowMonitorUI();
            UpdateClockStyleUI();
            UpdateLyricsSettingsUI();
            UpdateNetSpeedIdleUI();
            UpdateCapsuleElementsUI();
            UpdateVisualizerStyleUI();
            UpdatePetSettingsUI();
            UpdateDisplayAndBatterySettingsUI();
            UpdateNotifSettingsUI();
            UpdateSystemSettingsUI();
            UpdateSettingsHeaderRamBadge();
            SwitchSettingsTab(_currentSettingsTab, false);
            ApplyClockStyle(cur.ClockStyle);
            ApplyVisualizerStyle(cur.VisualizerStyle);
            ApplyIslandTheme(cur.IslandTheme);
            UpdateThemeModeUI();
            UpdateThemeStyleUI();
            UpdateIslandThemeUI();
            UpdateIslandNatureSettingsUI();
            ApplyThemeStyleVisuals();
            ApplyEdgeBorder(cur.ShowEdgeBorder);
            UpdateEdgeBorderUI();
            UpdateSecondaryDropletUI();
            SyncSpringMotionPreferences();
            UpdateSpectrumActivity();
            UpdateSettingsVersionAndStatusUI();
            PositionWindow();
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void OnSettingsPropertyChanged(string? propertyName)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => OnSettingsPropertyChanged(propertyName));
            return;
        }

        var cur = _settingsService.Current;
        switch (propertyName)
        {
            case nameof(AppSettings.EnableVirtualPet):
            case nameof(AppSettings.PetSpecies):
            case nameof(AppSettings.PetAccessory):
            case nameof(AppSettings.PetName):
            case nameof(AppSettings.PetAffectionScore):
            case nameof(AppSettings.PetWaterCount):
            case nameof(AppSettings.PetStretchCount):
            case nameof(AppSettings.PetTreatCount):
            case nameof(AppSettings.WellnessReminderMinutes):
                UpdatePetSettingsUI();
                EnsureRendering();
                break;

            case nameof(AppSettings.EnableSyncedLyrics):
            case nameof(AppSettings.ShowLyricsInCompact):
                UpdateLyricsSettingsUI();
                UpdateClockTimerInterval();
                OnLyricsLoaded();
                break;

            case nameof(AppSettings.AutoCheckUpdates):
            case nameof(AppSettings.AutoDownloadUpdates):
            case nameof(AppSettings.UpdateChannel):
            case nameof(AppSettings.LastUpdateCheckTime):
                UpdateSettingsVersionAndStatusUI();
                break;

            case nameof(AppSettings.HardwareNotchMode):
            case nameof(AppSettings.HardwareNotchWidth):
            case nameof(AppSettings.HardwareNotchHeight):
            case nameof(AppSettings.HardwareNotchRadius):
            case nameof(AppSettings.HardwareNotchPreset):
                UpdateNotchBezelUI();
                if (_machine.State == IslandState.Idle)
                {
                    var targetIdle = GetIdleShape();
                    _width.Target = targetIdle.Width;
                    _height.Target = targetIdle.Height;
                    _radius.Target = targetIdle.Radius;
                }
                ApplyGeometry();
                PositionWindow();
                EnsureRendering();
                break;

            case nameof(AppSettings.IsDetachedFloating):
                _isDetachedPiP = cur.IsDetachedFloating;
                _floatingCenterX = cur.FloatingX;
                _floatingCenterY = cur.FloatingY;
                ApplyGeometry();
                PositionWindow();
                EnsureRendering();
                break;

            case nameof(AppSettings.FollowActiveMonitor):
                UpdateFollowMonitorUI();
                break;

            case nameof(AppSettings.EnableChargingPulse):
            case nameof(AppSettings.LowBatteryAlertThreshold):
            case nameof(AppSettings.AutoPowerPlanOnBattery):
            case nameof(AppSettings.AutoPowerPlanOnAc):
            case nameof(AppSettings.EnableNightLightSim):
            case nameof(AppSettings.NightLightWarmthKelvin):
                UpdateDisplayAndBatterySettingsUI();
                UpdateNotifSettingsUI();
                break;

            case nameof(AppSettings.VisualizerStyle):
                ApplyVisualizerStyle(cur.VisualizerStyle);
                break;

            case nameof(AppSettings.ShowNetworkSpeedInIdle):
                UpdateNetSpeedIdleUI();
                _networkService?.UpdatePollingState();
                if (_machine.State == IslandState.Idle)
                {
                    var targetIdle = GetIdleShape();
                    _width.Target = targetIdle.Width;
                    _height.Target = targetIdle.Height;
                    _radius.Target = targetIdle.Radius;
                }
                ApplyGeometry();
                EnsureRendering();
                break;

            case nameof(AppSettings.ShowCameraLensIcon):
                UpdateCapsuleElementsUI();
                if (_machine.State == IslandState.Idle)
                {
                    var targetIdle = GetIdleShape();
                    _width.Target = targetIdle.Width;
                    _height.Target = targetIdle.Height;
                    _radius.Target = targetIdle.Radius;
                }
                ApplyGeometry();
                EnsureRendering();
                break;

            case nameof(AppSettings.NotchYOffset):
                ApplyGeometry();
                PositionWindow();
                break;

            case nameof(AppSettings.LiquidIntensity):
            case nameof(AppSettings.AnimationSpeed):
                SyncSpringMotionPreferences();
                break;

            case nameof(AppSettings.IslandScale):
                if (RootScaleTransform != null)
                {
                    RootScaleTransform.ScaleX = cur.IslandScale;
                    RootScaleTransform.ScaleY = cur.IslandScale;
                }
                PositionWindow();
                EnsureRendering();
                break;

            case nameof(AppSettings.BackdropOpacity):
                if (PillBackground != null) PillBackground.Opacity = Math.Clamp(cur.BackdropOpacity, 0.65, 1.0);
                EnsureRendering();
                break;

            case nameof(AppSettings.GlassBlurIntensity):
                DynamicIsland.Core.LiquidGlassTheme.GlobalBlurScale = (float)cur.GlassBlurIntensity;
                ForceRefreshGlassSurfaces();
                break;

            case nameof(AppSettings.GlassRefractionIntensity):
                DynamicIsland.Core.LiquidGlassTheme.GlobalThicknessScale = (float)cur.GlassRefractionIntensity;
                ForceRefreshGlassSurfaces();
                break;

            case nameof(AppSettings.DockPosition):
                UpdateDockButtonsUI();
                PositionWindow();
                break;

            case nameof(AppSettings.ExpandTrigger):
                ApplyExpandTrigger(cur.ExpandTrigger);
                UpdateExpandTriggerUI();
                break;

            case nameof(AppSettings.GhostModifierKey):
                UpdateGhostModifierUI();
                break;

            case nameof(AppSettings.EnableFastSwipeGuard):
                if (_cursor != null) _cursor.EnableFastSwipeGuard = cur.EnableFastSwipeGuard;
                UpdateFastSwipeGuardUI();
                break;

            case nameof(AppSettings.EnableQuickTuck):
                UpdateQuickTuckUI();
                break;

            case nameof(AppSettings.EnableAmbientGlow):
                UpdateHaloUI();
                UpdateSpectrumActivity();
                EnsureRendering();
                break;

            case nameof(AppSettings.ShowEdgeBorder):
                ApplyEdgeBorder(cur.ShowEdgeBorder);
                UpdateEdgeBorderUI();
                EnsureRendering();
                break;

            case nameof(AppSettings.EnableSecondaryDroplet):
                UpdateSecondaryDropletUI();
                EvaluateActivity();
                EnsureRendering();
                break;

            case nameof(AppSettings.ThemeName):
                ApplyTheme(cur.ThemeName);
                UpdateDockButtonsUI();
                break;

            case nameof(AppSettings.ThemeMode):
                ThemeManager.SetThemeMode(SettingsService.NormalizeThemeMode(cur.ThemeMode));
                UpdateThemeModeUI();
                ApplyTheme(cur.ThemeName);
                break;

            case nameof(AppSettings.ThemeStyle):
                ThemeManager.SetThemeStyle(SettingsService.NormalizeThemeStyle(cur.ThemeStyle));
                UpdateThemeStyleUI();
                ApplyThemeStyleVisuals();
                ApplyTheme(cur.ThemeName);
                break;

            case nameof(AppSettings.IslandTheme):
                ApplyIslandTheme(cur.IslandTheme);
                UpdateIslandThemeUI();
                UpdateIslandNatureSettingsUI();
                break;

            case nameof(AppSettings.NatureDensity):
            case nameof(AppSettings.NatureScale):
            case nameof(AppSettings.NatureDropLength):
            case nameof(AppSettings.NatureButterflyCount):
            case nameof(AppSettings.NatureBloomDensity):
            case nameof(AppSettings.NaturePollenEnabled):
            case nameof(AppSettings.NaturePollenCount):
                UpdateIslandNatureSettingsUI();
                break;

            case nameof(AppSettings.MonitorIndex):
            case nameof(AppSettings.MonitorDeviceName):
                _cachedTargetMonitor = IntPtr.Zero;
                UpdateMonitorUI();
                PositionWindow();
                break;

            case nameof(AppSettings.DoNotDisturb):
                UpdateDndUI();
                break;

            case nameof(AppSettings.ReplaceWindowsBanners):
                UpdateBannerModeUI();
                break;

            case nameof(AppSettings.EnableVolumeKeyOsd):
                UpdateVolumeOsdUI();
                UpdateVolumeKeyHook();
                break;

            case nameof(AppSettings.HiddenDockViews):
            case nameof(AppSettings.EnabledDroplets):
                UpdateDockVisibilityUI();
                UpdateDropletsUI();
                break;

            case nameof(AppSettings.ReduceMotion):
                UpdateMotionModeUI();
                SyncSpringMotionPreferences();
                _appliedWidth = -1;
                ApplyGeometry();
                break;

            case nameof(AppSettings.ClockStyle):
                ApplyClockStyle(cur.ClockStyle);
                break;

            case nameof(AppSettings.CustomHotkeys):
            case nameof(AppSettings.EnableDeveloperHotkeys):
                RegisterConfiguredHotKeys();
                UpdateHotkeysUI();
                break;

            case nameof(AppSettings.EnableLockKeyHud):
            case nameof(AppSettings.EnableKeyboardLayoutHud):
                UpdateVolumeKeyHook();
                UpdateHotkeysUI();
                break;

            case nameof(AppSettings.GitHubToken):
                _updateService?.SetGitHubToken(cur.GitHubToken);
                break;

            case nameof(AppSettings.WeatherCityOverride):
                UpdateSystemSettingsUI();
                _ = _weatherService?.RefreshAsync();
                break;

            case nameof(AppSettings.LowMemoryMode):
            case nameof(AppSettings.ShowLottieAnimation):
                UpdateSystemSettingsUI();
                break;

            case nameof(AppSettings.GroupNotificationsByApp):
            case nameof(AppSettings.MutedNotificationApps):
                UpdateNotifSettingsUI();
                break;

            default:
                ApplySettings();
                break;
        }
    }

    private void ApplyTheme(string themeName)
    {
        bool isDark = ThemeManager.IsDark;
        var palette = SettingsService.ResolveTheme(themeName);
        var accentColor = palette.GetAccent(isDark);
        var glowColor = palette.GetGlow(isDark);

        if (SettingsContainer?.SelectedThemeName != null) SettingsContainer.SelectedThemeName.Text = palette.Name;
        Resources["AccentBrush"] = ThemeBrushes.CreateFrozen(accentColor);
        if (PillGlowColor != null) PillGlowColor.Color = glowColor;
        if (PillGlowSecondaryColor != null) PillGlowSecondaryColor.Color = accentColor;
        if (NeonClockGlowEffect != null) NeonClockGlowEffect.Color = glowColor;

        Resources["ClockTileBgBrush"] = isDark ? ThemeBrushes.ClockTileBgDark : ThemeBrushes.ClockTileBgLight;
        Resources["ClockTileBorderBrush"] = isDark ? ThemeBrushes.ClockTileBorderDark : ThemeBrushes.ClockTileBorderLight;
        Resources["ClockTileDividerBrush"] = isDark ? ThemeBrushes.ClockTileDividerDark : ThemeBrushes.ClockTileDividerLight;
        Resources["ClockTileFgBrush"] = isDark ? ThemeBrushes.ClockTileFgDark : ThemeBrushes.ClockTileFgLight;
        Resources["ClockDialFaceBrush"] = isDark ? ThemeBrushes.ClockDialFaceDark : ThemeBrushes.ClockDialFaceLight;
        Resources["ClockDialBorderBrush"] = isDark ? ThemeBrushes.ClockDialBorderDark : ThemeBrushes.ClockDialBorderLight;
        Resources["ClockHandBrush"] = isDark ? ThemeBrushes.ClockHandDark : ThemeBrushes.ClockHandLight;

        if (PillDropShadow != null)
        {
            if (ThemeManager.IsLiquidGlass)
            {
                PillDropShadow.Opacity = isDark ? 0.65 : 0.22;
                PillDropShadow.BlurRadius = isDark ? 28 : 22;
                PillDropShadow.ShadowDepth = 2;
                PillDropShadow.Color = isDark ? Color.FromRgb(0, 0, 4) : Color.FromRgb(15, 25, 45);
            }
            else
            {
                PillDropShadow.Opacity = isDark ? 0.55 : 0.16;
                PillDropShadow.BlurRadius = isDark ? 20 : 20;
                PillDropShadow.ShadowDepth = 2;
                PillDropShadow.Color = Colors.Black;
            }
        }

        UpdateAccentSwatchUI(palette.Name);
        UpdateDockButtonsUI();
        UpdateExpandTriggerUI();
        UpdateGhostModifierUI();
        UpdateQuickTuckUI();
        UpdateThemeModeUI();
        UpdateThemeStyleUI();
        ApplyThemeStyleVisuals();
        SwitchSettingsTab(_currentSettingsTab, false);
        if (_dropletRegistry != null)
        {
            foreach (var item in _dropletRegistry.Items)
            {
                item.RefreshThemeBindings();
            }
            UpdateDropletsUI();
        }
        EnsureRendering();
    }

    private void UpdateAccentSwatchUI(string activeThemeName)
    {
        var panel = SettingsContainer?.AppearancePanel?.AccentSwatchesPanel;
        if (panel == null) return;

        foreach (var child in panel.Children)
        {
            if (child is Button btn && btn.Tag is string tag)
            {
                bool active = string.Equals(tag, activeThemeName, StringComparison.OrdinalIgnoreCase);
                btn.BorderThickness = active ? new Thickness(2) : new Thickness(0);
            }
        }
    }

    private static void StyleAccentToggleButton(
        Button? btn,
        bool active,
        Brush accent,
        Brush defaultBg,
        Brush defaultBorder,
        TextBlock? label = null,
        Brush? inactiveLabelBrush = null,
        bool boldWhenActive = false)
    {
        if (btn == null) return;
        btn.Background = active ? accent : defaultBg;
        btn.Foreground = active ? ThemeBrushes.DarkInk : ThemeBrushes.PrimaryText;
        btn.BorderBrush = active ? accent : defaultBorder;
        if (label != null)
        {
            label.Foreground = active ? ThemeBrushes.DarkInk : (inactiveLabelBrush ?? ThemeBrushes.PrimaryText);
            if (boldWhenActive)
                label.FontWeight = active ? FontWeights.Bold : FontWeights.SemiBold;
        }
    }

    private static void StyleCardOptionButton(
        Button? btn,
        bool active,
        Brush accent,
        Brush activeBg,
        Brush defaultBg,
        Brush defaultBorder,
        bool dimInactive = false)
    {
        if (btn == null) return;
        btn.BorderBrush = active ? accent : defaultBorder;
        btn.Background = active ? activeBg : defaultBg;
        if (dimInactive)
            btn.Opacity = active ? 1.0 : 0.72;
    }

    private void UpdateDockButtonsUI()
    {
        var dp = SettingsContainer?.DisplayPowerPanel;
        if (dp == null || dp.DockLeftBtn == null || dp.DockCenterBtn == null || dp.DockRightBtn == null) return;
        string dock = SettingsService.NormalizeDockPosition(_settingsService.Current.DockPosition);
        var accent = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var defaultBg = (Brush)FindResource("CardSurfaceBrush");

        StyleAccentToggleButton(dp.DockLeftBtn, dock == "Left", accent, defaultBg, defaultBorder);
        StyleAccentToggleButton(dp.DockCenterBtn, dock == "Center", accent, defaultBg, defaultBorder);
        StyleAccentToggleButton(dp.DockRightBtn, dock == "Right", accent, defaultBg, defaultBorder);
    }

    private void UpdateExpandTriggerUI()
    {
        var dp = SettingsContainer?.DisplayPowerPanel;
        if (dp == null || dp.ExpandTriggerClickBtn == null) return;
        string trigger = _settingsService.Current.ExpandTrigger ?? "HoverSnappy";
        var accent = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var defaultBg = (Brush)FindResource("CardSurfaceBrush");

        StyleAccentToggleButton(dp.ExpandTriggerClickBtn, trigger == "ClickOnly", accent, defaultBg, defaultBorder);
        StyleAccentToggleButton(dp.ExpandTriggerSnappyBtn, trigger == "HoverSnappy", accent, defaultBg, defaultBorder);
        StyleAccentToggleButton(dp.ExpandTriggerRelaxedBtn, trigger == "HoverRelaxed", accent, defaultBg, defaultBorder);
        StyleAccentToggleButton(dp.ExpandTriggerDwellBtn, trigger == "HoverDwell", accent, defaultBg, defaultBorder);
    }

    private void UpdateFastSwipeGuardUI()
    {
        var dp = SettingsContainer?.DisplayPowerPanel;
        if (dp?.FastSwipeGuardToggleSwitch != null)
        {
            dp.FastSwipeGuardToggleSwitch.IsChecked = _settingsService.Current.EnableFastSwipeGuard;
        }
    }

    internal void FastSwipeGuardToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.EnableFastSwipeGuard;
        _settingsService.Update(s => s.EnableFastSwipeGuard = next, nameof(AppSettings.EnableFastSwipeGuard));
        _machine.PushToast(new ToastContent(
            "\uE713",
            "Fast-Swipe Guard",
            next ? "Rapid passes toward browser tabs ignored" : "Immediate hover detection enabled",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(1.5),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "fast_swipe"));
    }

    private void UpdateGhostModifierUI()
    {
        var dp = SettingsContainer?.DisplayPowerPanel;
        if (dp == null || dp.GhostModCtrlBtn == null) return;
        string mod = _settingsService.Current.GhostModifierKey ?? "Ctrl";
        var accent = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var defaultBg = (Brush)FindResource("CardSurfaceBrush");

        StyleAccentToggleButton(dp.GhostModCtrlBtn, string.Equals(mod, "Ctrl", StringComparison.OrdinalIgnoreCase), accent, defaultBg, defaultBorder);
        StyleAccentToggleButton(dp.GhostModAltBtn, string.Equals(mod, "Alt", StringComparison.OrdinalIgnoreCase), accent, defaultBg, defaultBorder);
        StyleAccentToggleButton(dp.GhostModNoneBtn, string.Equals(mod, "None", StringComparison.OrdinalIgnoreCase), accent, defaultBg, defaultBorder);
    }

    private void UpdateQuickTuckUI()
    {
        var dp = SettingsContainer?.DisplayPowerPanel;
        if (dp?.QuickTuckToggleSwitch != null)
        {
            dp.QuickTuckToggleSwitch.IsChecked = _settingsService.Current.EnableQuickTuck;
        }
    }

    internal void ExpandTriggerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string mode })
        {
            _settingsService.Update(s => s.ExpandTrigger = mode, nameof(AppSettings.ExpandTrigger));
            _machine.PushToast(new ToastContent(
                "\uE713",
                "Expand Trigger",
                mode switch
                {
                    "ClickOnly" => "Click Only (Tabs Safe)",
                    "HoverRelaxed" => "Hover Relaxed (260ms)",
                    "HoverDwell" => "Hover Dwell (380ms)",
                    _ => "Hover Snappy (150ms)"
                },
                ThemeBrushes.BlueColor,
                TimeSpan.FromSeconds(1.5),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "expand_trigger"));
        }
    }

    internal void GhostModifierBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
        {
            _settingsService.Update(s => s.GhostModifierKey = key, nameof(AppSettings.GhostModifierKey));
            _machine.PushToast(new ToastContent(
                "\uE713",
                "Ghost Click-Through",
                key == "None" ? "Disabled" : $"Hold {key} to click through",
                ThemeBrushes.BlueColor,
                TimeSpan.FromSeconds(1.5),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "ghost_key"));
        }
    }

    internal void QuickTuckToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.EnableQuickTuck;
        _settingsService.Update(s => s.EnableQuickTuck = next, nameof(AppSettings.EnableQuickTuck));
        _machine.PushToast(new ToastContent(
            "\uE713",
            "Quick Tuck Gesture",
            next ? "Enabled (Double-click or Ctrl+Alt+Down)" : "Disabled",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(1.5),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "quick_tuck"));
    }

    private void ApplyExpandTrigger(string? trigger)
    {
        if (_machine == null) return;
        switch (trigger)
        {
            case "ClickOnly":
                _machine.ClickOnlyMode = true;
                break;
            case "HoverRelaxed":
                _machine.ClickOnlyMode = false;
                _machine.CustomHoverEnterDelay = TimeSpan.FromMilliseconds(260);
                break;
            case "HoverDwell":
                _machine.ClickOnlyMode = false;
                _machine.CustomHoverEnterDelay = TimeSpan.FromMilliseconds(380);
                break;
            default: // "HoverSnappy"
                _machine.ClickOnlyMode = false;
                _machine.CustomHoverEnterDelay = TimeSpan.FromMilliseconds(150);
                break;
        }
    }

    internal void ClockStyleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string style })
        {
            string normalized = SettingsService.NormalizeClockStyle(style);
            _settingsService.Update(s => s.ClockStyle = normalized, nameof(AppSettings.ClockStyle));

            string label = normalized switch
            {
                "Seconds" => "Seconds (Precision)",
                "DateBadge" => "Date & Time Badge",
                "Analog" => "Micro Horology Analog",
                "Neon" => "Cyber Neon Glow",
                "Flip" => "Retro Flip Card",
                "Matrix" => "Field Telemetry Matrix",
                "JumpHour" => "Haute Jump-Hour Guichet",
                _ => "Minimal Digital"
            };
            _machine.PushToast(new ToastContent(
                "\uE823",
                "Clock Face Changed",
                label,
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "clockstyle"));
        }
    }

    private void UpdateClockStyleUI()
    {
        var cp = SettingsContainer?.ClockAudioPanel;
        if (cp?.ActiveClockStyleLabel == null || cp.ClockStyleMinimalBtn == null) return;

        string style = SettingsService.NormalizeClockStyle(_settingsService.Current.ClockStyle);
        var accentBrush = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var activeBg = ThemeBrushes.PillToggleActiveBg;
        var defaultBg = (Brush)FindResource("CardSurfaceBrush");

        StyleCardOptionButton(cp.ClockStyleMinimalBtn, style == "Minimal", accentBrush, activeBg, defaultBg, defaultBorder, dimInactive: true);
        StyleCardOptionButton(cp.ClockStyleSecondsBtn, style == "Seconds", accentBrush, activeBg, defaultBg, defaultBorder, dimInactive: true);
        StyleCardOptionButton(cp.ClockStyleDateBtn, style == "DateBadge", accentBrush, activeBg, defaultBg, defaultBorder, dimInactive: true);
        StyleCardOptionButton(cp.ClockStyleAnalogBtn, style == "Analog", accentBrush, activeBg, defaultBg, defaultBorder, dimInactive: true);
        StyleCardOptionButton(cp.ClockStyleNeonBtn, style == "Neon", accentBrush, activeBg, defaultBg, defaultBorder, dimInactive: true);
        StyleCardOptionButton(cp.ClockStyleFlipBtn, style == "Flip", accentBrush, activeBg, defaultBg, defaultBorder, dimInactive: true);
        StyleCardOptionButton(cp.ClockStyleMatrixBtn, style == "Matrix", accentBrush, activeBg, defaultBg, defaultBorder, dimInactive: true);
        StyleCardOptionButton(cp.ClockStyleJumpHourBtn, style == "JumpHour", accentBrush, activeBg, defaultBg, defaultBorder, dimInactive: true);

        cp.ActiveClockStyleLabel.Text = style switch
        {
            "Seconds" => "Seconds (Precision) · Live Accent Chip",
            "DateBadge" => "Date & Time Badge · Apple Calendar Pill",
            "Analog" => "Micro Horology Analog · Swiss Railway Dial",
            "Neon" => "Cyber Neon Glow · Dual-Stage Cathode Tube",
            "Flip" => "Retro Flip Card · Solari Split-Flap",
            "Matrix" => "Field Telemetry Matrix · Teenage Engineering OP-1",
            "JumpHour" => "Haute Jump-Hour Guichet · A. Lange & Söhne",
            _ => "Minimal Digital · Clean Apple OLED"
        };
    }

    private void ApplyClockStyle(string style)
    {
        string normalized = SettingsService.NormalizeClockStyle(style);

        if (IdleClock != null) IdleClock.Visibility = normalized == "Minimal" ? Visibility.Visible : Visibility.Collapsed;
        if (IdleClockSecondsHost != null) IdleClockSecondsHost.Visibility = normalized == "Seconds" ? Visibility.Visible : Visibility.Collapsed;
        if (IdleClockDateHost != null) IdleClockDateHost.Visibility = normalized == "DateBadge" ? Visibility.Visible : Visibility.Collapsed;
        if (IdleClockAnalogHost != null) IdleClockAnalogHost.Visibility = normalized == "Analog" ? Visibility.Visible : Visibility.Collapsed;
        if (IdleClockNeonHost != null) IdleClockNeonHost.Visibility = normalized == "Neon" ? Visibility.Visible : Visibility.Collapsed;
        else if (IdleClockNeon != null) IdleClockNeon.Visibility = normalized == "Neon" ? Visibility.Visible : Visibility.Collapsed;
        if (IdleClockFlipHost != null) IdleClockFlipHost.Visibility = normalized == "Flip" ? Visibility.Visible : Visibility.Collapsed;
        if (IdleClockMatrixHost != null) IdleClockMatrixHost.Visibility = normalized == "Matrix" ? Visibility.Visible : Visibility.Collapsed;
        if (IdleClockJumpHourHost != null) IdleClockJumpHourHost.Visibility = normalized == "JumpHour" ? Visibility.Visible : Visibility.Collapsed;

        UpdateClockStyleUI();
        UpdateClockTimerInterval();

        var cp = SettingsContainer?.ClockAudioPanel;
        if (cp?.ClockStylesScrollViewer != null)
        {
            Button? activeBtn = normalized switch
            {
                "Seconds" => cp.ClockStyleSecondsBtn,
                "DateBadge" => cp.ClockStyleDateBtn,
                "Analog" => cp.ClockStyleAnalogBtn,
                "Neon" => cp.ClockStyleNeonBtn,
                "Flip" => cp.ClockStyleFlipBtn,
                "Matrix" => cp.ClockStyleMatrixBtn,
                "JumpHour" => cp.ClockStyleJumpHourBtn,
                _ => cp.ClockStyleMinimalBtn
            };
            activeBtn?.BringIntoView();
        }

        // Immediately update text on all clock controls
        _lastTimeText = null;
        _lastDateShortText = null;
        UpdateClockAndTimeline();

        if (_machine.State == IslandState.Idle)
        {
            var targetIdle = GetIdleShape();
            _width.Target = targetIdle.Width;
            _height.Target = targetIdle.Height;
            _radius.Target = targetIdle.Radius;
            EnsureRendering();
        }
    }

    internal void VisualizerStyleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string style })
        {
            string normalized = SettingsService.NormalizeVisualizerStyle(style);
            _settingsService.Update(s => s.VisualizerStyle = normalized, nameof(AppSettings.VisualizerStyle));

            string friendlyName = normalized switch
            {
                "Waveform" => "Continuous Waveform",
                "LiquidBlob" => "Liquid Blob",
                "NeonAurora" => "Neon Aurora",
                "SiriOrb" => "Siri Fluid Orb",
                _ => "Classic Bars"
            };
            var accent = _mediaService.Current?.Accent ?? ThemeBrushes.AmberColor;
            _machine.PushToast(new ToastContent(
                "\uE9D9",
                "Audio Visualizer",
                friendlyName,
                accent,
                TimeSpan.FromSeconds(2.0),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "visualizer"));
        }
    }

    private void UpdateVisualizerStyleUI()
    {
        var cp = SettingsContainer?.ClockAudioPanel;
        if (cp?.ActiveVisualizerStyleLabel == null || cp.VisualizerStyleBarsBtn == null) return;

        string style = SettingsService.NormalizeVisualizerStyle(_settingsService.Current.VisualizerStyle);
        var accentBrush = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var activeBg = ThemeBrushes.PillToggleActiveBg;
        var defaultBg = (Brush)FindResource("CardSurfaceBrush");

        StyleCardOptionButton(cp.VisualizerStyleBarsBtn, style == "ClassicBars", accentBrush, activeBg, defaultBg, defaultBorder);
        StyleCardOptionButton(cp.VisualizerStyleWaveBtn, style == "Waveform", accentBrush, activeBg, defaultBg, defaultBorder);
        StyleCardOptionButton(cp.VisualizerStyleBlobBtn, style == "LiquidBlob", accentBrush, activeBg, defaultBg, defaultBorder);
        StyleCardOptionButton(cp.VisualizerStyleAuroraBtn, style == "NeonAurora", accentBrush, activeBg, defaultBg, defaultBorder);
        StyleCardOptionButton(cp.VisualizerStyleSiriBtn, style == "SiriOrb", accentBrush, activeBg, defaultBg, defaultBorder);

        cp.ActiveVisualizerStyleLabel.Text = style switch
        {
            "Waveform" => "Continuous Waveform (Oscilloscope)",
            "LiquidBlob" => "Liquid Blob (Organic Droplet)",
            "NeonAurora" => "Neon Aurora (Glowing Waves)",
            "SiriOrb" => "Siri Fluid Orb (Iridescent)",
            _ => "Classic Bars (Studio Equalizer)"
        };

        if (MediaVisualizerBadgeText != null)
        {
            MediaVisualizerBadgeText.Text = style switch
            {
                "Waveform" => "Wave",
                "LiquidBlob" => "Blob",
                "NeonAurora" => "Aurora",
                "SiriOrb" => "Siri",
                _ => "Bars"
            };
        }
    }

    private void ApplyVisualizerStyle(string style)
    {
        string normalized = SettingsService.NormalizeVisualizerStyle(style);

        if (VisualizerBarsHost != null) VisualizerBarsHost.Visibility = normalized == "ClassicBars" ? Visibility.Visible : Visibility.Collapsed;
        if (VisualizerWaveHost != null) VisualizerWaveHost.Visibility = normalized == "Waveform" ? Visibility.Visible : Visibility.Collapsed;
        if (VisualizerBlobHost != null) VisualizerBlobHost.Visibility = normalized == "LiquidBlob" ? Visibility.Visible : Visibility.Collapsed;
        if (VisualizerAuroraHost != null) VisualizerAuroraHost.Visibility = normalized == "NeonAurora" ? Visibility.Visible : Visibility.Collapsed;
        if (VisualizerSiriHost != null) VisualizerSiriHost.Visibility = normalized == "SiriOrb" ? Visibility.Visible : Visibility.Collapsed;

        UpdateVisualizerStyleUI();
        EnsureRendering();
    }

    private void CycleVisualizerStyle()
    {
        string cur = SettingsService.NormalizeVisualizerStyle(_settingsService.Current.VisualizerStyle);
        string next = cur switch
        {
            "ClassicBars" => "Waveform",
            "Waveform" => "LiquidBlob",
            "LiquidBlob" => "NeonAurora",
            "NeonAurora" => "SiriOrb",
            _ => "ClassicBars"
        };

        _settingsService.Update(s => s.VisualizerStyle = next, nameof(AppSettings.VisualizerStyle));

        string friendlyName = next switch
        {
            "Waveform" => "Fluid Waveform",
            "LiquidBlob" => "Liquid Blob",
            "NeonAurora" => "Neon Aurora",
            "SiriOrb" => "Siri Fluid Orb",
            _ => "Studio Bars"
        };

        var accent = _mediaService.Current?.Accent ?? ThemeBrushes.AmberColor;
        _machine.PushToast(new ToastContent(
            "\uE9D9",
            "Audio Visualizer",
            friendlyName,
            accent,
            TimeSpan.FromSeconds(2.0),
            TargetView: "media",
            Priority: ToastPriority.Feedback,
            Category: "visualizer"));

        TriggerLiquidImpulse(180, 80, 15);
    }

    private void CompactVisualizer_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CycleVisualizerStyle();
    }

    internal void MediaVisualizerBadge_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CycleVisualizerStyle();
    }

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

    internal void LiquidIntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var ap = SettingsContainer?.AppearancePanel;
        if (_isApplyingSettings || _settingsService == null || ap?.LiquidIntensityText == null) return;
        double pct = Math.Round(e.NewValue);
        double normalized = Math.Clamp(pct / 100.0, 0.0, 1.0);
        if (Math.Abs(_settingsService.Current.LiquidIntensity - normalized) < 0.005) return;

        ap.LiquidIntensityText.Text = $"{pct}%";
        _settingsService.Update(s => s.LiquidIntensity = normalized, nameof(AppSettings.LiquidIntensity));

        // Give a live gooey ripple as the user drags so they immediately feel the liquid elasticity
        if (ap.LiquidIntensitySlider.IsMouseCaptureWithin && Math.Abs(_width.Velocity) < 80)
        {
            TriggerLiquidImpulse(140, -55, 18);
        }
    }

    internal void AnimationSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var ap = SettingsContainer?.AppearancePanel;
        if (_isApplyingSettings || _settingsService == null || ap?.AnimationSpeedText == null) return;
        double pct = Math.Round(e.NewValue);
        double normalized = Math.Clamp(pct / 100.0, 0.5, 1.6);
        if (Math.Abs(_settingsService.Current.AnimationSpeed - normalized) < 0.005) return;

        ap.AnimationSpeedText.Text = $"{pct}%";
        _settingsService.Update(s => s.AnimationSpeed = normalized, nameof(AppSettings.AnimationSpeed));

        if (ap.AnimationSpeedSlider.IsMouseCaptureWithin && Math.Abs(_width.Velocity) < 80)
        {
            TriggerLiquidImpulse(70, -25, 8);
        }
    }

    internal void IslandScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var ap = SettingsContainer?.AppearancePanel;
        if (_isApplyingSettings || _settingsService == null || ap?.IslandScaleText == null) return;
        double pct = Math.Round(e.NewValue);
        double normalized = Math.Clamp(pct / 100.0, 0.85, 1.25);
        if (Math.Abs(_settingsService.Current.IslandScale - normalized) < 0.005) return;

        ap.IslandScaleText.Text = $"{pct}%";
        _settingsService.Update(s => s.IslandScale = normalized, nameof(AppSettings.IslandScale));

        if (ap.IslandScaleSlider.IsMouseCaptureWithin && Math.Abs(_width.Velocity) < 80)
        {
            TriggerLiquidImpulse(90, -35, 10);
        }
    }

    internal void BackdropOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var ap = SettingsContainer?.AppearancePanel;
        if (_isApplyingSettings || _settingsService == null || ap?.BackdropOpacityText == null) return;
        double pct = Math.Round(e.NewValue);
        double normalized = Math.Clamp(pct / 100.0, 0.65, 1.0);
        if (Math.Abs(_settingsService.Current.BackdropOpacity - normalized) < 0.005) return;

        ap.BackdropOpacityText.Text = $"{pct}%";
        _settingsService.Update(s => s.BackdropOpacity = normalized, nameof(AppSettings.BackdropOpacity));
    }

    internal void GlassBlurSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var ap = SettingsContainer?.AppearancePanel;
        if (_isApplyingSettings || _settingsService == null || ap?.GlassBlurText == null) return;
        double pct = Math.Round(e.NewValue);
        double normalized = Math.Clamp(pct / 100.0, 0.20, 2.50);
        if (Math.Abs(_settingsService.Current.GlassBlurIntensity - normalized) < 0.005) return;

        ap.GlassBlurText.Text = $"{pct}%";
        _settingsService.Update(s => s.GlassBlurIntensity = normalized, nameof(AppSettings.GlassBlurIntensity));
    }

    internal void GlassRefractionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var ap = SettingsContainer?.AppearancePanel;
        if (_isApplyingSettings || _settingsService == null || ap?.GlassRefractionText == null) return;
        double pct = Math.Round(e.NewValue);
        double normalized = Math.Clamp(pct / 100.0, 0.0, 2.0);
        if (Math.Abs(_settingsService.Current.GlassRefractionIntensity - normalized) < 0.005) return;

        ap.GlassRefractionText.Text = $"{pct}%";
        _settingsService.Update(s => s.GlassRefractionIntensity = normalized, nameof(AppSettings.GlassRefractionIntensity));
    }

    private void UpdateGlassOpticsAvailabilityUI()
    {
        var ap = SettingsContainer?.AppearancePanel;
        if (ap == null) return;
        bool isLiquid = DynamicIsland.Core.ThemeManager.IsLiquidGlass;
        if (ap.GlassFrostingStatusText != null)
        {
            ap.GlassFrostingStatusText.Text = isLiquid ? "Active" : "OLED Mode (Switch to Liquid Glass)";
        }
        if (ap.GlassOpticsGroup != null)
        {
            ap.GlassOpticsGroup.IsEnabled = isLiquid;
            ap.GlassOpticsGroup.Opacity = isLiquid ? 1.0 : 0.45;
        }
    }

    private void ForceRefreshGlassSurfaces()
    {
        if (!DynamicIsland.Core.ThemeManager.IsLiquidGlass || !DynamicIsland.LiquidGlass.GlassManager.Instance.IsEnabled) return;
        _lastPillGlassW = -1;
        _lastPillGlassH = -1;
        UpdatePillLiquidGlass(Pill.ActualWidth > 0 ? Pill.ActualWidth : 148, Pill.ActualHeight > 0 ? Pill.ActualHeight : 30, (float)_radius.Value, _dropletOffset?.Value ?? 0);
        if (FloatingDockHost != null)
        {
            DynamicIsland.LiquidGlass.GlassHost.ForceRender(FloatingDockHost);
        }
    }

    internal void NotchOffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var dp = SettingsContainer?.DisplayPowerPanel;
        if (_isApplyingSettings || _settingsService == null || dp?.NotchOffsetText == null) return;
        double val = Math.Round(e.NewValue);
        if (Math.Abs(_settingsService.Current.NotchYOffset - val) < 0.01) return;
        dp.NotchOffsetText.Text = $"{val} px";
        _settingsService.Update(s => s.NotchYOffset = val, nameof(AppSettings.NotchYOffset));
    }

    private string _currentSettingsTab = "Appearance";

    internal void SettingsTabBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tab })
        {
            SwitchSettingsTab(tab, impulse: true);
        }
    }

    internal void SwitchSettingsTab(string tab, bool impulse = false)
    {
        if (SettingsContainer == null) return;
        _currentSettingsTab = tab;

        SettingsContainer.AppearancePanel.Visibility = tab == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        SettingsContainer.FloraPanel.Visibility = tab == "Themes" ? Visibility.Visible : Visibility.Collapsed;
        SettingsContainer.ClockAudioPanel.Visibility = tab == "Clock" ? Visibility.Visible : Visibility.Collapsed;
        SettingsContainer.DropletsPanel.Visibility = tab == "Droplets" ? Visibility.Visible : Visibility.Collapsed;
        SettingsContainer.DockPanel.Visibility = tab == "Dock" ? Visibility.Visible : Visibility.Collapsed;
        SettingsContainer.DisplayPowerPanel.Visibility = tab == "Screen" ? Visibility.Visible : Visibility.Collapsed;
        SettingsContainer.NotificationsPanel.Visibility = tab == "Notifications" ? Visibility.Visible : Visibility.Collapsed;
        SettingsContainer.PetPanel.Visibility = tab == "Pet" ? Visibility.Visible : Visibility.Collapsed;
        SettingsContainer.ShortcutsPanel.Visibility = tab == "Shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        SettingsContainer.SystemUpdatesPanel.Visibility = tab == "Updates" ? Visibility.Visible : Visibility.Collapsed;

        if (tab == "Screen")
        {
            UpdateDisplayAndBatterySettingsUI();
            UpdateNotchBezelUI();
            UpdateExpandTriggerUI();
            UpdateFastSwipeGuardUI();
        }
        else if (tab == "Themes")
        {
            UpdateIslandThemeUI();
            UpdateIslandNatureSettingsUI();
        }
        else if (tab == "Droplets")
        {
            UpdateDropletsUI();
        }
        else if (tab == "Dock")
        {
            UpdateDockVisibilityUI();
        }
        else if (tab == "Notifications")
        {
            UpdateNotifSettingsUI();
        }
        else if (tab == "Shortcuts")
        {
            UpdateHotkeysUI();
            UpdateVolumeOsdUI();
        }
        else if (tab == "Updates")
        {
            UpdateStartupUI();
            UpdateSystemSettingsUI();
            UpdateSettingsVersionAndStatusUI();
        }

        UpdateSettingsHeaderRamBadge();

        var activeBg = (Brush)FindResource("SettingsTabActiveBgBrush");
        var inactiveBg = Brushes.Transparent;
        var activeFg = (Brush)FindResource("PrimaryTextBrush");
        var inactiveFg = (Brush)FindResource("SecondaryBrush");

        void UpdateRailItem(Button? btn, TextBlock? text, Border? bar, bool isActive)
        {
            if (btn == null) return;
            btn.Background = isActive ? activeBg : inactiveBg;
            if (text != null)
            {
                text.Foreground = isActive ? activeFg : inactiveFg;
                text.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Medium;
            }
            if (bar != null)
            {
                bar.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        UpdateRailItem(SettingsContainer.SettingsTabAppearanceBtn, SettingsContainer.SettingsTabAppearanceText, SettingsContainer.SettingsRailBarAppearance, tab == "Appearance");
        UpdateRailItem(SettingsContainer.SettingsTabThemesBtn, SettingsContainer.SettingsTabThemesText, SettingsContainer.SettingsRailBarThemes, tab == "Themes");
        UpdateRailItem(SettingsContainer.SettingsTabClockBtn, SettingsContainer.SettingsTabClockText, SettingsContainer.SettingsRailBarClock, tab == "Clock");
        UpdateRailItem(SettingsContainer.SettingsTabDropletsBtn, SettingsContainer.SettingsTabDropletsText, SettingsContainer.SettingsRailBarDroplets, tab == "Droplets");
        UpdateRailItem(SettingsContainer.SettingsTabDockBtn, SettingsContainer.SettingsTabDockText, SettingsContainer.SettingsRailBarDock, tab == "Dock");
        UpdateRailItem(SettingsContainer.SettingsTabScreenBtn, SettingsContainer.SettingsTabScreenText, SettingsContainer.SettingsRailBarScreen, tab == "Screen");
        UpdateRailItem(SettingsContainer.SettingsTabNotifBtn, SettingsContainer.SettingsTabNotifText, SettingsContainer.SettingsRailBarNotif, tab == "Notifications");
        UpdateRailItem(SettingsContainer.SettingsTabPetBtn, SettingsContainer.SettingsTabPetText, SettingsContainer.SettingsRailBarPet, tab == "Pet");
        UpdateRailItem(SettingsContainer.SettingsTabShortcutsBtn, SettingsContainer.SettingsTabShortcutsText, SettingsContainer.SettingsRailBarShortcuts, tab == "Shortcuts");
        UpdateRailItem(SettingsContainer.SettingsTabUpdatesBtn, SettingsContainer.SettingsTabUpdatesText, SettingsContainer.SettingsRailBarUpdates, tab == "Updates");

        if (impulse && !_settingsService.Current.ReduceMotion)
        {
            TriggerLiquidImpulse(30, -10, 4);
        }
    }

    private void UpdateSettingsHeaderRamBadge()
    {
        if (SettingsContainer?.SettingsRamBadgeText == null) return;
        long mb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
        SettingsContainer.SettingsRamBadgeText.Text = $"{mb} MB";
    }

    internal void LiquidPulseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService.Current.ReduceMotion)
        {
            _settingsService.Update(s => s.ReduceMotion = false, nameof(AppSettings.ReduceMotion));
        }
        TriggerLiquidImpulse(480, -220, 55);
    }

    internal void ResetSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        var resetLabel = SettingsContainer?.SystemUpdatesPanel?.ResetSettingsText;
        bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (!_confirmGuard.TryConfirm("reset_settings", sender, "Confirm Reset?", explicitLabel: resetLabel, bypass: shiftHeld, dispatcher: Dispatcher))
        {
            _machine.PushToast(new ToastContent(
                "\uE7BA",
                "Confirm Reset",
                "Click 'Confirm Reset?' again within 3s to restore defaults",
                ThemeBrushes.AmberColor,
                TimeSpan.FromSeconds(2.5),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "settingsio"));
            return;
        }

        _settingsService.ResetToDefaults();
        TriggerLiquidImpulse(360, 180, 40);
        _machine.PushToast(new ToastContent(
            "\uE777",
            "Settings Reset",
            "All island configurations restored to defaults",
            ThemeBrushes.AmberColor,
            TimeSpan.FromSeconds(2.5),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "settingsio"));
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

    internal void ExportSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Dynamic Island Settings",
            Filter = "JSON Backup (*.json)|*.json|All Files (*.*)|*.*",
            FileName = "DynamicIsland_Settings_Backup.json",
            DefaultExt = ".json",
            InitialDirectory = Directory.Exists(downloads) ? downloads : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        bool wasPinned = _machine.IsPinned;
        if (!wasPinned) _machine.SetPinned(true);
        try
        {
            if (dlg.ShowDialog(this) == true && _settingsService.ExportToFile(dlg.FileName))
            {
                _shelfService.AddPaths(new[] { dlg.FileName });
                _machine.PushToast(new ToastContent(
                    "\uEDE1",
                    "Settings Exported",
                    $"Saved {Path.GetFileName(dlg.FileName)} & added to Drop Shelf",
                    ThemeBrushes.GreenColor,
                    TimeSpan.FromSeconds(2.5),
                    TargetView: "settings",
                    Priority: ToastPriority.Feedback,
                    Category: "settingsio"));
            }
        }
        finally
        {
            if (!wasPinned) _machine.SetPinned(false);
        }
    }

    internal void ImportSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Dynamic Island Settings",
            Filter = "JSON Backup (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".json",
            InitialDirectory = Directory.Exists(downloads) ? downloads : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        bool wasPinned = _machine.IsPinned;
        if (!wasPinned) _machine.SetPinned(true);
        try
        {
            if (dlg.ShowDialog(this) == true)
            {
                if (_settingsService.ImportFromFile(dlg.FileName))
                {
                    TriggerLiquidImpulse(320, 160, 35);
                    _machine.PushToast(new ToastContent(
                        "\uE8B5",
                        "Settings Imported",
                        Path.GetFileName(dlg.FileName),
                        ThemeBrushes.GreenColor,
                        TimeSpan.FromSeconds(2.5),
                        TargetView: "settings",
                        Priority: ToastPriority.Feedback,
                        Category: "settingsio"));
                }
                else
                {
                    _machine.PushToast(new ToastContent(
                        "\uE783",
                        "Import Failed",
                        "Selected file is not a valid Dynamic Island settings JSON",
                        ThemeBrushes.AmberColor,
                        TimeSpan.FromSeconds(2.5),
                        TargetView: "settings",
                        Priority: ToastPriority.Feedback,
                        Category: "settingsio"));
                }
            }
        }
        finally
        {
            if (!wasPinned) _machine.SetPinned(false);
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


    internal void DockButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string dock })
        {
            _settingsService.Update(s => s.DockPosition = dock, nameof(AppSettings.DockPosition));
            _machine.PushToast(new ToastContent(
                "\uE713",
                "Dock Position",
                $"Docked {dock}",
                ThemeBrushes.BlueColor,
                TimeSpan.FromSeconds(1.5),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "dock"));
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
