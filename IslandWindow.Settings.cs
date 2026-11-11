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

            UpdateDockButtonsUI();
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
            ThemeManager.SetThemeMode(SettingsService.NormalizeThemeMode(cur.ThemeMode));
            ThemeManager.SetThemeStyle(SettingsService.NormalizeThemeStyle(cur.ThemeStyle));
            ApplyTheme(cur.ThemeName);
            ApplyIslandTheme(cur.IslandTheme);
            UpdateThemeModeUI();
            UpdateThemeStyleUI();
            UpdateIslandThemeUI();
            ApplyThemeStyleVisuals();
            ApplyEdgeBorder(cur.ShowEdgeBorder);
            UpdateEdgeBorderUI();
            UpdateSecondaryDropletUI();
            SyncSpringMotionPreferences();
            UpdateSpectrumActivity();
            PositionWindow();
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void OnSettingsPropertyChanged(string? propertyName)
    {
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

            case nameof(AppSettings.HardwareNotchMode):
                UpdateNotchBezelUI();
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

    private void UpdateDockButtonsUI()
    {
        if (DockLeftBtn == null || DockCenterBtn == null || DockRightBtn == null) return;
        string dock = SettingsService.NormalizeDockPosition(_settingsService.Current.DockPosition);
        var accent = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var defaultBg = (Brush)FindResource("CardSurfaceBrush");

        void StyleDockBtn(Button btn, bool active)
        {
            btn.Background = active ? accent : defaultBg;
            btn.Foreground = active ? ThemeBrushes.DarkInk : ThemeBrushes.PrimaryText;
            btn.BorderBrush = active ? accent : defaultBorder;
        }

        StyleDockBtn(DockLeftBtn, dock == "Left");
        StyleDockBtn(DockCenterBtn, dock == "Center");
        StyleDockBtn(DockRightBtn, dock == "Right");
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

        void StyleBtn(Button? btn, bool active)
        {
            if (btn == null) return;
            btn.BorderBrush = active ? accentBrush : defaultBorder;
            btn.Background = active ? activeBg : defaultBg;
            btn.Opacity = active ? 1.0 : 0.72;
        }

        StyleBtn(ClockStyleMinimalBtn, style == "Minimal");
        StyleBtn(ClockStyleSecondsBtn, style == "Seconds");
        StyleBtn(ClockStyleDateBtn, style == "DateBadge");
        StyleBtn(ClockStyleAnalogBtn, style == "Analog");
        StyleBtn(ClockStyleNeonBtn, style == "Neon");
        StyleBtn(ClockStyleFlipBtn, style == "Flip");

        ActiveClockStyleLabel.Text = style switch
        {
            "Seconds" => "Seconds (Precision) · Live Accent Chip",
            "DateBadge" => "Date & Time Badge · Apple Calendar Pill",
            "Analog" => "Micro Horology Analog · Swiss Railway Dial",
            "Neon" => "Cyber Neon Glow · Dual-Stage Cathode Tube",
            "Flip" => "Retro Flip Card · Solari Split-Flap",
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

        UpdateClockStyleUI();
        UpdateClockTimerInterval();

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

        void StyleBtn(Button btn, bool active)
        {
            if (btn == null) return;
            btn.BorderBrush = active ? accentBrush : defaultBorder;
            btn.Background = active ? activeBg : defaultBg;
        }

        StyleBtn(VisualizerStyleBarsBtn, style == "ClassicBars");
        StyleBtn(VisualizerStyleBlobBtn, style == "LiquidBlob");
        StyleBtn(VisualizerStyleAuroraBtn, style == "NeonAurora");
        StyleBtn(VisualizerStyleSiriBtn, style == "SiriOrb");

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
    }

    internal void IslandScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isApplyingSettings || _settingsService == null || IslandScaleText == null) return;
        double pct = Math.Round(e.NewValue);
        double normalized = Math.Clamp(pct / 100.0, 0.85, 1.25);
        if (Math.Abs(_settingsService.Current.IslandScale - normalized) < 0.005) return;

        IslandScaleText.Text = $"{pct}%";
        _settingsService.Update(s => s.IslandScale = normalized, nameof(AppSettings.IslandScale));
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
        DynamicIsland.Core.LiquidGlassTheme.GlobalBlurScale = (float)normalized;
        ForceRefreshGlassSurfaces();
    }

    internal void GlassRefractionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isApplyingSettings || _settingsService == null || GlassRefractionText == null) return;
        double pct = Math.Round(e.NewValue);
        double normalized = Math.Clamp(pct / 100.0, 0.0, 2.0);
        if (Math.Abs(_settingsService.Current.GlassRefractionIntensity - normalized) < 0.005) return;

        GlassRefractionText.Text = $"{pct}%";
        _settingsService.Update(s => s.GlassRefractionIntensity = normalized, nameof(AppSettings.GlassRefractionIntensity));
        DynamicIsland.Core.LiquidGlassTheme.GlobalThicknessScale = (float)normalized;
        ForceRefreshGlassSurfaces();
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
        UpdateDockVisibilityUI();
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
        UpdateDockVisibilityUI();
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
        UpdateDockVisibilityUI();
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
        int totalTools = 18;

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
            ThemeManager.SetThemeMode(SettingsService.NormalizeThemeMode(mode));
            UpdateThemeModeUI();
            ApplyTheme(_settingsService.Current.ThemeName);
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

        void StyleModeBtn(Button btn, TextBlock? text, bool active)
        {
            btn.Background = active ? accentBrush : defaultBg;
            btn.BorderBrush = active ? accentBrush : defaultBorder;
            if (text != null)
            {
                text.Foreground = active ? ThemeBrushes.DarkInk : (Brush)FindResource("PrimaryTextBrush");
                text.FontWeight = active ? FontWeights.Bold : FontWeights.SemiBold;
            }
        }

        StyleModeBtn(ThemeModeSystemBtn, ThemeModeSystemText, current == "System");
        StyleModeBtn(ThemeModeDarkBtn, ThemeModeDarkText, current == "Dark");
        StyleModeBtn(ThemeModeLightBtn, ThemeModeLightText, current == "Light");

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
            ThemeManager.SetThemeStyle(normalized);
            UpdateThemeStyleUI();
            ApplyThemeStyleVisuals();
            ApplyTheme(_settingsService.Current.ThemeName);

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

        void StyleBtn(Button btn, TextBlock? text, bool active)
        {
            btn.Background = active ? accentBrush : defaultBg;
            btn.BorderBrush = active ? accentBrush : defaultBorder;
            if (text != null)
            {
                text.Foreground = active ? ThemeBrushes.DarkInk : (Brush)FindResource("PrimaryTextBrush");
                text.FontWeight = active ? FontWeights.Bold : FontWeights.SemiBold;
            }
        }

        StyleBtn(ThemeStyleDefaultBtn, ThemeStyleDefaultText, current == "Default");
        StyleBtn(ThemeStyleLiquidGlassBtn, ThemeStyleLiquidGlassText, current == "Liquid Glass");

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

        if (_isNatureThemeActive)
        {
            EnsureRendering();
        }
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
        StyleThemeBtn(IslandThemeSakuraBtn, IslandThemeSakuraText, current == "Sakura Branch");
        StyleThemeBtn(IslandThemeTreeBtn, IslandThemeTreeText, current == "Blossom Tree");
        StyleThemeBtn(IslandThemeMeadowBtn, IslandThemeMeadowText, current == "Meadow");

        IslandThemeSubtext.Text = current switch
        {
            "Ivy & Blooms" => "Ivy & Blooms · Climbing vines, morning glories & butterflies",
            "Sakura Branch" => "Sakura Branch · Cherry blossoms & tumbling petals",
            "Blossom Tree" => "Blossom Tree · Branching trunk with organic sway",
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
}
