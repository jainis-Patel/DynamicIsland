using System;
using System.Windows.Media;
using DynamicIsland.LiquidGlass;

namespace DynamicIsland.Core;

/// <summary>
/// Centralized configuration and tuning source for all Liquid Glass surfaces across Dynamic Island.
/// Provides unified presets, global optical scaling factors, dynamic adaptation, and quality management.
/// </summary>
public static class LiquidGlassTheme
{
    // Global optical multipliers (tunable at runtime via settings or CLI)
    public static float GlobalThicknessScale { get; set; } = 1.0f;
    public static float GlobalSpecularScale { get; set; } = 1.0f;
    public static float GlobalBlurScale { get; set; } = 1.0f;

    /// <summary>
    /// Optical preset for the compact Dynamic Island capsule.
    /// Punchy lens rim, deep obsidian tint, high specular reflection.
    /// </summary>
    public static GlassSettings PillCapsule => GlassPresets.Pill;

    /// <summary>
    /// Optical preset for the expanded interactive card surface.
    /// Thinner refraction to preserve interior text readability, higher diffusion blur.
    /// </summary>
    public static GlassSettings ExpandedCard => GlassPresets.Card;

    /// <summary>
    /// Optical preset for the floating bottom navigation dock.
    /// Balanced vitreous material with narrow 7px perimeter bevel and deep dark frosted contrast.
    /// </summary>
    public static GlassSettings FloatingDock => GlassPresets.Dock;

    /// <summary>
    /// Optical preset for secondary popup dialog cards (Notes, Clipboard).
    /// </summary>
    public static GlassSettings PopupCard => GlassPresets.Card;

    /// <summary>
    /// Optical preset for standalone floating widgets (Basket, MiniPlayer).
    /// </summary>
    public static GlassSettings FloatingWidget => GlassPresets.Regular;

    /// <summary>
    /// Computes contrast-adaptive foreground color based on estimated backdrop luminance.
    /// Returns near-white for dark backdrops, deep charcoal for light/bright backdrops.
    /// </summary>
    public static Color GetAdaptiveTextColor(float backdropLuminance)
    {
        return backdropLuminance > 0.55f
            ? Color.FromArgb(0xEE, 0x12, 0x14, 0x18)
            : Color.FromArgb(0xFA, 0xF5, 0xF5, 0xF7);
    }
}
