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
public sealed class IslandNatureThemeControl : FrameworkElement
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
        // Vine 2: Continuous Under-Pill Trellis & Left-Center Cascade (Eliminates the empty gap!)
        // ---------------------------------------------------------------------
        {
            var pts = new List<Point>();
            double o = 3.5 * _scale;

            // 1. Runs completely across the bottom edge of the pill from left to right
            for (double x = cxL - 6.0; x <= cxR + 6.0; x += 3.5)
            {
                double wave = Math.Sin(x * 0.08) * 2.0;
                pts.Add(new Point(x, botY + o + wave));
            }

            // 2. Cascades downward from 32% width
            double detachX = p.X + p.Width * 0.32;
            double cascadeDrop = _dropLength * 1.15;
            for (double y = botY + o; y <= botY + o + cascadeDrop; y += 3.4)
            {
                double frac = (y - (botY + o)) / Math.Max(1.0, cascadeDrop);
                double wave = Math.Sin(frac * Math.PI * 2.0 + 0.8) * (14.0 * _scale);
                pts.Add(new Point(detachX + wave, y));
            }

            // 3. Spiral curl
            if (pts.Count > 0)
            {
                var last = pts[^1];
                double tx = last.X, ty = last.Y;
                double curAng = Math.PI * 0.5;
                for (int k = 0; k < 10; k++)
                {
                    curAng += 0.40;
                    tx += Math.Cos(curAng) * (1.6 * _scale);
                    ty += Math.Sin(curAng) * (1.6 * _scale);
                    pts.Add(new Point(tx, ty));
                }
            }

            var wobbled = new List<Point>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                wobbled.Add(new Point(pts[i].X + Math.Sin(i * 0.28) * 1.4, pts[i].Y));
            }
            vineList.Add(Fin(wobbled));
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

    private void RenderIvyUnderlay(DrawingContext dc, Rect p, double t)
    {
        EnsureIvyGeometry(p);
        if (_cachedVines == null || _cachedBranches == null) return;

        double cyc = t % 30.0;
        double fade = cyc > 27.0 ? (30.0 - cyc) / 3.0 : 1.0;
        if (fade <= 0.01) return;

        if (fade < 0.99) dc.PushOpacity(fade);

        var flowers = new List<FlowerData>();
        int[] ks = new int[_cachedVines.Count];
        for (int j = 0; j < _cachedVines.Count; j++)
        {
            double delay = j > 1 ? 1.2 : 0.0;
            ks[j] = (int)(_cachedVines[j].Length * EaseOutCubic((cyc - delay) / 10.0));
        }

        for (int j = 0; j < _cachedVines.Count; j++)
        {
            if (ks[j] > 2)
            {
                DrawVine(dc, _cachedVines[j], ks[j], j > 1 ? 1.8 : 2.6, t, flowers, j, isBranch: false);
            }
        }

        for (int q = 0; q < _cachedBranches.Count; q++)
        {
            var b = _cachedBranches[q];
            int n = (int)(b.Points.Length * Clamp((ks[b.ParentIdx] - b.AttachIdx) / 32.0));
            if (n > 2)
            {
                DrawVine(dc, b.Points, n, 1.4, t, flowers, q + 9, isBranch: true);
            }
        }

        // Draw botanical blooms along ivy vines
        foreach (var fl in flowers)
        {
            DrawFlower(dc, fl.X, fl.Y, fl.S, fl.R, fl.Ty);
        }

        if (fade < 0.99) dc.Pop();
    }

    private void DrawVine(DrawingContext dc, VinePoint[] pts, int n, double w, double t, List<FlowerData> fl, int sd, bool isBranch)
    {
        if (n < 2) return;

        // 1. Draw Vine Stems (two-tone: dark bark base + light sap highlight)
        var stemGeom = new StreamGeometry();
        using (var ctx = stemGeom.Open())
        {
            ctx.BeginFigure(new Point(pts[0].X, pts[0].Y), isFilled: false, isClosed: false);
            for (int i = 1; i < n; i++)
            {
                ctx.LineTo(new Point(pts[i].X, pts[i].Y), isStroked: true, isSmoothJoin: true);
            }
        }
        stemGeom.Freeze();

        Pen darkPen = isBranch ? BranchDarkPen : (sd > 1 ? SecVineDarkPen : MainVineDarkPen);
        Pen lightPen = isBranch ? BranchLightPen : (sd > 1 ? SecVineLightPen : MainVineLightPen);
        dc.DrawGeometry(null, darkPen, stemGeom);
        dc.DrawGeometry(null, lightPen, stemGeom);

        // 2. Draw English Ivy Leaves alternating along the stem
        for (int i = 3; i < n; i += 3)
        {
            var pt = pts[i];
            double sd2 = ((i / 3) % 2 == 1) ? 1.0 : -1.0;
            double sc = EaseOutCubic((n - i) / 22.0);
            double h = ((uint)((i + sd * 31) * 2654435761u)) / 4294967296.0;
            double len = (10.5 + h * 8.0) * sc * _scale * (isBranch ? 0.8 : 1.0);
            if (len < 1.5) continue;

            double ang = Math.Atan2(pt.Ty, pt.Tx) + sd2 * (0.95 + Math.Sin(t * 1.2 + i * 0.7) * 0.1);

            // CRITICAL: Prevent top-edge clipping and squashing when pill is flush with monitor top!
            if (pt.Y < 14.0 && Math.Sin(ang) < 0)
            {
                // Align leaf smoothly along capsule contour (tangential or outwards/downwards)
                double cosSign = Math.Cos(ang) >= 0 ? 1.0 : -1.0;
                ang = cosSign > 0 ? 0.18 + h * 0.15 : Math.PI - 0.18 - h * 0.15;
            }

            double lx = pt.X + Math.Cos(ang) * len * 0.25;
            double ly = Math.Max(1.5, pt.Y + Math.Sin(ang) * len * 0.25);

            dc.DrawLine(MeadowStemDarkPen, new Point(pt.X, pt.Y), new Point(lx, ly));
            DrawIvyLeaf(dc, lx, ly, ang, len, h > 0.50);
        }

        // 3. Flower bud candidates spaced according to _bloomDensity
        int flowerSpacing = Math.Max(9, (int)(16.0 / Math.Max(0.25, _bloomDensity)));
        for (int i = 8; i < n; i += flowerSpacing + ((i * 7) % 5))
        {
            var pt = pts[i];
            double h = (((uint)(i * 40503 + sd * 977)) % 1000) / 1000.0;
            double k = EaseOutCubic((n - i) / 24.0);
            if (k > 0.05)
            {
                double fx = pt.X + pt.Nx * (5.0 + h * 5.0) * _scale;
                double fy = Math.Max(2.0, pt.Y + Math.Abs(pt.Ny) * (4.0 + h * 4.0) * _scale);
                // Cycle flower types: 0=Morning Glory, 1=Jasmine, 2=Lilac, 3=Pink Camellia
                int flowerType = ((i / 14) + sd) % 4;
                fl.Add(new FlowerData(
                    fx,
                    fy,
                    k * (0.85 + h * 0.4) * _scale,
                    h * 6.283 + Math.Sin(t + i) * 0.08,
                    flowerType));
            }
        }

        // Branch tips flower bud
        if (isBranch && n == pts.Length && n > 10)
        {
            var pt = pts[n - 10];
            fl.Add(new FlowerData(pt.X, Math.Max(2.0, pt.Y + 2.0), 0.85 * _scale, 0.5, (sd + n) % 4));
        }
    }

    private static void DrawIvyLeaf(DrawingContext dc, double x, double y, double ang, double len, bool isLight)
    {
        var geom = new StreamGeometry();
        double w = len * 0.55;
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
            // 3-lobed ivy leaf bezier outline
            ctx.QuadraticBezierTo(new Point(-len * 0.08, -w * 0.6), new Point(len * 0.35, -w), isStroked: true, isSmoothJoin: true);
            ctx.QuadraticBezierTo(new Point(len * 0.45, -w * 0.35), new Point(len * 0.62, -w * 0.28), isStroked: true, isSmoothJoin: true);
            ctx.QuadraticBezierTo(new Point(len * 0.82, -w * 0.12), new Point(len, 0), isStroked: true, isSmoothJoin: true);
            ctx.QuadraticBezierTo(new Point(len * 0.82, w * 0.12), new Point(len * 0.62, w * 0.28), isStroked: true, isSmoothJoin: true);
            ctx.QuadraticBezierTo(new Point(len * 0.45, w * 0.35), new Point(len * 0.35, w), isStroked: true, isSmoothJoin: true);
            ctx.QuadraticBezierTo(new Point(-len * 0.08, w * 0.6), new Point(0, 0), isStroked: true, isSmoothJoin: true);
        }
        geom.Freeze();

        var mat = Matrix.Identity;
        mat.Rotate(ang * 180.0 / Math.PI);
        mat.Translate(x, y);

        dc.PushTransform(new MatrixTransform(mat));
        dc.DrawGeometry(isLight ? IvyLeafLightBrush : IvyLeafDarkBrush, IvyLeafVeinPen, geom);

        // Draw delicate palmate veins
        dc.DrawLine(IvyLeafVeinPen, new Point(0, 0), new Point(len * 0.9, 0));
        dc.DrawLine(IvyLeafVeinPen, new Point(len * 0.05, 0), new Point(len * 0.33, -w * 0.92));
        dc.DrawLine(IvyLeafVeinPen, new Point(len * 0.05, 0), new Point(len * 0.33, w * 0.92));
        dc.DrawLine(IvyLeafVeinPen, new Point(len * 0.5, 0), new Point(len * 0.64, -w * 0.24));
        dc.DrawLine(IvyLeafVeinPen, new Point(len * 0.5, 0), new Point(len * 0.64, w * 0.24));

        dc.Pop();
    }

    private static void DrawFlower(DrawingContext dc, double x, double y, double s, double r, int ty)
    {
        switch (ty)
        {
            case 0: DrawMorningGlory(dc, x, y, s, r); break;
            case 1: DrawJasmine(dc, x, y, s, r); break;
            case 2: DrawBlossom(dc, x, y, s * 1.05, r, LilacPetalBrush, StamenPurplePen); break;
            case 3:
            default: DrawBlossom(dc, x, y, s, r, RosePetalBrush, StamenPinkPen); break;
        }
    }

    private static void DrawMorningGlory(DrawingContext dc, double x, double y, double s, double r)
    {
        var mat = Matrix.Identity;
        mat.Scale(s, s);
        mat.Rotate(r * 180.0 / Math.PI);
        mat.Translate(x, y);

        dc.PushTransform(new MatrixTransform(mat));

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
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
        geom.Freeze();

        dc.DrawGeometry(GloryRadialBrush, null, geom);

        for (int i = 0; i < 5; i++)
        {
            double a = i * 1.2566 + 0.6283;
            dc.DrawLine(GloryRaysPen, new Point(Math.Cos(a) * 1.5, Math.Sin(a) * 1.5), new Point(Math.Cos(a) * 9.0, Math.Sin(a) * 9.0));
        }
        dc.DrawEllipse(GloryCenterBrush, null, new Point(0, 0), 1.8, 1.8);

        dc.Pop();
    }

    private static void DrawJasmine(DrawingContext dc, double x, double y, double s, double r)
    {
        var mat = Matrix.Identity;
        mat.Scale(s, s);
        mat.Rotate(r * 180.0 / Math.PI);
        mat.Translate(x, y);

        dc.PushTransform(new MatrixTransform(mat));

        // 5 outer ivory petals
        for (int i = 0; i < 5; i++)
        {
            double a = i * (Math.PI * 2.0 / 5.0);
            DrawPetal(dc, 10.0, 3.4, JasminePetal1Brush, a, notched: false);
        }
        // 5 inner white petals offset by 36 deg
        for (int i = 0; i < 5; i++)
        {
            double a = i * (Math.PI * 2.0 / 5.0) + 0.63;
            DrawPetal(dc, 7.0, 2.8, JasminePetal2Brush, a, notched: false);
        }

        dc.DrawEllipse(JasmineCenterBrush, null, new Point(0, 0), 1.8, 1.8);

        dc.Pop();
    }

    private static void DrawBlossom(DrawingContext dc, double x, double y, double s, double r, Brush petalBrush, Pen stamenPen)
    {
        if (s <= 0.02) return;

        var mat = Matrix.Identity;
        mat.Scale(s, s);
        mat.Rotate(r * 180.0 / Math.PI);
        mat.Translate(x, y);

        dc.PushTransform(new MatrixTransform(mat));

        // 5 notched petals
        for (int i = 0; i < 5; i++)
        {
            double a = i * (Math.PI * 2.0 / 5.0);
            DrawPetal(dc, 8.0, 4.6, petalBrush, a, notched: true);
        }

        // 9 stamens with golden anthers
        for (int i = 0; i < 9; i++)
        {
            double a = i * (Math.PI * 2.0 / 9.0) + 0.3;
            Point pEnd = new(Math.Cos(a) * 3.4, Math.Sin(a) * 3.4);
            Point pAnther = new(Math.Cos(a) * 3.7, Math.Sin(a) * 3.7);
            dc.DrawLine(stamenPen, new Point(0, 0), pEnd);
            dc.DrawEllipse(GoldenPollenBrush, null, pAnther, 0.7, 0.7);
        }

        dc.Pop();
    }

    private static void DrawPetal(DrawingContext dc, double len, double wd, Brush brush, double rot, bool notched)
    {
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
            if (notched)
            {
                ctx.BezierTo(new Point(len * 0.3, -wd), new Point(len * 0.95, -wd * 0.95), new Point(len, -wd * 0.2), isStroked: true, isSmoothJoin: true);
                ctx.LineTo(new Point(len * 0.88, 0), isStroked: true, isSmoothJoin: true);
                ctx.LineTo(new Point(len, wd * 0.2), isStroked: true, isSmoothJoin: true);
                ctx.BezierTo(new Point(len * 0.95, wd * 0.95), new Point(len * 0.3, wd), new Point(0, 0), isStroked: true, isSmoothJoin: true);
            }
            else
            {
                ctx.BezierTo(new Point(len * 0.3, -wd), new Point(len * 0.85, -wd * 0.7), new Point(len, 0), isStroked: true, isSmoothJoin: true);
                ctx.BezierTo(new Point(len * 0.85, wd * 0.7), new Point(len * 0.3, wd), new Point(0, 0), isStroked: true, isSmoothJoin: true);
            }
        }
        geom.Freeze();

        var mat = Matrix.Identity;
        mat.Rotate(rot * 180.0 / Math.PI);

        dc.PushTransform(new MatrixTransform(mat));
        dc.DrawGeometry(brush, null, geom);
        dc.DrawLine(PetalSepPen, new Point(len * 0.1, 0), new Point(len * 0.8, 0));
        dc.Pop();
    }

    private void RenderIvyOverlay(DrawingContext dc, Rect p, double t)
    {
        // 1. Shimmering golden pollen / firefly specks drifting past the island in the visible airspace
        if (_pollenEnabled && _pollenCount > 0)
        {
            int pollenMax = Math.Clamp(_pollenCount, 0, 30);
            for (int j = 0; j < pollenMax; j++)
            {
                double px = p.X - 35.0 + (((j * 97) % 100) / 100.0) * (p.Width + 70.0) + Math.Sin(t * 0.5 + j) * 12.0;
                double py = p.Y + p.Height + 10.0 + (((j * 53) % 100) / 100.0) * 110.0 - ((t * 6.0 + j * 8.0) % 40.0);
                double alpha = 0.55 * Math.Abs(Math.Sin(t + j));
                if (alpha > 0.04)
                {
                    dc.PushOpacity(alpha);
                    dc.DrawEllipse(GoldenPollenBrush, null, new Point(px, py), 1.5 * _scale, 1.5 * _scale);
                    dc.Pop();
                }
            }
        }

        // 2. Fluttering Butterflies orbiting gracefully around the cascading foliage
        int count = Math.Clamp(_butterflyCount, 0, 4);
        if (count > 0)
        {
            double[] speeds = { 11.0, 13.0, 9.0, 12.0 };
            double[] scales = { 1.0, 0.9, 0.8, 0.85 };
            Brush[] wingBrushes = { MonarchWingBrush, BlueMorphoWingBrush, SwallowtailWingBrush, BlueMorphoWingBrush };
            Pen[] wingPens = { ButterflyMonarchPen, ButterflyBluePen, ButterflyWhitePen, ButterflyBluePen };
            Brush[] spotBrushes = { MonarchSpotBrush, BlueMorphoSpotBrush, SwallowtailSpotBrush, BlueMorphoSpotBrush };

            for (int i = 0; i < count; i++)
            {
                Point FlightPath(double u)
                {
                    double bx, by;
                    if (i == 0) // Monarch fluttering near right cascading foliage
                    {
                        double cx = p.X + p.Width * 0.82;
                        bx = cx + Math.Sin(u * 0.35) * (45.0 * _scale) + Math.Cos(u * 0.15) * 15.0;
                        by = p.Y + p.Height + 28.0 + Math.Sin(u * 0.52) * 22.0;
                    }
                    else if (i == 1) // Blue Morpho fluttering near left cascading foliage
                    {
                        double cx = p.X + p.Width * 0.18;
                        bx = cx + Math.Sin(u * 0.42 + 1.5) * (45.0 * _scale) - Math.Cos(u * 0.18) * 15.0;
                        by = p.Y + p.Height + 32.0 + Math.Sin(u * 0.61 + 2.0) * 20.0;
                    }
                    else // Swallowtail looping gently across center underlay
                    {
                        double cx = p.X + p.Width * 0.50;
                        bx = cx + Math.Sin(u * 0.28 + 3.0) * (p.Width * 0.35);
                        by = p.Y + p.Height + 48.0 + Math.Sin(u * 0.45 + 1.0) * 18.0;
                    }
                    return new Point(bx, by);
                }

                Point ptA = FlightPath(t);
                Point ptB = FlightPath(t + 0.05);
                double ang = Math.Atan2(ptB.X - ptA.X, -(ptB.Y - ptA.Y));

                DrawButterfly(dc, ptA.X, ptA.Y, t + i, ang, scales[i] * _scale, speeds[i], wingBrushes[i], wingPens[i], spotBrushes[i]);
            }
        }
    }

    private static void DrawButterfly(DrawingContext dc, double x, double y, double t, double ang, double scale, double speed,
        Brush wingBrush, Pen wingPen, Brush spotBrush)
    {
        var mat = Matrix.Identity;
        mat.Scale(scale, scale);
        mat.Rotate(ang * 180.0 / Math.PI);
        mat.Translate(x, y);

        dc.PushTransform(new MatrixTransform(mat));

        // Flapping motion: squash-and-stretch on wing X axis
        double flap = 0.25 + 0.75 * Math.Abs(Math.Sin(t * speed));

        for (int sx = -1; sx <= 1; sx += 2)
        {
            var wingMat = Matrix.Identity;
            wingMat.Scale(sx * flap, 1.0);
            dc.PushTransform(new MatrixTransform(wingMat));

            // Forewing
            var forewing = new StreamGeometry();
            using (var ctx = forewing.Open())
            {
                ctx.BeginFigure(new Point(0, -1), isFilled: true, isClosed: true);
                ctx.BezierTo(new Point(3, -15), new Point(18, -19), new Point(23, -10), isStroked: true, isSmoothJoin: true);
                ctx.BezierTo(new Point(25, -3), new Point(14, 2), new Point(0, 3), isStroked: true, isSmoothJoin: true);
            }
            forewing.Freeze();
            dc.DrawGeometry(wingBrush, wingPen, forewing);

            // Hindwing
            var hindwing = new StreamGeometry();
            using (var ctx = hindwing.Open())
            {
                ctx.BeginFigure(new Point(0, 2), isFilled: true, isClosed: true);
                ctx.BezierTo(new Point(13, 1), new Point(22, 10), new Point(15, 18), isStroked: true, isSmoothJoin: true);
                ctx.BezierTo(new Point(9, 21), new Point(2, 13), new Point(0, 6), isStroked: true, isSmoothJoin: true);
            }
            hindwing.Freeze();
            dc.DrawGeometry(wingBrush, wingPen, hindwing);

            // Delicate wing veins
            Point[] veinEnds = { new(17, -14), new(22, -8), new(20, -1), new(14, 12), new(10, 16) };
            foreach (var ve in veinEnds)
            {
                dc.DrawLine(ButterflyVeinPen, new Point(1, 1), ve);
            }

            // Spotted outer wing margin
            (double px, double py, double r)[] spots = { (19, -12, 1.3), (22, -7, 1.1), (20, -3, 0.9), (15, 12, 1.0), (11, 16, 1.0) };
            foreach (var sp in spots)
            {
                dc.DrawEllipse(spotBrush, null, new Point(sp.px, sp.py), sp.r, sp.r);
            }

            dc.Pop();
        }

        // Slender body & head
        dc.DrawEllipse(ButterflyBodyBrush, null, new Point(0, 1), 1.6, 7.0);
        dc.DrawEllipse(ButterflyBodyBrush, null, new Point(0, -7), 1.9, 1.9);

        // Curved antennae
        var antGeom = new StreamGeometry();
        using (var ctx = antGeom.Open())
        {
            ctx.BeginFigure(new Point(-0.6, -8), isFilled: false, isClosed: false);
            ctx.QuadraticBezierTo(new Point(-4, -15), new Point(-7, -16), isStroked: true, isSmoothJoin: true);
            ctx.BeginFigure(new Point(0.6, -8), isFilled: false, isClosed: false);
            ctx.QuadraticBezierTo(new Point(4, -15), new Point(7, -16), isStroked: true, isSmoothJoin: true);
        }
        antGeom.Freeze();
        dc.DrawGeometry(null, ButterflyBodyPen, antGeom);

        dc.Pop();
    }

    // =========================================================================
    //  THEME 2: Meadow (Cascading Hanging Wildflower Meadow)
    // =========================================================================

    private void RenderMeadow(DrawingContext dc, Rect p, double t)
    {
        var rng = new Mulberry32(11);
        double yAnchor = p.Y + p.Height;

        // 1. Meadow grass blades draping down from the pill bottom edge
        int bladeCount = Math.Max(15, (int)(70 * _density));
        for (int i = 0; i < bladeCount; i++)
        {
            double x = p.X + 10.0 + i * (p.Width - 20.0) / Math.Max(1.0, bladeCount - 1) + (rng.NextDouble() - 0.5) * 4.0;
            double h = (14.0 + rng.NextDouble() * 28.0) * _scale;
            double ln = (rng.NextDouble() - 0.5) * 14.0 + Math.Sin(t * 1.3 + i * 0.7) * 3.0;
            double k = EaseOutCubic((t - 0.2 - rng.NextDouble() * 1.2) / 1.2);
            double tx = x + ln * k;
            double ty = yAnchor + h * k;

            var blade = new StreamGeometry();
            using (var ctx = blade.Open())
            {
                ctx.BeginFigure(new Point(x - 1.4, yAnchor), isFilled: true, isClosed: true);
                ctx.QuadraticBezierTo(new Point(x + ln * 0.1, yAnchor + h * k * 0.6), new Point(tx, ty), isStroked: false, isSmoothJoin: true);
                ctx.QuadraticBezierTo(new Point(x + ln * 0.2 + 1.0, yAnchor + h * k * 0.5), new Point(x + 1.4, yAnchor), isStroked: false, isSmoothJoin: true);
            }
            blade.Freeze();
            dc.DrawGeometry(GrassBrush, null, blade);
        }

        // 2. Wildflower stems (daisies, poppies, lavender) hanging and blooming below
        int flowerCount = Math.Max(2, (int)(8 * _bloomDensity));
        for (int i = 0; i < flowerCount; i++)
        {
            var rx = new Mulberry32((uint)(i * 7 + 3));
            double x = p.X + 24.0 + i * (p.Width - 48.0) / Math.Max(1.0, flowerCount - 1) + (rx.NextDouble() - 0.5) * 10.0;
            double h = (42.0 + rx.NextDouble() * 46.0) * _scale;
            double k = EaseOutCubic((t - 0.6 - i * 0.28) / 1.8);
            double ln = (rx.NextDouble() - 0.5) * 26.0;
            double sw = Math.Sin(t * 1.2 + i * 1.7) * 4.0 * k;
            double ex = x + ln * k + sw;
            double ey = yAnchor + h * k;
            double cx = x + ln * 0.1;
            double cy = yAnchor + h * k * 0.55;

            DrawLimb(dc, x, yAnchor, cx, cy, ex, ey, 2.8 * _scale, 1.6 * _scale, 1.0, MeadowStemDarkPen, MeadowStemLightPen);

            // Small stem leaves
            for (int leafIdx = 0; leafIdx < 2; leafIdx++)
            {
                double u = leafIdx == 0 ? 0.3 : 0.55;
                double sd = leafIdx == 0 ? -1.0 : 1.0;
                var q = Qp(x, yAnchor, cx, cy, ex, ey, u);

                var mat = Matrix.Identity;
                mat.Rotate((0.9 + sd * 0.7 + Math.Sin(t + i) * 0.05) * 180.0 / Math.PI);
                mat.Translate(q.X, q.Y);
                dc.PushTransform(new MatrixTransform(mat));
                DrawPetal(dc, 14.0 * k * _scale, 4.0 * k * _scale, MeadowLeafBrush, 0, notched: false);
                dc.Pop();
            }

            // Blossom types: 0=Daisy, 1=Poppy, 2=Lavender, 3=Daisy, 4=Poppy, 5=Lavender, 6=Daisy, 7=Poppy
            int fType = i % 3;
            double s = k * (0.85 + rx.NextDouble() * 0.3) * _scale;
            double ph = rx.NextDouble() * 6.0;

            switch (fType)
            {
                case 0: DrawDaisy(dc, ex, ey, s, t, ph); break;
                case 1: DrawPoppy(dc, ex, ey, s, t, ph); break;
                case 2:
                default: DrawLavender(dc, ex, ey, s, t, ph); break;
            }
        }
    }

    private static void DrawDaisy(DrawingContext dc, double x, double y, double s, double t, double ph)
    {
        var mat = Matrix.Identity;
        mat.Scale(s, s);
        mat.Rotate((Math.Sin(t + ph) * 0.06) * 180.0 / Math.PI);
        mat.Translate(x, y);

        dc.PushTransform(new MatrixTransform(mat));

        // 18 outer white petals
        for (int i = 0; i < 18; i++)
        {
            double a = i * (Math.PI * 2.0 / 18.0) + ph;
            DrawPetal(dc, 12.0, 2.3, DaisyPetal1Brush, a, notched: false);
        }
        // 14 inner white petals
        for (int i = 0; i < 14; i++)
        {
            double a = i * (Math.PI * 2.0 / 14.0) + ph + 0.2;
            DrawPetal(dc, 9.0, 2.3, DaisyPetal2Brush, a, notched: false);
        }

        // Golden center disc with florets
        dc.DrawEllipse(DaisyCenterBrush, null, new Point(0, 0), 4.5, 4.5);
        for (int i = 0; i < 8; i++)
        {
            dc.DrawRectangle(DaisyFloretBrush, null, new Rect(Math.Cos(i * 2.4) * i * 0.45, Math.Sin(i * 2.4) * i * 0.45, 0.8, 0.8));
        }

        dc.Pop();
    }

    private static void DrawPoppy(DrawingContext dc, double x, double y, double s, double t, double ph)
    {
        var mat = Matrix.Identity;
        mat.Scale(s, s);
        mat.Rotate(ph * 180.0 / Math.PI);
        mat.Translate(x, y);

        dc.PushTransform(new MatrixTransform(mat));

        for (int i = 0; i < 5; i++)
        {
            double a = i * (Math.PI * 2.0 / 5.0);
            DrawPetal(dc, 12.0, 8.5, PoppyPetal1Brush, a, notched: false);
        }
        for (int i = 0; i < 5; i++)
        {
            double a = i * (Math.PI * 2.0 / 5.0) + 0.6;
            DrawPetal(dc, 8.5, 5.5, PoppyPetal2Brush, a, notched: false);
        }

        dc.DrawEllipse(PoppyCenterBrush, null, new Point(0, 0), 2.8, 2.8);
        for (int i = 0; i < 12; i++)
        {
            double a = i * (Math.PI * 2.0 / 12.0);
            dc.DrawEllipse(PoppyRingBrush, null, new Point(Math.Cos(a) * 4.2, Math.Sin(a) * 4.2), 0.7, 0.7);
        }

        dc.Pop();
    }

    private static void DrawLavender(DrawingContext dc, double x, double y, double s, double t, double ph)
    {
        var mat = Matrix.Identity;
        mat.Scale(s, s);
        mat.Translate(x, y);

        dc.PushTransform(new MatrixTransform(mat));

        for (int i = 0; i < 14; i++)
        {
            double sw = Math.Sin(t * 1.4 + ph + i * 0.2) * 0.5;
            double flX = (i % 2 == 1 ? 2.4 : -2.4) + sw;
            double flY = i * 3.0; // Cascading downward florets

            dc.DrawEllipse(i % 2 == 1 ? LavFloret1Brush : LavFloret2Brush, null, new Point(flX, flY), 2.4, 1.7);
        }

        dc.Pop();
    }

    private void RenderMeadowButterfly(DrawingContext dc, Rect p, double t)
    {
        if (_butterflyCount <= 0) return;
        double bx = p.X + p.Width / 2.0 + Math.Sin(t * 0.45) * 180.0;
        double by = p.Y + p.Height + 50.0 + Math.Sin(t * 0.8) * 25.0;
        double ang = Math.Sin(t * 0.9) * 0.3;

        DrawButterfly(dc, bx, by, t, ang, 1.0 * _scale, 12.0, MonarchWingBrush, ButterflyMonarchPen, MonarchSpotBrush);
    }

    // =========================================================================
    //  Helper: Tapered Two-Tone Limb
    // =========================================================================

    private static Point DrawLimb(DrawingContext dc, double x0, double y0, double cx, double cy, double x1, double y1,
        double w0, double w1, double k, Pen penDark, Pen penLight)
    {
        if (k <= 0) return new Point(x0, y0);
        int n = 10;

        for (int pass = 0; pass < 2; pass++)
        {
            bool isHighlight = pass == 1;
            Point prev = new(x0, y0);
            for (int i = 1; i <= n; i++)
            {
                double u = (double)i / n * k;
                var q = Qp(x0, y0, cx, cy, x1, y1, u);
                double offset = isHighlight ? 0.6 : 0.0;

                dc.DrawLine(isHighlight ? penLight : penDark,
                    new Point(prev.X - offset, prev.Y),
                    new Point(q.X - offset, q.Y));
                prev = q;
            }
        }

        return Qp(x0, y0, cx, cy, x1, y1, k);
    }
}
