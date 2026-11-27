using DynamicIsland.Services;

namespace DynamicIsland.Core;

internal readonly record struct PillShape(double Width, double Height, double Radius);

/// <summary>
/// Pure deterministic pill geometry table for all Dynamic Island states, expanded views,
/// idle complications, hardware notch overrides, and tactile hover/press tension.
/// </summary>
internal static class IslandShapeTable
{
    public const double HoverWidthSwell = 10.0;
    public const double HoverHeightSwell = 2.5;
    public const double PressWidthTension = -6.0;
    public const double PressHeightTension = -1.5;

    public static readonly IReadOnlyDictionary<IslandState, PillShape> BaseShapes = new Dictionary<IslandState, PillShape>
    {
        [IslandState.Idle]     = new(148, 30, 15),
        [IslandState.Compact]  = new(228, 36, 18),
        [IslandState.Toast]    = new(336, 58, 29),
        [IslandState.Expanded] = new(484, 176, 34),
        [IslandState.Hidden]   = new(120, 24, 12),
    };

    public static readonly PillShape SettingsExpandedShape       = new(536, 368, 36);
    public static readonly PillShape ClipboardExpandedShape      = new(588, 296, 36);
    public static readonly PillShape NetworkExpandedShape        = new(484, 196, 34);
    public static readonly PillShape BatteryExpandedShape        = new(484, 216, 34);
    public static readonly PillShape SystemHudExpandedShape      = new(484, 232, 34);
    public static readonly PillShape LyricsExpandedShape         = new(484, 216, 34);
    public static readonly PillShape ConverterExpandedShape      = new(484, 232, 36);
    public static readonly PillShape WidgetsGalleryExpandedShape = new(520, 356, 36);
    public static readonly PillShape LauncherExpandedShape       = new(484, 320, 36);
    public static readonly PillShape TermiExpandedShape          = new(484, 232, 36);
    public static readonly PillShape PhoneBridgeExpandedShape    = new(484, 288, 36);
    public static readonly PillShape MechSoundsExpandedShape     = new(484, 220, 34);
    public static readonly PillShape CaptureStudioExpandedShape  = new(484, 232, 36);
    public static readonly PillShape CalendarExpandedShape       = new(484, 256, 36);
    public static readonly PillShape NotesExpandedShape          = new(484, 240, 36);
    public static readonly PillShape AudioExpandedShape          = new(484, 340, 36);
    public static readonly PillShape ShelfExpandedShape          = new(588, 268, 36);
    public static readonly PillShape QuickReplyExpandedShape     = new(484, 156, 36);
    public static readonly PillShape PetExpandedShape            = new(484, 216, 34);
    public static readonly PillShape MirrorExpandedShape         = new(484, 248, 36);
    public static readonly PillShape ActivityInspectorExpandedShape = new(484, 264, 36);

    public static PillShape GetNotificationsExpandedShape(int totalCount, int groupCount, int expandedExtraItems)
    {
        if (groupCount <= 0)
        {
            return new PillShape(484, totalCount > 0 ? 228 : 196, 36);
        }

        int visibleCount = Math.Clamp(groupCount + expandedExtraItems, 1, 4);
        double targetHeight = Math.Clamp(126 + (visibleCount * 82), 224, 412);
        return new PillShape(484, targetHeight, 38);
    }

    public static PillShape GetToastShape(ToastContent? toast)
    {
        if (toast != null && (toast.OnPromptDecision != null || !string.IsNullOrWhiteSpace(toast.PromptActivityId)))
        {
            double w = !string.IsNullOrWhiteSpace(toast.DiffText) ? 416 : 384;
            return new PillShape(w, 58, 29);
        }
        if (toast?.CanReply == true)
        {
            return new PillShape(368, 58, 29);
        }
        return BaseShapes[IslandState.Toast];
    }

    public static PillShape GetIdleShape(
        bool hardwareNotch,
        bool isDetached,
        double notchWidth,
        double notchHeight,
        double notchRadius,
        string? clockStyle,
        bool showNetworkSpeedInIdle,
        bool enableVirtualPet,
        bool showCameraLensIcon)
    {
        if (hardwareNotch && !isDetached)
        {
            return new PillShape(notchWidth, notchHeight, notchRadius);
        }

        string style = SettingsService.NormalizeClockStyle(clockStyle);
        double baseW = style switch
        {
            "Seconds"   => 158,
            "DateBadge" => 184,
            "Analog"    => 160,
            "Neon"      => 144,
            "Flip"      => 154,
            "Matrix"    => 176,
            "JumpHour"  => 164,
            _           => 142,
        };

        double complicationExtra = 0;
        if (showNetworkSpeedInIdle)
        {
            complicationExtra = 44;
        }
        else if (enableVirtualPet)
        {
            complicationExtra = 28;
        }

        if (!showCameraLensIcon)
        {
            complicationExtra -= 12;
        }

        double finalW = Math.Clamp(baseW + complicationExtra, 104, 244);
        return new PillShape(finalW, 30, 15);
    }

    public static PillShape GetExpandedViewShape(
        ExpandedView activeView,
        bool hasMedia,
        bool showLyrics,
        PillShape notificationsShape)
    {
        return activeView switch
        {
            ExpandedView.Calendar       => CalendarExpandedShape,
            ExpandedView.Settings       => SettingsExpandedShape,
            ExpandedView.Clipboard      => ClipboardExpandedShape,
            ExpandedView.Shelf          => ShelfExpandedShape,
            ExpandedView.Notes          => NotesExpandedShape,
            ExpandedView.Audio          => AudioExpandedShape,
            ExpandedView.Network        => NetworkExpandedShape,
            ExpandedView.Battery        => BatteryExpandedShape,
            ExpandedView.SystemHud      => SystemHudExpandedShape,
            ExpandedView.Converter      => ConverterExpandedShape,
            ExpandedView.WidgetsGallery => WidgetsGalleryExpandedShape,
            ExpandedView.Launcher       => LauncherExpandedShape,
            ExpandedView.Termi          => TermiExpandedShape,
            ExpandedView.PhoneBridge    => PhoneBridgeExpandedShape,
            ExpandedView.MechSounds     => MechSoundsExpandedShape,
            ExpandedView.CaptureStudio  => CaptureStudioExpandedShape,
            ExpandedView.QuickReply     => QuickReplyExpandedShape,
            ExpandedView.Pet            => PetExpandedShape,
            ExpandedView.Mirror         => MirrorExpandedShape,
            ExpandedView.ActivityInspector => ActivityInspectorExpandedShape,
            ExpandedView.Notifications  => notificationsShape,
            ExpandedView.Media when !hasMedia => new PillShape(484, 114, 28),
            ExpandedView.Media when showLyrics => LyricsExpandedShape,
            ExpandedView.Media          => new PillShape(484, 152, 34),
            _                           => BaseShapes[IslandState.Expanded]
        };
    }

    public static PillShape ApplyPointerTension(PillShape baseShape, IslandState state, bool isPressed, bool pointerInside)
    {
        if (state is IslandState.Idle or IslandState.Compact)
        {
            if (isPressed)
            {
                return new PillShape(
                    baseShape.Width + PressWidthTension,
                    baseShape.Height + PressHeightTension,
                    (baseShape.Height + PressHeightTension) * 0.5);
            }
            if (pointerInside)
            {
                return new PillShape(
                    baseShape.Width + HoverWidthSwell,
                    baseShape.Height + HoverHeightSwell,
                    (baseShape.Height + HoverHeightSwell) * 0.5);
            }
        }
        return baseShape;
    }
}
