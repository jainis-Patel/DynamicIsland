using System.Windows;
using Microsoft.Win32;

namespace DynamicIsland.Core;

public enum ThemeMode
{
    System,
    Dark,
    Light
}

public enum AppTheme
{
    Dark,
    Light
}

public enum ThemeStyle
{
    Default,
    LiquidGlass
}

/// <summary>
/// High-performance, zero-allocation Theme Manager for Dynamic Island.
/// Monitors Windows OS system theme changes via registry and Win32 messages (WM_SETTINGCHANGE),
/// synchronizes application resources dynamically, and enforces frozen brushes for 60Hz-240Hz rendering safety.
/// Supports both Default (Classic OLED) and Liquid Glass (macOS frosted translucency & prismatic specular rim) UI themes.
/// </summary>
internal static class ThemeManager
{
    public const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public const string AppsUseLightThemeValue = "AppsUseLightTheme";

    private static ThemeMode _currentMode = ThemeMode.System;
    private static AppTheme _currentTheme = AppTheme.Dark;
    private static ThemeStyle _currentStyle = ThemeStyle.Default;
    private static int _lastSystemAppsUseLightTheme = -1;
    private static bool _initialized;

    public static ThemeMode CurrentMode => _currentMode;
    public static AppTheme CurrentTheme => _currentTheme;
    public static ThemeStyle CurrentStyle => _currentStyle;
    public static bool IsDark => _currentTheme == AppTheme.Dark;
    public static bool IsLight => _currentTheme == AppTheme.Light;
    public static bool IsLiquidGlass => _currentStyle == ThemeStyle.LiquidGlass;
    public static bool Initialized => _initialized;

    public static event Action<AppTheme>? ThemeChanged;
    public static event Action<ThemeStyle>? ThemeStyleChanged;

