using System.Windows.Media;

namespace DynamicIsland.LiquidGlass;

/// <summary>
/// Pre-configured standard presets for common Liquid Glass surfaces across the application.
/// </summary>
public static class GlassPresets
{
    /// <summary>
    /// Standard balanced glass aesthetic matching modern macOS Sonoma / Sequoia vitreous materials.
    /// </summary>
    public static readonly GlassSettings Regular = GlassSettings.Create()
        .WithThickness(18.0f)
        .WithBevelWidth(20.0f)
        .WithIndexOfRefraction(1.50f)
        .WithBlurRadius(22.0f)
        .WithSaturation(1.20f)
        .WithBrightness(1.05f)
        .WithTint(Color.FromArgb(0x40, 0x14, 0x16, 0x22))
        .WithSpecular(0.90f, rimPower: 2.5f)
        .WithCornerRadius(20.0f)
        .WithCornerSmoothing(0.85f)
        .Build();

    /// <summary>
    /// Pure crystal glass with minimal tint and optical refraction.
    /// </summary>
    public static readonly GlassSettings Clear = GlassSettings.Create()
        .WithThickness(24.0f)
        .WithBevelWidth(16.0f)
        .WithIndexOfRefraction(1.55f)
        .WithBlurRadius(8.0f)
        .WithSaturation(1.10f)
        .WithBrightness(1.10f)
        .WithTint(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF))
        .WithSpecular(1.15f, rimPower: 3.0f)
        .WithCornerRadius(18.0f)
        .WithCornerSmoothing(0.80f)
        .Build();

    /// <summary>
    /// Deep obsidian glass tailored for OLED dark mode and Dynamic Island contrast.
    /// </summary>
    public static readonly GlassSettings Dark = GlassSettings.Create()
        .WithThickness(16.0f)
        .WithBevelWidth(18.0f)
        .WithIndexOfRefraction(1.48f)
        .WithBlurRadius(28.0f)
        .WithSaturation(1.25f)
        .WithBrightness(0.95f)
        .WithTint(Color.FromArgb(0x85, 0x07, 0x07, 0x0B))
        .WithSpecular(0.80f, rimPower: 2.2f)
        .WithCornerRadius(22.0f)
        .WithCornerSmoothing(0.85f)
        .Build();

    /// <summary>
    /// Vibrant tinted glass responding dynamically to music album artwork or system accent colors.
    /// </summary>
    public static GlassSettings Tinted(Color accentColor, float opacity = 0.35f)
    {
        byte alpha = (byte)Math.Clamp((int)(opacity * 255f), 0, 255);
        return GlassSettings.Create()
            .WithThickness(20.0f)
            .WithBevelWidth(20.0f)
            .WithIndexOfRefraction(1.50f)
            .WithBlurRadius(24.0f)
            .WithSaturation(1.35f)
            .WithBrightness(1.02f)
            .WithTint(Color.FromArgb(alpha, accentColor.R, accentColor.G, accentColor.B))
            .WithSpecular(0.95f, rimPower: 2.6f)
            .WithCornerRadius(18.0f)
            .Build();
    }

    /// <summary>
    /// Compact capsule button with accentuated refractive rim and punchy specular lens effect.
    /// </summary>
    public static readonly GlassSettings CapsuleButton = GlassSettings.Create()
        .WithThickness(28.0f)
        .WithBevelWidth(12.0f)
        .WithIndexOfRefraction(1.58f)
        .WithBlurRadius(14.0f)
        .WithSaturation(1.20f)
        .WithBrightness(1.12f)
        .WithTint(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF))
        .WithSpecular(1.30f, rimPower: 3.5f)
        .WithShape(GlassShapeType.Capsule)
        .Build();

    /// <summary>
    /// Dynamic Island pill capsule profile (smooth continuous squircle, subtle dark obsidian body).
    /// </summary>
    public static readonly GlassSettings Pill = GlassSettings.Create()
        .WithThickness(18.0f)
        .WithBevelWidth(16.0f)
        .WithIndexOfRefraction(1.50f)
        .WithBlurRadius(26.0f)
        .WithSaturation(1.22f)
        .WithBrightness(0.98f)
        .WithTint(Color.FromArgb(0x75, 0x05, 0x05, 0x09))
        .WithSpecular(0.88f, rimPower: 2.4f)
        .WithCornerRadius(15.0f)
        .WithCornerSmoothing(0.90f)
        .WithShape(GlassShapeType.Capsule)
        .Build();

    /// <summary>
    /// Expanded in-island telemetry card or secondary dialog backdrop.
    /// Lower refraction thickness to avoid text distortions; higher diffusion blur.
    /// </summary>
    public static readonly GlassSettings Card = GlassSettings.Create()
        .WithThickness(10.0f)
        .WithBevelWidth(24.0f)
        .WithIndexOfRefraction(1.45f)
        .WithBlurRadius(32.0f)
        .WithSaturation(1.28f)
        .WithBrightness(1.00f)
        .WithTint(Color.FromArgb(0x55, 0x10, 0x11, 0x18))
        .WithSpecular(0.70f, rimPower: 2.0f)
        .WithCornerRadius(16.0f)
        .WithCornerSmoothing(0.75f)
        .Build();

    /// <summary>
    /// Floating bottom navigation dock profile.
    /// Subtle 7px lens bevel on the perimeter leaves the center flat and smooth;
    /// rich frosted obsidian tint gives high contrast so navigation icons pop.
    /// </summary>
    public static readonly GlassSettings Dock = GlassSettings.Create()
        .WithThickness(8.0f)
        .WithBevelWidth(7.0f)
        .WithIndexOfRefraction(1.48f)
        .WithBlurRadius(32.0f)
        .WithSaturation(1.25f)
        .WithBrightness(1.02f)
        .WithTint(Color.FromArgb(0x95, 0x0B, 0x0D, 0x14))
        .WithSpecular(0.65f, rimPower: 2.2f)
        .WithCornerRadius(20.0f)
        .WithCornerSmoothing(0.85f)
        .WithShape(GlassShapeType.Squircle)
        .Build();
}
