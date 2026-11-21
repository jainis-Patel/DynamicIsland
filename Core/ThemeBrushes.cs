using System.Windows.Media;

namespace DynamicIsland.Core;

/// <summary>
/// Pre-allocated, frozen WPF <see cref="SolidColorBrush"/> and <see cref="LinearGradientBrush"/> instances
/// and <see cref="Color"/> constants calibrated to Apple iOS/macOS Dark Mode and Light Mode Human Interface Guidelines.
/// Freezing brushes eliminates WPF render-thread change-notification overhead and avoids GC allocations.
/// </summary>
internal static class ThemeBrushes
{
    // ── Core Apple HIG System Colors (Dark Mode) ─────────────────────
    public static readonly Color GreenColor = Color.FromRgb(0x30, 0xD1, 0x58);
    public static readonly Color AmberColor = Color.FromRgb(0xFF, 0x9F, 0x0A);
    public static readonly Color RedColor = Color.FromRgb(0xFF, 0x45, 0x3A);
    public static readonly Color BlueColor = Color.FromRgb(0x0A, 0x84, 0xFF);
    public static readonly Color PurpleColor = Color.FromRgb(0x5E, 0x5C, 0xE6);
    public static readonly Color PinkColor = Color.FromRgb(0xFF, 0x33, 0x66);
    public static readonly Color CyanColor = Color.FromRgb(0x32, 0xAD, 0xE6);
    public static readonly Color PrimaryTextColor = Color.FromRgb(0xF5, 0xF5, 0xF7);
    public static readonly Color SecondaryTextColor = Color.FromRgb(0x86, 0x86, 0x8B);
    public static readonly Color MutedGreyColor = Color.FromRgb(0x98, 0x98, 0x9D);
    public static readonly Color DarkInkColor = Color.FromRgb(0x0A, 0x0A, 0x0C);

    // ── Core Apple HIG System Colors (Light Mode) ────────────────────
    public static readonly Color GreenColorLight = Color.FromRgb(0x24, 0x8A, 0x3D);
    public static readonly Color AmberColorLight = Color.FromRgb(0xD9, 0x77, 0x06);
    public static readonly Color RedColorLight = Color.FromRgb(0xD7, 0x26, 0x26);
    public static readonly Color BlueColorLight = Color.FromRgb(0x00, 0x7A, 0xFF);
    public static readonly Color PurpleColorLight = Color.FromRgb(0x89, 0x44, 0xAB);
    public static readonly Color PinkColorLight = Color.FromRgb(0xD8, 0x1B, 0x60);
    public static readonly Color CyanColorLight = Color.FromRgb(0x00, 0x88, 0xCC);
    public static readonly Color PrimaryTextColorLight = Color.FromRgb(0x1C, 0x1C, 0x1E);
    public static readonly Color SecondaryTextColorLight = Color.FromRgb(0x6E, 0x6E, 0x73);
    public static readonly Color MutedGreyColorLight = Color.FromRgb(0x8E, 0x8E, 0x93);
    public static readonly Color DarkInkColorLight = Color.FromRgb(0xF2, 0xF2, 0xF7);

    // ── Pre-Frozen Solid Brushes (Dark Mode) ─────────────────────────
    public static readonly SolidColorBrush GreenDark = CreateFrozen(GreenColor);
    public static readonly SolidColorBrush AmberDark = CreateFrozen(AmberColor);
    public static readonly SolidColorBrush RedDark = CreateFrozen(RedColor);
    public static readonly SolidColorBrush BlueDark = CreateFrozen(BlueColor);
    public static readonly SolidColorBrush PinkDark = CreateFrozen(PinkColor);
    public static readonly SolidColorBrush PurpleDark = CreateFrozen(PurpleColor);
    public static readonly SolidColorBrush CyanDark = CreateFrozen(CyanColor);
    public static readonly SolidColorBrush PrimaryTextDark = CreateFrozen(PrimaryTextColor);
    public static readonly SolidColorBrush SecondaryTextDark = CreateFrozen(SecondaryTextColor);
    public static readonly SolidColorBrush MutedGreyDark = CreateFrozen(MutedGreyColor);
    public static readonly SolidColorBrush DarkInkDark = CreateFrozen(DarkInkColor);