    /// <summary>
    /// Checks whether the Windows OS Apps Theme is currently set to Light Mode.
    /// Safely reads the Personalize registry subkey. Falls back to dark (false) if unavailable.
    /// </summary>
    public static bool IsSystemInLightMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            if (key != null)
            {
                object? val = key.GetValue(AppsUseLightThemeValue);
                if (val is int intVal)
                {
                    return intVal == 1;
                }
                if (val != null && int.TryParse(val.ToString(), out int parsed))
                {
                    return parsed == 1;
                }
            }
        }
        catch
        {
            // Graceful fallback to dark mode if registry is inaccessible
        }
        return false;
    }

    /// <summary>
    /// Initial startup bootstrap called before UI windows instantiate.
    /// </summary>
    public static void Initialize(string? initialMode = null, string? initialStyle = null)
    {
        _currentMode = ParseThemeMode(initialMode);
        _currentStyle = ParseThemeStyle(initialStyle);
        bool systemLight = IsSystemInLightMode();
        _lastSystemAppsUseLightTheme = systemLight ? 1 : 0;

        AppTheme resolvedTheme = _currentMode switch
        {
            ThemeMode.Light => AppTheme.Light,
            ThemeMode.Dark => AppTheme.Dark,
            _ => systemLight ? AppTheme.Light : AppTheme.Dark
        };

        LiquidUiEngine.Initialize();
        ApplyThemeCore(resolvedTheme, _currentStyle);
        _initialized = true;
    }

    public static ThemeMode ParseThemeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return ThemeMode.System;
        string trimmed = mode.Trim();
        if (string.Equals(trimmed, "Dark", StringComparison.OrdinalIgnoreCase))
            return ThemeMode.Dark;
        if (string.Equals(trimmed, "Light", StringComparison.OrdinalIgnoreCase))
            return ThemeMode.Light;
        return ThemeMode.System;
    }

    public static ThemeStyle ParseThemeStyle(string? style)
    {
        if (string.IsNullOrWhiteSpace(style)) return ThemeStyle.Default;
        string trimmed = style.Trim();
        if (string.Equals(trimmed, "Liquid Glass", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "LiquidGlass", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Liquid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Glass", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Frosted", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Frosted Glass", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "FrostedGlass", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Frost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Mac", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "macOS", StringComparison.OrdinalIgnoreCase))
        {
            return ThemeStyle.LiquidGlass;
        }
        return ThemeStyle.Default;
    }

    /// <summary>
    /// Updates the configured ThemeMode ("System", "Dark", or "Light") and updates resources immediately.
    /// </summary>
    public static void SetThemeMode(ThemeMode mode)
    {
        _currentMode = mode;
        bool systemLight = IsSystemInLightMode();
        _lastSystemAppsUseLightTheme = systemLight ? 1 : 0;

        AppTheme targetTheme = mode switch
        {
            ThemeMode.Light => AppTheme.Light,
            ThemeMode.Dark => AppTheme.Dark,
            _ => systemLight ? AppTheme.Light : AppTheme.Dark
        };

        ApplyThemeCore(targetTheme, _currentStyle);
    }

    /// <summary>
    /// String overload accepting "System", "Dark", or "Light".
    /// </summary>
    public static void SetThemeMode(string? mode) => SetThemeMode(ParseThemeMode(mode));

    /// <summary>
    /// Updates the configured ThemeStyle ("Default" or "LiquidGlass") and updates resources immediately.
    /// </summary>
    public static void SetThemeStyle(ThemeStyle style)
    {
        _currentStyle = style;
        ApplyThemeCore(_currentTheme, _currentStyle);
    }

    /// <summary>
    /// String overload accepting "Default" or "Liquid Glass".
    /// </summary>
    public static void SetThemeStyle(string? style) => SetThemeStyle(ParseThemeStyle(style));

    /// <summary>
    /// Evaluates if the Windows OS theme changed. Called reactively from WndProc upon WM_SETTINGCHANGE.
    /// Returns true if a theme transition was detected and applied.
    /// </summary>
    public static bool CheckSystemThemeChanged()
    {
        if (_currentMode != ThemeMode.System) return false;

        bool systemLight = IsSystemInLightMode();
        int currentInt = systemLight ? 1 : 0;
        if (_lastSystemAppsUseLightTheme != currentInt)
        {
            _lastSystemAppsUseLightTheme = currentInt;
            var newTheme = systemLight ? AppTheme.Light : AppTheme.Dark;
            if (newTheme != _currentTheme)
            {
                ApplyThemeCore(newTheme, _currentStyle);
                return true;
            }
        }
        return false;
    }

    private static void ApplyThemeCore(AppTheme theme, ThemeStyle? style = null)
    {
        _currentTheme = theme;
        if (style.HasValue) _currentStyle = style.Value;
        bool isDark = theme == AppTheme.Dark;
        bool isLiquid = _currentStyle == ThemeStyle.LiquidGlass;

        // 1. Update static frozen brushes in ThemeBrushes
        ThemeBrushes.ApplyActiveTheme(isDark, isLiquid);

        // 2. Update Application.Current.Resources if dispatcher/WPF is active
        if (Application.Current != null)
        {
            void UpdateResources()
            {
                var res = Application.Current.Resources;
                res["PrimaryTextBrush"] = isDark ? ThemeBrushes.PrimaryTextDark : ThemeBrushes.PrimaryTextLight;
                res["SecondaryBrush"] = isDark ? ThemeBrushes.SecondaryTextDark : ThemeBrushes.SecondaryTextLight;
                res["TertiaryBrush"] = isDark ? ThemeBrushes.MutedGreyDark : ThemeBrushes.MutedGreyLight;

                res["CardSurfaceBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.CardSurfaceLiquidDark : ThemeBrushes.CardSurfaceLiquidLight)
                    : (isDark ? ThemeBrushes.CardSurfaceDark : ThemeBrushes.CardSurfaceLight);

                res["CardBorderBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.CardBorderLiquidDark : ThemeBrushes.CardBorderLiquidLight)
                    : (isDark ? ThemeBrushes.CardBorderDark : ThemeBrushes.CardBorderLight);

                res["PillBackgroundBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.PillBackgroundLiquidDark : ThemeBrushes.PillBackgroundLiquidLight)
                    : (isDark ? ThemeBrushes.PillBackgroundDark : ThemeBrushes.PillBackgroundLight);

                res["PillStrokeBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.PillStrokeLiquidDark : ThemeBrushes.PillStrokeLiquidLight)
                    : (isDark ? ThemeBrushes.PillStrokeDark : ThemeBrushes.PillStrokeLight);

                res["PillGlassSheenBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.PillGlassSheenLiquidDark : ThemeBrushes.PillGlassSheenLiquidLight)
                    : ThemeBrushes.PillGlassSheenTransparent;

                res["PillNotchEarBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.PillNotchEarLiquidDark : ThemeBrushes.PillNotchEarLiquidLight)
                    : (isDark ? ThemeBrushes.PillNotchEarDark : ThemeBrushes.PillNotchEarLight);

                res["IslandButtonBgBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.ButtonSubtleBgLiquidDark : ThemeBrushes.ButtonSubtleBgLiquidLight)
                    : (isDark ? ThemeBrushes.ButtonSubtleBgDark : ThemeBrushes.ButtonSubtleBgLight);

                res["IslandButtonBorderBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.ButtonSubtleBorderLiquidDark : ThemeBrushes.ButtonSubtleBorderLiquidLight)
                    : (isDark ? ThemeBrushes.ButtonSubtleBorderDark : ThemeBrushes.ButtonSubtleBorderLight);

                res["IslandButtonHoverVeilBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.ButtonHoverVeilLiquidDark : ThemeBrushes.ButtonHoverVeilLiquidLight)
                    : (isDark ? ThemeBrushes.ButtonHoverVeilDark : ThemeBrushes.ButtonHoverVeilLight);

                res["IslandSliderTrackBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.SliderTrackLiquidDark : ThemeBrushes.SliderTrackLiquidLight)
                    : (isDark ? ThemeBrushes.SliderTrackDark : ThemeBrushes.SliderTrackLight);

                res["IslandSliderFillBrush"] = isDark ? ThemeBrushes.SliderFillDark : ThemeBrushes.SliderFillLight;
                res["IslandSliderThumbBrush"] = isDark ? ThemeBrushes.SliderThumbDark : ThemeBrushes.SliderThumbLight;
                res["ScrollBarThumbBrush"] = isDark ? ThemeBrushes.ScrollBarThumbDark : ThemeBrushes.ScrollBarThumbLight;

                res["DockToolButtonBgBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.DockInactiveBgLiquidDark : ThemeBrushes.DockInactiveBgLiquidLight)
                    : (isDark ? ThemeBrushes.DockInactiveBgDark : ThemeBrushes.DockInactiveBgLight);

                res["DockToolButtonFgBrush"] = isDark ? ThemeBrushes.DockToolButtonFgDark : ThemeBrushes.DockToolButtonFgLight;

                res["DockInactiveBgBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.DockInactiveBgLiquidDark : ThemeBrushes.DockInactiveBgLiquidLight)
                    : (isDark ? ThemeBrushes.DockInactiveBgDark : ThemeBrushes.DockInactiveBgLight);

                res["DockActiveBgBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.DockActiveBgLiquidDark : ThemeBrushes.DockActiveBgLiquidLight)
                    : (isDark ? ThemeBrushes.DockActiveBgDark : ThemeBrushes.DockActiveBgLight);

                res["DockActiveBorderBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.DockActiveBorderLiquidDark : ThemeBrushes.DockActiveBorderLiquidLight)
                    : (isDark ? ThemeBrushes.DockActiveBorderDark : ThemeBrushes.DockActiveBorderLight);

                res["DockInactiveTextBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.DockInactiveTextLiquidDark : ThemeBrushes.DockInactiveTextLiquidLight)
                    : (isDark ? ThemeBrushes.SecondaryTextDark : ThemeBrushes.SecondaryTextLight);

                res["SettingsTabHoverBrush"] = isDark ? ThemeBrushes.SettingsTabHoverDark : ThemeBrushes.SettingsTabHoverDarkLight;
                res["SettingsTabActiveBgBrush"] = isDark ? ThemeBrushes.SettingsTabActiveBgDark : ThemeBrushes.SettingsTabActiveBgLight;

                res["ChipBgBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.ChipBgLiquidDark : ThemeBrushes.ChipBgLiquidLight)
                    : (isDark ? ThemeBrushes.ChipBgDark : ThemeBrushes.ChipBgLight);

                res["ChipBorderBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.ChipBorderLiquidDark : ThemeBrushes.ChipBorderLiquidLight)
                    : (isDark ? ThemeBrushes.ChipBorderDark : ThemeBrushes.ChipBorderLight);

                res["ElevatedSurfaceBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.ElevatedSurfaceLiquidDark : ThemeBrushes.ElevatedSurfaceLiquidLight)
                    : (isDark ? ThemeBrushes.ElevatedSurfaceDark : ThemeBrushes.ElevatedSurfaceLight);

                res["MediaPlayButtonBgBrush"] = isDark ? ThemeBrushes.MediaPlayButtonBgDark : ThemeBrushes.MediaPlayButtonBgLight;
                res["MediaPlayButtonFgBrush"] = isDark ? ThemeBrushes.MediaPlayButtonFgDark : ThemeBrushes.MediaPlayButtonFgLight;

                res["MenuBackground"] = isLiquid
                    ? (isDark ? ThemeBrushes.MenuBackgroundLiquidDark : ThemeBrushes.MenuBackgroundLiquidLight)
                    : (isDark ? ThemeBrushes.MenuBackgroundDark : ThemeBrushes.MenuBackgroundLight);

                res["MenuBorder"] = isLiquid
                    ? (isDark ? ThemeBrushes.MenuBorderLiquidDark : ThemeBrushes.MenuBorderLiquidLight)
                    : (isDark ? ThemeBrushes.MenuBorderDark : ThemeBrushes.MenuBorderLight);

                res["MenuSeparator"] = isDark ? ThemeBrushes.MenuSeparatorDark : ThemeBrushes.MenuSeparatorLight;

                res["FloatingDockBackgroundBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.FloatingDockBackgroundLiquidDark : ThemeBrushes.FloatingDockBackgroundLiquidLight)
                    : (isDark ? ThemeBrushes.FloatingDockBackgroundDark : ThemeBrushes.FloatingDockBackgroundLight);

                res["FloatingDockBorderBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.FloatingDockBorderLiquidDark : ThemeBrushes.FloatingDockBorderLiquidLight)
                    : (isDark ? ThemeBrushes.FloatingDockBorderDark : ThemeBrushes.FloatingDockBorderLight);

                res["ClockTileBgBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.ClockTileBgLiquidDark : ThemeBrushes.ClockTileBgLiquidLight)
                    : (isDark ? ThemeBrushes.ClockTileBgDark : ThemeBrushes.ClockTileBgLight);

                res["ClockTileBorderBrush"] = isLiquid
                    ? (isDark ? ThemeBrushes.ClockTileBorderLiquidDark : ThemeBrushes.ClockTileBorderLiquidLight)
                    : (isDark ? ThemeBrushes.ClockTileBorderDark : ThemeBrushes.ClockTileBorderLight);
                res["ClockTileDividerBrush"] = isDark ? ThemeBrushes.ClockTileDividerDark : ThemeBrushes.ClockTileDividerLight;
                res["ClockTileFgBrush"] = isDark ? ThemeBrushes.ClockTileFgDark : ThemeBrushes.ClockTileFgLight;
                res["ClockDialFaceBrush"] = isDark ? ThemeBrushes.ClockDialFaceDark : ThemeBrushes.ClockDialFaceLight;
                res["ClockDialBorderBrush"] = isDark ? ThemeBrushes.ClockDialBorderDark : ThemeBrushes.ClockDialBorderLight;
                res["ClockHandBrush"] = isDark ? ThemeBrushes.ClockHandDark : ThemeBrushes.ClockHandLight;

                LiquidUiEngine.ApplyStyle(isLiquid, isDark, res);
            }

            if (Application.Current.Dispatcher.CheckAccess())
            {
                UpdateResources();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(UpdateResources);
            }
        }

        // 3. Notify subscribers
        ThemeChanged?.Invoke(_currentTheme);
        ThemeStyleChanged?.Invoke(_currentStyle);
    }
}