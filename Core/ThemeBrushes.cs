using System.Windows.Media;

namespace DynamicIsland.Core;

/// <summary>
/// Pre-allocated, frozen WPF <see cref="SolidColorBrush"/> instances and <see cref="Color"/> constants
/// calibrated to Apple iOS/macOS Dark Mode Human Interface Guidelines.
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
    public static readonly Color PrimaryTextColor = Color.FromRgb(0xF5, 0xF5, 0xF7);
    public static readonly Color SecondaryTextColor = Color.FromRgb(0x86, 0x86, 0x8B);
    public static readonly Color MutedGreyColor = Color.FromRgb(0x98, 0x98, 0x9D);
    public static readonly Color DarkInkColor = Color.FromRgb(0x0A, 0x0A, 0x0C);

    // ── Pre-Frozen Solid Brushes ─────────────────────────────────────
    public static readonly SolidColorBrush Green = CreateFrozen(GreenColor);
    public static readonly SolidColorBrush Amber = CreateFrozen(AmberColor);
    public static readonly SolidColorBrush Red = CreateFrozen(RedColor);
    public static readonly SolidColorBrush Blue = CreateFrozen(BlueColor);
    public static readonly SolidColorBrush PrimaryText = CreateFrozen(PrimaryTextColor);
    public static readonly SolidColorBrush SecondaryText = CreateFrozen(SecondaryTextColor);
    public static readonly SolidColorBrush MutedGrey = CreateFrozen(MutedGreyColor);
    public static readonly SolidColorBrush DarkInk = CreateFrozen(DarkInkColor);

    // ── Translucent Apple Material Surface Brushes ───────────────────
    public static readonly SolidColorBrush DockInactiveBg = CreateFrozen(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush DockActiveBg = CreateFrozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush DockActiveBorder = CreateFrozen(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush ButtonSubtleBg = CreateFrozen(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush MicMutedBg = CreateFrozen(Color.FromArgb(0x2E, 0xFF, 0x45, 0x3A));

    public static readonly SolidColorBrush PlanPerfActiveBg = CreateFrozen(Color.FromArgb(0x22, 0xFF, 0x9F, 0x0A));
    public static readonly SolidColorBrush PlanBalancedActiveBg = CreateFrozen(Color.FromArgb(0x22, 0x0A, 0x84, 0xFF));
    public static readonly SolidColorBrush PlanSaverActiveBg = CreateFrozen(Color.FromArgb(0x22, 0x30, 0xD1, 0x58));

    public static readonly SolidColorBrush MenuBackground = CreateFrozen(Color.FromRgb(0x14, 0x14, 0x17));
    public static readonly SolidColorBrush MenuBorder = CreateFrozen(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush MenuSeparator = CreateFrozen(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));

    public static SolidColorBrush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
