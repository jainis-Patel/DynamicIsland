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
            UpdateAutoHideModeUI();
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
            UpdateAlbumArtStyleUI();
            UpdatePetSettingsUI();
            UpdateDisplayAndBatterySettingsUI();
            UpdateNotifSettingsUI();
            UpdateSystemSettingsUI();
            UpdateSettingsHeaderRamBadge();
            SwitchSettingsTab(_currentSettingsTab, false);
            ApplyClockStyle(cur.ClockStyle);
            ApplyVisualizerStyle(cur.VisualizerStyle);
            ApplyAlbumArtStyle(cur.AlbumArtStyle);
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
            case nameof(AppSettings.EnableHighLoadAlerts):
            case nameof(AppSettings.HighCpuAlertThreshold):
            case nameof(AppSettings.HighRamAlertThreshold):
            case nameof(AppSettings.EnableNightLightSim):
            case nameof(AppSettings.NightLightWarmthKelvin):
                UpdateDisplayAndBatterySettingsUI();
                UpdateNotifSettingsUI();
                break;

            case nameof(AppSettings.VisualizerStyle):
                ApplyVisualizerStyle(cur.VisualizerStyle);
                break;

            case nameof(AppSettings.AlbumArtStyle):
                ApplyAlbumArtStyle(cur.AlbumArtStyle);
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

            case nameof(AppSettings.AutoHideIsland):
                UpdateAutoHideModeUI();
                EvaluateAutoHideState();
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
        UpdateAutoHideModeUI();
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

}
