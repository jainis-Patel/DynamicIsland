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
        var ap = SettingsContainer?.AppearancePanel;
        if (ap == null || ap.ThemeModeSystemBtn == null || ap.ThemeModeDarkBtn == null || ap.ThemeModeLightBtn == null) return;

        string current = SettingsService.NormalizeThemeMode(_settingsService.Current.ThemeMode);
        var accentBrush = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var defaultBg = (Brush)FindResource("IslandButtonBgBrush");
        var primaryText = (Brush)FindResource("PrimaryTextBrush");

        StyleAccentToggleButton(ap.ThemeModeSystemBtn, current == "System", accentBrush, defaultBg, defaultBorder, ap.ThemeModeSystemText, primaryText, boldWhenActive: true);
        StyleAccentToggleButton(ap.ThemeModeDarkBtn, current == "Dark", accentBrush, defaultBg, defaultBorder, ap.ThemeModeDarkText, primaryText, boldWhenActive: true);
        StyleAccentToggleButton(ap.ThemeModeLightBtn, current == "Light", accentBrush, defaultBg, defaultBorder, ap.ThemeModeLightText, primaryText, boldWhenActive: true);

        if (ap.ThemeModeSubtext != null)
        {
            ap.ThemeModeSubtext.Text = current == "System"
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
        var ap = SettingsContainer?.AppearancePanel;
        if (ap == null || ap.ThemeStyleDefaultBtn == null || ap.ThemeStyleLiquidGlassBtn == null) return;

        string current = SettingsService.NormalizeThemeStyle(_settingsService.Current.ThemeStyle);
        var accentBrush = (Brush)Resources["AccentBrush"];
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var defaultBg = (Brush)FindResource("IslandButtonBgBrush");
        var primaryText = (Brush)FindResource("PrimaryTextBrush");

        StyleAccentToggleButton(ap.ThemeStyleDefaultBtn, current == "Default", accentBrush, defaultBg, defaultBorder, ap.ThemeStyleDefaultText, primaryText, boldWhenActive: true);
        StyleAccentToggleButton(ap.ThemeStyleLiquidGlassBtn, current == "Liquid Glass", accentBrush, defaultBg, defaultBorder, ap.ThemeStyleLiquidGlassText, primaryText, boldWhenActive: true);

        if (ap.ThemeStyleSubtext != null)
        {
            ap.ThemeStyleSubtext.Text = current == "Liquid Glass"
                ? "Apple Liquid Glass UI · Frosted Material & Specular Lensing"
                : "Classic OLED Capsule";
        }
    }

    private void ApplyThemeStyleVisuals()
    {
        bool isLiquid = ThemeManager.IsLiquidGlass;
        bool isDark = ThemeManager.IsDark;

        UpdateGlassOpticsAvailabilityUI();

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
        var fp = SettingsContainer?.FloraPanel;
        if (fp == null) return;

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

        StyleThemeBtn(fp.ThemesTabNoneBtn, fp.ThemesTabNoneText, current == "None");
        StyleThemeBtn(fp.ThemesTabIvyBtn, fp.ThemesTabIvyText, current == "Ivy & Blooms");
        StyleThemeBtn(fp.ThemesTabMeadowBtn, fp.ThemesTabMeadowText, current == "Meadow");

        if (fp.NatureActiveThemeBadge != null) fp.NatureActiveThemeBadge.Text = current;
        if (fp.NatureThemeSubtext != null)
        {
            fp.NatureThemeSubtext.Text = current switch
            {
                "Ivy & Blooms" => "Climbing leaves, morning glories, wild roses & butterflies",
                "Meadow" => "Wild meadow grass, daisies, poppies & lavender",
                _ => "Clean island capsule without decorations"
            };
        }

        if (fp.FloraControlsGroup != null)
        {
            bool active = current != "None";
            fp.FloraControlsGroup.IsEnabled = active;
            fp.FloraControlsGroup.Opacity = active ? 1.0 : 0.45;
        }
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
        var fp = SettingsContainer?.FloraPanel;
        if (_settingsService?.Current == null || fp == null) return;
        var cur = _settingsService.Current;
        var accentBrush = TryFindResource("AccentBrush") as Brush ?? (Resources["AccentBrush"] as Brush) ?? ThemeBrushes.Blue;
        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var defaultBg = (Brush)FindResource("IslandButtonBgBrush");

        // 1. Sliders & Text Readouts
        if (fp.NatureDensitySlider != null)
            fp.NatureDensitySlider.Value = cur.NatureDensity * 100.0;
        if (fp.NatureDensityText != null)
        {
            int pct = (int)Math.Round(cur.NatureDensity * 100.0);
            string desc = pct <= 40 ? "Minimal" : (pct <= 80 ? "Balanced" : "Lush");
            fp.NatureDensityText.Text = $"{pct}% · {desc}";
        }

        if (fp.NatureBloomSlider != null)
            fp.NatureBloomSlider.Value = cur.NatureBloomDensity * 100.0;
        if (fp.NatureBloomText != null)
        {
            int pct = (int)Math.Round(cur.NatureBloomDensity * 100.0);
            string desc = pct <= 40 ? "Accents" : (pct <= 85 ? "Balanced" : "Full Bloom");
            fp.NatureBloomText.Text = $"{pct}% · {desc}";
        }

        if (fp.NatureScaleSlider != null)
            fp.NatureScaleSlider.Value = cur.NatureScale * 100.0;
        if (fp.NatureScaleText != null)
        {
            int pct = (int)Math.Round(cur.NatureScale * 100.0);
            string desc = pct <= 65 ? "Delicate" : (pct <= 95 ? "Standard" : "Bold");
            fp.NatureScaleText.Text = $"{pct}% · {desc}";
        }

        if (fp.NatureDropSlider != null)
            fp.NatureDropSlider.Value = cur.NatureDropLength;
        if (fp.NatureDropText != null)
        {
            int drop = (int)Math.Round(cur.NatureDropLength);
            string desc = drop <= 35 ? "Rim Hugger" : (drop <= 70 ? "Moderate" : "Deep Cascade");
            fp.NatureDropText.Text = $"{drop} px · {desc}";
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

        StyleCountBtn(fp.NatureButterfly0Btn, fp.NatureButterfly0Text, cur.NatureButterflyCount == 0);
        StyleCountBtn(fp.NatureButterfly1Btn, fp.NatureButterfly1Text, cur.NatureButterflyCount == 1);
        StyleCountBtn(fp.NatureButterfly2Btn, fp.NatureButterfly2Text, cur.NatureButterflyCount == 2);
        StyleCountBtn(fp.NatureButterfly3Btn, fp.NatureButterfly3Text, cur.NatureButterflyCount == 3);
        StyleCountBtn(fp.NatureButterfly4Btn, fp.NatureButterfly4Text, cur.NatureButterflyCount >= 4);

        if (fp.NatureButterflySubtext != null)
        {
            fp.NatureButterflySubtext.Text = cur.NatureButterflyCount switch
            {
                0 => "0 Butterflies · No motion distraction",
                1 => "1 Butterfly · Monarch solo flight",
                2 => "2 Butterflies · Monarch & Blue Morpho pair",
                3 => "3 Butterflies · Monarch, Blue Morpho & White Swallowtail",
                _ => $"{cur.NatureButterflyCount} Butterflies · Swarm companions"
            };
        }

        // 3. Drifting Pollen
        if (fp.NaturePollenToggleSwitch != null)
        {
            fp.NaturePollenToggleSwitch.IsChecked = cur.NaturePollenEnabled;
        }
        if (fp.NaturePollenSlider != null)
        {
            fp.NaturePollenSlider.Value = cur.NaturePollenCount;
            fp.NaturePollenSlider.IsEnabled = cur.NaturePollenEnabled;
            fp.NaturePollenSlider.Opacity = cur.NaturePollenEnabled ? 1.0 : 0.45;
        }
        if (fp.NaturePollenText != null)
        {
            fp.NaturePollenText.Text = cur.NaturePollenEnabled ? $"{cur.NaturePollenCount} motes" : "Off";
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
}
