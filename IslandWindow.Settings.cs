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
            if (NotchOffsetSlider != null) NotchOffsetSlider.Value = cur.NotchYOffset;
            if (NotchOffsetText != null) NotchOffsetText.Text = $"{cur.NotchYOffset} px";

            double liquidPct = Math.Round(cur.LiquidIntensity * 100);
            if (LiquidIntensitySlider != null) LiquidIntensitySlider.Value = liquidPct;
            if (LiquidIntensityText != null) LiquidIntensityText.Text = $"{liquidPct}%";

            double speedPct = Math.Round(cur.AnimationSpeed * 100);
            if (AnimationSpeedSlider != null) AnimationSpeedSlider.Value = speedPct;
            if (AnimationSpeedText != null) AnimationSpeedText.Text = $"{speedPct}%";

            double scalePct = Math.Round(cur.IslandScale * 100);
            if (IslandScaleSlider != null) IslandScaleSlider.Value = scalePct;
            if (IslandScaleText != null) IslandScaleText.Text = $"{scalePct}%";
            if (RootScaleTransform != null)
            {
                RootScaleTransform.ScaleX = cur.IslandScale;
                RootScaleTransform.ScaleY = cur.IslandScale;
            }

            double opacityPct = Math.Round(cur.BackdropOpacity * 100);
            if (BackdropOpacitySlider != null) BackdropOpacitySlider.Value = opacityPct;
            if (BackdropOpacityText != null) BackdropOpacityText.Text = $"{opacityPct}%";
            if (PillBackground != null) PillBackground.Opacity = Math.Clamp(cur.BackdropOpacity, 0.65, 1.0);

            double blurPct = Math.Round(cur.GlassBlurIntensity * 100);
            if (GlassBlurSlider != null) GlassBlurSlider.Value = blurPct;
            if (GlassBlurText != null) GlassBlurText.Text = $"{blurPct}%";
            DynamicIsland.Core.LiquidGlassTheme.GlobalBlurScale = (float)cur.GlassBlurIntensity;

            double refPct = Math.Round(cur.GlassRefractionIntensity * 100);
            if (GlassRefractionSlider != null) GlassRefractionSlider.Value = refPct;
            if (GlassRefractionText != null) GlassRefractionText.Text = $"{refPct}%";
            DynamicIsland.Core.LiquidGlassTheme.GlobalThicknessScale = (float)cur.GlassRefractionIntensity;

            if (GlassFrostingStatusText != null)
            {
                GlassFrostingStatusText.Text = DynamicIsland.Core.ThemeManager.IsLiquidGlass ? "Active" : "OLED Mode";
            }

            ThemeManager.SetThemeMode(SettingsService.NormalizeThemeMode(cur.ThemeMode));
            ThemeManager.SetThemeStyle(SettingsService.NormalizeThemeStyle(cur.ThemeStyle));
            ApplyTheme(cur.ThemeName);
            UpdateDockButtonsUI();
            UpdateExpandTriggerUI();
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

            case nameof(AppSettings.GitHubToken):
                _updateService?.SetGitHubToken(cur.GitHubToken);
                break;

            case nameof(AppSettings.WeatherCityOverride):
                _ = _weatherService?.RefreshAsync();
                break;

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

        if (SelectedThemeName != null) SelectedThemeName.Text = palette.Name;
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

        UpdateDockButtonsUI();
        UpdateExpandTriggerUI();
        UpdateGhostModifierUI();
        UpdateQuickTuckUI();
        UpdateThemeModeUI();
        UpdateThemeStyleUI();
        ApplyThemeStyleVisuals();
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
        if (DockLeftBtn == null || DockCenterBtn == null || DockRightBtn == null) return;
        string dock = SettingsService.NormalizeDockPosition(_settingsService.Current.DockPosition);
        var accent = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var defaultBg = (Brush)FindResource("CardSurfaceBrush");

        StyleAccentToggleButton(DockLeftBtn, dock == "Left", accent, defaultBg, defaultBorder);
        StyleAccentToggleButton(DockCenterBtn, dock == "Center", accent, defaultBg, defaultBorder);
        StyleAccentToggleButton(DockRightBtn, dock == "Right", accent, defaultBg, defaultBorder);
    }

    private void UpdateExpandTriggerUI()
    {
        if (ExpandTriggerClickBtn == null || ExpandTriggerSnappyBtn == null || ExpandTriggerRelaxedBtn == null) return;
        string trigger = _settingsService.Current.ExpandTrigger ?? "HoverSnappy";
        var accent = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var defaultBg = (Brush)FindResource("CardSurfaceBrush");

        StyleAccentToggleButton(ExpandTriggerClickBtn, trigger == "ClickOnly", accent, defaultBg, defaultBorder);
        StyleAccentToggleButton(ExpandTriggerSnappyBtn, trigger == "HoverSnappy", accent, defaultBg, defaultBorder);
        StyleAccentToggleButton(ExpandTriggerRelaxedBtn, trigger == "HoverRelaxed", accent, defaultBg, defaultBorder);
    }

    private void UpdateGhostModifierUI()
    {
        if (GhostModCtrlBtn == null || GhostModAltBtn == null || GhostModNoneBtn == null) return;
        string mod = _settingsService.Current.GhostModifierKey ?? "Ctrl";
        var accent = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var defaultBg = (Brush)FindResource("CardSurfaceBrush");

        StyleAccentToggleButton(GhostModCtrlBtn, string.Equals(mod, "Ctrl", StringComparison.OrdinalIgnoreCase), accent, defaultBg, defaultBorder);
        StyleAccentToggleButton(GhostModAltBtn, string.Equals(mod, "Alt", StringComparison.OrdinalIgnoreCase), accent, defaultBg, defaultBorder);
        StyleAccentToggleButton(GhostModNoneBtn, string.Equals(mod, "None", StringComparison.OrdinalIgnoreCase), accent, defaultBg, defaultBorder);
    }

    private void UpdateQuickTuckUI()
    {
        if (QuickTuckToggleBtn == null || QuickTuckToggleText == null) return;
        bool enabled = _settingsService.Current.EnableQuickTuck;
        var accent = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var defaultBg = (Brush)FindResource("CardSurfaceBrush");

        StyleAccentToggleButton(
            QuickTuckToggleBtn,
            enabled,
            accent,
            defaultBg,
            defaultBorder,
            QuickTuckToggleText,
            ThemeBrushes.SecondaryText);
        QuickTuckToggleText.Text = enabled ? "Tuck Enabled" : "Tuck Disabled";
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
        if (ActiveClockStyleLabel == null || ClockStyleMinimalBtn == null) return;

        string style = SettingsService.NormalizeClockStyle(_settingsService.Current.ClockStyle);
        var accentBrush = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var activeBg = ThemeBrushes.PillToggleActiveBg;
        var defaultBg = (Brush)FindResource("CardSurfaceBrush");

        StyleCardOptionButton(ClockStyleMinimalBtn, style == "Minimal", accentBrush, activeBg, defaultBg, defaultBorder, dimInactive: true);
        StyleCardOptionButton(ClockStyleSecondsBtn, style == "Seconds", accentBrush, activeBg, defaultBg, defaultBorder, dimInactive: true);
        StyleCardOptionButton(ClockStyleDateBtn, style == "DateBadge", accentBrush, activeBg, defaultBg, defaultBorder, dimInactive: true);
        StyleCardOptionButton(ClockStyleAnalogBtn, style == "Analog", accentBrush, activeBg, defaultBg, defaultBorder, dimInactive: true);
        StyleCardOptionButton(ClockStyleNeonBtn, style == "Neon", accentBrush, activeBg, defaultBg, defaultBorder, dimInactive: true);
        StyleCardOptionButton(ClockStyleFlipBtn, style == "Flip", accentBrush, activeBg, defaultBg, defaultBorder, dimInactive: true);
        StyleCardOptionButton(ClockStyleMatrixBtn, style == "Matrix", accentBrush, activeBg, defaultBg, defaultBorder, dimInactive: true);
        StyleCardOptionButton(ClockStyleJumpHourBtn, style == "JumpHour", accentBrush, activeBg, defaultBg, defaultBorder, dimInactive: true);

        ActiveClockStyleLabel.Text = style switch
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

        if (ClockStylesScrollViewer != null)
        {
            Button? activeBtn = normalized switch
            {
                "Seconds" => ClockStyleSecondsBtn,
                "DateBadge" => ClockStyleDateBtn,
                "Analog" => ClockStyleAnalogBtn,
                "Neon" => ClockStyleNeonBtn,
                "Flip" => ClockStyleFlipBtn,
                "Matrix" => ClockStyleMatrixBtn,
                "JumpHour" => ClockStyleJumpHourBtn,
                _ => ClockStyleMinimalBtn
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
        if (ActiveVisualizerStyleLabel == null || VisualizerStyleBarsBtn == null) return;

        string style = SettingsService.NormalizeVisualizerStyle(_settingsService.Current.VisualizerStyle);
        var accentBrush = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var activeBg = ThemeBrushes.PillToggleActiveBg;
        var defaultBg = (Brush)FindResource("CardSurfaceBrush");

        StyleCardOptionButton(VisualizerStyleBarsBtn, style == "ClassicBars", accentBrush, activeBg, defaultBg, defaultBorder);
        StyleCardOptionButton(VisualizerStyleBlobBtn, style == "LiquidBlob", accentBrush, activeBg, defaultBg, defaultBorder);
        StyleCardOptionButton(VisualizerStyleAuroraBtn, style == "NeonAurora", accentBrush, activeBg, defaultBg, defaultBorder);
        StyleCardOptionButton(VisualizerStyleSiriBtn, style == "SiriOrb", accentBrush, activeBg, defaultBg, defaultBorder);

        ActiveVisualizerStyleLabel.Text = style switch
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
        bool on = _settingsService.Current.EnableAmbientGlow;
        HaloToggleText.Text = on ? "Glow On" : "Glow Off";
        HaloToggleText.Foreground = on ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
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

        if (EdgeBorderToggleText != null)
        {
            EdgeBorderToggleText.Text = enabled ? "Border On" : "Border Off";
            EdgeBorderToggleText.Foreground = enabled ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
        }
        if (EdgeBorderSubtext != null)
        {
            EdgeBorderSubtext.Text = enabled ? "Subtle edge outline visible around island & dock" : "Edge border hidden for seamless blend";
        }
    }

    private void UpdateSecondaryDropletUI()
    {
        if (_settingsService == null) return;
        bool enabled = _settingsService.Current.EnableSecondaryDroplet;

        if (SecondaryDropletToggleText != null)
        {
            SecondaryDropletToggleText.Text = enabled ? "Droplet On" : "Droplet Off";
            SecondaryDropletToggleText.Foreground = enabled ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
        }
        if (SecondaryDropletSubtext != null)
        {
            SecondaryDropletSubtext.Text = enabled ? "Detached circular bubble when two activities run at once" : "Single unified capsule only (detached bubble off)";
        }
    }

    private void UpdateDndUI()
    {
        if (DndToggleText == null) return;
        bool dnd = _settingsService.Current.DoNotDisturb;
        DndToggleText.Text = dnd ? "DND On" : "DND Off";
        DndToggleText.Foreground = dnd ? ThemeBrushes.Amber : ThemeBrushes.SecondaryText;
        if (NotificationsDndBadge != null)
        {
            NotificationsDndBadge.Visibility = dnd ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateBannerModeUI()
    {
        if (BannerModeText == null) return;
        bool replace = _settingsService.Current.ReplaceWindowsBanners;
        BannerModeText.Text = replace ? "Island Only" : "Island + Win";
        BannerModeText.Foreground = replace ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
    }

    private void UpdateMotionModeUI()
    {
        if (MotionModeText == null) return;
        bool reduced = _settingsService.Current.ReduceMotion;
        MotionModeText.Text = reduced ? "Reduced Motion" : "Liquid Motion";
        MotionModeText.Foreground = reduced ? ThemeBrushes.Amber : ThemeBrushes.Green;
    }

    private void UpdateStartupUI()
    {
        if (StartupToggleText == null) return;
        bool on = StartupService.IsStartupEnabled();
        StartupToggleText.Text = on ? "Startup On" : "Startup Off";
        StartupToggleText.Foreground = on ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
    }

    private void UpdateMonitorUI()
    {
        if (MonitorSwitchText == null) return;
        var monitors = GetAvailableMonitors();
        int idx = Math.Clamp(_settingsService.Current.MonitorIndex, 0, Math.Max(0, monitors.Count - 1));
        MonitorSwitchText.Text = $"Screen {idx + 1}/{Math.Max(1, monitors.Count)}";

        if (MonitorDetailsText != null)
        {
            var topo = DisplayTopologyService.QueryConnectedMonitors(GetTargetMonitor());
            if (idx >= 0 && idx < topo.Count)
            {
                var mon = topo[idx];
                MonitorDetailsText.Text = $"{mon.FriendlyName}: {mon.SummaryText}{(mon.IsPrimary ? " \u2022 Primary" : "")}";
            }
            else
            {
                MonitorDetailsText.Text = $"Display {idx + 1}: Active \u2022 Island Docked";
            }
        }
    }

    internal void MonitorSwitchBtn_Click(object sender, RoutedEventArgs e)
    {
        SwitchToNextMonitor();
    }

    internal void LiquidIntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isApplyingSettings || _settingsService == null || LiquidIntensityText == null) return;
        double pct = Math.Round(e.NewValue);
        double normalized = Math.Clamp(pct / 100.0, 0.0, 1.0);
        if (Math.Abs(_settingsService.Current.LiquidIntensity - normalized) < 0.005) return;

        LiquidIntensityText.Text = $"{pct}%";
        _settingsService.Update(s => s.LiquidIntensity = normalized, nameof(AppSettings.LiquidIntensity));
        DynamicIsland.Core.LiquidGlassTheme.GlobalThicknessScale = (float)Math.Clamp(normalized * 1.5, 0.4, 1.8);

        // Give a live gooey ripple as the user drags so they immediately feel the liquid elasticity
        if (LiquidIntensitySlider.IsMouseCaptureWithin && Math.Abs(_width.Velocity) < 80)
        {
            TriggerLiquidImpulse(140, -55, 18);
        }
    }

    internal void AnimationSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isApplyingSettings || _settingsService == null || AnimationSpeedText == null) return;
        double pct = Math.Round(e.NewValue);
        double normalized = Math.Clamp(pct / 100.0, 0.5, 1.6);
        if (Math.Abs(_settingsService.Current.AnimationSpeed - normalized) < 0.005) return;

        AnimationSpeedText.Text = $"{pct}%";
        _settingsService.Update(s => s.AnimationSpeed = normalized, nameof(AppSettings.AnimationSpeed));

        if (AnimationSpeedSlider.IsMouseCaptureWithin && Math.Abs(_width.Velocity) < 80)
        {
            TriggerLiquidImpulse(70, -25, 8);
        }
    }

    internal void IslandScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isApplyingSettings || _settingsService == null || IslandScaleText == null) return;
        double pct = Math.Round(e.NewValue);
        double normalized = Math.Clamp(pct / 100.0, 0.85, 1.25);
        if (Math.Abs(_settingsService.Current.IslandScale - normalized) < 0.005) return;

        IslandScaleText.Text = $"{pct}%";
        _settingsService.Update(s => s.IslandScale = normalized, nameof(AppSettings.IslandScale));

        if (IslandScaleSlider.IsMouseCaptureWithin && Math.Abs(_width.Velocity) < 80)
        {
            TriggerLiquidImpulse(90, -35, 10);
        }
    }

    internal void BackdropOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isApplyingSettings || _settingsService == null || BackdropOpacityText == null) return;
        double pct = Math.Round(e.NewValue);
        double normalized = Math.Clamp(pct / 100.0, 0.65, 1.0);
        if (Math.Abs(_settingsService.Current.BackdropOpacity - normalized) < 0.005) return;

        BackdropOpacityText.Text = $"{pct}%";
        _settingsService.Update(s => s.BackdropOpacity = normalized, nameof(AppSettings.BackdropOpacity));
    }

    internal void GlassBlurSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isApplyingSettings || _settingsService == null || GlassBlurText == null) return;
        double pct = Math.Round(e.NewValue);
        double normalized = Math.Clamp(pct / 100.0, 0.20, 2.50);
        if (Math.Abs(_settingsService.Current.GlassBlurIntensity - normalized) < 0.005) return;

        GlassBlurText.Text = $"{pct}%";
        _settingsService.Update(s => s.GlassBlurIntensity = normalized, nameof(AppSettings.GlassBlurIntensity));
    }

    internal void GlassRefractionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isApplyingSettings || _settingsService == null || GlassRefractionText == null) return;
        double pct = Math.Round(e.NewValue);
        double normalized = Math.Clamp(pct / 100.0, 0.0, 2.0);
        if (Math.Abs(_settingsService.Current.GlassRefractionIntensity - normalized) < 0.005) return;

        GlassRefractionText.Text = $"{pct}%";
        _settingsService.Update(s => s.GlassRefractionIntensity = normalized, nameof(AppSettings.GlassRefractionIntensity));
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
        if (_isApplyingSettings || _settingsService == null || NotchOffsetText == null) return;
        double val = Math.Round(e.NewValue);
        if (Math.Abs(_settingsService.Current.NotchYOffset - val) < 0.01) return;
        NotchOffsetText.Text = $"{val} px";
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

    private void SwitchSettingsTab(string tab, bool impulse = false)
    {
        _currentSettingsTab = tab;

        if (SettingsAppearanceTab == null || SettingsClockAudioTab == null ||
            SettingsScreenTab == null || SettingsPetTab == null || SettingsShortcutsTab == null)
            return;

        SettingsAppearanceTab.Visibility = tab == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        if (SettingsThemesTab != null)
            SettingsThemesTab.Visibility = tab == "Themes" ? Visibility.Visible : Visibility.Collapsed;
        if (SettingsDropletsTab != null)
            SettingsDropletsTab.Visibility = tab == "Droplets" ? Visibility.Visible : Visibility.Collapsed;
        if (SettingsDockTab != null)
            SettingsDockTab.Visibility = tab == "Dock" ? Visibility.Visible : Visibility.Collapsed;
        SettingsClockAudioTab.Visibility = tab == "Clock" ? Visibility.Visible : Visibility.Collapsed;
        SettingsScreenTab.Visibility = tab == "Screen" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPetTab.Visibility = tab == "Pet" ? Visibility.Visible : Visibility.Collapsed;
        SettingsShortcutsTab.Visibility = tab == "Shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        if (SettingsNotifTab != null)
            SettingsNotifTab.Visibility = tab == "Notifications" ? Visibility.Visible : Visibility.Collapsed;

        if (tab == "Screen")
        {
            UpdateDisplayAndBatterySettingsUI();
            UpdateNotifSettingsUI();
        }
        else if (tab == "Themes")
        {
            UpdateIslandThemeUI();
            UpdateIslandNatureSettingsUI();
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

        var activeBg = (Brush)FindResource("SettingsTabActiveBgBrush");
        var inactiveBg = Brushes.Transparent;
        var activeFg = (Brush)FindResource("PrimaryTextBrush");
        var inactiveFg = (Brush)FindResource("SecondaryBrush");

        void UpdateTab(Button? btn, TextBlock? text, bool isActive)
        {
            if (btn == null) return;
            btn.Background = isActive ? activeBg : inactiveBg;
            if (text != null)
            {
                text.Foreground = isActive ? activeFg : inactiveFg;
                text.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        UpdateTab(SettingsTabAppearanceBtn, SettingsTabAppearanceText, tab == "Appearance");
        UpdateTab(SettingsTabThemesBtn, SettingsTabThemesText, tab == "Themes");
        UpdateTab(SettingsTabDropletsBtn, SettingsTabDropletsText, tab == "Droplets");
        UpdateTab(SettingsTabDockBtn, SettingsTabDockText, tab == "Dock");
        UpdateTab(SettingsTabClockBtn, SettingsTabClockText, tab == "Clock");
        UpdateTab(SettingsTabScreenBtn, SettingsTabScreenText, tab == "Screen");
        UpdateTab(SettingsTabPetBtn, SettingsTabPetText, tab == "Pet");
        UpdateTab(SettingsTabShortcutsBtn, SettingsTabShortcutsText, tab == "Shortcuts");
        UpdateTab(SettingsTabNotifBtn, SettingsTabNotifText, tab == "Notifications");

        if (impulse && !_settingsService.Current.ReduceMotion)
        {
            TriggerLiquidImpulse(30, -10, 4);
        }
    }

    internal void LiquidPulseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService.Current.ReduceMotion)
        {
            _settingsService.Update(s => s.ReduceMotion = false, nameof(AppSettings.ReduceMotion));
        }
        TriggerLiquidImpulse(480, -220, 55);
    }

    private DateTime _lastResetClickTime = DateTime.MinValue;

    internal void ResetSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        if ((DateTime.UtcNow - _lastResetClickTime).TotalSeconds > 3.5)
        {
            _lastResetClickTime = DateTime.UtcNow;
            if (ResetSettingsText != null)
            {
                ResetSettingsText.Text = "Confirm Reset?";
                ResetSettingsText.Foreground = ThemeBrushes.Amber;
            }
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

        _lastResetClickTime = DateTime.MinValue;
        if (ResetSettingsText != null)
        {
            ResetSettingsText.Text = "Reset Defaults";
            ResetSettingsText.Foreground = ThemeBrushes.PrimaryText;
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
        if (VolumeOsdText == null) return;
        bool on = _settingsService.Current.EnableVolumeKeyOsd;
        VolumeOsdText.Text = on ? "Vol Keys On" : "Vol Keys Off";
        VolumeOsdText.Foreground = on ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
        if (VolumeOsdBtn != null)
        {
            VolumeOsdBtn.BorderBrush = on
                ? ThemeBrushes.CreateFrozen(Color.FromArgb(0x45, 0x30, 0xD1, 0x58))
                : Brushes.Transparent;
        }
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

    internal void ExportSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "DynamicIsland_Settings_Backup.json");

        if (_settingsService.ExportToFile(path))
        {
            _shelfService.AddPaths(new[] { path });
            _machine.PushToast(new ToastContent(
                "\uEDE1",
                "Settings Exported",
                "Saved to Downloads & added to Drop Shelf",
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.5),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "settingsio"));
        }
    }

    internal void ImportSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        string? candidate = _shelfService.Items
            .FirstOrDefault(i => i.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))?.FullPath;
        candidate ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "DynamicIsland_Settings_Backup.json");

        if (File.Exists(candidate) && _settingsService.ImportFromFile(candidate))
        {
            TriggerLiquidImpulse(320, 160, 35);
            _machine.PushToast(new ToastContent(
                "\uE8B5",
                "Settings Imported",
                Path.GetFileName(candidate),
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.5),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "settingsio"));
        }
        else
        {
            _machine.PushToast(new ToastContent(
                "\uE8B5",
                "No Backup JSON Found",
                "Export first or drop a .json backup onto Drop Shelf",
                ThemeBrushes.AmberColor,
                TimeSpan.FromSeconds(2.5),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "settingsio"));
        }
    }

    internal void HotkeyRecordBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string action })
        {
            if (_recordingHotkeyAction == action)
            {
                _recordingHotkeyAction = null;
                SetKeyboardFocusable(false);
            }
            else
            {
                _recordingHotkeyAction = action;
                SetKeyboardFocusable(true);
            }
            UpdateHotkeysUI();
        }
    }

    private void UpdateHotkeysUI()
    {
        if (HotkeyBindClipboardText == null) return;
        var custom = _settingsService.Current.CustomHotkeys;

        string FormatVk(string action, string label, uint defaultVk)
        {
            if (_recordingHotkeyAction == action) return "Press key...";
            uint vk = (custom != null && custom.TryGetValue(action, out uint c)) ? c : defaultVk;
            if (vk == 0) return $"{label}: Off";
            Key k = KeyInterop.KeyFromVirtualKey((int)vk);
            bool conflict = _hotkeyRegistrationStatus.TryGetValue(action, out bool ok) && !ok;
            return conflict ? $"{label}: {k} (!)" : $"{label}: {k}";
        }

        HotkeyBindClipboardText.Text = FormatVk("Clipboard", "Clip", 0x56);
        HotkeyBindCalcText.Text = FormatVk("Calculator", "Calc", 0x43);
        HotkeyBindAudioText.Text = FormatVk("AudioDevices", "Audio", 0x4F);
        HotkeyBindWifiText.Text = FormatVk("Network", "Wi-Fi", 0x57);
        HotkeyBindTimerText.Text = FormatVk("Stopwatch", "Timer", 0x41);
        if (HotkeyBindNotifText != null) HotkeyBindNotifText.Text = FormatVk("Notifications", "Alerts", 0x4E);
        if (HotkeyBindHudText != null) HotkeyBindHudText.Text = FormatVk("SystemHud", "HUD", 0x48);
        if (HotkeyBindMemoText != null) HotkeyBindMemoText.Text = FormatVk("VoiceMemo", "Memo", 0x52);
        if (HotkeyBindDevicesText != null) HotkeyBindDevicesText.Text = FormatVk("Peripherals", "BT", 0x44);
        if (HotkeyBindPowerText != null) HotkeyBindPowerText.Text = FormatVk("BatteryPower", "Power", 0x50);
        if (HotkeyBindSettingsText != null) HotkeyBindSettingsText.Text = FormatVk("Settings", "Cfg", 0x53);
    }

    internal void DockPinToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string viewKey })
        {
            _settingsService.Update(s =>
            {
                if (!s.HiddenDockViews.Remove(viewKey))
                {
                    s.HiddenDockViews.Add(viewKey);
                }
            }, nameof(AppSettings.HiddenDockViews));
        }
    }

    internal void SettingsTrimRamBtn_Click(object sender, RoutedEventArgs e)
    {
        long beforeMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
        Core.IslandMemoryManager.ExecuteConservativeTrim();
        long afterMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
        long freed = Math.Max(0, beforeMb - afterMb);
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

    internal void DockPinAllBtn_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Update(s => s.HiddenDockViews.Clear(), nameof(AppSettings.HiddenDockViews));
        TriggerLiquidImpulse(30, -10, 4);
        _machine.PushToast(new ToastContent(
            "\uE718",
            "Dock Strip Updated",
            "All 18 tools enabled in dock strip",
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "dock_pins"));
    }

    internal void DockPinMinimalBtn_Click(object sender, RoutedEventArgs e)
    {
        var toHide = new List<string>
        {
            "notes", "converter", "launcher", "termi",
            "phonebridge", "mechsounds", "capturestudio",
            "systemhud", "voicememo", "peripherals", "network", "micmute"
        };
        _settingsService.Update(s =>
        {
            s.HiddenDockViews.Clear();
            s.HiddenDockViews.AddRange(toHide);
        }, nameof(AppSettings.HiddenDockViews));
        TriggerLiquidImpulse(30, -10, 4);
        _machine.PushToast(new ToastContent(
            "\uE718",
            "Minimalist Dock Active",
            "Pinned core essentials: Shelf, Clipboard, Calc, Audio, Settings",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "dock_pins"));
    }

    internal void DockPinResetBtn_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Update(s => s.HiddenDockViews.Clear(), nameof(AppSettings.HiddenDockViews));
        TriggerLiquidImpulse(30, -10, 4);
        _machine.PushToast(new ToastContent(
            "\uE777",
            "Dock Reset",
            "Dock configuration restored to defaults",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "dock_pins"));
    }

    private void UpdateDockVisibilityUI()
    {
        var hidden = _settingsService.Current.HiddenDockViews;
        bool IsDisabledByDroplet(string viewKey)
        {
            if (_dropletRegistry == null) return false;
            foreach (var d in _dropletRegistry.Items)
            {
                if (string.Equals(d.DockViewKey, viewKey, StringComparison.OrdinalIgnoreCase) && !d.IsEnabled)
                    return true;
            }
            return false;
        }

        int visibleCount = 0;
        int totalTools = 19;

        void ApplyPin(string key, Button? dockBtn, Button? pinBtn, TextBlock? statusText = null)
        {
            bool isHidden = hidden.Contains(key) || IsDisabledByDroplet(key);
            if (dockBtn != null)
            {
                dockBtn.Visibility = isHidden ? Visibility.Collapsed : Visibility.Visible;
                if (!isHidden) visibleCount++;
            }
            if (pinBtn != null)
            {
                pinBtn.Opacity = isHidden ? 0.5 : 1.0;
                pinBtn.BorderBrush = isHidden ? ThemeBrushes.CardBorder : ThemeBrushes.Green;
                pinBtn.Background = isHidden ? ThemeBrushes.DockInactiveBg : ThemeBrushes.PlanSaverActiveBg;
            }
            if (statusText != null)
            {
                statusText.Text = isHidden ? "Hidden" : "Visible";
                statusText.Foreground = isHidden ? ThemeBrushes.SecondaryText : ThemeBrushes.Green;
            }
        }

        ApplyPin("shelf", ShelfButton, DockPinShelfBtn, DockPinShelfText);
        ApplyPin("clipboard", ClipboardButton, DockPinClipBtn, DockPinClipText);
        ApplyPin("calc", CalcButton, DockPinCalcBtn, DockPinCalcText);
        ApplyPin("notes", NotesButton, DockPinNotesBtn, DockPinNotesText);
        ApplyPin("converter", ConverterButton, DockPinConverterBtn, DockPinConverterText);
        ApplyPin("widgets", WidgetsGalleryButton, null, null);
        ApplyPin("launcher", LauncherButton, DockPinLauncherBtn, DockPinLauncherText);
        ApplyPin("termi", TermiButton, DockPinTermiBtn, DockPinTermiText);
        ApplyPin("phonebridge", PhoneBridgeButton, DockPinPhoneBridgeBtn, DockPinPhoneBridgeText);
        ApplyPin("mechsounds", MechSoundsButton, DockPinMechSoundsBtn, DockPinMechSoundsText);
        ApplyPin("capturestudio", CaptureStudioButton, DockPinCaptureStudioBtn, DockPinCaptureStudioText);
        ApplyPin("audio", AudioButton, DockPinAudioBtn, DockPinAudioText);
        ApplyPin("battery", BatteryButton, DockPinBatteryBtn, DockPinBatteryText);
        ApplyPin("systemhud", SystemHudButton, DockPinHudBtn, DockPinHudText);
        ApplyPin("voicememo", RecordButton, DockPinMemoBtn, DockPinMemoText);
        ApplyPin("peripherals", PeripheralsButton, DockPinDevicesBtn, DockPinDevicesText);
        ApplyPin("network", NetworkButton, DockPinWifiBtn, DockPinWifiText);
        ApplyPin("notifications", NotificationsButton, DockPinNotifBtn, DockPinNotifText);
        ApplyPin("micmute", MicMuteButton, DockPinMicBtn, DockPinMicText);

        if (DockHeaderSummaryText != null)
        {
            DockHeaderSummaryText.Text = $"{visibleCount} / {totalTools} Visible in Dock";
        }
    }

    private string _activeDropletCategory = "All";
    private string _dropletSearchFilter = "";

    internal void DropletFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string cat })
        {
            _activeDropletCategory = cat;
            TriggerLiquidImpulse(18, -6, 2);
            UpdateDropletsUI();
        }
    }

    internal void DropletSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _dropletSearchFilter = DropletSearchBox?.Text?.Trim() ?? "";
        UpdateDropletsUI();
    }

    internal void DropletEnableAllBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_dropletRegistry == null) return;
        _dropletRegistry.SetAll(true);
        UpdateDropletsUI();
        UpdateDockVisibilityUI();
        TriggerLiquidImpulse(30, -10, 4);
        _machine.PushToast(new ToastContent(
            "\uE73E",
            "Modular Droplets",
            $"All {_dropletRegistry.Items.Count} droplets enabled",
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "droplets"));
    }

    internal void DropletDisableAllBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_dropletRegistry == null) return;
        _dropletRegistry.SetAll(false);
        UpdateDropletsUI();
        UpdateDockVisibilityUI();
        TriggerLiquidImpulse(30, -10, 4);
        _machine.PushToast(new ToastContent(
            "\uE711",
            "Modular Droplets",
            "All droplets disabled (0 KB RAM, 0% CPU)",
            ThemeBrushes.AmberColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "droplets"));
    }

    internal void DropletResetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_dropletRegistry == null) return;
        _dropletRegistry.ResetToDefaults();
        UpdateDropletsUI();
        UpdateDockVisibilityUI();
        TriggerLiquidImpulse(30, -10, 4);
        _machine.PushToast(new ToastContent(
            "\uE777",
            "Modular Droplets",
            "Reset to factory default droplet configuration",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "droplets"));
    }

    internal void DropletToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string dropletId } && _dropletRegistry != null)
        {
            bool next = _dropletRegistry.Toggle(dropletId);
            var desc = _dropletRegistry.Get(dropletId);
            UpdateDropletsUI();
            UpdateDockVisibilityUI();
            TriggerLiquidImpulse(24, -8, 3);
            if (_keyboardSoundService != null && _dropletRegistry.IsEnabled("mech_sounds"))
            {
                _keyboardSoundService.PlayKeyClick();
            }
            if (desc != null)
            {
                _machine.PushToast(new ToastContent(
                    desc.Glyph,
                    next ? $"{desc.Title} Enabled" : $"{desc.Title} Disabled",
                    next ? desc.Subtitle : "Hooks & timers unloaded (0 KB overhead, 0% CPU)",
                    next ? ThemeBrushes.GreenColor : ThemeBrushes.MutedGreyColor,
                    TimeSpan.FromSeconds(2.0),
                    TargetView: "settings",
                    Priority: ToastPriority.Feedback,
                    Category: "droplets"));
            }
        }
    }

    internal void DropletActionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string dropletId })
        {
            switch (dropletId.ToLowerInvariant())
            {
                case "notes":
                    SwitchExpandedView(ExpandedView.Notes);
                    break;
                case "clipboard_pro":
                    SummonClipboardPopupWindow();
                    break;
                case "element_capture":
                    _machine.ExpandNow();
                    SwitchExpandedView(ExpandedView.CaptureStudio);
                    CaptureStudioContainer?.UpdatePinnedCountBadge();
                    TriggerLiquidImpulse(32, -10, 4);
                    break;
                case "jiggle_basket":
                    SummonBasketButton_Click(sender, e);
                    _machine.PushToast(new ToastContent(
                        "\uE8B7",
                        "Floating Drop Basket",
                        "Summoned basket at cursor · Drop files anywhere",
                        ThemeBrushes.GreenColor,
                        TimeSpan.FromSeconds(2.0),
                        TargetView: "shelf",
                        Priority: ToastPriority.Feedback,
                        Category: "basket"));
                    break;
                case "phone_bridge":
                    _machine.ExpandNow();
                    SwitchExpandedView(ExpandedView.PhoneBridge);
                    PhoneBridgeContainer?.RefreshSession();
                    TriggerLiquidImpulse(32, -10, 4);
                    break;
                case "file_converter":
                    _machine.ExpandNow();
                    SwitchExpandedView(ExpandedView.Converter);
                    TriggerLiquidImpulse(32, -10, 4);
                    break;
                case "drop_shelf":
                    SwitchExpandedView(ExpandedView.Shelf);
                    break;
                case "quick_calc":
                    SwitchExpandedView(ExpandedView.Calc);
                    break;
                case "voice_memo":
                case "voice_transcription":
                    SwitchExpandedView(ExpandedView.VoiceMemo);
                    break;
                case "system_hud":
                    SwitchExpandedView(ExpandedView.SystemHud);
                    break;
                case "peripherals":
                    SwitchExpandedView(ExpandedView.Peripherals);
                    break;
                case "caffeine":
                    SwitchExpandedView(ExpandedView.Battery);
                    break;
                case "mech_sounds":
                    _machine.ExpandNow();
                    SwitchExpandedView(ExpandedView.MechSounds);
                    MechSoundsContainer?.RefreshUI();
                    TriggerLiquidImpulse(32, -10, 4);
                    break;
                case "quick_launcher":
                    _machine.ExpandNow();
                    SwitchExpandedView(ExpandedView.Launcher);
                    LauncherContainer?.RefreshItems();
                    TriggerLiquidImpulse(32, -10, 4);
                    break;
                case "termi_notch":
                    _machine.ExpandNow();
                    SwitchExpandedView(ExpandedView.Termi);
                    TermiContainer?.PopulateSnippetPills();
                    TriggerLiquidImpulse(32, -10, 4);
                    break;
                case "window_snap":
                    _machine.PushToast(new ToastContent(
                        "\uE7C4",
                        "Window Snapping Active",
                        "Drag any window to top (maximize), edges (halves), or corners (quarters)",
                        ThemeBrushes.BlueColor,
                        TimeSpan.FromSeconds(3.5),
                        TargetView: "settings",
                        Priority: ToastPriority.Feedback,
                        Category: "snap"));
                    TriggerLiquidImpulse(28, -10, 4);
                    break;
            }
        }
    }

    private void UpdateDropletsUI()
    {
        if (_dropletRegistry == null) return;

        // Update Category Filter Counts
        int totalCount = _dropletRegistry.Items.Count;
        int prodCount = _dropletRegistry.Items.Count(d => d.Category == Core.DropletCategory.Productivity);
        int mediaCount = _dropletRegistry.Items.Count(d => d.Category == Core.DropletCategory.Media);
        int sysCount = _dropletRegistry.Items.Count(d => d.Category == Core.DropletCategory.System);
        int capCount = _dropletRegistry.Items.Count(d => d.Category == Core.DropletCategory.Capture);

        if (DropletFilterAllText != null) DropletFilterAllText.Text = $"All ({totalCount})";
        if (DropletFilterProdText != null) DropletFilterProdText.Text = $"⚡ Productivity ({prodCount})";
        if (DropletFilterMediaText != null) DropletFilterMediaText.Text = $"🎵 Media ({mediaCount})";
        if (DropletFilterSysText != null) DropletFilterSysText.Text = $"⚙️ System ({sysCount})";
        if (DropletFilterCapText != null) DropletFilterCapText.Text = $"📸 Capture ({capCount})";

        // Filter active styling
        var activeBg = (Brush)FindResource("SettingsTabActiveBgBrush");
        var inactiveBg = Brushes.Transparent;
        var activeFg = (Brush)FindResource("PrimaryTextBrush");
        var inactiveFg = (Brush)FindResource("SecondaryBrush");

        void UpdateFilterBtn(Button? btn, TextBlock? text, bool isActive)
        {
            if (btn == null) return;
            btn.Background = isActive ? activeBg : inactiveBg;
            if (text != null)
            {
                text.Foreground = isActive ? activeFg : inactiveFg;
                text.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        UpdateFilterBtn(DropletFilterAllBtn, DropletFilterAllText, string.Equals(_activeDropletCategory, "All", StringComparison.OrdinalIgnoreCase));
        UpdateFilterBtn(DropletFilterProdBtn, DropletFilterProdText, string.Equals(_activeDropletCategory, "Productivity", StringComparison.OrdinalIgnoreCase));
        UpdateFilterBtn(DropletFilterMediaBtn, DropletFilterMediaText, string.Equals(_activeDropletCategory, "Media", StringComparison.OrdinalIgnoreCase));
        UpdateFilterBtn(DropletFilterSysBtn, DropletFilterSysText, string.Equals(_activeDropletCategory, "System", StringComparison.OrdinalIgnoreCase));
        UpdateFilterBtn(DropletFilterCapBtn, DropletFilterCapText, string.Equals(_activeDropletCategory, "Capture", StringComparison.OrdinalIgnoreCase));

        // Filter droplets collection
        IEnumerable<Core.DropletDescriptor> filtered = _dropletRegistry.Items;
        if (!string.Equals(_activeDropletCategory, "All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(d => string.Equals(d.CategoryLabel, _activeDropletCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_dropletSearchFilter))
        {
            filtered = filtered.Where(d =>
                d.Title.Contains(_dropletSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                d.Subtitle.Contains(_dropletSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                d.CategoryLabel.Contains(_dropletSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                d.FeatureBadge.Contains(_dropletSearchFilter, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.ToList();
        if (DropletsItemsControl != null)
        {
            DropletsItemsControl.ItemsSource = list;
        }

        if (DropletsEmptyPlaceholder != null)
        {
            DropletsEmptyPlaceholder.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (DropletsHeaderSummaryText != null)
        {
            DropletsHeaderSummaryText.Text = $"{_dropletRegistry.EnabledCount} / {_dropletRegistry.Items.Count} Active · ~{_dropletRegistry.TotalActiveRamKb} KB";
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
        bool dnd = _settingsService?.Current.DoNotDisturb ?? false;
        if (SettingsDndToggleText != null)
        {
            SettingsDndToggleText.Text = dnd ? "Active" : "Off";
            SettingsDndToggleText.Foreground = dnd ? ThemeBrushes.Amber : ThemeBrushes.SecondaryText;
        }
        bool replace = _settingsService?.Current.ReplaceWindowsBanners ?? true;
        if (ReplaceBannersToggleText != null)
        {
            ReplaceBannersToggleText.Text = replace ? "Active" : "Off";
            ReplaceBannersToggleText.Foreground = replace ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
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

    internal void ThemeSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string themeName })
        {
            _settingsService.Update(s => s.ThemeName = themeName, nameof(AppSettings.ThemeName));
            _machine.PushToast(new ToastContent(
                "\uE790",
                "Theme Applied",
                themeName,
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(1.8),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "theme"));
        }
    }

    internal void ThemeModeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string mode })
        {
            _settingsService.Update(s => s.ThemeMode = mode, nameof(AppSettings.ThemeMode));
            _machine.PushToast(new ToastContent(
                mode == "Light" ? "\uE706" : (mode == "Dark" ? "\uE708" : "\uE770"),
                "Theme Mode",
                mode == "System" ? $"System ({ThemeManager.CurrentTheme})" : $"{mode} Mode",
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(1.8),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "theme"));
        }
    }

    private void UpdateThemeModeUI()
    {
        if (ThemeModeSystemBtn == null || ThemeModeDarkBtn == null || ThemeModeLightBtn == null) return;

        string current = SettingsService.NormalizeThemeMode(_settingsService.Current.ThemeMode);
        var accentBrush = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var defaultBg = (Brush)FindResource("IslandButtonBgBrush");
        var primaryText = (Brush)FindResource("PrimaryTextBrush");

        StyleAccentToggleButton(ThemeModeSystemBtn, current == "System", accentBrush, defaultBg, defaultBorder, ThemeModeSystemText, primaryText, boldWhenActive: true);
        StyleAccentToggleButton(ThemeModeDarkBtn, current == "Dark", accentBrush, defaultBg, defaultBorder, ThemeModeDarkText, primaryText, boldWhenActive: true);
        StyleAccentToggleButton(ThemeModeLightBtn, current == "Light", accentBrush, defaultBg, defaultBorder, ThemeModeLightText, primaryText, boldWhenActive: true);

        if (ThemeModeSubtext != null)
        {
            ThemeModeSubtext.Text = current == "System"
                ? $"Auto ({ThemeManager.CurrentTheme})"
                : $"Manual ({current})";
        }
    }

    internal void ThemeStyleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string style })
        {
            string normalized = SettingsService.NormalizeThemeStyle(style);
            _settingsService.Update(s => s.ThemeStyle = normalized, nameof(AppSettings.ThemeStyle));

            bool isLiquid = ThemeManager.IsLiquidGlass;
            _machine.PushToast(new ToastContent(
                isLiquid ? "\uE781" : "\uE790",
                "UI Theme",
                isLiquid ? "Apple Liquid Glass (Frosted Material)" : "Default (Classic OLED)",
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(1.8),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "theme"));
        }
    }

    private void UpdateThemeStyleUI()
    {
        if (ThemeStyleDefaultBtn == null || ThemeStyleLiquidGlassBtn == null) return;

        string current = SettingsService.NormalizeThemeStyle(_settingsService.Current.ThemeStyle);
        var accentBrush = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var defaultBg = (Brush)FindResource("IslandButtonBgBrush");
        var primaryText = (Brush)FindResource("PrimaryTextBrush");

        StyleAccentToggleButton(ThemeStyleDefaultBtn, current == "Default", accentBrush, defaultBg, defaultBorder, ThemeStyleDefaultText, primaryText, boldWhenActive: true);
        StyleAccentToggleButton(ThemeStyleLiquidGlassBtn, current == "Liquid Glass", accentBrush, defaultBg, defaultBorder, ThemeStyleLiquidGlassText, primaryText, boldWhenActive: true);

        if (ThemeStyleSubtext != null)
        {
            ThemeStyleSubtext.Text = current == "Liquid Glass"
                ? "Apple Liquid Glass UI · Frosted Material & Specular Lensing"
                : "Classic OLED Capsule";
        }
    }

    private void ApplyThemeStyleVisuals()
    {
        bool isLiquid = ThemeManager.IsLiquidGlass;
        bool isDark = ThemeManager.IsDark;

        if (GlassFrostingStatusText != null)
        {
            GlassFrostingStatusText.Text = isLiquid ? "Active" : "OLED Mode";
        }

        if (_hwnd != IntPtr.Zero)
        {
            DynamicIsland.Interop.WindowBlurHelper.DisableFrostedGlass(_hwnd);
        }

        if (PillDropShadow != null)
        {
            if (isLiquid)
            {
                PillDropShadow.BlurRadius = 28;
                PillDropShadow.ShadowDepth = 4;
                PillDropShadow.Opacity = isDark ? 0.65 : 0.25;
                PillDropShadow.Color = isDark ? Colors.Black : Color.FromRgb(0x20, 0x30, 0x50);
            }
            else
            {
                PillDropShadow.BlurRadius = 20;
                PillDropShadow.ShadowDepth = 2;
                PillDropShadow.Opacity = 0.55;
                PillDropShadow.Color = Colors.Black;
            }
        }

        if (isLiquid && DynamicIsland.LiquidGlass.GlassManager.Instance.IsEnabled)
        {
            // Collapse fake overlay borders — the GPU shader already renders specular rim and bevel
            if (PillGlassSheen != null) PillGlassSheen.Visibility = System.Windows.Visibility.Collapsed;
            if (PillStroke != null) PillStroke.Visibility = System.Windows.Visibility.Collapsed;

            if (FloatingDockHost != null)
            {
                DynamicIsland.LiquidGlass.GlassHost.Attach(FloatingDockHost, DynamicIsland.LiquidGlass.GlassPresets.Dock);
            }
            UpdatePillLiquidGlass(Pill.ActualWidth > 0 ? Pill.ActualWidth : 148, Pill.ActualHeight > 0 ? Pill.ActualHeight : 30, 15.0f, _dropletOffset?.Value ?? 0);
        }
        else
        {
            // Restore fake overlay borders for non-liquid themes
            if (PillGlassSheen != null) PillGlassSheen.Visibility = System.Windows.Visibility.Visible;
            if (PillStroke != null) PillStroke.Visibility = System.Windows.Visibility.Visible;

            if (PillBackground != null)
            {
                PillBackground.SetResourceReference(Border.BackgroundProperty, "PillBackgroundBrush");
            }
            if (FloatingDockHost != null)
            {
                DynamicIsland.LiquidGlass.GlassHost.Detach(FloatingDockHost);
                FloatingDockHost.SetResourceReference(Border.BackgroundProperty, "FloatingDockBackgroundBrush");
            }
        }

        EnsureRendering();
    }

    private void ApplyIslandTheme(string? theme)
    {
        string normalized = SettingsService.NormalizeIslandTheme(theme);
        _isNatureThemeActive = normalized != "None";

        if (IslandThemeUnderlay != null)
        {
            IslandThemeUnderlay.ThemeName = normalized;
            IslandThemeUnderlay.Visibility = _isNatureThemeActive ? Visibility.Visible : Visibility.Collapsed;
        }
        if (IslandThemeOverlay != null)
        {
            IslandThemeOverlay.ThemeName = normalized;
            IslandThemeOverlay.Visibility = _isNatureThemeActive ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateNatureThemeTimerState();
    }

    private void UpdateIslandThemeUI()
    {
        if (IslandThemeSubtext == null) return;

        string current = SettingsService.NormalizeIslandTheme(_settingsService.Current.IslandTheme);
        var accentBrush = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var defaultBg = (Brush)FindResource("IslandButtonBgBrush");

        void StyleThemeBtn(Button? btn, TextBlock? text, bool active)
        {
            if (btn == null) return;
            btn.Background = active ? accentBrush : defaultBg;
            btn.BorderBrush = active ? accentBrush : defaultBorder;
            if (text != null)
            {
                text.Foreground = active ? ThemeBrushes.DarkInk : (Brush)FindResource("PrimaryTextBrush");
                text.FontWeight = active ? FontWeights.Bold : FontWeights.SemiBold;
            }
        }

        StyleThemeBtn(IslandThemeNoneBtn, IslandThemeNoneText, current == "None");
        StyleThemeBtn(IslandThemeIvyBtn, IslandThemeIvyText, current == "Ivy & Blooms");
        StyleThemeBtn(IslandThemeMeadowBtn, IslandThemeMeadowText, current == "Meadow");

        StyleThemeBtn(ThemesTabNoneBtn, ThemesTabNoneText, current == "None");
        StyleThemeBtn(ThemesTabIvyBtn, ThemesTabIvyText, current == "Ivy & Blooms");
        StyleThemeBtn(ThemesTabMeadowBtn, ThemesTabMeadowText, current == "Meadow");

        if (NatureActiveThemeBadge != null) NatureActiveThemeBadge.Text = current;
        if (NatureThemeSubtext != null)
        {
            NatureThemeSubtext.Text = current switch
            {
                "Ivy & Blooms" => "Climbing leaves, morning glories, wild roses & butterflies",
                "Meadow" => "Wild meadow grass, daisies, poppies & lavender",
                _ => "Clean island capsule without decorations"
            };
        }

        IslandThemeSubtext.Text = current switch
        {
            "Ivy & Blooms" => "Leaves & Blooms · Climbing vines, leaves & butterflies",
            "Meadow" => "Meadow · Daisies, poppies, lavender & drifting monarch",
            _ => "Clean · No nature decorations"
        };
    }

    internal void IslandThemeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string themeName })
        {
            string normalized = SettingsService.NormalizeIslandTheme(themeName);
            _settingsService.Update(s => s.IslandTheme = normalized, nameof(AppSettings.IslandTheme));
            _machine.PushToast(new ToastContent(
                "\uE790",
                "Theme Applied",
                normalized,
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "islandtheme"));
        }
    }

    private void UpdateIslandNatureSettingsUI()
    {
        if (_settingsService?.Current == null) return;
        var cur = _settingsService.Current;
        var accentBrush = TryFindResource("AccentBrush") as Brush ?? (Resources["AccentBrush"] as Brush) ?? ThemeBrushes.Blue;
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var defaultBg = (Brush)FindResource("IslandButtonBgBrush");

        // 1. Sliders & Text Readouts
        if (NatureDensitySlider != null)
        {
            NatureDensitySlider.Value = cur.NatureDensity * 100.0;
        }
        if (NatureDensityText != null)
        {
            int pct = (int)Math.Round(cur.NatureDensity * 100.0);
            string desc = pct <= 40 ? "Minimal" : (pct <= 80 ? "Balanced" : "Lush");
            NatureDensityText.Text = $"{pct}% · {desc}";
        }

        if (NatureBloomSlider != null)
        {
            NatureBloomSlider.Value = cur.NatureBloomDensity * 100.0;
        }
        if (NatureBloomText != null)
        {
            int pct = (int)Math.Round(cur.NatureBloomDensity * 100.0);
            string desc = pct <= 40 ? "Accents" : (pct <= 85 ? "Balanced" : "Full Bloom");
            NatureBloomText.Text = $"{pct}% · {desc}";
        }

        if (NatureScaleSlider != null)
        {
            NatureScaleSlider.Value = cur.NatureScale * 100.0;
        }
        if (NatureScaleText != null)
        {
            int pct = (int)Math.Round(cur.NatureScale * 100.0);
            string desc = pct <= 65 ? "Delicate" : (pct <= 95 ? "Standard" : "Bold");
            NatureScaleText.Text = $"{pct}% · {desc}";
        }

        if (NatureDropSlider != null)
        {
            NatureDropSlider.Value = cur.NatureDropLength;
        }
        if (NatureDropText != null)
        {
            int drop = (int)Math.Round(cur.NatureDropLength);
            string desc = drop <= 35 ? "Rim Hugger" : (drop <= 70 ? "Moderate" : "Deep Cascade");
            NatureDropText.Text = $"{drop} px · {desc}";
        }

        // 2. Butterfly Count
        void StyleCountBtn(Button? btn, TextBlock? text, bool active)
        {
            if (btn == null) return;
            btn.Background = active ? accentBrush : defaultBg;
            btn.BorderBrush = active ? accentBrush : defaultBorder;
            if (text != null)
            {
                text.Foreground = active ? ThemeBrushes.DarkInk : (Brush)FindResource("PrimaryTextBrush");
                text.FontWeight = active ? FontWeights.Bold : FontWeights.SemiBold;
            }
        }

        StyleCountBtn(NatureButterfly0Btn, NatureButterfly0Text, cur.NatureButterflyCount == 0);
        StyleCountBtn(NatureButterfly1Btn, NatureButterfly1Text, cur.NatureButterflyCount == 1);
        StyleCountBtn(NatureButterfly2Btn, NatureButterfly2Text, cur.NatureButterflyCount == 2);
        StyleCountBtn(NatureButterfly3Btn, NatureButterfly3Text, cur.NatureButterflyCount == 3);
        StyleCountBtn(NatureButterfly4Btn, NatureButterfly4Text, cur.NatureButterflyCount >= 4);

        if (NatureButterflySubtext != null)
        {
            NatureButterflySubtext.Text = cur.NatureButterflyCount switch
            {
                0 => "0 Butterflies · No motion distraction",
                1 => "1 Butterfly · Monarch solo flight",
                2 => "2 Butterflies · Monarch & Blue Morpho pair",
                3 => "3 Butterflies · Monarch, Blue Morpho & White Swallowtail",
                _ => $"{cur.NatureButterflyCount} Butterflies · Swarm companions"
            };
        }

        // 3. Drifting Pollen
        if (NaturePollenToggleText != null)
        {
            NaturePollenToggleText.Text = cur.NaturePollenEnabled ? "Active" : "Disabled";
            NaturePollenToggleText.Foreground = cur.NaturePollenEnabled ? ThemeBrushes.Green : (Brush)FindResource("SecondaryBrush");
        }
        if (NaturePollenSlider != null)
        {
            NaturePollenSlider.Value = cur.NaturePollenCount;
            NaturePollenSlider.IsEnabled = cur.NaturePollenEnabled;
        }
        if (NaturePollenText != null)
        {
            NaturePollenText.Text = cur.NaturePollenEnabled ? $"{cur.NaturePollenCount} motes" : "Off";
        }
    }

    internal void NatureDensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isApplyingSettings || _settingsService?.Current == null) return;
        double val = Math.Clamp(e.NewValue / 100.0, 0.20, 1.50);
        if (Math.Abs(_settingsService.Current.NatureDensity - val) < 0.01) return;
        _settingsService.Update(s => s.NatureDensity = val, nameof(AppSettings.NatureDensity));
    }

    internal void NatureBloomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isApplyingSettings || _settingsService?.Current == null) return;
        double val = Math.Clamp(e.NewValue / 100.0, 0.20, 1.50);
        if (Math.Abs(_settingsService.Current.NatureBloomDensity - val) < 0.01) return;
        _settingsService.Update(s => s.NatureBloomDensity = val, nameof(AppSettings.NatureBloomDensity));
    }

    internal void NatureScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isApplyingSettings || _settingsService?.Current == null) return;
        double val = Math.Clamp(e.NewValue / 100.0, 0.40, 1.40);
        if (Math.Abs(_settingsService.Current.NatureScale - val) < 0.01) return;
        _settingsService.Update(s => s.NatureScale = val, nameof(AppSettings.NatureScale));
    }

    internal void NatureDropSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isApplyingSettings || _settingsService?.Current == null) return;
        double val = Math.Clamp(e.NewValue, 20.0, 120.0);
        if (Math.Abs(_settingsService.Current.NatureDropLength - val) < 0.5) return;
        _settingsService.Update(s => s.NatureDropLength = val, nameof(AppSettings.NatureDropLength));
    }

    internal void NatureButterflyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService?.Current == null) return;
        if (sender is FrameworkElement { Tag: string tagStr } && int.TryParse(tagStr, out int count))
        {
            _settingsService.Update(s => s.NatureButterflyCount = Math.Clamp(count, 0, 4), nameof(AppSettings.NatureButterflyCount));
        }
    }

    internal void NaturePollenSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isApplyingSettings || _settingsService?.Current == null) return;
        int val = (int)Math.Round(e.NewValue);
        if (_settingsService.Current.NaturePollenCount == val) return;
        _settingsService.Update(s => s.NaturePollenCount = val, nameof(AppSettings.NaturePollenCount));
    }

    internal void NaturePollenToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Update(s => s.NaturePollenEnabled = !s.NaturePollenEnabled, nameof(AppSettings.NaturePollenEnabled));
    }

    internal void NaturePresetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService?.Current == null) return;
        if (sender is FrameworkElement { Tag: string preset })
        {
            switch (preset)
            {
                case "Minimalist":
                    _settingsService.Update(s =>
                    {
                        s.NatureDensity = 0.35;
                        s.NatureScale = 0.70;
                        s.NatureDropLength = 25.0;
                        s.NatureButterflyCount = 0;
                        s.NatureBloomDensity = 0.30;
                        s.NaturePollenEnabled = false;
                        s.NaturePollenCount = 0;
                    }, nameof(AppSettings.NatureDensity));
                    break;

                case "Subtle":
                    _settingsService.Update(s =>
                    {
                        s.NatureDensity = 0.55;
                        s.NatureScale = 0.80;
                        s.NatureDropLength = 40.0;
                        s.NatureButterflyCount = 1;
                        s.NatureBloomDensity = 0.50;
                        s.NaturePollenEnabled = true;
                        s.NaturePollenCount = 8;
                    }, nameof(AppSettings.NatureDensity));
                    break;

                case "Balanced":
                    _settingsService.Update(s =>
                    {
                        s.NatureDensity = 0.70;
                        s.NatureScale = 0.85;
                        s.NatureDropLength = 60.0;
                        s.NatureButterflyCount = 2;
                        s.NatureBloomDensity = 0.75;
                        s.NaturePollenEnabled = true;
                        s.NaturePollenCount = 14;
                    }, nameof(AppSettings.NatureDensity));
                    break;

                case "Lush":
                    _settingsService.Update(s =>
                    {
                        s.NatureDensity = 1.05;
                        s.NatureScale = 1.00;
                        s.NatureDropLength = 90.0;
                        s.NatureButterflyCount = 3;
                        s.NatureBloomDensity = 1.10;
                        s.NaturePollenEnabled = true;
                        s.NaturePollenCount = 22;
                    }, nameof(AppSettings.NatureDensity));
                    break;
            }

            _machine.PushToast(new ToastContent(
                "\uE790",
                "Preset Applied",
                preset,
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "naturepreset"));
        }
    }

    internal void GoToThemesTabBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        SwitchSettingsTab("Themes", impulse: true);
    }

    // =================================================================
    //  GitHub Releases & Version Control Manager
    // =================================================================

    private void CheckPostUpdateLaunchStatus()
    {
        if (App.IsPostUpdateLaunch)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(1200);
                _machine.PushToast(new ToastContent(
                    Glyph: "\uE73E",
                    Title: "Updated to v" + _updateService.CurrentVersionString,
                    Subtitle: "Dynamic Island has been successfully upgraded.",
                    Accent: ThemeBrushes.GreenColor,
                    Duration: TimeSpan.FromSeconds(4.5),
                    Priority: ToastPriority.Feedback
                ));
            });
        }
    }

    private void OnUpdateAvailable(UpdateManifest manifest)
    {
        if (manifest.IsCritical)
        {
            _machine.PushToast(new ToastContent(
                Glyph: "\uE7BA",
                Title: $"⚠️ Critical Update: v{manifest.Version}",
                Subtitle: string.IsNullOrWhiteSpace(manifest.Summary) ? "Mandatory security update required. Click to install." : manifest.Summary,
                Accent: Color.FromRgb(0xFF, 0x45, 0x3A),
                Duration: TimeSpan.FromSeconds(10.0),
                TargetView: "settings",
                Priority: ToastPriority.Critical
            ));
        }
        else
        {
            _machine.PushToast(new ToastContent(
                Glyph: "\uE896",
                Title: $"Dynamic Island v{manifest.Version} Available",
                Subtitle: string.IsNullOrWhiteSpace(manifest.Summary) ? "Click to open settings and install" : manifest.Summary,
                Accent: Color.FromRgb(0x0A, 0x84, 0xFF),
                Duration: TimeSpan.FromSeconds(6.0),
                TargetView: "settings",
                Priority: ToastPriority.Feedback
            ));
        }

        UpdateSettingsVersionAndStatusUI();
    }

    private void OnUpdateStatusChanged(UpdateStatus status, string? message)
    {
        if (status == UpdateStatus.ReadyToInstall && _updateService.LatestManifest != null)
        {
            _machine.PushToast(new ToastContent(
                Glyph: "\uE73E",
                Title: $"Dynamic Island v{_updateService.LatestManifest.Version} Ready",
                Subtitle: "Update downloaded & verified. Click to restart and update.",
                Accent: ThemeBrushes.GreenColor,
                Duration: TimeSpan.FromSeconds(7.0),
                TargetView: "settings",
                Priority: ToastPriority.Feedback
            ));
        }

        UpdateSettingsVersionAndStatusUI();
    }

    private void OnUpdateDownloadProgressChanged(UpdateProgressInfo info)
    {
        if (UpdateProgressBar != null)
        {
            UpdateProgressBar.Visibility = Visibility.Visible;
            UpdateProgressBar.Value = info.Percent;
        }

        if (UpdateProgressText != null)
        {
            UpdateProgressText.Visibility = Visibility.Visible;
            UpdateProgressText.Text = info.StatusMessage;
        }
    }

    internal void UpdateSettingsVersionAndStatusUI()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(UpdateSettingsVersionAndStatusUI);
            return;
        }

        if (AppVersionText != null)
        {
            AppVersionText.Text = _updateService.CurrentVersionString;
        }

        if (UpdateChannelText != null)
        {
            UpdateChannelText.Text = _settingsService.Current.UpdateChannel;
        }

        if (AutoCheckToggleText != null)
        {
            bool auto = _settingsService.Current.AutoCheckUpdates;
            AutoCheckToggleText.Text = auto ? "On" : "Off";
            AutoCheckToggleText.Foreground = auto ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
        }

        if (AutoDownloadToggleText != null)
        {
            bool autoDl = _settingsService.Current.AutoDownloadUpdates;
            AutoDownloadToggleText.Text = autoDl ? "On" : "Off";
            AutoDownloadToggleText.Foreground = autoDl ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
        }

        if (GitHubTokenBox != null && !GitHubTokenBox.IsFocused && !GitHubTokenBox.IsKeyboardFocused)
        {
            string stored = _settingsService.Current.GitHubToken ?? "";
            if (!string.Equals(GitHubTokenBox.Password, stored, StringComparison.Ordinal))
            {
                GitHubTokenBox.Password = stored;
            }
        }

        if (LastCheckTimestampText != null)
        {
            var last = _settingsService.Current.LastUpdateCheckTime;
            LastCheckTimestampText.Text = last.HasValue
                ? $"Last checked: {last.Value.ToLocalTime():g}"
                : "Last checked: Never";
        }

        if (UpdateStatusSubtitleText != null)
        {
            UpdateStatusSubtitleText.Text = _updateService.Status switch
            {
                UpdateStatus.Checking => "Connecting to GitHub...",
                UpdateStatus.Downloading => "Downloading update package...",
                UpdateStatus.Verifying => "Verifying package integrity (SHA-256)...",
                UpdateStatus.ReadyToInstall => "Update package verified and ready to install.",
                UpdateStatus.Installing => "Restarting Dynamic Island...",
                UpdateStatus.Failed => _updateService.LastErrorMessage ?? "Update check failed.",
                UpdateStatus.UpToDate => $"Dynamic Island {_updateService.CurrentVersionString} is up to date.",
                _ => "Check releases, switch channels, or rollback"
            };
        }

        if (_updateService.Status is UpdateStatus.Idle or UpdateStatus.UpToDate)
        {
            if (UpdateProgressBar != null) UpdateProgressBar.Visibility = Visibility.Collapsed;
            if (UpdateProgressText != null) UpdateProgressText.Visibility = Visibility.Collapsed;
        }

        if (UpdateAvailableBanner != null)
        {
            bool hasUpdate = _updateService.LatestManifest != null &&
                             _updateService.LatestManifest.ParsedVersion > _updateService.CurrentVersion;

            UpdateAvailableBanner.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;

            if (hasUpdate && _updateService.LatestManifest != null)
            {
                if (UpdateVersionTitleText != null)
                {
                    UpdateVersionTitleText.Text = _updateService.LatestManifest.IsCritical
                        ? $"⚠️ v{_updateService.LatestManifest.Version} Critical Update"
                        : $"v{_updateService.LatestManifest.Version} Available";
                }

                if (UpdateSummaryText != null)
                {
                    UpdateSummaryText.Text = string.IsNullOrWhiteSpace(_updateService.LatestManifest.Summary)
                        ? "New release published on GitHub."
                        : _updateService.LatestManifest.Summary;
                }

                if (ViewChangelogBtn != null)
                {
                    bool hasChangelog = !string.IsNullOrWhiteSpace(_updateService.LatestManifest.ChangelogUrl);
                    ViewChangelogBtn.Visibility = hasChangelog ? Visibility.Visible : Visibility.Collapsed;
                }

                if (InstallUpdateBtnText != null)
                {
                    InstallUpdateBtnText.Text = _updateService.Status switch
                    {
                        UpdateStatus.Downloading => "Downloading...",
                        UpdateStatus.Verifying => "Verifying...",
                        UpdateStatus.ReadyToInstall => "Restart & Update",
                        UpdateStatus.Installing => "Updating...",
                        _ => "Install & Restart"
                    };
                }
            }
        }

        // Update notification badges (Island Pill dot & Dock Settings icon badge)
        bool hasUpdateDot = _updateService.Status == UpdateStatus.UpdateAvailable ||
                            _updateService.Status == UpdateStatus.ReadyToInstall;

        if (SettingsUpdateBadgeDot != null)
        {
            SettingsUpdateBadgeDot.Visibility = hasUpdateDot ? Visibility.Visible : Visibility.Collapsed;
            if (hasUpdateDot)
            {
                SettingsUpdateBadgeDot.Background = _updateService.Status == UpdateStatus.ReadyToInstall
                    ? ThemeBrushes.Green
                    : ThemeBrushes.Blue;
            }
        }

        if (UpdateAvailableDot != null)
        {
            UpdateAvailableDot.Visibility = hasUpdateDot ? Visibility.Visible : Visibility.Collapsed;
            if (hasUpdateDot)
            {
                bool isReady = _updateService.Status == UpdateStatus.ReadyToInstall;
                if (UpdateDotCore != null)
                {
                    UpdateDotCore.Fill = isReady ? ThemeBrushes.Green : ThemeBrushes.Blue;
                }
                if (UpdateDotGlowStop != null)
                {
                    UpdateDotGlowStop.Color = isReady
                        ? Color.FromArgb(0x80, 0x30, 0xD1, 0x58)
                        : Color.FromArgb(0x80, 0x0A, 0x84, 0xFF);
                }
                UpdateAvailableDot.ToolTip = isReady
                    ? $"Dynamic Island v{_updateService.LatestManifest?.Version} Ready to Install · Click to open Settings"
                    : $"Dynamic Island v{_updateService.LatestManifest?.Version} Available · Click to open Settings";
            }
        }
    }

    internal void UpdateAvailableDot_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _machine.ExpandNow();
        SwitchExpandedView(ExpandedView.Settings, true);
    }

    internal void ViewChangelogBtn_Click(object sender, RoutedEventArgs e)
    {
        string? url = _updateService.LatestManifest?.ChangelogUrl;
        if (!string.IsNullOrWhiteSpace(url))
        {
            SafeProcessLauncher.TryOpenUrl(url);
        }
    }

    private bool _isLoadingVersionHistory;

    internal async void CheckUpdatesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (CheckUpdatesBtnText != null) CheckUpdatesBtnText.Text = "Checking...";
        try
        {
            var manifest = await _updateService.CheckForUpdatesAsync(silent: false, force: true);
            if (_isClosing) return;
            if (manifest != null && (_updateService.Status == UpdateStatus.UpdateAvailable || _updateService.Status == UpdateStatus.ReadyToInstall))
            {
                _machine.PushToast(new ToastContent(
                    "\uE896",
                    manifest.IsCritical ? $"Critical Update v{manifest.Version}" : $"Update v{manifest.Version} Available",
                    _updateService.Status == UpdateStatus.ReadyToInstall ? "Ready to install from Settings" : "Click Install & Restart in Settings",
                    ThemeBrushes.BlueColor,
                    TimeSpan.FromSeconds(3.5),
                    TargetView: "settings",
                    Priority: ToastPriority.Feedback
                ));
            }
            else if (manifest == null && _updateService.Status == UpdateStatus.UpToDate)
            {
                _machine.PushToast(new ToastContent(
                    "\uE73E",
                    "Up to Date",
                    $"Dynamic Island v{_updateService.CurrentVersionString} is the latest version",
                    ThemeBrushes.GreenColor,
                    TimeSpan.FromSeconds(2.5),
                    TargetView: "settings",
                    Priority: ToastPriority.Feedback
                ));
            }
            else if (_updateService.Status == UpdateStatus.Failed)
            {
                _machine.PushToast(new ToastContent(
                    "\uE783",
                    "Update Check Failed",
                    _updateService.LastErrorMessage ?? "Could not connect to GitHub.",
                    ThemeBrushes.AmberColor,
                    TimeSpan.FromSeconds(3.5),
                    TargetView: "settings",
                    Priority: ToastPriority.Feedback
                ));
            }
        }
        catch (Exception ex)
        {
            App.Log($"[Settings] CheckUpdatesBtn_Click error: {ex}");
            if (!_isClosing)
            {
                _machine.PushToast(new ToastContent(
                    "\uE783",
                    "Update Check Failed",
                    ex.Message,
                    ThemeBrushes.AmberColor,
                    TimeSpan.FromSeconds(3.5),
                    TargetView: "settings",
                    Priority: ToastPriority.Feedback
                ));
            }
        }
        finally
        {
            if (!_isClosing)
            {
                if (CheckUpdatesBtnText != null) CheckUpdatesBtnText.Text = "Check";
                UpdateSettingsVersionAndStatusUI();
            }
        }
    }

    internal void UpdateChannelBtn_Click(object sender, RoutedEventArgs e)
    {
        string next = string.Equals(_settingsService.Current.UpdateChannel, "Stable", StringComparison.OrdinalIgnoreCase) ? "Beta" : "Stable";
        _settingsService.Update(s => s.UpdateChannel = next, nameof(AppSettings.UpdateChannel));
        _machine.PushToast(new ToastContent(
            "\uE713",
            "Update Channel Changed",
            $"Switched to {next} release channel",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback
        ));
    }

    internal async void InstallUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_updateService.Status == UpdateStatus.ReadyToInstall)
            {
                _updateService.ApplyUpdateAndRestart();
                return;
            }

            if (_updateService.Status == UpdateStatus.UpdateAvailable || _updateService.Status == UpdateStatus.Failed)
            {
                if (InstallUpdateBtnText != null) InstallUpdateBtnText.Text = "Downloading...";
                if (UpdateProgressBar != null) UpdateProgressBar.Visibility = Visibility.Visible;
                if (UpdateProgressText != null) UpdateProgressText.Visibility = Visibility.Visible;

                bool ok = await _updateService.DownloadUpdateAsync();
                if (_isClosing) return;

                if (ok)
                {
                    if (InstallUpdateBtnText != null) InstallUpdateBtnText.Text = "Restart Now";
                    if (UpdateProgressText != null) UpdateProgressText.Text = "Verified & ready to install";
                    _machine.PushToast(new ToastContent(
                        "\uE896",
                        "Ready to Install",
                        "Click 'Restart Now' to finish upgrading Dynamic Island",
                        ThemeBrushes.GreenColor,
                        TimeSpan.FromSeconds(4.0),
                        TargetView: "settings",
                        Priority: ToastPriority.Feedback
                    ));
                }
                else
                {
                    if (InstallUpdateBtnText != null) InstallUpdateBtnText.Text = "Retry Download";
                    if (UpdateProgressText != null) UpdateProgressText.Text = _updateService.LastErrorMessage ?? "Download failed";
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[Settings] InstallUpdateBtn_Click error: {ex}");
            if (!_isClosing)
            {
                if (InstallUpdateBtnText != null) InstallUpdateBtnText.Text = "Retry Download";
                if (UpdateProgressText != null) UpdateProgressText.Text = ex.Message;
            }
        }
    }

    internal void AutoCheckToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.AutoCheckUpdates;
        _settingsService.Update(s => s.AutoCheckUpdates = next, nameof(AppSettings.AutoCheckUpdates));
    }

    internal void AutoDownloadToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.AutoDownloadUpdates;
        _settingsService.Update(s => s.AutoDownloadUpdates = next, nameof(AppSettings.AutoDownloadUpdates));
    }

    internal void GitHubTokenBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitGitHubToken();
            e.Handled = true;
        }
    }

    internal void GitHubTokenBox_LostFocus(object sender, RoutedEventArgs e) => CommitGitHubToken();

    private void CommitGitHubToken()
    {
        if (GitHubTokenBox != null)
        {
            string? token = string.IsNullOrWhiteSpace(GitHubTokenBox.Password) ? null : GitHubTokenBox.Password.Trim();
            if (_settingsService.Current.GitHubToken != token)
            {
                _settingsService.Update(s => s.GitHubToken = token, nameof(AppSettings.GitHubToken));
                _machine.PushToast(new ToastContent(
                    Glyph: "\uE73E",
                    Title: "GitHub Token Saved",
                    Subtitle: string.IsNullOrEmpty(token) ? "Token cleared (standard 60 req/hr rate limit)" : "Token activated (5,000 req/hr rate limit)",
                    Accent: ThemeBrushes.GreenColor,
                    Duration: TimeSpan.FromSeconds(2.5),
                    TargetView: "settings",
                    Priority: ToastPriority.Feedback
                ));
            }
        }
    }

    internal async void ToggleVersionHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        if (VersionHistoryContainer == null) return;
        bool isVisible = VersionHistoryContainer.Visibility == Visibility.Visible;
        VersionHistoryContainer.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
        if (ToggleVersionHistoryText != null)
        {
            ToggleVersionHistoryText.Text = isVisible ? "View Releases" : "Hide Releases";
        }

        if (!isVisible && VersionHistoryList != null && _updateService.ReleaseHistory.Count == 0 && !_isLoadingVersionHistory)
        {
            _isLoadingVersionHistory = true;
            try
            {
                await PopulateVersionHistoryUIAsync();
            }
            catch (Exception ex)
            {
                App.Log($"[Settings] ToggleVersionHistoryBtn_Click error: {ex}");
            }
            finally
            {
                _isLoadingVersionHistory = false;
            }
        }
    }

    private async Task PopulateVersionHistoryUIAsync()
    {
        if (VersionHistoryList == null || _isClosing) return;

        VersionHistoryList.Children.Clear();
        var loadingText = new TextBlock
        {
            Text = "Fetching release history from GitHub...",
            FontSize = 7.5,
            Foreground = ThemeBrushes.SecondaryText,
            Margin = new Thickness(4, 2, 4, 2)
        };
        VersionHistoryList.Children.Add(loadingText);

        var history = await _updateService.FetchVersionHistoryAsync();
        if (_isClosing || VersionHistoryList == null) return;
        VersionHistoryList.Children.Clear();

        if (history.Count == 0)
        {
            VersionHistoryList.Children.Add(new TextBlock
            {
                Text = "No previous releases found or GitHub rate limit reached.",
                FontSize = 7.5,
                Foreground = ThemeBrushes.SecondaryText,
                Margin = new Thickness(4, 2, 4, 2)
            });
            return;
        }

        foreach (var item in history)
        {
            var dock = new DockPanel { Margin = new Thickness(2, 3, 2, 3), LastChildFill = false };
            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            bool isCurrent = string.Equals(item.Version, _updateService.CurrentVersionString, StringComparison.OrdinalIgnoreCase);

            var titleBlock = new TextBlock
            {
                Text = $"{item.ReleaseTag}{(isCurrent ? " (Installed)" : "")}",
                FontSize = 8.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = isCurrent ? ThemeBrushes.Green : ThemeBrushes.PrimaryText
            };
            var dateBlock = new TextBlock
            {
                Text = $"{item.ReleaseDate.ToLocalTime():yyyy-MM-dd} • {(item.IsPreRelease ? "Beta" : "Stable")}",
                FontSize = 7,
                Foreground = ThemeBrushes.SecondaryText
            };

            textStack.Children.Add(titleBlock);
            textStack.Children.Add(dateBlock);
            dock.Children.Add(textStack);

            if (!isCurrent && !string.IsNullOrEmpty(item.InstallerUrl))
            {
                string actionText = SemVersion.TryParse(item.Version, out var v) && v > _updateService.CurrentVersion ? "Upgrade" : "Rollback";
                var btnLabel = new TextBlock
                {
                    Text = actionText,
                    FontSize = 7.5,
                    FontWeight = FontWeights.Medium
                };

                var rollbackBtn = new Button
                {
                    Content = btnLabel,
                    Height = 18,
                    Padding = new Thickness(6, 0, 6, 0),
                    Style = (Style)FindResource("IslandButton"),
                    Tag = item
                };
                DockPanel.SetDock(rollbackBtn, Dock.Right);

                rollbackBtn.Click += async (s, _) =>
                {
                    if (s is not Button btn || btn.Tag is not ReleaseHistoryItem targetRelease || string.IsNullOrEmpty(targetRelease.InstallerUrl))
                    {
                        return;
                    }

                    string originalAction = actionText;
                    btn.IsEnabled = false;
                    btnLabel.Text = "Starting...";

                    string tempDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DynamicIsland", "Updates"));
                    Directory.CreateDirectory(tempDir);
                    string safeVer = System.Text.RegularExpressions.Regex.Replace(targetRelease.Version ?? "unknown", @"[^0-9A-Za-z._-]", "_");
                    if (string.IsNullOrWhiteSpace(safeVer)) safeVer = "unknown";

                    string dest = string.Empty;
                    try
                    {
                        if (!GitHubReleaseClient.IsTrustedGitHubUrl(targetRelease.InstallerUrl))
                        {
                            throw new InvalidOperationException("Untrusted release download URL rejected.");
                        }

                        string urlPath = new Uri(targetRelease.InstallerUrl).AbsolutePath;
                        string ext = Path.GetExtension(urlPath);
                        if (string.IsNullOrEmpty(ext)) ext = ".exe";
                        string targetFileName = $"DynamicIsland-Setup-{safeVer}{ext}";
                        dest = Path.GetFullPath(Path.Combine(tempDir, targetFileName));

                        _machine.PushToast(new ToastContent(
                            "\uE896",
                            $"{originalAction} Started",
                            $"Resolving checksum for {targetRelease.ReleaseTag}...",
                            ThemeBrushes.BlueColor,
                            TimeSpan.FromSeconds(3.5),
                            TargetView: "settings",
                            Priority: ToastPriority.Feedback
                        ));

                        var progress = new Progress<UpdateProgressInfo>(info =>
                        {
                            Dispatcher.InvokeAsync(() =>
                            {
                                if (_isClosing) return;
                                btnLabel.Text = $"{info.Percent:0}%";
                                if (UpdateProgressBar != null)
                                {
                                    UpdateProgressBar.Visibility = Visibility.Visible;
                                    UpdateProgressBar.Value = info.Percent;
                                }
                                if (UpdateProgressText != null)
                                {
                                    UpdateProgressText.Visibility = Visibility.Visible;
                                    UpdateProgressText.Text = $"{originalAction} {targetRelease.ReleaseTag}: {info.Percent:0}%";
                                }
                            });
                        });

                        // 1. Fetch expected cryptographic SHA-256 hash BEFORE downloading and fail closed if absent
                        btnLabel.Text = "Resolving...";
                        string? expectedHash = await _updateService.FetchSha256ForReleaseAsync(targetRelease, "DynamicIsland-Setup.exe").ConfigureAwait(true);
                        if (_isClosing) return;

                        if (string.IsNullOrWhiteSpace(expectedHash))
                        {
                            _machine.PushToast(new ToastContent(
                                "\uE783",
                                "Security Verification Blocked",
                                $"No SHA-256 checksum published for {targetRelease.ReleaseTag}. Aborting {originalAction.ToLowerInvariant()}.",
                                ThemeBrushes.AmberColor,
                                TimeSpan.FromSeconds(5.0),
                                TargetView: "settings",
                                Priority: ToastPriority.Feedback
                            ));
                            btnLabel.Text = originalAction;
                            btn.IsEnabled = true;
                            return;
                        }

                        // 2. Download package with real-time percentage progress (using user's GitHub token)
                        btnLabel.Text = "Downloading...";
                        using var client = new GitHubReleaseClient(
                            appVersion: _updateService.CurrentVersionString,
                            gitHubToken: _settingsService.Current.GitHubToken);
                        await client.DownloadFileWithProgressAsync(targetRelease.InstallerUrl, dest, progress).ConfigureAwait(true);
                        if (_isClosing) return;

                        // 3. Mandatory cryptographic SHA-256 integrity verification
                        btnLabel.Text = "Verifying...";
                        bool verified = await UpdateIntegrityVerifier.VerifyFileAsync(dest, expectedHash).ConfigureAwait(true);
                        if (!verified)
                        {
                            UpdateIntegrityVerifier.TryCleanFile(dest);
                            _machine.PushToast(new ToastContent(
                                "\uE783",
                                "Integrity Check Failed",
                                $"SHA-256 mismatch for {targetRelease.ReleaseTag}. Package was discarded for security.",
                                ThemeBrushes.AmberColor,
                                TimeSpan.FromSeconds(5.0),
                                TargetView: "settings",
                                Priority: ToastPriority.Feedback
                            ));
                            btnLabel.Text = originalAction;
                            btn.IsEnabled = true;
                            return;
                        }

                        // 4. Verification passed: launch installer handoff and restart
                        btnLabel.Text = "Launching...";
                        _machine.PushToast(new ToastContent(
                            "\uE73E",
                            $"{originalAction} Ready",
                            $"Launching {targetRelease.ReleaseTag} installer...",
                            ThemeBrushes.GreenColor,
                            TimeSpan.FromSeconds(3.0),
                            TargetView: "settings",
                            Priority: ToastPriority.Feedback
                        ));

                        UpdateHandoffEngine.ExecuteHandoff(dest, isInstaller: true);
                    }
                    catch (Exception ex)
                    {
                        if (!string.IsNullOrEmpty(dest)) UpdateIntegrityVerifier.TryCleanFile(dest);
                        App.Log($"[UpdateManager] {originalAction} failed: {ex.Message}");
                        if (!_isClosing)
                        {
                            _machine.PushToast(new ToastContent(
                                "\uE783",
                                $"{originalAction} Failed",
                                ex.Message,
                                ThemeBrushes.AmberColor,
                                TimeSpan.FromSeconds(4.5),
                                TargetView: "settings",
                                Priority: ToastPriority.Feedback
                            ));
                            btnLabel.Text = originalAction;
                            btn.IsEnabled = true;
                        }
                    }
                };
                dock.Children.Add(rollbackBtn);
            }

            VersionHistoryList.Children.Add(dock);
        }
    }
}