    // ── Pre-Frozen Solid Brushes (Light Mode) ────────────────────────
    public static readonly SolidColorBrush GreenLight = CreateFrozen(GreenColorLight);
    public static readonly SolidColorBrush AmberLight = CreateFrozen(AmberColorLight);
    public static readonly SolidColorBrush RedLight = CreateFrozen(RedColorLight);
    public static readonly SolidColorBrush BlueLight = CreateFrozen(BlueColorLight);
    public static readonly SolidColorBrush PinkLight = CreateFrozen(PinkColorLight);
    public static readonly SolidColorBrush PurpleLight = CreateFrozen(PurpleColorLight);
    public static readonly SolidColorBrush CyanLight = CreateFrozen(CyanColorLight);
    public static readonly SolidColorBrush PrimaryTextLight = CreateFrozen(PrimaryTextColorLight);
    public static readonly SolidColorBrush SecondaryTextLight = CreateFrozen(SecondaryTextColorLight);
    public static readonly SolidColorBrush MutedGreyLight = CreateFrozen(MutedGreyColorLight);
    public static readonly SolidColorBrush DarkInkLight = CreateFrozen(DarkInkColorLight);

    // ── Translucent Surface Brushes (Dark Mode) ──────────────────────
    public static readonly SolidColorBrush CardSurfaceDark = CreateFrozen(Color.FromRgb(0x12, 0x12, 0x16));
    public static readonly SolidColorBrush CardBorderDark = CreateFrozen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush DockInactiveBgDark = CreateFrozen(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush DockActiveBgDark = CreateFrozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush DockActiveBorderDark = CreateFrozen(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush ButtonSubtleBgDark = CreateFrozen(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush ButtonSubtleBorderDark = CreateFrozen(Color.FromArgb(0x00, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush ButtonHoverVeilDark = CreateFrozen(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush BorderSubtleDark = CreateFrozen(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush MicMutedBgDark = CreateFrozen(Color.FromArgb(0x2E, 0xFF, 0x45, 0x3A));
    public static readonly SolidColorBrush MenuBackgroundDark = CreateFrozen(Color.FromRgb(0x14, 0x14, 0x17));
    public static readonly SolidColorBrush MenuBorderDark = CreateFrozen(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush MenuSeparatorDark = CreateFrozen(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush SliderTrackDark = CreateFrozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush SliderFillDark = CreateFrozen(PrimaryTextColor);
    public static readonly SolidColorBrush SliderThumbDark = CreateFrozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush ScrollBarThumbDark = CreateFrozen(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush DockToolButtonFgDark = CreateFrozen(Color.FromRgb(0xE5, 0xE5, 0xEA));
    public static readonly SolidColorBrush SettingsTabHoverDark = CreateFrozen(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush SettingsTabActiveBgDark = CreateFrozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush ChipBgDark = CreateFrozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush ChipBorderDark = CreateFrozen(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush ElevatedSurfaceDark = CreateFrozen(Color.FromRgb(0x1A, 0x1A, 0x22));
    public static readonly SolidColorBrush MediaPlayButtonBgDark = CreateFrozen(Color.FromRgb(0xF5, 0xF5, 0xF7));
    public static readonly SolidColorBrush MediaPlayButtonFgDark = CreateFrozen(Color.FromRgb(0x0A, 0x0A, 0x0C));
    public static readonly SolidColorBrush PillNotchEarDark = CreateFrozen(Color.FromRgb(0x04, 0x04, 0x06));
    public static readonly SolidColorBrush AlertAmberBgDark = CreateFrozen(Color.FromArgb(0x28, 0xFF, 0x9F, 0x0A));
    public static readonly SolidColorBrush AlertAmberBorderDark = CreateFrozen(Color.FromArgb(0x60, 0xFF, 0x9F, 0x0A));
    public static readonly SolidColorBrush AlertRedBgDark = CreateFrozen(Color.FromArgb(0x28, 0xFF, 0x45, 0x3A));
    public static readonly SolidColorBrush AlertRedBorderDark = CreateFrozen(Color.FromArgb(0x60, 0xFF, 0x45, 0x3A));

    public static readonly LinearGradientBrush PillBackgroundDark = CreateFrozenLinearGradient(
        Color.FromRgb(0x03, 0x03, 0x04),
        Color.FromRgb(0x08, 0x08, 0x0B),
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));

    public static readonly LinearGradientBrush PillStrokeDark = CreateFrozenLinearGradient(
        Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF),
        Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));

    public static readonly LinearGradientBrush FloatingDockBackgroundDark = CreateFrozenLinearGradient(
        Color.FromArgb(0xF0, 0x0E, 0x0E, 0x14),
        Color.FromArgb(0xEE, 0x06, 0x06, 0x0A),
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));

    public static readonly LinearGradientBrush FloatingDockBorderDark = CreateFrozenLinearGradient(
        Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF),
        Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF),
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));

    // ── Translucent Surface Brushes (Light Mode) ─────────────────────
    public static readonly SolidColorBrush CardSurfaceLight = CreateFrozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush CardBorderLight = CreateFrozen(Color.FromArgb(0x14, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush DockInactiveBgLight = CreateFrozen(Color.FromArgb(0x0A, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush DockActiveBgLight = CreateFrozen(Color.FromArgb(0x18, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush DockActiveBorderLight = CreateFrozen(Color.FromArgb(0x2A, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush ButtonSubtleBgLight = CreateFrozen(Color.FromArgb(0x0C, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush ButtonSubtleBorderLight = CreateFrozen(Color.FromArgb(0x10, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush ButtonHoverVeilLight = CreateFrozen(Color.FromArgb(0xFF, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush BorderSubtleLight = CreateFrozen(Color.FromArgb(0x12, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush MicMutedBgLight = CreateFrozen(Color.FromArgb(0x22, 0xD7, 0x26, 0x26));
    public static readonly SolidColorBrush MenuBackgroundLight = CreateFrozen(Color.FromRgb(0xFA, 0xFA, 0xFC));
    public static readonly SolidColorBrush MenuBorderLight = CreateFrozen(Color.FromArgb(0x1E, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush MenuSeparatorLight = CreateFrozen(Color.FromArgb(0x12, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush SliderTrackLight = CreateFrozen(Color.FromArgb(0x14, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush SliderFillLight = CreateFrozen(PrimaryTextColorLight);
    public static readonly SolidColorBrush SliderThumbLight = CreateFrozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush ScrollBarThumbLight = CreateFrozen(Color.FromArgb(0x30, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush DockToolButtonFgLight = CreateFrozen(Color.FromRgb(0x2C, 0x2C, 0x2E));
    public static readonly SolidColorBrush SettingsTabHoverDarkLight = CreateFrozen(Color.FromArgb(0x0A, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush SettingsTabActiveBgLight = CreateFrozen(Color.FromArgb(0x14, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush ChipBgLight = CreateFrozen(Color.FromArgb(0x0A, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush ChipBorderLight = CreateFrozen(Color.FromArgb(0x12, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush ElevatedSurfaceLight = CreateFrozen(Color.FromRgb(0xEE, 0xEE, 0xF2));
    public static readonly SolidColorBrush MediaPlayButtonBgLight = CreateFrozen(Color.FromRgb(0x1C, 0x1C, 0x1E));
    public static readonly SolidColorBrush MediaPlayButtonFgLight = CreateFrozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush PillNotchEarLight = CreateFrozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush AlertAmberBgLight = CreateFrozen(Color.FromArgb(0x20, 0xD9, 0x77, 0x06));
    public static readonly SolidColorBrush AlertAmberBorderLight = CreateFrozen(Color.FromArgb(0x50, 0xD9, 0x77, 0x06));
    public static readonly SolidColorBrush AlertRedBgLight = CreateFrozen(Color.FromArgb(0x20, 0xD7, 0x26, 0x26));
    public static readonly SolidColorBrush AlertRedBorderLight = CreateFrozen(Color.FromArgb(0x50, 0xD7, 0x26, 0x26));

    public static readonly LinearGradientBrush PillBackgroundLight = CreateFrozenLinearGradient(
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0xEE, 0xF0, 0xF5),
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));

    public static readonly LinearGradientBrush PillStrokeLight = CreateFrozenLinearGradient(
        Color.FromArgb(0x28, 0x00, 0x00, 0x00),
        Color.FromArgb(0x14, 0x00, 0x00, 0x00),
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));

    public static readonly LinearGradientBrush FloatingDockBackgroundLight = CreateFrozenLinearGradient(
        Color.FromArgb(0xF4, 0xFF, 0xFF, 0xFF),
        Color.FromArgb(0xEE, 0xEE, 0xF0, 0xF5),
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));

    public static readonly LinearGradientBrush FloatingDockBorderLight = CreateFrozenLinearGradient(
        Color.FromArgb(0x28, 0x00, 0x00, 0x00),
        Color.FromArgb(0x12, 0x00, 0x00, 0x00),
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));

    // ── Apple Liquid Glass UI Brushes (Dark Mode - Frosted Obsidian Glass) ─────────
    public static readonly LinearGradientBrush PillBackgroundLiquidDark = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0xCC, 0x14, 0x16, 0x22), 0.00),  // Frosted obsidian glass top
        (Color.FromArgb(0xAA, 0x0C, 0x0E, 0x16), 0.35),  // Deep vitreous core
        (Color.FromArgb(0xDC, 0x06, 0x07, 0x0B), 1.00)); // Grounded solid frosted base

    public static readonly LinearGradientBrush PillStrokeLiquidDark = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), 0.00),  // Crisp 44% white specular rim
        (Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF), 1.00)); // Delicate separation bottom border

    public static readonly LinearGradientBrush PillInnerBevelLiquidDark = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF), 0.00),  // Subtle inner top specular highlight
        (Color.FromArgb(0x06, 0xFF, 0xFF, 0xFF), 0.20),
        (Color.FromArgb(0x00, 0x00, 0x00, 0x00), 1.00));

    public static readonly LinearGradientBrush PillGlassSheenLiquidDark = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 0.00),  // Gentle top surface light wash
        (Color.FromArgb(0x06, 0xFF, 0xFF, 0xFF), 0.40),
        (Color.FromArgb(0x00, 0x00, 0x00, 0x00), 1.00));

    public static readonly LinearGradientBrush FloatingDockBackgroundLiquidDark = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0xA5, 0x0C, 0x0E, 0x16), 0.0),   // Rich dark frosted obsidian for dock contrast
        (Color.FromArgb(0x95, 0x08, 0x0A, 0x10), 1.0));

    public static readonly LinearGradientBrush FloatingDockBorderLiquidDark = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF), 0.0),   // Refined hairline specular highlight rim
        (Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), 0.45),
        (Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF), 1.0));

    public static readonly LinearGradientBrush CardSurfaceLiquidDark = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0xD4, 0x16, 0x18, 0x26), 0.0),  // Rich dark frosted acrylic (high contrast + readability)
        (Color.FromArgb(0xC0, 0x10, 0x12, 0x1C), 0.45),
        (Color.FromArgb(0xDC, 0x0A, 0x0B, 0x12), 1.0));

    public static readonly LinearGradientBrush CardBorderLiquidDark = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF), 0.0),
        (Color.FromArgb(0x2C, 0xFF, 0xFF, 0xFF), 0.45),
        (Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF), 1.0));

    public static readonly SolidColorBrush DockInactiveBgLiquidDark = CreateFrozen(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush DockActiveBgLiquidDark = CreateFrozen(Color.FromArgb(0x3E, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush DockActiveBorderLiquidDark = CreateFrozen(Color.FromArgb(0x65, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush DockInactiveTextLiquidDark = CreateFrozen(Color.FromArgb(0xBA, 0xF0, 0xF2, 0xF8));
    public static readonly SolidColorBrush DockInactiveTextLiquidLight = CreateFrozen(Color.FromArgb(0xBA, 0x1A, 0x1C, 0x24));

    public static readonly LinearGradientBrush ButtonSubtleBgLiquidDark = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF), 0.0),
        (Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 1.0));

    public static readonly LinearGradientBrush ButtonSubtleBorderLiquidDark = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF), 0.0),
        (Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), 1.0));

    public static readonly SolidColorBrush ButtonHoverVeilLiquidDark = CreateFrozen(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));

    public static readonly LinearGradientBrush ChipBgLiquidDark = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF), 0.0),
        (Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF), 1.0));

    public static readonly LinearGradientBrush ChipBorderLiquidDark = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF), 0.0),
        (Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF), 1.0));

    public static readonly SolidColorBrush ElevatedSurfaceLiquidDark = CreateFrozen(Color.FromArgb(0x50, 0x18, 0x18, 0x24));
    public static readonly SolidColorBrush SliderTrackLiquidDark = CreateFrozen(Color.FromArgb(0x2C, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush MenuBackgroundLiquidDark = CreateFrozen(Color.FromArgb(0xCC, 0x14, 0x14, 0x1B));
    public static readonly SolidColorBrush MenuBorderLiquidDark = CreateFrozen(Color.FromArgb(0x42, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush PillNotchEarLiquidDark = CreateFrozen(Color.FromArgb(0xEE, 0x14, 0x16, 0x20));
    public static readonly SolidColorBrush ClockTileBgLiquidDark = CreateFrozen(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush ClockTileBorderLiquidDark = CreateFrozen(Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF));

    // ── Apple Liquid Glass UI Brushes (Light Mode - Frosted Ice Glass) ────────
    public static readonly LinearGradientBrush PillBackgroundLiquidLight = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF), 0.00),  // Luminous frosty white reflection
        (Color.FromArgb(0xCA, 0xF2, 0xF5, 0xFA), 0.40),
        (Color.FromArgb(0xDC, 0xE5, 0xEC, 0xF5), 1.00)); // Solid frosted ice base

    public static readonly LinearGradientBrush PillStrokeLiquidLight = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0x55, 0x00, 0x00, 0x00), 0.00),
        (Color.FromArgb(0x20, 0x00, 0x00, 0x00), 1.00));

    public static readonly LinearGradientBrush PillInnerBevelLiquidLight = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), 0.00),
        (Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 0.25),
        (Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.00));

    public static readonly LinearGradientBrush PillGlassSheenLiquidLight = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF), 0.00),
        (Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 0.45),
        (Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.00));

    public static readonly SolidColorBrush PillGlassSheenTransparent = CreateFrozen(Color.FromArgb(0x00, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush PillInnerBevelTransparent = CreateFrozen(Color.FromArgb(0x00, 0x00, 0x00, 0x00));

    public static readonly LinearGradientBrush FloatingDockBackgroundLiquidLight = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF), 0.0),
        (Color.FromArgb(0xD8, 0xEE, 0xF1, 0xF8), 1.0));

    public static readonly LinearGradientBrush FloatingDockBorderLiquidLight = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0x95, 0xFF, 0xFF, 0xFF), 0.0),
        (Color.FromArgb(0x30, 0x00, 0x00, 0x00), 1.0));

    public static readonly LinearGradientBrush CardSurfaceLiquidLight = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF), 0.0),
        (Color.FromArgb(0xD4, 0xF0, 0xF3, 0xFA), 0.45),
        (Color.FromArgb(0xE6, 0xE4, 0xEB, 0xF4), 1.0));

    public static readonly LinearGradientBrush CardBorderLiquidLight = CreateFrozenLinearGradient(
        new System.Windows.Point(0, 0), new System.Windows.Point(0, 1),
        (Color.FromArgb(0xA5, 0xFF, 0xFF, 0xFF), 0.0),
        (Color.FromArgb(0x28, 0x00, 0x00, 0x00), 1.0));

    public static readonly SolidColorBrush DockInactiveBgLiquidLight = CreateFrozen(Color.FromArgb(0x18, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush DockActiveBgLiquidLight = CreateFrozen(Color.FromArgb(0x32, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush DockActiveBorderLiquidLight = CreateFrozen(Color.FromArgb(0x48, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush ButtonSubtleBgLiquidLight = CreateFrozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush ButtonSubtleBorderLiquidLight = CreateFrozen(Color.FromArgb(0x65, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush ButtonHoverVeilLiquidLight = CreateFrozen(Color.FromArgb(0x25, 0x00, 0x7A, 0xFF));
    public static readonly SolidColorBrush ChipBgLiquidLight = CreateFrozen(Color.FromArgb(0x25, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush ChipBorderLiquidLight = CreateFrozen(Color.FromArgb(0x35, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush ElevatedSurfaceLiquidLight = CreateFrozen(Color.FromArgb(0x75, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush SliderTrackLiquidLight = CreateFrozen(Color.FromArgb(0x26, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush MenuBackgroundLiquidLight = CreateFrozen(Color.FromArgb(0xD8, 0xF5, 0xF7, 0xFB));
    public static readonly SolidColorBrush MenuBorderLiquidLight = CreateFrozen(Color.FromArgb(0x35, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush PillNotchEarLiquidLight = CreateFrozen(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush ClockTileBgLiquidLight = CreateFrozen(Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush ClockTileBorderLiquidLight = CreateFrozen(Color.FromArgb(0x30, 0x00, 0x00, 0x00));

    // ── Active Theme-Bound Static Properties (Preserves backward compatibility) ─
    public static SolidColorBrush Accent => Blue;
    public static Color AccentColor => BlueColor;
    public static SolidColorBrush Green { get; internal set; } = GreenDark;
    public static SolidColorBrush Amber { get; internal set; } = AmberDark;
    public static SolidColorBrush Red { get; internal set; } = RedDark;
    public static SolidColorBrush Blue { get; internal set; } = BlueDark;
    public static SolidColorBrush Pink { get; internal set; } = PinkDark;
    public static SolidColorBrush Purple { get; internal set; } = PurpleDark;
    public static SolidColorBrush PrimaryText { get; internal set; } = PrimaryTextDark;
    public static SolidColorBrush SecondaryText { get; internal set; } = SecondaryTextDark;
    public static SolidColorBrush MutedGrey { get; internal set; } = MutedGreyDark;
    public static SolidColorBrush DarkInk { get; internal set; } = DarkInkDark;

    public static Brush CardSurface { get; internal set; } = CardSurfaceDark;
    public static Brush CardBorder { get; internal set; } = CardBorderDark;
    public static Brush DockInactiveBg { get; internal set; } = DockInactiveBgDark;
    public static SolidColorBrush DockActiveBg { get; internal set; } = DockActiveBgDark;
    public static SolidColorBrush DockActiveBorder { get; internal set; } = DockActiveBorderDark;
    public static SolidColorBrush DockInactiveText { get; internal set; } = SecondaryTextDark;
    public static Brush ButtonSubtleBg { get; internal set; } = ButtonSubtleBgDark;
    public static Brush BorderSubtle { get; internal set; } = BorderSubtleDark;
    public static SolidColorBrush MicMutedBg { get; internal set; } = MicMutedBgDark;
    public static SolidColorBrush AlertAmberBg { get; internal set; } = AlertAmberBgDark;
    public static SolidColorBrush AlertAmberBorder { get; internal set; } = AlertAmberBorderDark;
    public static SolidColorBrush AlertRedBg { get; internal set; } = AlertRedBgDark;
    public static SolidColorBrush AlertRedBorder { get; internal set; } = AlertRedBorderDark;

    public static SolidColorBrush PlanPerfActiveBg { get; internal set; } = CreateFrozen(Color.FromArgb(0x22, 0xFF, 0x9F, 0x0A));
    public static SolidColorBrush PlanBalancedActiveBg { get; internal set; } = CreateFrozen(Color.FromArgb(0x22, 0x0A, 0x84, 0xFF));
    public static SolidColorBrush PlanSaverActiveBg { get; internal set; } = CreateFrozen(Color.FromArgb(0x22, 0x30, 0xD1, 0x58));
    public static SolidColorBrush PlanPerfActiveBorder { get; internal set; } = CreateFrozen(Color.FromArgb(0x50, 0xFF, 0x9F, 0x0A));
    public static SolidColorBrush PlanBalancedActiveBorder { get; internal set; } = CreateFrozen(Color.FromArgb(0x50, 0x0A, 0x84, 0xFF));
    public static SolidColorBrush PlanSaverActiveBorder { get; internal set; } = CreateFrozen(Color.FromArgb(0x50, 0x30, 0xD1, 0x58));

    // ── Shared Pre-Frozen Status, Toggle & Badge Brushes (Zero-Allocation) ──
    public static readonly SolidColorBrush GreenBorderActive = CreateFrozen(Color.FromArgb(0x45, 0x30, 0xD1, 0x58));
    public static readonly SolidColorBrush BlueBorderActive = CreateFrozen(Color.FromArgb(0x45, 0x0A, 0x84, 0xFF));
    public static readonly SolidColorBrush AmberBorderActive = CreateFrozen(Color.FromArgb(0x50, 0xFF, 0x9F, 0x0A));
    public static readonly SolidColorBrush RedBorderActive = CreateFrozen(Color.FromArgb(0x45, 0xFF, 0x45, 0x3A));

    public static readonly SolidColorBrush RadioWifiActiveBg = CreateFrozen(Color.FromArgb(0x28, 0x0A, 0x84, 0xFF));
    public static readonly SolidColorBrush RadioWifiActiveBorder = CreateFrozen(Color.FromArgb(0x55, 0x0A, 0x84, 0xFF));
    public static readonly SolidColorBrush RadioBtActiveBg = CreateFrozen(Color.FromArgb(0x28, 0x5E, 0x5C, 0xE6));
    public static readonly SolidColorBrush RadioBtActiveBorder = CreateFrozen(Color.FromArgb(0x55, 0x5E, 0x5C, 0xE6));
    public static readonly SolidColorBrush RadioInactiveBg = CreateFrozen(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush RadioInactiveBorder = CreateFrozen(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));

    public static readonly SolidColorBrush SignalBarInactive = CreateFrozen(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush PillToggleActiveBg = CreateFrozen(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush FilterPillActiveBg = CreateFrozen(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush FilterPillInactiveBg = CreateFrozen(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush FilterPillInactiveBorder = CreateFrozen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush PrivacySensorOff = CreateFrozen(Color.FromRgb(0x3A, 0x3A, 0x44));
    public static readonly SolidColorBrush MicActivePillBg = CreateFrozen(Color.FromArgb(0x1E, 0x30, 0xD1, 0x58));
    public static readonly SolidColorBrush MicMutedPillBg = CreateFrozen(Color.FromArgb(0x30, 0xFF, 0x45, 0x3A));
    public static readonly SolidColorBrush CaffeineActiveBg = CreateFrozen(Color.FromArgb(0x28, 0xFF, 0x9F, 0x0A));
    public static readonly SolidColorBrush NightLightActiveBg = CreateFrozen(Color.FromArgb(0x26, 0xFF, 0x9F, 0x0A));

    public static readonly SolidColorBrush ClipBadgeCode = CreateFrozen(Color.FromArgb(0xFF, 0x30, 0xD1, 0x58));
    public static readonly SolidColorBrush ClipBadgeUrl = CreateFrozen(Color.FromArgb(0xFF, 0x0A, 0x84, 0xFF));
    public static readonly SolidColorBrush ClipBadgeColor = CreateFrozen(Color.FromArgb(0xFF, 0xFF, 0x9F, 0x0A));
    public static readonly SolidColorBrush ClipBadgeImage = CreateFrozen(Color.FromArgb(0xFF, 0xAF, 0x52, 0xDE));
    public static readonly SolidColorBrush ClipBadgeFile = CreateFrozen(Color.FromArgb(0xFF, 0x00, 0xF0, 0xFF));
    public static readonly SolidColorBrush ClipBadgeText = CreateFrozen(Color.FromArgb(0xFF, 0x86, 0x86, 0x8B));

    // ── Clock Surface & Tile Brushes (Dark vs Light Adaptive) ──────────
    public static readonly SolidColorBrush ClockTileBgDark = CreateFrozen(Color.FromArgb(0xE0, 0x18, 0x18, 0x22));
    public static readonly SolidColorBrush ClockTileBgLight = CreateFrozen(Color.FromArgb(0xEB, 0xE5, 0xE8, 0xF0));
    public static readonly SolidColorBrush ClockTileBorderDark = CreateFrozen(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush ClockTileBorderLight = CreateFrozen(Color.FromArgb(0x28, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush ClockTileDividerDark = CreateFrozen(Color.FromArgb(0x90, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush ClockTileDividerLight = CreateFrozen(Color.FromArgb(0x25, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush ClockTileFgDark = CreateFrozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush ClockTileFgLight = CreateFrozen(Color.FromRgb(0x11, 0x18, 0x27));
    public static readonly SolidColorBrush ClockDialFaceDark = CreateFrozen(Color.FromRgb(0x0E, 0x10, 0x16));
    public static readonly SolidColorBrush ClockDialFaceLight = CreateFrozen(Color.FromRgb(0xE4, 0xE7, 0xEE));
    public static readonly SolidColorBrush ClockDialBorderDark = CreateFrozen(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush ClockDialBorderLight = CreateFrozen(Color.FromArgb(0x25, 0x00, 0x00, 0x00));
    public static readonly SolidColorBrush ClockHandDark = CreateFrozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush ClockHandLight = CreateFrozen(Color.FromRgb(0x11, 0x18, 0x27));

    public static SolidColorBrush Cyan { get; internal set; } = CyanDark;
    public static SolidColorBrush ClockTileBg { get; internal set; } = ClockTileBgDark;
    public static SolidColorBrush ClockTileBorder { get; internal set; } = ClockTileBorderDark;
    public static SolidColorBrush ClockTileDivider { get; internal set; } = ClockTileDividerDark;
    public static SolidColorBrush ClockTileFg { get; internal set; } = ClockTileFgDark;
    public static SolidColorBrush ClockDialFace { get; internal set; } = ClockDialFaceDark;
    public static SolidColorBrush ClockDialBorder { get; internal set; } = ClockDialBorderDark;
    public static SolidColorBrush ClockHand { get; internal set; } = ClockHandDark;

    public static SolidColorBrush MenuBackground { get; internal set; } = MenuBackgroundDark;
    public static SolidColorBrush MenuBorder { get; internal set; } = MenuBorderDark;
    public static SolidColorBrush MenuSeparator { get; internal set; } = MenuSeparatorDark;

    public static LinearGradientBrush FloatingDockBackground { get; internal set; } = FloatingDockBackgroundDark;
    public static LinearGradientBrush FloatingDockBorder { get; internal set; } = FloatingDockBorderDark;
    public static Brush PillGlassSheen { get; internal set; } = PillGlassSheenTransparent;
    public static Brush PillInnerBevel { get; internal set; } = PillInnerBevelTransparent;

    public static void ApplyActiveTheme(bool isDark) =>
        ApplyActiveTheme(isDark, ThemeManager.IsLiquidGlass);

    public static void ApplyActiveTheme(bool isDark, bool isLiquidGlass)
    {
        Green = isDark ? GreenDark : GreenLight;
        Amber = isDark ? AmberDark : AmberLight;
        Red = isDark ? RedDark : RedLight;
        Blue = isDark ? BlueDark : BlueLight;
        Pink = isDark ? PinkDark : PinkLight;
        Purple = isDark ? PurpleDark : PurpleLight;
        Cyan = isDark ? CyanDark : CyanLight;
        PrimaryText = isDark ? PrimaryTextDark : PrimaryTextLight;
        SecondaryText = isDark ? SecondaryTextDark : SecondaryTextLight;
        MutedGrey = isDark ? MutedGreyDark : MutedGreyLight;
        DarkInk = isDark ? DarkInkDark : DarkInkLight;

        PillGlassSheen = isLiquidGlass
            ? (isDark ? PillGlassSheenLiquidDark : PillGlassSheenLiquidLight)
            : PillGlassSheenTransparent;

        PillInnerBevel = isLiquidGlass
            ? (isDark ? PillInnerBevelLiquidDark : PillInnerBevelLiquidLight)
            : PillInnerBevelTransparent;

        if (isLiquidGlass)
        {
            CardSurface = isDark ? CardSurfaceLiquidDark : CardSurfaceLiquidLight;
            CardBorder = isDark ? CardBorderLiquidDark : CardBorderLiquidLight;
            DockInactiveBg = isDark ? DockInactiveBgLiquidDark : DockInactiveBgLiquidLight;
            DockActiveBg = isDark ? DockActiveBgLiquidDark : DockActiveBgLiquidLight;
            DockActiveBorder = isDark ? DockActiveBorderLiquidDark : DockActiveBorderLiquidLight;
            ButtonSubtleBg = isDark ? ButtonSubtleBgLiquidDark : ButtonSubtleBgLiquidLight;
            BorderSubtle = isDark ? CardBorderLiquidDark : CardBorderLiquidLight;
            MenuBackground = isDark ? MenuBackgroundLiquidDark : MenuBackgroundLiquidLight;
            MenuBorder = isDark ? MenuBorderLiquidDark : MenuBorderLiquidLight;
            FloatingDockBackground = isDark ? FloatingDockBackgroundLiquidDark : FloatingDockBackgroundLiquidLight;
            FloatingDockBorder = isDark ? FloatingDockBorderLiquidDark : FloatingDockBorderLiquidLight;
            ClockTileBg = isDark ? ClockTileBgLiquidDark : ClockTileBgLiquidLight;
            ClockTileBorder = isDark ? ClockTileBorderLiquidDark : ClockTileBorderLiquidLight;
            DockInactiveText = isDark ? DockInactiveTextLiquidDark : DockInactiveTextLiquidLight;
        }
        else
        {
            CardSurface = isDark ? CardSurfaceDark : CardSurfaceLight;
            CardBorder = isDark ? CardBorderDark : CardBorderLight;
            DockInactiveBg = isDark ? DockInactiveBgDark : DockInactiveBgLight;
            DockActiveBg = isDark ? DockActiveBgDark : DockActiveBgLight;
            DockActiveBorder = isDark ? DockActiveBorderDark : DockActiveBorderLight;
            DockInactiveText = isDark ? SecondaryTextDark : SecondaryTextLight;
            ButtonSubtleBg = isDark ? ButtonSubtleBgDark : ButtonSubtleBgLight;
            BorderSubtle = isDark ? BorderSubtleDark : BorderSubtleLight;
            MenuBackground = isDark ? MenuBackgroundDark : MenuBackgroundLight;
            MenuBorder = isDark ? MenuBorderDark : MenuBorderLight;
            FloatingDockBackground = isDark ? FloatingDockBackgroundDark : FloatingDockBackgroundLight;
            FloatingDockBorder = isDark ? FloatingDockBorderDark : FloatingDockBorderLight;
            ClockTileBg = isDark ? ClockTileBgDark : ClockTileBgLight;
            ClockTileBorder = isDark ? ClockTileBorderDark : ClockTileBorderLight;
        }

        MicMutedBg = isDark ? MicMutedBgDark : MicMutedBgLight;
        AlertAmberBg = isDark ? AlertAmberBgDark : AlertAmberBgLight;
        AlertAmberBorder = isDark ? AlertAmberBorderDark : AlertAmberBorderLight;
        AlertRedBg = isDark ? AlertRedBgDark : AlertRedBgLight;
        AlertRedBorder = isDark ? AlertRedBorderDark : AlertRedBorderLight;
        MenuSeparator = isDark ? MenuSeparatorDark : MenuSeparatorLight;

        ClockTileDivider = isDark ? ClockTileDividerDark : ClockTileDividerLight;
        ClockTileFg = isDark ? ClockTileFgDark : ClockTileFgLight;
        ClockDialFace = isDark ? ClockDialFaceDark : ClockDialFaceLight;
        ClockDialBorder = isDark ? ClockDialBorderDark : ClockDialBorderLight;
        ClockHand = isDark ? ClockHandDark : ClockHandLight;
    }

    public static SolidColorBrush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static LinearGradientBrush CreateFrozenLinearGradient(Color c1, Color c2, System.Windows.Point p1, System.Windows.Point p2)
    {
        var brush = new LinearGradientBrush(c1, c2, p1, p2);
        brush.Freeze();
        return brush;
    }

    public static LinearGradientBrush CreateFrozenLinearGradient(
        System.Windows.Point p1, System.Windows.Point p2,
        params (Color Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = p1, EndPoint = p2 };
        foreach (var (col, off) in stops)
        {
            brush.GradientStops.Add(new GradientStop(col, off));
        }
        brush.Freeze();
        return brush;
    }
}