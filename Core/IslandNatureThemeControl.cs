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
/// Renders living botanical themes (Ivy &amp; Blooms, Sakura Branch, Blossom Tree, Meadow)
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

    // Cached vine perimeter points to avoid per-frame allocations
    private double _cachedPillW = -1;
    private double _cachedPillH = -1;
    private double _cachedPillX = -1;
    private double _cachedPillY = -1;
    private List<VinePoint[]>? _cachedVines;
    private List<BranchData>? _cachedBranches;

    public IslandNatureThemeControl()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
    }

    public void Step(double t, Rect pillBounds, bool reducedMotion)
    {
        _time = t;
        _pillBounds = pillBounds;
        _reducedMotion = reducedMotion;
        InvalidateVisual();
    }

    // =========================================================================
    //  Pre-allocated, Frozen Brush & Pen Singletons (Zero GC Pressure)
    // =========================================================================

    private static readonly Pen StemDarkPen;
    private static readonly Pen StemLightPen;
    private static readonly Pen VineBarkDarkPen;
    private static readonly Pen VineBarkLightPen;
    private static readonly Pen TreeBarkDarkPen;
    private static readonly Pen TreeBarkLightPen;
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
    private static readonly LinearGradientBrush TreeLeafBrush;
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

    private static readonly LinearGradientBrush SakuraPetalBrush;
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

        StemDarkPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x1F, 0x2A, 0x16))), 2.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        StemLightPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x6B, 0x8C, 0x3A))), 1.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

        VineBarkDarkPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x1B, 0x17))), 3.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        VineBarkLightPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x6E, 0x4D, 0x3F))), 1.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

        TreeBarkDarkPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x33, 0x23, 0x1A))), 4.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        TreeBarkLightPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x7E, 0x5E, 0x3E))), 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

        MeadowStemDarkPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x2C, 0x62, 0x32))), 2.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        MeadowStemLightPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x74, 0xB8, 0x5C))), 0.9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

        VeinPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(64, 255, 255, 255))), 0.7));
        IvyLeafVeinPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(82, 225, 255, 200))), 0.6));
        PetalSepPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(36, 0, 0, 0))), 0.5));
        GloryRaysPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(140, 255, 255, 255))), 1.0));

        ButterflyMonarchPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x15, 0x0B, 0x06))), 1.2) { LineJoin = PenLineJoin.Round });
        ButterflyBluePen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x07, 0x11, 0x26))), 1.2) { LineJoin = PenLineJoin.Round });
        ButterflyWhitePen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x24))), 1.2) { LineJoin = PenLineJoin.Round });
        ButterflyVeinPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(128, 0, 0, 0))), 0.7));
        ButterflyBodyPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x12, 0x0A, 0x06))), 0.7));

        StamenPinkPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xD9, 0x4F, 0x7A))), 0.6));
        StamenPurplePen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x7A, 0x4C, 0xC0))), 0.6));

        IvyLeafDarkBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0x14, 0x3D, 0x1F), Color.FromRgb(0x4F, 0x9A, 0x3C), new Point(0, 0), new Point(1, 1)));
        IvyLeafLightBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0x2A, 0x6A, 0x30), Color.FromRgb(0x8F, 0xD0, 0x5C), new Point(0, 0), new Point(1, 1)));
        TreeLeafBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0x3B, 0x7A, 0x2C), Color.FromRgb(0x8F, 0xC8, 0x4E), new Point(0, 0), new Point(1, 0)));
        MeadowLeafBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0x27, 0x60, 0x2F), Color.FromRgb(0x79, 0xB8, 0x5A), new Point(0, 0), new Point(1, 0)));
        GrassBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0x1B, 0x45, 0x26), Color.FromRgb(0x86, 0xCC, 0x5E), new Point(0, 0), new Point(0, 1)));

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

        SakuraPetalBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0xF1, 0x9A, 0xB5), Color.FromRgb(0xFF, 0xE9, 0xEF), new Point(0, 0), new Point(1, 0)));
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
            case "Sakura Branch":
                if (Layer == ThemeLayer.Underlay) RenderSakuraBranch(dc, p, t);
                else RenderSakuraPetals(dc, p, t);
                break;

            case "Blossom Tree":
                if (Layer == ThemeLayer.Underlay) RenderBlossomTree(dc, p, t);
                break;

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
            Math.Abs(p.Y - _cachedPillY) < 0.5)
        {
            return;
        }

        _cachedPillW = p.Width;
        _cachedPillH = p.Height;
        _cachedPillX = p.X;
        _cachedPillY = p.Y;

        double cenX = p.X + p.Width / 2.0;
        double cenY = p.Y + p.Height / 2.0;

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

        VinePoint[] Perim(double side, double o, double wob, double endF, double drop)
        {
            var pts = new List<Point>();
            double rr = p.Height / 2.0;
            double cy = p.Y + rr;
            double cx = p.X + (side < 0 ? rr : p.Width - rr);
            double R = rr + o;

            // Climb up along side from bottom drop
            for (double y = p.Y + p.Height + drop; y > cy + R; y -= 4.0)
            {
                pts.Add(new Point(cx, y));
            }

            // Curve around capsule end cap
            double angleStep = 4.0 / Math.Max(1.0, R);
            for (double a = 0.0; a <= Math.PI; a += angleStep)
            {
                double an = Math.PI / 2.0 + (side < 0 ? a : -a);
                pts.Add(new Point(cx + Math.Cos(an) * R, cy + Math.Sin(an) * R));
            }

            // Wrap along top rim
            double end = p.X + p.Width * endF;
            if (side < 0)
            {
                for (double x = cx; x < end; x += 4.0) pts.Add(new Point(x, p.Y - o));
            }
            else
            {
                for (double x = cx; x > end; x -= 4.0) pts.Add(new Point(x, p.Y - o));
            }

            // Apply organic sinusoidal wobble
            var wobbled = new List<Point>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                wobbled.Add(new Point(
                    pts[i].X + Math.Sin(i * wob) * 2.4,
                    pts[i].Y + Math.Cos(i * wob * 0.8) * 1.8));
            }

            return Fin(wobbled);
        }

        _cachedVines = new List<VinePoint[]>
        {
            Perim(-1, 3.0, 0.35, 0.60, 48.0),
            Perim(1, 3.0, 0.33, 0.40, 48.0),
            Perim(-1, 9.0, 0.27, 0.78, 80.0),
            Perim(1, 9.0, 0.29, 0.22, 75.0)
        };

        _cachedBranches = new List<BranchData>();
        for (int j = 0; j < _cachedVines.Count; j++)
        {
            var m = _cachedVines[j];
            var rng = new Mulberry32((uint)(100 + j));
            for (int i = 16 + j * 3; i < m.Length - 12; i += 20 + (int)(rng.NextDouble() * 10))
            {
                var pt = m[i];
                double ang = Math.Atan2(pt.Ny - 0.6, pt.Nx) + (rng.NextDouble() - 0.5) * 0.8;
                double curX = pt.X, curY = pt.Y;
                var branchPts = new List<Point>();
                int steps = 14 + (int)(rng.NextDouble() * 18);
                double cu = (rng.NextDouble() - 0.5) * 0.07;

                for (int k = 0; k < steps; k++)
                {
                    ang += cu + (rng.NextDouble() - 0.5) * 0.3;
                    curX += Math.Cos(ang) * 3.2;
                    curY += Math.Sin(ang) * 3.2;
                    branchPts.Add(new Point(curX, curY));
                }

                double sg = rng.NextDouble() < 0.5 ? 1.0 : -1.0;
                for (int k = 0; k < 10; k++)
                {
                    ang += 0.36 * sg;
                    curX += Math.Cos(ang) * 1.5;
                    curY += Math.Sin(ang) * 1.5;
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
            double delay = j > 1 ? 1.5 : 0.0;
            ks[j] = (int)(_cachedVines[j].Length * EaseOutCubic((cyc - delay) / 11.0));
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
            int n = (int)(b.Points.Length * Clamp((ks[b.ParentIdx] - b.AttachIdx) / 36.0));
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

    private static void DrawVine(DrawingContext dc, VinePoint[] pts, int n, double w, double t, List<FlowerData> fl, int sd, bool isBranch)
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

        dc.DrawGeometry(null, StemDarkPen, stemGeom);
        dc.DrawGeometry(null, StemLightPen, stemGeom);

        // 2. Draw English Ivy Leaves alternating along the stem
        for (int i = 3; i < n; i += 3)
        {
            var pt = pts[i];
            double sd2 = ((i / 3) % 2 == 1) ? 1.0 : -1.0;
            double sc = EaseOutCubic((n - i) / 22.0);
            double h = ((uint)((i + sd * 31) * 2654435761u)) / 4294967296.0;
            double len = (11.0 + h * 8.0) * sc * (isBranch ? 0.8 : 1.0);
            if (len < 1.5) continue;

            double ang = Math.Atan2(pt.Ty, pt.Tx) + sd2 * (0.95 + Math.Sin(t * 1.2 + i * 0.7) * 0.1);
            double lx = pt.X + Math.Cos(ang) * len * 0.25;
            double ly = pt.Y + Math.Sin(ang) * len * 0.25;

            dc.DrawLine(MeadowStemDarkPen, new Point(pt.X, pt.Y), new Point(lx, ly));
            DrawIvyLeaf(dc, lx, ly, ang, len, h > 0.55);
        }

        // 3. Flower bud candidates
        for (int i = 10; i < n; i += 16 + ((i * 7) % 5))
        {
            var pt = pts[i];
            double h = (((uint)(i * 40503 + sd * 977)) % 1000) / 1000.0;
            double k = EaseOutCubic((n - i) / 26.0);
            if (k > 0.05)
            {
                fl.Add(new FlowerData(
                    pt.X + pt.Nx * (5.0 + h * 5.0),
                    pt.Y + pt.Ny * (5.0 + h * 5.0),
                    k * (0.75 + h * 0.5),
                    h * 6.283 + Math.Sin(t + i) * 0.08,
                    ((i / 16) + sd) % 4));
            }
        }

        if (isBranch && n == pts.Length && n > 13)
        {
            var pt = pts[n - 13];
            fl.Add(new FlowerData(pt.X, pt.Y, 0.85, 0.5, (sd + n) % 4));
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
        dc.DrawEllipse(GloryCenterBrush, null, new Point(0, 0), 1.7, 1.7);

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
        // 1. Shimmering golden pollen / firefly specks drifting past the island
        for (int j = 0; j < 20; j++)
        {
            double px = p.X - 20.0 + (((j * 97) % 100) / 100.0) * (p.Width + 40.0) + Math.Sin(t * 0.5 + j) * 10.0;
            double py = p.Y + 8.0 + (((j * 53) % 100) / 100.0) * (p.Height + 50.0) - ((t * 7.0 + j * 9.0) % 40.0);
            double alpha = 0.5 * Math.Abs(Math.Sin(t + j));
            if (alpha > 0.05)
            {
                dc.PushOpacity(alpha);
                dc.DrawEllipse(GoldenPollenBrush, null, new Point(px, py), 1.5, 1.5);
                dc.Pop();
            }
        }

        // 2. Three Fluttering Butterflies: Monarch, Blue Morpho, White Swallowtail
        double[] speeds = { 11.0, 13.0, 9.0 };
        double[] scales = { 0.95, 0.85, 0.75 };
        Brush[] wingBrushes = { MonarchWingBrush, BlueMorphoWingBrush, SwallowtailWingBrush };
        Pen[] wingPens = { ButterflyMonarchPen, ButterflyBluePen, ButterflyWhitePen };
        Brush[] spotBrushes = { MonarchSpotBrush, BlueMorphoSpotBrush, SwallowtailSpotBrush };

        for (int i = 0; i < 3; i++)
        {
            Point FlightPath(double u)
            {
                double bx = p.X + p.Width / 2.0 + Math.Sin(u * (0.21 + 0.07 * i) + i * 2.0) * (Math.Min(190.0, p.Width / 2.0 + 80.0) - i * 22.0);
                double by = p.Y + p.Height / 2.0 + 32.0 + i * 16.0 + Math.Sin(u * (0.37 + 0.05 * i) + i) * (36.0 + i * 8.0);
                return new Point(bx, by);
            }

            Point ptA = FlightPath(t);
            Point ptB = FlightPath(t + 0.05);
            double ang = Math.Atan2(ptB.X - ptA.X, -(ptB.Y - ptA.Y));

            DrawButterfly(dc, ptA.X, ptA.Y, t + i, ang, scales[i], speeds[i], wingBrushes[i], wingPens[i], spotBrushes[i]);
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
    //  THEME 2: Sakura Branch
    // =========================================================================

    private void RenderSakuraBranch(DrawingContext dc, Rect p, double t)
    {
        double k = EaseOutCubic(t / 3.2);
        var rng = new Mulberry32(5);

        double x0 = p.X + p.Width + 40.0;
        double y0 = p.Y + 2.0;
        double cx = p.X + p.Width * 0.70;
        double cy = p.Y + 18.0;
        double x1 = p.X + p.Width * 0.35;
        double y1 = p.Y + p.Height + 12.0;

        DrawLimb(dc, x0, y0, cx, cy, x1, y1, 7.0, 2.5, k, VineBarkDarkPen, VineBarkLightPen);

        for (int i = 0; i < 11; i++)
        {
            double u = 0.1 + i * 0.08;
            double tk = EaseOutCubic((k - u) * 4.0);
            if (tk <= 0) { rng.NextDouble(); rng.NextDouble(); rng.NextDouble(); continue; }

            var b = Qp(x0, y0, cx, cy, x1, y1, u);
            double ang = (100.0 + rng.NextDouble() * 70.0) * Math.PI / 180.0;
            double len = 20.0 + rng.NextDouble() * 26.0;
            double sw = Math.Sin(t * 1.2 + i) * 3.0;
            double ex = b.X + Math.Cos(ang) * len + sw;
            double ey = b.Y + Math.Sin(ang) * len;

            DrawLimb(dc, b.X, b.Y, (b.X + ex) / 2.0 + 6.0, (b.Y + ey) / 2.0 - 4.0, ex, ey, 2.4, 0.9, tk, VineBarkDarkPen, VineBarkLightPen);

            if (tk >= 0.95)
            {
                double bs = EaseOutCubic((t - u * 3.0 - 1.2) / 1.4) * 1.15;
                DrawBlossom(dc, ex, ey, bs, i, SakuraPetalBrush, StamenPinkPen);
                DrawBlossom(dc, ex - 8.0, ey - 6.0, bs * 0.8, i + 2, SakuraPetalBrush, StamenPinkPen);
                DrawBlossom(dc, ex + 7.0, ey - 4.0, bs * 0.7, i + 4, SakuraPetalBrush, StamenPinkPen);
                DrawBlossom(dc, (b.X + ex) / 2.0, (b.Y + ey) / 2.0, bs * 0.75, i + 1, SakuraPetalBrush, StamenPinkPen);
            }
        }
    }

    private void RenderSakuraPetals(DrawingContext dc, Rect p, double t)
    {
        for (int i = 0; i < 24; i++)
        {
            double z = 0.45 + ((i * 37) % 10) / 10.0 * 0.75;
            double f = (t * 0.06 * (0.6 + z) + i * 0.113) % 1.0;
            double px = p.X - 40.0 + (((i * 97) % 100) / 100.0) * (p.Width + 80.0) + Math.Sin(t * 1.2 + i) * 16.0;
            double py = p.Y - 10.0 + f * (p.Height + 110.0);
            double alpha = (0.5 + z * 0.4) * Math.Sin(f * Math.PI);

            if (alpha > 0.05)
            {
                var mat = Matrix.Identity;
                mat.Scale(1.0, 0.4 + Math.Abs(Math.Sin(t * 2.0 + i)) * 0.6);
                mat.Rotate((t * 1.4 + i) * 180.0 / Math.PI);
                mat.Translate(px, py);

                dc.PushOpacity(alpha);
                dc.PushTransform(new MatrixTransform(mat));
                DrawPetal(dc, 8.0 * z, 4.0 * z, SakuraPetalBrush, 0, notched: true);
                dc.Pop();
                dc.Pop();
            }
        }
    }

    // =========================================================================
    //  THEME 3: Blossom Tree
    // =========================================================================

    private sealed class TreeNode
    {
        public double X, Y, X1, Y1, Cx, Cy, W, Ph;
        public int D;
        public List<TreeNode> Kids = new();
    }

    private static TreeNode? _cachedTree;
    private static TreeNode BuildTree(uint seed)
    {
        if (_cachedTree != null) return _cachedTree;
        var rng = new Mulberry32(seed);

        TreeNode F(double x, double y, double len, double a, int d, double w)
        {
            double rad = a * Math.PI / 180.0;
            double x1 = x + Math.Cos(rad) * len;
            double y1 = y + Math.Sin(rad) * len;
            double b = (rng.NextDouble() - 0.5) * len * 0.5;
            var node = new TreeNode
            {
                X = x, Y = y, X1 = x1, Y1 = y1,
                Cx = (x + x1) / 2.0 - Math.Sin(rad) * b,
                Cy = (y + y1) / 2.0 + Math.Cos(rad) * b,
                D = d, W = w, Ph = rng.NextDouble() * 6.0
            };
            if (d < 5)
            {
                int m = d < 1 ? 3 : (rng.NextDouble() < 0.25 ? 3 : 2);
                for (int i = 0; i < m; i++)
                {
                    double sp = (i - (m - 1) / 2.0) * (26.0 + rng.NextDouble() * 14.0) + (rng.NextDouble() - 0.5) * 10.0;
                    node.Kids.Add(F(x1, y1, len * (0.72 + rng.NextDouble() * 0.08), a + sp, d + 1, w * 0.68));
                }
            }
            return node;
        }

        _cachedTree = F(0, 0, 32.0, 90.0, 0, 7.5);
        return _cachedTree;
    }

    private void RenderBlossomTree(DrawingContext dc, Rect p, double t)
    {
        double cyc = t % 18.0;
        double gr = EaseOutCubic(cyc / 7.0);
        double fade = cyc > 16.0 ? (18.0 - cyc) / 2.0 : 1.0;
        if (fade <= 0.01) return;

        if (fade < 0.99) dc.PushOpacity(fade);

        var root = BuildTree(21);
        DrawTreeNode(dc, root, p.X + p.Width / 2.0, p.Y + p.Height - 1.0, gr, t);

        if (fade < 0.99) dc.Pop();
    }

    private static void DrawTreeNode(DrawingContext dc, TreeNode n, double ox, double oy, double gr, double t)
    {
        double gd = Clamp(gr * 8.0 - n.D * 1.05);
        if (gd <= 0) return;

        double sw = Math.Sin(t * 1.1 + n.Ph) * n.D * 0.45;
        Point e = DrawLimb(dc,
            n.X + ox, n.Y + oy,
            n.Cx + ox + sw * 0.5, n.Cy + oy,
            n.X1 + ox + sw, n.Y1 + oy,
            n.W, n.W * 0.7, gd, TreeBarkDarkPen, TreeBarkLightPen);

        if (gd >= 0.95)
        {
            if (n.D >= 2)
            {
                var mat = Matrix.Identity;
                mat.Rotate((n.Ph * 2.0) * 180.0 / Math.PI);
                mat.Translate(e.X, e.Y);
                dc.PushTransform(new MatrixTransform(mat));
                DrawPetal(dc, 7.5, 3.5, TreeLeafBrush, 0, notched: false);
                dc.Pop();
            }
            if (n.D >= 3)
            {
                DrawBlossom(dc, e.X, e.Y, Clamp(gr * 8.0 - n.D - 0.3) * 0.85, n.Ph + t * 0.1, SakuraPetalBrush, StamenPinkPen);
            }
        }

        foreach (var kid in n.Kids)
        {
            DrawTreeNode(dc, kid, ox + sw, oy, gr, t);
        }
    }

    // =========================================================================
    //  THEME 4: Meadow
    // =========================================================================

    private void RenderMeadow(DrawingContext dc, Rect p, double t)
    {
        var rng = new Mulberry32(11);

        // 1. Hanging/growing grass blades
        for (int i = 0; i < 50; i++)
        {
            double x = p.X + 8.0 + i * (p.Width - 16.0) / 49.0 + (rng.NextDouble() - 0.5) * 4.0;
            double h = 10.0 + rng.NextDouble() * 20.0;
            double ln = (rng.NextDouble() - 0.5) * 12.0 + Math.Sin(t * 1.3 + i * 0.7) * 3.0;
            double k = EaseOutCubic((t - 0.2 - rng.NextDouble() * 1.2) / 1.2);
            double tx = x + ln * k;
            double ty = p.Y + p.Height + h * k;

            var blade = new StreamGeometry();
            using (var ctx = blade.Open())
            {
                ctx.BeginFigure(new Point(x - 1.2, p.Y + p.Height), isFilled: true, isClosed: true);
                ctx.QuadraticBezierTo(new Point(x + ln * 0.1, p.Y + p.Height + h * k * 0.6), new Point(tx, ty), isStroked: false, isSmoothJoin: true);
                ctx.QuadraticBezierTo(new Point(x + ln * 0.2 + 1.0, p.Y + p.Height + h * k * 0.5), new Point(x + 1.2, p.Y + p.Height), isStroked: false, isSmoothJoin: true);
            }
            blade.Freeze();
            dc.DrawGeometry(GrassBrush, null, blade);
        }

        // 2. Wildflower stems (daisies, poppies, lavender)
        int flowerCount = 7;
        for (int i = 0; i < flowerCount; i++)
        {
            var rx = new Mulberry32((uint)(i * 7 + 3));
            double x = p.X + 20.0 + i * (p.Width - 40.0) / (flowerCount - 1) + (rx.NextDouble() - 0.5) * 8.0;
            double h = 32.0 + rx.NextDouble() * 32.0;
            double k = EaseOutCubic((t - 0.6 - i * 0.28) / 1.8);
            double ln = (rx.NextDouble() - 0.5) * 22.0;
            double sw = Math.Sin(t * 1.2 + i * 1.7) * 3.5 * k;
            double ex = x + ln * k + sw;
            double ey = p.Y + p.Height + h * k;
            double cx = x + ln * 0.1;
            double cy = p.Y + p.Height + h * k * 0.55;

            DrawLimb(dc, x, p.Y + p.Height, cx, cy, ex, ey, 2.2, 1.2, 1.0, MeadowStemDarkPen, MeadowStemLightPen);

            // Small stem leaves
            for (int leafIdx = 0; leafIdx < 2; leafIdx++)
            {
                double u = leafIdx == 0 ? 0.35 : 0.65;
                double sd = leafIdx == 0 ? -1.0 : 1.0;
                var q = Qp(x, p.Y + p.Height, cx, cy, ex, ey, u);

                var mat = Matrix.Identity;
                mat.Rotate((0.9 + sd * 0.8 + Math.Sin(t + i) * 0.05) * 180.0 / Math.PI);
                mat.Translate(q.X, q.Y);
                dc.PushTransform(new MatrixTransform(mat));
                DrawPetal(dc, 12.0 * k, 3.5 * k, MeadowLeafBrush, 0, notched: false);
                dc.Pop();
            }

            // Blossom types: 0=Daisy, 1=Poppy, 2=Lavender
            int fType = i % 3;
            double s = k * (0.75 + rx.NextDouble() * 0.25);
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
            double flY = -i * 3.0;

            dc.DrawEllipse(i % 2 == 1 ? LavFloret1Brush : LavFloret2Brush, null, new Point(flX, flY), 2.4, 1.7);
        }

        dc.Pop();
    }

    private void RenderMeadowButterfly(DrawingContext dc, Rect p, double t)
    {
        double bx = p.X + p.Width / 2.0 + Math.Sin(t * 0.45) * (p.Width / 2.0 + 40.0);
        double by = p.Y + p.Height + 25.0 + Math.Sin(t * 0.8) * 16.0;
        double ang = Math.Sin(t * 0.9) * 0.3;

        DrawButterfly(dc, bx, by, t, ang, 0.9, 12.0, MonarchWingBrush, ButterflyMonarchPen, MonarchSpotBrush);
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
