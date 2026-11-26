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

    private void UpdateAutoHideModeUI()
    {
        var dp = SettingsContainer?.DisplayPowerPanel;
        if (dp == null || dp.AutoHideAlwaysBtn == null || dp.AutoHideAutoBtn == null) return;
        bool autoHide = _settingsService.Current.AutoHideIsland;
        var accent = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var defaultBg = (Brush)FindResource("CardSurfaceBrush");

        StyleAccentToggleButton(dp.AutoHideAlwaysBtn, !autoHide, accent, defaultBg, defaultBorder);
        StyleAccentToggleButton(dp.AutoHideAutoBtn, autoHide, accent, defaultBg, defaultBorder);
        if (dp.AutoHideModeSubtext != null)
        {
            dp.AutoHideModeSubtext.Text = autoHide
                ? "Hides at top edge; hover top edge to pull back"
                : "Always visible at top edge";
        }
    }

    internal void AutoHideModeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string mode })
        {
            bool autoHide = string.Equals(mode, "AutoHide", StringComparison.OrdinalIgnoreCase);
            _settingsService.Update(s => s.AutoHideIsland = autoHide, nameof(AppSettings.AutoHideIsland));
            _machine.PushToast(new ToastContent(
                "\uE8B0",
                autoHide ? "Auto-Hide Enabled" : "Always Visible",
                autoHide ? "Hover top edge to pull island back" : "Dynamic Island stays visible",
                ThemeBrushes.BlueColor,
                TimeSpan.FromSeconds(1.8),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "auto_hide"));
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

    internal void AlbumArtStyleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string style })
        {
            string normalized = SettingsService.NormalizeAlbumArtStyle(style);
            _settingsService.Update(s => s.AlbumArtStyle = normalized, nameof(AppSettings.AlbumArtStyle));

            string friendlyName = normalized switch
            {
                "OpticalDisc" => "Full-Art Disc",
                "StaticSquircle" => "Classic Static Squircle",
                _ => "Studio Vinyl LP"
            };
            var accent = _mediaService.Current?.Accent ?? ThemeBrushes.AmberColor;
            _machine.PushToast(new ToastContent(
                "\uE958",
                "Player Disc",
                friendlyName,
                accent,
                TimeSpan.FromSeconds(2.0),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "albumart"));
        }
    }

    private void UpdateAlbumArtStyleUI()
    {
        var cp = SettingsContainer?.ClockAudioPanel;
        if (cp?.ActiveAlbumArtStyleLabel == null || cp.AlbumArtVinylBtn == null) return;

        string style = SettingsService.NormalizeAlbumArtStyle(_settingsService.Current.AlbumArtStyle);
        var accentBrush = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var activeBg = ThemeBrushes.PillToggleActiveBg;
        var defaultBg = (Brush)FindResource("CardSurfaceBrush");

        StyleCardOptionButton(cp.AlbumArtVinylBtn, style == "VinylDisc", accentBrush, activeBg, defaultBg, defaultBorder);
        StyleCardOptionButton(cp.AlbumArtOpticalBtn, style == "OpticalDisc", accentBrush, activeBg, defaultBg, defaultBorder);
        StyleCardOptionButton(cp.AlbumArtStaticBtn, style == "StaticSquircle", accentBrush, activeBg, defaultBg, defaultBorder);

        cp.ActiveAlbumArtStyleLabel.Text = style switch
        {
            "OpticalDisc" => "Full-Art Disc (Spinning)",
            "StaticSquircle" => "Classic Squircle (Static)",
            _ => "Studio Vinyl LP (Turntable)"
        };
    }

    private void ApplyAlbumArtStyle(string? style)
    {
        string normalized = SettingsService.NormalizeAlbumArtStyle(style);
        CompactArtDisc?.SetStyle(normalized);
        ExpandedArtDisc?.SetStyle(normalized);
        _widgetsGalleryContainer?.ApplyAlbumArtStyle(normalized);
        _miniPlayerWindow?.ApplyAlbumArtStyle(normalized);

        UpdateAlbumArtStyleUI();
        EnsureRendering();
    }

    internal void CycleAlbumArtStyle()
    {
        string cur = SettingsService.NormalizeAlbumArtStyle(_settingsService.Current.AlbumArtStyle);
        string next = cur switch
        {
            "VinylDisc" => "OpticalDisc",
            "OpticalDisc" => "StaticSquircle",
            _ => "VinylDisc"
        };

        _settingsService.Update(s => s.AlbumArtStyle = next, nameof(AppSettings.AlbumArtStyle));

        string friendlyName = next switch
        {
            "OpticalDisc" => "Full-Art Disc",
            "StaticSquircle" => "Classic Static",
            _ => "Studio Vinyl LP"
        };
        var accent = _mediaService.Current?.Accent ?? ThemeBrushes.AmberColor;
        _machine.PushToast(new ToastContent(
            "\uE958",
            "Player Disc",
            friendlyName,
            accent,
            TimeSpan.FromSeconds(1.8),
            TargetView: "media",
            Priority: ToastPriority.Feedback,
            Category: "albumart"));
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

    private void UpdateContextProfilesUI()
    {
        var dp = SettingsContainer?.DisplayPowerPanel;
        if (dp == null || dp.ProfileDefaultBtn == null) return;

        string active = _settingsService.Current.ActiveProfileId ?? "default";
        var accent = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var defaultBg = (Brush)FindResource("CardSurfaceBrush");

        StyleAccentToggleButton(dp.ProfileDefaultBtn, string.Equals(active, "default", StringComparison.OrdinalIgnoreCase), accent, defaultBg, defaultBorder);
        StyleAccentToggleButton(dp.ProfileFocusBtn, string.Equals(active, "focus", StringComparison.OrdinalIgnoreCase), accent, defaultBg, defaultBorder);
        StyleAccentToggleButton(dp.ProfileGamingBtn, string.Equals(active, "gaming", StringComparison.OrdinalIgnoreCase), accent, defaultBg, defaultBorder);
        StyleAccentToggleButton(dp.ProfilePresentingBtn, string.Equals(active, "presenting", StringComparison.OrdinalIgnoreCase), accent, defaultBg, defaultBorder);

        if (dp.AutoProfileSwitchToggle != null)
        {
            dp.AutoProfileSwitchToggle.IsChecked = _settingsService.Current.EnableAutoProfileSwitching;
        }
    }

    internal void ContextProfileBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string profileId })
        {
            _contextProfileService?.ApplyProfile(profileId);
            UpdateContextProfilesUI();
        }
    }

    internal void AutoProfileSwitchToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.EnableAutoProfileSwitching;
        _settingsService.Update(s => s.EnableAutoProfileSwitching = next, nameof(AppSettings.EnableAutoProfileSwitching));
        UpdateContextProfilesUI();
    }

}
