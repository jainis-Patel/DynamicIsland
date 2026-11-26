using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.Core;

public enum ThemeLayer
{
    Underlay,
    Overlay
}

/// <summary>
/// High-performance vector living nature theme renderer for Dynamic Island.
/// Renders living botanical themes (Ivy &amp; Blooms, Meadow)
/// around the island capsule with zero runtime memory allocations, frozen brushes,
/// and smooth 60Hz-240Hz+ frame-rate invariance.
/// </summary>
public sealed partial class IslandNatureThemeControl : FrameworkElement
{
    public static readonly DependencyProperty LayerProperty =
        DependencyProperty.Register(nameof(Layer), typeof(ThemeLayer), typeof(IslandNatureThemeControl),
            new FrameworkPropertyMetadata(ThemeLayer.Underlay, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThemeNameProperty =
        DependencyProperty.Register(nameof(ThemeName), typeof(string), typeof(IslandNatureThemeControl),
            new FrameworkPropertyMetadata("None", FrameworkPropertyMetadataOptions.AffectsRender));

    public ThemeLayer Layer
    {
        get => (ThemeLayer)GetValue(LayerProperty);
        set => SetValue(LayerProperty, value);
    }

    public string ThemeName
    {
        get => (string)GetValue(ThemeNameProperty);
        set => SetValue(ThemeNameProperty, value);
    }

    private double _time;
    private Rect _pillBounds;
    private bool _reducedMotion;

    // Granular nature theme customization parameters
    private double _density = 0.70;
    private double _scale = 0.85;
    private double _dropLength = 60.0;
    private int _butterflyCount = 2;
    private double _bloomDensity = 0.75;
    private bool _pollenEnabled = true;
    private int _pollenCount = 14;

    // Cached vine perimeter points to avoid per-frame allocations
    private double _cachedPillW = -1;
    private double _cachedPillH = -1;
    private double _cachedPillX = -1;
    private double _cachedPillY = -1;
    private double _cachedDensity = -1;
    private double _cachedScale = -1;
    private double _cachedDropLength = -1;
    private double _cachedBloomDensity = -1;
    private List<VinePoint[]>? _cachedVines;
    private List<BranchData>? _cachedBranches;
    private readonly List<FlowerData> _flowersBuffer = new(64);

    public IslandNatureThemeControl()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
    }

    public void Step(
        double t,
        Rect pillBounds,
        bool reducedMotion,
        double density = 0.70,
        double scale = 0.85,
        double dropLength = 60.0,
        int butterflyCount = 2,
        double bloomDensity = 0.75,
        bool pollenEnabled = true,
        int pollenCount = 14)
    {
        _time = t;
        _pillBounds = pillBounds;
        _reducedMotion = reducedMotion;
        _density = Math.Clamp(density, 0.20, 1.50);
        _scale = Math.Clamp(scale, 0.40, 1.40);
        _dropLength = Math.Clamp(dropLength, 20.0, 120.0);
        _butterflyCount = Math.Clamp(butterflyCount, 0, 4);
        _bloomDensity = Math.Clamp(bloomDensity, 0.20, 1.50);
        _pollenEnabled = pollenEnabled;
        _pollenCount = Math.Clamp(pollenCount, 0, 30);
        InvalidateVisual();
    }

    // =========================================================================
    //  Pre-allocated, Frozen Brush & Pen Singletons (Zero GC Pressure)
    // =========================================================================

    private static readonly Pen MainVineDarkPen;
    private static readonly Pen MainVineLightPen;
    private static readonly Pen SecVineDarkPen;
    private static readonly Pen SecVineLightPen;
    private static readonly Pen BranchDarkPen;
    private static readonly Pen BranchLightPen;

    private static readonly Pen StemDarkPen;
    private static readonly Pen StemLightPen;
    private static readonly Pen MeadowStemDarkPen;
    private static readonly Pen MeadowStemLightPen;
    private static readonly Pen VeinPen;
    private static readonly Pen IvyLeafVeinPen;
    private static readonly Pen PetalSepPen;
    private static readonly Pen GloryRaysPen;
    private static readonly Pen ButterflyMonarchPen;
    private static readonly Pen ButterflyBluePen;
    private static readonly Pen ButterflyWhitePen;
    private static readonly Pen ButterflyVeinPen;
    private static readonly Pen ButterflyBodyPen;
    private static readonly Pen StamenPinkPen;
    private static readonly Pen StamenPurplePen;

    private static readonly LinearGradientBrush IvyLeafDarkBrush;
    private static readonly LinearGradientBrush IvyLeafLightBrush;
    private static readonly LinearGradientBrush MeadowLeafBrush;
    private static readonly LinearGradientBrush GrassBrush;

    private static readonly RadialGradientBrush GloryRadialBrush;
    private static readonly RadialGradientBrush DaisyCenterBrush;
    private static readonly SolidColorBrush DaisyFloretBrush;
    private static readonly SolidColorBrush PoppyCenterBrush;
    private static readonly SolidColorBrush PoppyRingBrush;
    private static readonly SolidColorBrush LavFloret1Brush;
    private static readonly SolidColorBrush LavFloret2Brush;
    private static readonly SolidColorBrush JasmineCenterBrush;
    private static readonly SolidColorBrush GoldenPollenBrush;
    private static readonly SolidColorBrush GloryCenterBrush;
    private static readonly SolidColorBrush ButterflyBodyBrush;

    private static readonly LinearGradientBrush MonarchWingBrush;
    private static readonly LinearGradientBrush BlueMorphoWingBrush;
    private static readonly LinearGradientBrush SwallowtailWingBrush;
    private static readonly SolidColorBrush MonarchSpotBrush;
    private static readonly SolidColorBrush BlueMorphoSpotBrush;
    private static readonly SolidColorBrush SwallowtailSpotBrush;

    private static readonly LinearGradientBrush LilacPetalBrush;
    private static readonly LinearGradientBrush RosePetalBrush;
    private static readonly LinearGradientBrush JasminePetal1Brush;
    private static readonly LinearGradientBrush JasminePetal2Brush;
    private static readonly LinearGradientBrush DaisyPetal1Brush;
    private static readonly LinearGradientBrush DaisyPetal2Brush;
    private static readonly LinearGradientBrush PoppyPetal1Brush;
    private static readonly LinearGradientBrush PoppyPetal2Brush;

    private static readonly SolidColorBrush[] GoldenPollenAlphaBrushes;
    private static readonly Pen[] IvyLeafOutlinePens;
    private static readonly StreamGeometry UnitIvyLeafGeometry;
    private static readonly StreamGeometry MorningGloryGeometry;
    private static readonly StreamGeometry UnitSmoothPetalGeometry;
    private static readonly StreamGeometry UnitNotchedPetalGeometry;
    private static readonly StreamGeometry ButterflyForewingGeometry;
    private static readonly StreamGeometry ButterflyHindwingGeometry;
    private static readonly StreamGeometry ButterflyAntennaeGeometry;

    private static readonly double[] ButterflySpeeds = { 11.0, 13.0, 9.0, 12.0 };
    private static readonly double[] ButterflyScales = { 1.0, 0.9, 0.8, 0.85 };
    private static readonly Brush[] ButterflyWingBrushes;
    private static readonly Pen[] ButterflyWingPens;
    private static readonly Brush[] ButterflySpotBrushes;
    private static readonly Point[] ButterflyVeinEnds =
    {
        new(17, -14), new(22, -8), new(20, -1), new(14, 12), new(10, 16)
    };
    private static readonly (double px, double py, double r)[] ButterflySpots =
    {
        (19, -12, 1.3), (22, -7, 1.1), (20, -3, 0.9), (15, 12, 1.0), (11, 16, 1.0)
    };

    static IslandNatureThemeControl()
    {
        T Freeze<T>(T obj) where T : Freezable { obj.Freeze(); return obj; }

        MainVineDarkPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x15))), 2.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });
        MainVineLightPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x6E, 0x9C, 0x3E))), 1.1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });
        SecVineDarkPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x15))), 2.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });
        SecVineLightPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x6E, 0x9C, 0x3E))), 0.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });
        BranchDarkPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x15))), 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });
        BranchLightPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x6E, 0x9C, 0x3E))), 0.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });

        StemDarkPen = MainVineDarkPen;
        StemLightPen = MainVineLightPen;

        MeadowStemDarkPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x2C, 0x62, 0x32))), 2.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        MeadowStemLightPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x74, 0xB8, 0x5C))), 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

        VeinPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(64, 255, 255, 255))), 0.7));
        IvyLeafVeinPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(100, 225, 255, 195))), 0.7));
        PetalSepPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(36, 0, 0, 0))), 0.5));
        GloryRaysPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255))), 1.1));

        ButterflyMonarchPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x15, 0x0B, 0x06))), 1.3) { LineJoin = PenLineJoin.Round });
        ButterflyBluePen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x07, 0x11, 0x26))), 1.3) { LineJoin = PenLineJoin.Round });
        ButterflyWhitePen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x24))), 1.3) { LineJoin = PenLineJoin.Round });
        ButterflyVeinPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(128, 0, 0, 0))), 0.7));
        ButterflyBodyPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x12, 0x0A, 0x06))), 0.7));

        StamenPinkPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xD9, 0x4F, 0x7A))), 0.6));
        StamenPurplePen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x7A, 0x4C, 0xC0))), 0.6));

        IvyLeafDarkBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0x1B, 0x4D, 0x22), Color.FromRgb(0x48, 0x94, 0x38), new Point(0, 0), new Point(1, 1)));
        IvyLeafLightBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0x2E, 0x75, 0x2E), Color.FromRgb(0x82, 0xC4, 0x4A), new Point(0, 0), new Point(1, 1)));
        MeadowLeafBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0x27, 0x60, 0x2F), Color.FromRgb(0x79, 0xB8, 0x5A), new Point(0, 0), new Point(1, 0)));
        GrassBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0x1B, 0x45, 0x26), Color.FromRgb(0x86, 0xCC, 0x5E), new Point(0, 1), new Point(0, 0)));

        var gloryRadial = new RadialGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x33, 0x39, 0x9F));
        gloryRadial.GradientStops.Add(new GradientStop(Color.FromRgb(0xCF, 0xE3, 0xFF), 0.25));
        gloryRadial.GradientStops.Add(new GradientStop(Color.FromRgb(0x5A, 0x72, 0xE6), 0.70));
        GloryRadialBrush = Freeze(gloryRadial);

        var daisyCenter = new RadialGradientBrush(Color.FromRgb(0xFF, 0xE0, 0x66), Color.FromRgb(0xC7, 0x7A, 0x00));
        daisyCenter.GradientOrigin = new Point(0.4, 0.4);
        DaisyCenterBrush = Freeze(daisyCenter);

        DaisyFloretBrush = Freeze(new SolidColorBrush(Color.FromArgb(128, 90, 50, 0)));
        PoppyCenterBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1C, 0x10, 0x14)));
        PoppyRingBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x1A, 0x22)));
        LavFloret1Brush = Freeze(new SolidColorBrush(Color.FromRgb(0x7D, 0x5B, 0xC8)));
        LavFloret2Brush = Freeze(new SolidColorBrush(Color.FromRgb(0xA5, 0x8B, 0xE6)));
        JasmineCenterBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xC5, 0x47)));
        GoldenPollenBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF4, 0xD3, 0x5E)));
        GloryCenterBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xB0)));
        ButterflyBodyBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x12, 0x0A, 0x06)));

        MonarchWingBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0xC9, 0x4A, 0x08), Color.FromRgb(0xF2, 0x8A, 0x1E), new Point(0, 0), new Point(1, 0)));
        BlueMorphoWingBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0x1D, 0x4F, 0xD0), Color.FromRgb(0x58, 0xB4, 0xFF), new Point(0, 0), new Point(1, 0)));
        SwallowtailWingBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0xDF, 0xE5, 0xCF), Color.FromRgb(0xFF, 0xFF, 0xFF), new Point(0, 0), new Point(1, 0)));
        MonarchSpotBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xD0)));
        BlueMorphoSpotBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xCF, 0xEA, 0xFF)));
        SwallowtailSpotBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)));

        LilacPetalBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0xC9, 0xA2, 0xF0), Color.FromRgb(0xF4, 0xEA, 0xFF), new Point(0, 0), new Point(1, 0)));
        RosePetalBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0xF5, 0x8F, 0xB4), Color.FromRgb(0xFF, 0xE3, 0xEC), new Point(0, 0), new Point(1, 0)));
        JasminePetal1Brush = Freeze(new LinearGradientBrush(Color.FromRgb(0xEA, 0xDF, 0xBF), Color.FromRgb(0xFF, 0xFF, 0xFF), new Point(0, 0), new Point(1, 0)));
        JasminePetal2Brush = Freeze(new LinearGradientBrush(Color.FromRgb(0xF6, 0xF0, 0xDE), Color.FromRgb(0xFF, 0xFF, 0xFF), new Point(0, 0), new Point(1, 0)));
        DaisyPetal1Brush = Freeze(new LinearGradientBrush(Color.FromRgb(0xCF, 0xCD, 0xB8), Color.FromRgb(0xFF, 0xFF, 0xFF), new Point(0, 0), new Point(1, 0)));
        DaisyPetal2Brush = Freeze(new LinearGradientBrush(Color.FromRgb(0xE4, 0xE2, 0xD0), Color.FromRgb(0xFF, 0xFF, 0xFF), new Point(0, 0), new Point(1, 0)));
        PoppyPetal1Brush = Freeze(new LinearGradientBrush(Color.FromRgb(0x5C, 0x08, 0x08), Color.FromRgb(0xF0, 0x40, 0x2A), new Point(0, 0), new Point(1, 0)));
        PoppyPetal2Brush = Freeze(new LinearGradientBrush(Color.FromRgb(0x7A, 0x0E, 0x0E), Color.FromRgb(0xE0, 0x30, 0x1C), new Point(0, 0), new Point(1, 0)));

        ButterflyWingBrushes = new Brush[] { MonarchWingBrush, BlueMorphoWingBrush, SwallowtailWingBrush, BlueMorphoWingBrush };
        ButterflyWingPens = new Pen[] { ButterflyMonarchPen, ButterflyBluePen, ButterflyWhitePen, ButterflyBluePen };
        ButterflySpotBrushes = new Brush[] { MonarchSpotBrush, BlueMorphoSpotBrush, SwallowtailSpotBrush, BlueMorphoSpotBrush };

        GoldenPollenAlphaBrushes = new SolidColorBrush[16];
        for (int i = 0; i < GoldenPollenAlphaBrushes.Length; i++)
        {
            double alpha = (i + 1) / 16.0 * 0.55;
            GoldenPollenAlphaBrushes[i] = Freeze(new SolidColorBrush(Color.FromArgb((byte)Math.Clamp((int)Math.Round(alpha * 255.0), 1, 255), 0xF4, 0xD3, 0x5E)));
        }

        var ivyOutlineColor = Freeze(new SolidColorBrush(Color.FromArgb(100, 225, 255, 195)));
        IvyLeafOutlinePens = new Pen[24];
        for (int i = 0; i < IvyLeafOutlinePens.Length; i++)
        {
            IvyLeafOutlinePens[i] = Freeze(new Pen(ivyOutlineColor, 0.7 / (i + 1)));
        }

        // Unit ivy leaf (length = 1.0, half-width w = 0.55)
        var unitLeaf = new StreamGeometry();
        using (var ctx = unitLeaf.Open())
        {
            const double w = 0.55;
            ctx.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
            ctx.QuadraticBezierTo(new Point(-0.08, -w * 0.6), new Point(0.35, -w), isStroked: true, isSmoothJoin: true);
            ctx.QuadraticBezierTo(new Point(0.45, -w * 0.35), new Point(0.62, -w * 0.28), isStroked: true, isSmoothJoin: true);
            ctx.QuadraticBezierTo(new Point(0.82, -w * 0.12), new Point(1.0, 0), isStroked: true, isSmoothJoin: true);
            ctx.QuadraticBezierTo(new Point(0.82, w * 0.12), new Point(0.62, w * 0.28), isStroked: true, isSmoothJoin: true);
            ctx.QuadraticBezierTo(new Point(0.45, w * 0.35), new Point(0.35, w), isStroked: true, isSmoothJoin: true);
            ctx.QuadraticBezierTo(new Point(-0.08, w * 0.6), new Point(0, 0), isStroked: true, isSmoothJoin: true);
        }
        UnitIvyLeafGeometry = Freeze(unitLeaf);

        var glory = new StreamGeometry();
        using (var ctx = glory.Open())
        {
            for (int i = 0; i < 5; i++)
            {
                double a0 = i * 1.2566;
                double am = a0 + 0.6283;
                double a1 = a0 + 1.2566;
                Point p0 = new(Math.Cos(a0) * 9.0, Math.Sin(a0) * 9.0);
                Point pm = new(Math.Cos(am) * 13.5, Math.Sin(am) * 13.5);
                Point p1 = new(Math.Cos(a1) * 9.0, Math.Sin(a1) * 9.0);
                if (i == 0) ctx.BeginFigure(p0, isFilled: true, isClosed: true);
                ctx.QuadraticBezierTo(pm, p1, isStroked: true, isSmoothJoin: true);
            }
        }
        MorningGloryGeometry = Freeze(glory);

        var smoothPetal = new StreamGeometry();
        using (var ctx = smoothPetal.Open())
        {
            ctx.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
            ctx.BezierTo(new Point(0.3, -1.0), new Point(0.85, -0.7), new Point(1.0, 0), isStroked: true, isSmoothJoin: true);
            ctx.BezierTo(new Point(0.85, 0.7), new Point(0.3, 1.0), new Point(0, 0), isStroked: true, isSmoothJoin: true);
        }
        UnitSmoothPetalGeometry = Freeze(smoothPetal);

        var notchedPetal = new StreamGeometry();
        using (var ctx = notchedPetal.Open())
        {
            ctx.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
            ctx.BezierTo(new Point(0.3, -1.0), new Point(0.95, -0.95), new Point(1.0, -0.2), isStroked: true, isSmoothJoin: true);
            ctx.LineTo(new Point(0.88, 0), isStroked: true, isSmoothJoin: true);
            ctx.LineTo(new Point(1.0, 0.2), isStroked: true, isSmoothJoin: true);
            ctx.BezierTo(new Point(0.95, 0.95), new Point(0.3, 1.0), new Point(0, 0), isStroked: true, isSmoothJoin: true);
        }
        UnitNotchedPetalGeometry = Freeze(notchedPetal);

        var forewing = new StreamGeometry();
        using (var ctx = forewing.Open())
        {
            ctx.BeginFigure(new Point(0, -1), isFilled: true, isClosed: true);
            ctx.BezierTo(new Point(3, -15), new Point(18, -19), new Point(23, -10), isStroked: true, isSmoothJoin: true);
            ctx.BezierTo(new Point(25, -3), new Point(14, 2), new Point(0, 3), isStroked: true, isSmoothJoin: true);
        }
        ButterflyForewingGeometry = Freeze(forewing);

        var hindwing = new StreamGeometry();
        using (var ctx = hindwing.Open())
        {
            ctx.BeginFigure(new Point(0, 2), isFilled: true, isClosed: true);
            ctx.BezierTo(new Point(13, 1), new Point(22, 10), new Point(15, 18), isStroked: true, isSmoothJoin: true);
            ctx.BezierTo(new Point(9, 21), new Point(2, 13), new Point(0, 6), isStroked: true, isSmoothJoin: true);
        }
        ButterflyHindwingGeometry = Freeze(hindwing);

        var antennae = new StreamGeometry();
        using (var ctx = antennae.Open())
        {
            ctx.BeginFigure(new Point(-0.6, -8), isFilled: false, isClosed: false);
            ctx.QuadraticBezierTo(new Point(-4, -15), new Point(-7, -16), isStroked: true, isSmoothJoin: true);
            ctx.BeginFigure(new Point(0.6, -8), isFilled: false, isClosed: false);
            ctx.QuadraticBezierTo(new Point(4, -15), new Point(7, -16), isStroked: true, isSmoothJoin: true);
        }
        ButterflyAntennaeGeometry = Freeze(antennae);
    }

    // =========================================================================
    //  Deterministic PRNG & Bezier Math
    // =========================================================================

    private sealed class Mulberry32
    {
        private uint _state;
        public Mulberry32(uint seed) => _state = seed;
        public double NextDouble()
        {
            _state += 0x6D2B79F5u;
            uint t = _state ^ (_state >> 15);
            t = unchecked(t * (1u | _state));
            t ^= t + unchecked((t ^ (t >> 7)) * 61u);
            return ((t ^ (t >> 14)) >> 0) / 4294967296.0;
        }
    }

    private static double Clamp(double x, double a = 0.0, double b = 1.0) => Math.Min(b, Math.Max(a, x));
    private static double EaseOutCubic(double x) { double c = 1.0 - Clamp(x); return 1.0 - c * c * c; }

    private static Point Qp(double x0, double y0, double cx, double cy, double x1, double y1, double u)
    {
        double m = 1.0 - u;
        return new Point(
            m * m * x0 + 2.0 * m * u * cx + u * u * x1,
            m * m * y0 + 2.0 * m * u * cy + u * u * y1);
    }

    // =========================================================================
    //  Render Dispatcher
    // =========================================================================

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        string theme = ThemeName;
        if (string.Equals(theme, "None", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(theme))
        {
            return;
        }

        double t = _reducedMotion ? 9.0 : _time;
        Rect p = _pillBounds;
        if (p.Width <= 0 || p.Height <= 0)
        {
            p = new Rect(266, 0, 148, 30);
        }

        switch (theme)
        {
            case "Meadow":
                if (Layer == ThemeLayer.Underlay) RenderMeadow(dc, p, t);
                else RenderMeadowButterfly(dc, p, t);
                break;

            case "Ivy & Blooms":
            default:
                if (Layer == ThemeLayer.Underlay) RenderIvyUnderlay(dc, p, t);
                else RenderIvyOverlay(dc, p, t);
                break;
        }
    }

    // =========================================================================
    //  THEME 1: Ivy & Blooms
    // =========================================================================

    private readonly record struct VinePoint(double X, double Y, double Tx, double Ty, double Nx, double Ny);
    private readonly record struct BranchData(int ParentIdx, int AttachIdx, VinePoint[] Points);
    private readonly record struct FlowerData(double X, double Y, double S, double R, int Ty);

    private void EnsureIvyGeometry(Rect p)
    {
        if (_cachedVines != null &&
            Math.Abs(p.Width - _cachedPillW) < 0.5 &&
            Math.Abs(p.Height - _cachedPillH) < 0.5 &&
            Math.Abs(p.X - _cachedPillX) < 0.5 &&
            Math.Abs(p.Y - _cachedPillY) < 0.5 &&
            Math.Abs(_density - _cachedDensity) < 0.01 &&
            Math.Abs(_scale - _cachedScale) < 0.01 &&
            Math.Abs(_dropLength - _cachedDropLength) < 0.5 &&
            Math.Abs(_bloomDensity - _cachedBloomDensity) < 0.01)
        {
            return;
        }

        _cachedPillW = p.Width;
        _cachedPillH = p.Height;
        _cachedPillX = p.X;
        _cachedPillY = p.Y;
        _cachedDensity = _density;
        _cachedScale = _scale;
        _cachedDropLength = _dropLength;
        _cachedBloomDensity = _bloomDensity;

        double cenX = p.X + p.Width / 2.0;
        double cenY = p.Y + p.Height / 2.0;
        double rr = p.Height / 2.0;
        double cy = p.Y + rr;
        double cxL = p.X + rr;
        double cxR = p.X + p.Width - rr;
        double topY = Math.Max(0.0, p.Y);
        double botY = p.Y + p.Height;

        VinePoint[] Fin(List<Point> pts)
        {
            var res = new VinePoint[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[Math.Max(0, i - 1)];
                var b = pts[Math.Min(pts.Count - 1, i + 1)];
                double tx = b.X - a.X, ty = b.Y - a.Y;
                double m = Math.Sqrt(tx * tx + ty * ty);
                if (m < 0.0001) m = 1.0;
                tx /= m; ty /= m;
                double nx = -ty, ny = tx;
                if (nx * (pts[i].X - cenX) + ny * (pts[i].Y - cenY) < 0)
                {
                    nx = -nx; ny = -ny;
                }
                res[i] = new VinePoint(pts[i].X, pts[i].Y, tx, ty, nx, ny);
            }
            return res;
        }

        var vineList = new List<VinePoint[]>();

        // ---------------------------------------------------------------------
        // Vine 0: Left Perimeter Runner & Cascading Wing Tendril
        // ---------------------------------------------------------------------
        {
            var pts = new List<Point>();
            double o = 2.5 * _scale;
            double R = rr + o;

            // 1. Cling along top rim towards left cap (strictly kept below top boundary)
            double topYClamped = Math.Max(1.8, topY + 1.8);
            double startX = p.X + p.Width * 0.42;
            for (double x = startX; x >= cxL; x -= 3.5)
            {
                pts.Add(new Point(x, topYClamped));
            }

            // 2. Round the left semicircle cap
            double angleStep = 4.0 / Math.Max(1.0, R);
            for (double a = -Math.PI / 2.0; a <= Math.PI / 2.0; a += angleStep)
            {
                double px = cxL - Math.Cos(a) * R;
                double py = cy + Math.Sin(a) * R;
                pts.Add(new Point(px, Math.Max(1.5, py)));
            }

            // 3. Hug bottom-left rim eastward
            double bEnd = p.X + p.Width * 0.22;
            for (double x = cxL; x <= bEnd; x += 3.5)
            {
                pts.Add(new Point(x, botY + o));
            }

            // 4. Cascade gracefully downward and outward to the left
            double cascadeDrop = _dropLength * 0.95;
            for (double y = botY + o; y <= botY + o + cascadeDrop; y += 3.2)
            {
                double frac = (y - (botY + o)) / Math.Max(1.0, cascadeDrop);
                double outCurve = -Math.Sin(frac * Math.PI * 0.85) * (34.0 * _scale);
                double sway = Math.Sin(frac * Math.PI * 2.2 + 0.5) * 8.0;
                pts.Add(new Point(bEnd + outCurve + sway, y));
            }

            // 5. Delicate terminal spiral curl
            if (pts.Count > 0)
            {
                var last = pts[^1];
                double tx = last.X, ty = last.Y;
                double curAng = Math.PI * 0.55;
                for (int k = 0; k < 11; k++)
                {
                    curAng += 0.38;
                    tx += Math.Cos(curAng) * (1.8 * _scale);
                    ty += Math.Sin(curAng) * (1.8 * _scale);
                    pts.Add(new Point(tx, ty));
                }
            }

            // Apply gentle natural wobble
            var wobbled = new List<Point>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                double wy = pts[i].Y + Math.Cos(i * 0.35) * 1.5;
                wobbled.Add(new Point(pts[i].X + Math.Sin(i * 0.4) * 1.8, Math.Max(1.5, wy)));
            }
            vineList.Add(Fin(wobbled));
        }

        // ---------------------------------------------------------------------
        // Vine 1: Right Perimeter Runner & Cascading Wing Tendril
        // ---------------------------------------------------------------------
        {
            var pts = new List<Point>();
            double o = 2.5 * _scale;
            double R = rr + o;

            // 1. Cling along top rim towards right cap
            double topYClamped = Math.Max(1.8, topY + 1.8);
            double startX = p.X + p.Width * 0.58;
            for (double x = startX; x <= cxR; x += 3.5)
            {
                pts.Add(new Point(x, topYClamped));
            }

            // 2. Round the right semicircle cap
            double angleStep = 4.0 / Math.Max(1.0, R);
            for (double a = -Math.PI / 2.0; a <= Math.PI / 2.0; a += angleStep)
            {
                double px = cxR + Math.Cos(a) * R;
                double py = cy + Math.Sin(a) * R;
                pts.Add(new Point(px, Math.Max(1.5, py)));
            }

            // 3. Hug bottom-right rim westward
            double bEnd = p.X + p.Width * 0.78;
            for (double x = cxR; x >= bEnd; x -= 3.5)
            {
                pts.Add(new Point(x, botY + o));
            }

            // 4. Cascade gracefully downward and outward to the right
            double cascadeDrop = _dropLength * 0.95;
            for (double y = botY + o; y <= botY + o + cascadeDrop; y += 3.2)
            {
                double frac = (y - (botY + o)) / Math.Max(1.0, cascadeDrop);
                double outCurve = Math.Sin(frac * Math.PI * 0.85) * (34.0 * _scale);
                double sway = Math.Sin(frac * Math.PI * 2.2 + 1.2) * 8.0;
                pts.Add(new Point(bEnd + outCurve + sway, y));
            }

            // 5. Terminal spiral curl
            if (pts.Count > 0)
            {
                var last = pts[^1];
                double tx = last.X, ty = last.Y;
                double curAng = Math.PI * 0.45;
                for (int k = 0; k < 11; k++)
                {
                    curAng -= 0.38;
                    tx += Math.Cos(curAng) * (1.8 * _scale);
                    ty += Math.Sin(curAng) * (1.8 * _scale);
                    pts.Add(new Point(tx, ty));
                }
            }

            var wobbled = new List<Point>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                double wy = pts[i].Y + Math.Cos(i * 0.33) * 1.5;
                wobbled.Add(new Point(pts[i].X + Math.Sin(i * 0.38) * 1.8, Math.Max(1.5, wy)));
            }
            vineList.Add(Fin(wobbled));
        }

        // ---------------------------------------------------------------------
        // Vine 2a & 2b: Under-Pill Trellis + Separate Left-Center Cascade
        // Split so the trellis does not draw a diagonal chord back to 32% width.
        // ---------------------------------------------------------------------
        {
            double o = 3.5 * _scale;

            // 2a. Runs completely across the bottom edge of the pill from left to right
            var trellisPts = new List<Point>();
            for (double x = cxL - 6.0; x <= cxR + 6.0; x += 3.5)
            {
                double wave = Math.Sin(x * 0.08) * 2.0;
                trellisPts.Add(new Point(x, botY + o + wave));
            }
            var wobbledTrellis = new List<Point>(trellisPts.Count);
            for (int i = 0; i < trellisPts.Count; i++)
            {
                wobbledTrellis.Add(new Point(trellisPts[i].X + Math.Sin(i * 0.28) * 1.4, trellisPts[i].Y));
            }
            vineList.Add(Fin(wobbledTrellis));

            // 2b. Cascades downward from 32% width
            var cascadePts = new List<Point>();
            double detachX = p.X + p.Width * 0.32;
            double cascadeDrop = _dropLength * 1.15;
            for (double y = botY + o; y <= botY + o + cascadeDrop; y += 3.4)
            {
                double frac = (y - (botY + o)) / Math.Max(1.0, cascadeDrop);
                double wave = Math.Sin(frac * Math.PI * 2.0 + 0.8) * (14.0 * _scale);
                cascadePts.Add(new Point(detachX + wave, y));
            }

            // Spiral curl at cascade tip
            if (cascadePts.Count > 0)
            {
                var last = cascadePts[^1];
                double tx = last.X, ty = last.Y;
                double curAng = Math.PI * 0.5;
                for (int k = 0; k < 10; k++)
                {
                    curAng += 0.40;
                    tx += Math.Cos(curAng) * (1.6 * _scale);
                    ty += Math.Sin(curAng) * (1.6 * _scale);
                    cascadePts.Add(new Point(tx, ty));
                }
            }

            var wobbledCascade = new List<Point>(cascadePts.Count);
            for (int i = 0; i < cascadePts.Count; i++)
            {
                wobbledCascade.Add(new Point(cascadePts[i].X + Math.Sin(i * 0.28) * 1.4, cascadePts[i].Y));
            }
            vineList.Add(Fin(wobbledCascade));
        }

        // ---------------------------------------------------------------------
        // Vine 3: Right-Center Cascade (if density > 0.45)
        // ---------------------------------------------------------------------
        if (_density > 0.45)
        {
            var pts = new List<Point>();
            double o = 4.0 * _scale;
            double detachX = p.X + p.Width * 0.68;
            double cascadeDrop = _dropLength * 1.08;

            for (double y = botY + o; y <= botY + o + cascadeDrop; y += 3.4)
            {
                double frac = (y - (botY + o)) / Math.Max(1.0, cascadeDrop);
                double wave = Math.Sin(frac * Math.PI * 2.1 + 2.0) * (16.0 * _scale);
                pts.Add(new Point(detachX + wave, y));
            }

            if (pts.Count > 0)
            {
                var last = pts[^1];
                double tx = last.X, ty = last.Y;
                double curAng = Math.PI * 0.5;
                for (int k = 0; k < 10; k++)
                {
                    curAng -= 0.40;
                    tx += Math.Cos(curAng) * (1.6 * _scale);
                    ty += Math.Sin(curAng) * (1.6 * _scale);
                    pts.Add(new Point(tx, ty));
                }
            }

            var wobbled = new List<Point>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                wobbled.Add(new Point(pts[i].X + Math.Sin(i * 0.32) * 1.4, pts[i].Y));
            }
            vineList.Add(Fin(wobbled));
        }

        // ---------------------------------------------------------------------
        // Vine 4: Center Droplet Tendril (if density > 0.75)
        // ---------------------------------------------------------------------
        if (_density > 0.75)
        {
            var pts = new List<Point>();
            double detachX = p.X + p.Width * 0.50;
            double cascadeDrop = _dropLength * 0.68;

            for (double y = botY + 2.0; y <= botY + 2.0 + cascadeDrop; y += 3.2)
            {
                double frac = (y - (botY + 2.0)) / Math.Max(1.0, cascadeDrop);
                double wave = Math.Sin(frac * Math.PI * 2.4) * (8.0 * _scale);
                pts.Add(new Point(detachX + wave, y));
            }

            if (pts.Count > 0)
            {
                var last = pts[^1];
                double tx = last.X, ty = last.Y;
                double curAng = Math.PI * 0.5;
                for (int k = 0; k < 9; k++)
                {
                    curAng += 0.44;
                    tx += Math.Cos(curAng) * (1.4 * _scale);
                    ty += Math.Sin(curAng) * (1.4 * _scale);
                    pts.Add(new Point(tx, ty));
                }
            }

            vineList.Add(Fin(pts));
        }

        _cachedVines = vineList;

        // ---------------------------------------------------------------------
        // Secondary Branches (Lateral Sprouts with Terminal Tendril Curls)
        // ---------------------------------------------------------------------
        _cachedBranches = new List<BranchData>();
        int branchSpacing = Math.Max(11, (int)(20.0 / Math.Max(0.3, _density)));

        for (int j = 0; j < _cachedVines.Count; j++)
        {
            var m = _cachedVines[j];
            var rng = new Mulberry32((uint)(100 + j));
            for (int i = 12 + j * 3; i < m.Length - 14; i += branchSpacing + (int)(rng.NextDouble() * 7))
            {
                var pt = m[i];
                // Outward and downward branching vector
                double dirX = pt.Nx * (rng.NextDouble() < 0.25 ? -0.3 : 0.8) + (rng.NextDouble() - 0.5) * 0.4;
                double dirY = Math.Max(0.3, Math.Abs(pt.Ny) + 0.35 + rng.NextDouble() * 0.45);
                double ang = Math.Atan2(dirY, dirX);

                double curX = pt.X;
                double curY = Math.Max(2.0, pt.Y);
                var branchPts = new List<Point>();
                int steps = Math.Max(8, (int)((12 + rng.NextDouble() * 16) * Math.Min(1.2, _scale * 1.1)));
                double cu = (rng.NextDouble() - 0.5) * 0.08;

                for (int k = 0; k < steps; k++)
                {
                    ang += cu + (rng.NextDouble() - 0.5) * 0.22;
                    curX += Math.Cos(ang) * 3.0 * _scale;
                    curY += Math.Sin(ang) * 3.0 * _scale;
                    curY = Math.Max(2.0, curY);
                    branchPts.Add(new Point(curX, curY));
                }

                double sg = rng.NextDouble() < 0.5 ? 1.0 : -1.0;
                for (int k = 0; k < 10; k++)
                {
                    ang += 0.36 * sg;
                    curX += Math.Cos(ang) * 1.4 * _scale;
                    curY += Math.Sin(ang) * 1.4 * _scale;
                    curY = Math.Max(2.0, curY);
                    branchPts.Add(new Point(curX, curY));
                }

                _cachedBranches.Add(new BranchData(j, i, Fin(branchPts)));
            }
        }
    }

}
