using System;
using System.Windows;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Core;

/// <summary>
/// Vector desktop pet (Cat / Dog / Bunny) â€“ v3 (Full-Body Articulated Edition).
///
/// Visual upgrades
///  - Full-body anatomy: distinct seated pear-shaped torso, chest/belly fur patch, hind haunches
///    and back feet, articulated front legs with toe beans, and species-specific neckwear
///    (cat collar + swinging gold bell, Shiba bandana + charm, bunny ribbon bow).
///  - Multi-part skeletal motion: torso squashes and pivots on the feet while the head pivots
///    independently on the neck with velocity lag, cursor-tracking head tilt, and sleeping loaf pose.
///  - Animated limbs: front legs tap/raise during Happy &amp; Dancing, tuck under the chin when Sleeping,
///    and reach forward during Stretch.
///  - Rump-anchored tails: tapered tabby cat tail with travelling wave, curled Shiba makio tail,
///    and wiggling bunny cotton pom-pom rooted at the lower hip.
///
/// Performance
///  - Every brush, pen, and geometry is created once, frozen, and cached at type initialization.
///  - Particle fades use pre-built 16-level alpha tables with zero per-frame brush/geometry allocations.
/// </summary>
public sealed partial class VirtualPetControl : FrameworkElement
{
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Dependency properties (unchanged public API)
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public static readonly DependencyProperty SpeciesProperty =
        DependencyProperty.Register(nameof(Species), typeof(string), typeof(VirtualPetControl),
            new FrameworkPropertyMetadata("Cat", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccessoryProperty =
        DependencyProperty.Register(nameof(Accessory), typeof(string), typeof(VirtualPetControl),
            new FrameworkPropertyMetadata("None", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MoodProperty =
        DependencyProperty.Register(nameof(Mood), typeof(PetMood), typeof(VirtualPetControl),
            new FrameworkPropertyMetadata(PetMood.Sleeping, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PetNameProperty =
        DependencyProperty.Register(nameof(PetName), typeof(string), typeof(VirtualPetControl),
            new FrameworkPropertyMetadata("Mochi"));

    public static readonly DependencyProperty AffectionProperty =
        DependencyProperty.Register(nameof(Affection), typeof(int), typeof(VirtualPetControl),
            new FrameworkPropertyMetadata(25, OnAffectionPropertyChanged));

    public static readonly DependencyProperty IsScamperingProperty =
        DependencyProperty.Register(nameof(IsScampering), typeof(bool), typeof(VirtualPetControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsPeekingProperty =
        DependencyProperty.Register(nameof(IsPeeking), typeof(bool), typeof(VirtualPetControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private static void OnAffectionPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VirtualPetControl pet && (int)e.NewValue > (int)e.OldValue)
            pet.PetClickImpulse();
    }

    public string Species { get => (string)GetValue(SpeciesProperty); set => SetValue(SpeciesProperty, value); }
    public string Accessory { get => (string)GetValue(AccessoryProperty); set => SetValue(AccessoryProperty, value); }
    public PetMood Mood { get => (PetMood)GetValue(MoodProperty); set => SetValue(MoodProperty, value); }
    public string PetName { get => (string)GetValue(PetNameProperty); set => SetValue(PetNameProperty, value); }
    public int Affection { get => (int)GetValue(AffectionProperty); set => SetValue(AffectionProperty, value); }
    public bool IsScampering { get => (bool)GetValue(IsScamperingProperty); set => SetValue(IsScamperingProperty, value); }
    public bool IsPeeking { get => (bool)GetValue(IsPeekingProperty); set => SetValue(IsPeekingProperty, value); }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Build helpers (run once, at type initialisation)
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private const int AlphaLevels = 16;
    private const int CatTailSegs = 9;
    private const double FootY = 9.5;      // local Y of the ground plane the pet stands on
    private const double HeadScale = 0.72; // proportional head scale relative to full body

    private static T F<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static Color C(uint rgb, byte a = 255) =>
        Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static SolidColorBrush Solid(uint rgb, byte a = 255) => F(new SolidColorBrush(C(rgb, a)));

    private static RadialGradientBrush Radial(double cx, double cy, double ox, double oy, double r,
        params (Color c, double o)[] stops)
    {
        var b = new RadialGradientBrush
        {
            Center = new Point(cx, cy), GradientOrigin = new Point(ox, oy), RadiusX = r, RadiusY = r
        };
        foreach (var s in stops) b.GradientStops.Add(new GradientStop(s.c, s.o));
        return F(b);
    }

    private static LinearGradientBrush Linear(double x0, double y0, double x1, double y1,
        params (Color c, double o)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(x0, y0), EndPoint = new Point(x1, y1) };
        foreach (var s in stops) b.GradientStops.Add(new GradientStop(s.c, s.o));
        return F(b);
    }

    private static Pen MakePen(Brush b, double w) =>
        F(new Pen(b, w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });

    private static Pen[] MakePens(Brush b, double w0, double w1, int n)
    {
        var p = new Pen[n];
        for (int i = 0; i < n; i++) p[i] = MakePen(b, w0 + (w1 - w0) * i / Math.Max(1, n - 1));
        return p;
    }

    private static SolidColorBrush[] BrushTable(uint rgb, byte maxA)
    {
        var t = new SolidColorBrush[AlphaLevels];
        for (int i = 0; i < AlphaLevels; i++) t[i] = Solid(rgb, (byte)(maxA * i / (AlphaLevels - 1)));
        return t;
    }

    private static Pen[] PenTable(SolidColorBrush[] brushes, double w)
    {
        var t = new Pen[AlphaLevels];
        for (int i = 0; i < AlphaLevels; i++) t[i] = MakePen(brushes[i], w);
        return t;
    }

    private static int Lvl(double a) => (int)(Math.Clamp(a, 0.0, 1.0) * (AlphaLevels - 1) + 0.5);

    private static StreamGeometry Geo(Action<StreamGeometryContext> build)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open()) build(ctx);
        g.Freeze();
        return g;
    }

    private const int TransformPoolSize = 64;
    private static readonly MatrixTransform[] TransformPool = CreateTransformPool();
    private static int _transformPoolIndex;

    private static MatrixTransform[] CreateTransformPool()
    {
        var pool = new MatrixTransform[TransformPoolSize];
        for (int i = 0; i < TransformPoolSize; i++)
        {
            pool[i] = new MatrixTransform();
        }
        return pool;
    }

    private static void Push(DrawingContext dc, Matrix m)
    {
        var t = TransformPool[_transformPoolIndex++ & (TransformPoolSize - 1)];
        t.Matrix = m;
        dc.PushTransform(t);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Cached palette
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private static readonly RadialGradientBrush ContactShadowBrush =
        Radial(0.5, 0.5, 0.5, 0.5, 0.5, (C(0, 105), 0.0), (C(0, 45), 0.55), (C(0, 0), 1.0));

    private static readonly RadialGradientBrush DanceGlowBrush =
        Radial(0.5, 0.5, 0.5, 0.5, 0.5, (C(0xBF5AF2, 140), 0.0), (C(0xFF2D95, 50), 0.6), (C(0xFF2D95, 0), 1.0));

    private static readonly RadialGradientBrush HappyGlowBrush =
        Radial(0.5, 0.5, 0.5, 0.5, 0.5, (C(0xFFD60A, 120), 0.0), (C(0xFF9F0A, 40), 0.6), (C(0xFF9F0A, 0), 1.0));

    // Top-light + bottom ambient occlusion for head & torso
    private static readonly LinearGradientBrush HeadShadeBrush =
        Linear(0, 0, 0, 1, (C(0xFFFFFF, 48), 0.0), (C(0xFFFFFF, 0), 0.38), (C(0x000000, 0), 0.58), (C(0x3A1400, 68), 1.0));

    private static readonly LinearGradientBrush BodyShadeBrush =
        Linear(0, 0, 0, 1, (C(0x3A1400, 55), 0.0), (C(0x000000, 0), 0.28), (C(0x000000, 0), 0.68), (C(0x2A0E00, 75), 1.0));

    // Neckwear (Collar / Bell / Bandana / Bow)
    private static readonly LinearGradientBrush CollarRedBrush =
        Linear(0, 0, 0, 1, (C(0xFF5263), 0.0), (C(0xD92136), 0.6), (C(0x9E0E1F), 1.0));
    private static readonly Pen CatCollarPen = MakePen(CollarRedBrush, 1.35);
    private static readonly RadialGradientBrush GoldBellBrush =
        Radial(0.35, 0.3, 0.3, 0.25, 0.65, (C(0xFFF3A1), 0.0), (C(0xFFD026), 0.55), (C(0xB87B00), 1.0));
    private static readonly Pen GoldBellOutlinePen = MakePen(Solid(0x8C5800, 190), 0.38);
    private static readonly SolidColorBrush BellSlotBrush = Solid(0x5E3800);
    private static readonly LinearGradientBrush BunnyBowBrush =
        Linear(0, 0, 0, 1, (C(0xFF94B4), 0.0), (C(0xFF5C8D), 0.65), (C(0xD43566), 1.0));
    private static readonly Pen BunnyBowPen = MakePen(Solid(0xB52653, 170), 0.38);

    // \U0001F431 Cat
    private static readonly RadialGradientBrush CatCoatBrush =
        Radial(0.38, 0.32, 0.35, 0.30, 0.65, (C(0xFFAF62), 0.0), (C(0xE68836), 0.55), (C(0xB85816), 1.0));
    private static readonly RadialGradientBrush CatChestBrush =
        Radial(0.5, 0.3, 0.5, 0.25, 0.6, (C(0xFFFFFF), 0.0), (C(0xFFF4E8), 0.7), (C(0xEED8C4), 1.0));
    private static readonly RadialGradientBrush CatIrisBrush =
        Radial(0.4, 0.35, 0.35, 0.3, 0.65, (C(0x6AF5B2), 0.0), (C(0x24BD76), 0.6), (C(0x0C663E), 1.0));
    private static readonly RadialGradientBrush CatEarInnerBrush =
        Radial(0.5, 0.6, 0.5, 0.5, 0.6, (C(0xFFE4EC), 0.0), (C(0xFFA8BE), 0.65), (C(0xDE6682), 1.0));
    private static readonly Pen CatTabbyPen = MakePen(Solid(0x9E430B), 0.85);
    private static readonly Pen CatBodyTabbyPen = MakePen(Solid(0x9E430B, 175), 0.72);
    private static readonly Pen CatOutlinePen = MakePen(Solid(0x9E430B, 170), 0.48);

    private static readonly SolidColorBrush CatTailDarkBrush = Solid(0x9A4A12);
    private static readonly SolidColorBrush CatTailMidBrush = Solid(0xE68836);
    private static readonly SolidColorBrush CatTailBandBrush = Solid(0xB45A1A);
    private static readonly SolidColorBrush CatTailHiBrush = Solid(0xFFB46E, 200);
    private static readonly Pen[] CatTailDark = MakePens(CatTailDarkBrush, 2.9, 2.1, CatTailSegs);
    private static readonly Pen[] CatTailMid = MakePens(CatTailMidBrush, 2.2, 1.5, CatTailSegs);
    private static readonly Pen[] CatTailBand = MakePens(CatTailBandBrush, 2.2, 1.5, CatTailSegs);
    private static readonly Pen[] CatTailHi = MakePens(CatTailHiBrush, 0.65, 0.4, CatTailSegs);

    // \U0001F436 Dog (Shiba)
    private static readonly RadialGradientBrush DogCoatBrush =
        Radial(0.4, 0.32, 0.38, 0.30, 0.65, (C(0xF7BA74), 0.0), (C(0xE0984E), 0.55), (C(0xAB611A), 1.0));
    private static readonly RadialGradientBrush DogUrajiroBrush =
        Radial(0.5, 0.3, 0.5, 0.25, 0.6, (C(0xFFFFFF), 0.0), (C(0xFFF7EE), 0.7), (C(0xEBD6C0), 1.0));
    private static readonly LinearGradientBrush DogEarDarkBrush =
        Linear(0, 0, 1, 1, (C(0x925124), 0.0), (C(0x562A0E), 1.0));
    private static readonly SolidColorBrush DogTanDotBrush = Solid(0xFFFCF5);
    private static readonly RadialGradientBrush DogIrisBrush =
        Radial(0.4, 0.35, 0.35, 0.3, 0.65, (C(0xB0723A), 0.0), (C(0x5E3412), 0.65), (C(0x281405), 1.0));
    private static readonly RadialGradientBrush DogTongueBrush =
        Radial(0.5, 0.25, 0.5, 0.2, 0.6, (C(0xFF8EA6), 0.0), (C(0xFF5077), 0.7), (C(0xD4244B), 1.0));
    private static readonly Pen DogOutlinePen = MakePen(Solid(0x8F4C10, 165), 0.48);
    private static readonly Pen DogTailPen = MakePen(DogCoatBrush, 2.4);
    private static readonly Pen DogTailInnerPen = MakePen(DogUrajiroBrush, 1.1);
    private static readonly Pen DogTailOutlinePen = MakePen(Solid(0x8F4C10, 200), 3.1);

    // \U0001F430 Bunny
    private static readonly RadialGradientBrush BunnyCoatBrush =
        Radial(0.4, 0.3, 0.38, 0.28, 0.65, (C(0xFFFFFF), 0.0), (C(0xF7F7FD), 0.65), (C(0xD5D5EC), 1.0));
    private static readonly RadialGradientBrush BunnyBellyBrush =
        Radial(0.5, 0.35, 0.5, 0.3, 0.6, (C(0xFFFFFF), 0.0), (C(0xFFF6FA), 0.75), (C(0xF2E4EE), 1.0));
    private static readonly RadialGradientBrush BunnyEarInnerBrush =
        Radial(0.5, 0.5, 0.5, 0.4, 0.6, (C(0xFFF0F4), 0.0), (C(0xFFAEC2), 0.6), (C(0xEA728E), 1.0));
    private static readonly RadialGradientBrush BlushBrush =
        Radial(0.5, 0.5, 0.5, 0.5, 0.5, (C(0xFF5F87, 145), 0.0), (C(0xFF5F87, 0), 1.0));
    private static readonly RadialGradientBrush BunnyIrisBrush =
        Radial(0.4, 0.35, 0.35, 0.3, 0.65, (C(0xC64672), 0.0), (C(0x721937), 0.6), (C(0x280512), 1.0));
    private static readonly Pen BunnyOutlinePen = MakePen(Solid(0xA6A6C8, 165), 0.48);

    // \U0001F98A Fox (Kitsune)
    private static readonly RadialGradientBrush FoxCoatBrush =
        Radial(0.38, 0.30, 0.35, 0.28, 0.68, (C(0xFF8842), 0.0), (C(0xE85C1C), 0.55), (C(0xA63305), 1.0));
    private static readonly RadialGradientBrush FoxCreamBrush =
        Radial(0.5, 0.3, 0.5, 0.25, 0.6, (C(0xFFFFFF), 0.0), (C(0xFFF8F0), 0.7), (C(0xEDD8C4), 1.0));
    private static readonly LinearGradientBrush FoxDarkBrush =
        Linear(0, 0, 0, 1, (C(0x452216), 0.0), (C(0x241008), 1.0));
    private static readonly RadialGradientBrush FoxIrisBrush =
        Radial(0.4, 0.35, 0.35, 0.3, 0.65, (C(0xFFD644), 0.0), (C(0xD9820B), 0.6), (C(0x5E3000), 1.0));
    private static readonly LinearGradientBrush FoxScarfBrush =
        Linear(0, 0, 0, 1, (C(0x34D97B), 0.0), (C(0x169E52), 0.65), (C(0x0B6330), 1.0));
    private static readonly Pen FoxOutlinePen = MakePen(Solid(0x7D2400, 180), 0.48);

    // \U0001F43C Panda (Bamboo Giant Panda)
    private static readonly RadialGradientBrush PandaWhiteBrush =
        Radial(0.4, 0.3, 0.38, 0.28, 0.68, (C(0xFFFFFF), 0.0), (C(0xFAF8F5), 0.65), (C(0xDDD8D0), 1.0));
    private static readonly RadialGradientBrush PandaDarkBrush =
        Radial(0.38, 0.32, 0.35, 0.30, 0.65, (C(0x3D3E48), 0.0), (C(0x22232B), 0.6), (C(0x101116), 1.0));
    private static readonly RadialGradientBrush PandaIrisBrush =
        Radial(0.4, 0.35, 0.35, 0.3, 0.65, (C(0x78E08F), 0.0), (C(0x28965A), 0.65), (C(0x0D4024), 1.0));
    private static readonly Pen PandaOutlinePen = MakePen(Solid(0x5C5C6E, 175), 0.48);
    private static readonly Pen PandaDarkOutlinePen = MakePen(Solid(0x0E0E14, 210), 0.48);

    // \U0001F432 Dragon (Celestial Baby Dragon)
    private static readonly RadialGradientBrush DragonCoatBrush =
        Radial(0.38, 0.30, 0.35, 0.28, 0.68, (C(0x64D2FF), 0.0), (C(0x3B7CFF), 0.55), (C(0x2E2A85), 1.0));
    private static readonly RadialGradientBrush DragonBellyBrush =
        Radial(0.5, 0.3, 0.5, 0.25, 0.65, (C(0xFFF3B0), 0.0), (C(0xFFD15C), 0.65), (C(0xE0961F), 1.0));
    private static readonly LinearGradientBrush DragonWingBrush =
        Linear(0, 0, 1, 1, (C(0xBF5AF2), 0.0), (C(0x6E3AFF), 0.55), (C(0x26176E), 1.0));
    private static readonly LinearGradientBrush DragonHornBrush =
        Linear(0, 0, 0, 1, (C(0xFFFDF5), 0.0), (C(0xF5D38C), 0.65), (C(0xB88228), 1.0));
    private static readonly RadialGradientBrush DragonIrisBrush =
        Radial(0.4, 0.35, 0.35, 0.3, 0.65, (C(0x00F5D4), 0.0), (C(0x00A8E8), 0.6), (C(0x003459), 1.0));
    private static readonly Pen DragonOutlinePen = MakePen(Solid(0x1E1B5E, 185), 0.48);
    private static readonly Pen DragonBellyLinePen = MakePen(Solid(0xC97A10, 145), 0.42);

    // Accessories & Treats Palette
    private static readonly LinearGradientBrush CrownGoldBrush =
        Linear(0, 0, 0, 1, (C(0xFFF6A8), 0.0), (C(0xFFD426), 0.55), (C(0xC48200), 1.0));
    private static readonly LinearGradientBrush WizardHatBrush =
        Linear(0, 0, 0, 1, (C(0x8E44AD), 0.0), (C(0x4A1D7C), 0.65), (C(0x230B42), 1.0));
    private static readonly LinearGradientBrush HeadphoneBandBrush =
        Linear(0, 0, 1, 0, (C(0x2C2C36), 0.0), (C(0x525264), 0.5), (C(0x2C2C36), 1.0));
    private static readonly RadialGradientBrush HeadphoneCupBrush =
        Radial(0.4, 0.4, 0.35, 0.35, 0.65, (C(0x00F0FF), 0.0), (C(0x0A84FF), 0.65), (C(0x00357A), 1.0));
    private static readonly LinearGradientBrush SunglassesLensBrush =
        Linear(0, 0, 0, 1, (C(0x2B2D42, 240), 0.0), (C(0x0D0E17, 250), 1.0));
    private static readonly Pen SunglassesFramePen = MakePen(Solid(0x11121A), 0.65);
    private static readonly Pen HaloGoldPen = MakePen(CrownGoldBrush, 1.25);
    private static readonly Pen HaloGlowPen = MakePen(Solid(0xFFD60A, 95), 2.4);
    private static readonly LinearGradientBrush SproutLeafBrush =
        Linear(0, 0, 1, 1, (C(0x7CF286), 0.0), (C(0x30D158), 0.65), (C(0x157A2E), 1.0));
    private static readonly Pen SproutStemPen = MakePen(Solid(0x1E8A38), 0.85);
    private static readonly RadialGradientBrush LaserDotBrush =
        Radial(0.5, 0.5, 0.5, 0.5, 0.5, (C(0xFFFFFF), 0.0), (C(0xFF2D55), 0.45), (C(0xFF2D55, 0), 1.0));
    private static readonly RadialGradientBrush TreatSalmonBrush =
        Radial(0.4, 0.4, 0.4, 0.4, 0.6, (C(0xFFA685), 0.0), (C(0xFF6347), 0.7), (C(0xC8371D), 1.0));

    // Universal face & paw bits
    private static readonly SolidColorBrush DeepPupilBrush = Solid(0x141018);
    private static readonly SolidColorBrush WhiteSpecularBrush = Solid(0xFFFFFF);
    private static readonly SolidColorBrush SoftSecondarySpecularBrush = Solid(0xFFFFFF, 190);
    private static readonly SolidColorBrush EyeRimBrush = Solid(0xFFFFFF, 70);
    private static readonly SolidColorBrush PinkNoseBrush = Solid(0xFF7899);
    private static readonly SolidColorBrush DarkNoseBrush = Solid(0x241E26);
    private static readonly SolidColorBrush PawPadBrush = Solid(0xFF9BB2);
    private static readonly Pen ToeLinePen = MakePen(Solid(0x6E4228, 110), 0.38);
    private static readonly Pen BunnyToeLinePen = MakePen(Solid(0x9E9EBE, 110), 0.38);
    private static readonly Pen WhiskerPen = MakePen(Solid(0xFFFFFF, 0xB0), 0.68);
    private static readonly Pen ClosedEyePen = MakePen(DeepPupilBrush, 1.3);
    private static readonly Pen MouthPen = MakePen(DeepPupilBrush, 0.78);

    // Particle tables (alpha is quantised to 16 levels -> zero per-frame brush/pen creation)
    private static readonly SolidColorBrush[] ZBrushes = BrushTable(0xFFFFFF, 230);
    private static readonly Pen[] ZPens = PenTable(ZBrushes, 1.05);
    private static readonly SolidColorBrush[] HeartBrushes = BrushTable(0xFF2D55, 245);
    private static readonly SolidColorBrush[] StarBrushes = BrushTable(0xFFD60A, 220);
    private static readonly SolidColorBrush[] NoteBrushes = BrushTable(0xFF9F0A, 235);
    private static readonly Pen[] NotePens = PenTable(NoteBrushes, 1.1);
    private static readonly Pen[] AuraPens = PenTable(BrushTable(0x30D158, 90), 1.2);
    private static readonly SolidColorBrush WaterDropBrush = Solid(0x0A84FF);
    private static readonly Pen RipplePen = MakePen(Solid(0x0A84FF, 90), 0.8);
    private static readonly SolidColorBrush SnoreBubbleBrush = Solid(0x70C0FF, 0x60);
    private static readonly Pen SnoreBubblePen = MakePen(Solid(0xFFFFFF, 120), 0.4);

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Cached full-body & head geometry (all frozen, built once)
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Shared full-body seated torso & chest patches (in Body Space: neck at yâ‰ˆ0.4, ground at yâ‰ˆ9.5)
    private static readonly StreamGeometry TorsoGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-3.5, 0.4), true, true);
        c.BezierTo(new Point(-5.4, 2.0), new Point(-6.2, 5.4), new Point(-5.2, 8.2), true, false);
        c.QuadraticBezierTo(new Point(-4.4, 9.3), new Point(0.0, 9.3), true, false);
        c.QuadraticBezierTo(new Point(4.4, 9.3), new Point(5.2, 8.2), true, false);
        c.BezierTo(new Point(6.2, 5.4), new Point(5.4, 2.0), new Point(3.5, 0.4), true, false);
        c.QuadraticBezierTo(new Point(0.0, -0.2), new Point(-3.5, 0.4), true, false);
    });
    private static readonly Rect TorsoRect = new Rect(-6.4, -0.2, 12.8, 9.8);

    // Cozy sleeping loaf torso (wider, lower profile)
    private static readonly StreamGeometry LoafTorsoGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-4.6, 2.2), true, true);
        c.BezierTo(new Point(-6.8, 3.8), new Point(-7.2, 6.8), new Point(-5.8, 8.6), true, false);
        c.QuadraticBezierTo(new Point(-4.5, 9.5), new Point(0.0, 9.5), true, false);
        c.QuadraticBezierTo(new Point(4.5, 9.5), new Point(5.8, 8.6), true, false);
        c.BezierTo(new Point(7.2, 6.8), new Point(6.8, 3.8), new Point(4.6, 2.2), true, false);
        c.QuadraticBezierTo(new Point(0.0, 1.4), new Point(-4.6, 2.2), true, false);
    });

    // Chest / tummy fur patch
    private static readonly StreamGeometry ChestPatchGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-2.2, 1.2), true, true);
        c.BezierTo(new Point(-3.8, 2.8), new Point(-3.8, 6.6), new Point(-2.2, 8.4), true, false);
        c.QuadraticBezierTo(new Point(0.0, 9.0), new Point(2.2, 8.4), true, false);
        c.BezierTo(new Point(3.8, 6.6), new Point(3.8, 2.8), new Point(2.2, 1.2), true, false);
        c.QuadraticBezierTo(new Point(0.0, 0.6), new Point(-2.2, 1.2), true, false);
    });

    // Front legs (tapered forelegs from shoulder to wrist)
    private static readonly StreamGeometry FrontLegLGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-3.6, 2.4), true, true);
        c.QuadraticBezierTo(new Point(-3.9, 5.8), new Point(-3.7, 8.7), true, false);
        c.QuadraticBezierTo(new Point(-2.5, 9.3), new Point(-1.4, 8.7), true, false);
        c.QuadraticBezierTo(new Point(-1.2, 5.8), new Point(-1.4, 2.6), true, false);
    });
    private static readonly StreamGeometry FrontLegRGeo = Geo(c =>
    {
        c.BeginFigure(new Point(1.4, 2.6), true, true);
        c.QuadraticBezierTo(new Point(1.2, 5.8), new Point(1.4, 8.7), true, false);
        c.QuadraticBezierTo(new Point(2.5, 9.3), new Point(3.7, 8.7), true, false);
        c.QuadraticBezierTo(new Point(3.9, 5.8), new Point(3.6, 2.4), true, false);
    });

    // Neckwear geometries
    private static readonly StreamGeometry CatCollarGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-3.6, 0.5), false, false);
        c.QuadraticBezierTo(new Point(0.0, 1.55), new Point(3.6, 0.5), true, false);
    });
    private static readonly StreamGeometry DogBandanaGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-4.0, 0.35), true, true);
        c.QuadraticBezierTo(new Point(0.0, 1.3), new Point(4.0, 0.35), true, false);
        c.LineTo(new Point(3.5, 1.45), true, false);
        c.QuadraticBezierTo(new Point(1.4, 2.2), new Point(0.0, 3.3), true, false);
        c.QuadraticBezierTo(new Point(-1.4, 2.2), new Point(-3.5, 1.45), true, false);
    });
    private static readonly StreamGeometry BunnyBowGeo = Geo(c =>
    {
        c.BeginFigure(new Point(0.0, 1.15), true, true);
        c.BezierTo(new Point(-1.8, -0.1), new Point(-2.8, 0.5), new Point(-2.4, 1.55), true, false);
        c.BezierTo(new Point(-2.0, 2.3), new Point(-0.9, 1.9), new Point(0.0, 1.15), true, false);
        c.BeginFigure(new Point(0.0, 1.15), true, true);
        c.BezierTo(new Point(1.8, -0.1), new Point(2.8, 0.5), new Point(2.4, 1.55), true, false);
        c.BezierTo(new Point(2.0, 2.3), new Point(0.9, 1.9), new Point(0.0, 1.15), true, false);
    });

    // Eye arcs (in Head Space)
    private static readonly StreamGeometry SleepArcL = Geo(c => { c.BeginFigure(new Point(-5.2, -0.6), false, false); c.QuadraticBezierTo(new Point(-3.6, -2.4), new Point(-2.0, -0.6), true, false); });
    private static readonly StreamGeometry SleepArcR = Geo(c => { c.BeginFigure(new Point(2.0, -0.6), false, false); c.QuadraticBezierTo(new Point(3.6, -2.4), new Point(5.2, -0.6), true, false); });
    private static readonly StreamGeometry HappyArcL = Geo(c => { c.BeginFigure(new Point(-5.2, -0.4), false, false); c.QuadraticBezierTo(new Point(-3.6, -2.4), new Point(-2.0, -0.4), true, false); });
    private static readonly StreamGeometry HappyArcR = Geo(c => { c.BeginFigure(new Point(2.0, -0.4), false, false); c.QuadraticBezierTo(new Point(3.6, -2.4), new Point(5.2, -0.4), true, false); });

    // Cat Head & Ears (in Head Space)
    private static readonly Point CatPivotL = new Point(-4.8, -3.8);
    private static readonly Point CatPivotR = new Point(4.8, -3.8);
    private static readonly StreamGeometry CatEarL = Geo(c =>
    {
        c.BeginFigure(new Point(-7.5, -3.5), true, true);
        c.QuadraticBezierTo(new Point(-8.2, -8.2), new Point(-5.8, -12.8), true, false);
        c.QuadraticBezierTo(new Point(-3.2, -8.0), new Point(-1.8, -4.0), true, false);
    });
    private static readonly StreamGeometry CatEarInnerL = Geo(c =>
    {
        c.BeginFigure(new Point(-6.5, -4.0), true, true);
        c.LineTo(new Point(-5.6, -10.6), true, false);
        c.LineTo(new Point(-3.1, -4.2), true, false);
    });
    private static readonly StreamGeometry CatEarR = Geo(c =>
    {
        c.BeginFigure(new Point(1.8, -4.0), true, true);
        c.QuadraticBezierTo(new Point(3.2, -8.0), new Point(5.8, -12.8), true, false);
        c.QuadraticBezierTo(new Point(8.2, -8.2), new Point(7.5, -3.5), true, false);
    });
    private static readonly StreamGeometry CatEarInnerR = Geo(c =>
    {
        c.BeginFigure(new Point(3.1, -4.2), true, true);
        c.LineTo(new Point(5.6, -10.6), true, false);
        c.LineTo(new Point(6.5, -4.0), true, false);
    });
    private static readonly StreamGeometry CatHeadGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-8.0, 0.8), true, true);
        c.QuadraticBezierTo(new Point(-9.9, 3.0), new Point(-8.6, 4.9), true, false);
        c.QuadraticBezierTo(new Point(-9.4, 6.1), new Point(-6.8, 7.2), true, false);
        c.QuadraticBezierTo(new Point(0.0, 8.6), new Point(6.8, 7.2), true, false);
        c.QuadraticBezierTo(new Point(9.4, 6.1), new Point(8.6, 4.9), true, false);
        c.QuadraticBezierTo(new Point(9.9, 3.0), new Point(8.0, 0.8), true, false);
        c.QuadraticBezierTo(new Point(0.0, -5.8), new Point(-8.0, 0.8), true, false);
    });
    private static readonly Rect CatHeadRect = new Rect(-10.0, -6.0, 20.0, 15.0);
    private static readonly StreamGeometry CatNoseGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-1.2, 0.4), true, true);
        c.LineTo(new Point(1.2, 0.4), true, false);
        c.LineTo(new Point(0.0, 1.8), true, false);
    });

    // Dog Head, Ears & Rump-Mounted Spiral Tail
    private static readonly Point DogPivotL = new Point(-6.2, -2.0);
    private static readonly Point DogPivotR = new Point(6.2, -2.0);
    private static readonly Point DogTailBase = new Point(4.6, 7.2);
    private static Point DogSpiral(double u)
    {
        double th = Math.PI * 0.65 - u * 1.25 * 2.0 * Math.PI;
        double r = 2.7 - 1.75 * u;
        return new Point(7.2 + r * Math.Cos(th), 4.3 + r * Math.Sin(th));
    }
    private static readonly Point DogTailTip = DogSpiral(1.0);
    private static readonly StreamGeometry DogTailGeo = Geo(c =>
    {
        c.BeginFigure(DogTailBase, false, false);
        for (int i = 0; i <= 20; i++) c.LineTo(DogSpiral(i / 20.0), true, true);
    });
    private static readonly StreamGeometry DogEarL = Geo(c =>
    {
        c.BeginFigure(new Point(-8.4, -1.0), true, true);
        c.LineTo(new Point(-7.8, -9.8), true, false);
        c.QuadraticBezierTo(new Point(-4.6, -8.6), new Point(-1.8, -5.2), true, false);
    });
    private static readonly StreamGeometry DogEarInnerL = Geo(c =>
    {
        c.BeginFigure(new Point(-7.0, -2.6), true, true);
        c.LineTo(new Point(-7.0, -7.5), true, false);
        c.QuadraticBezierTo(new Point(-5.0, -6.8), new Point(-3.4, -4.6), true, false);
    });
    private static readonly StreamGeometry DogEarR = Geo(c =>
    {
        c.BeginFigure(new Point(8.4, -1.0), true, true);
        c.LineTo(new Point(7.8, -9.8), true, false);
        c.QuadraticBezierTo(new Point(4.6, -8.6), new Point(1.8, -5.2), true, false);
    });
    private static readonly StreamGeometry DogEarInnerR = Geo(c =>
    {
        c.BeginFigure(new Point(7.0, -2.6), true, true);
        c.LineTo(new Point(7.0, -7.5), true, false);
        c.QuadraticBezierTo(new Point(5.0, -6.8), new Point(3.4, -4.6), true, false);
    });
    private static readonly EllipseGeometry DogHeadGeo = F(new EllipseGeometry(new Point(0, 0.8), 8.6, 7.1));
    private static readonly Rect DogHeadRect = new Rect(-8.6, -6.4, 17.2, 14.4);
    private static readonly StreamGeometry DogUrajiroGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-6.8, 1.2), true, true);
        c.QuadraticBezierTo(new Point(-8.2, 4.5), new Point(-4.8, 7.0), true, false);
        c.QuadraticBezierTo(new Point(0.0, 8.4), new Point(4.8, 7.0), true, false);
        c.QuadraticBezierTo(new Point(8.2, 4.5), new Point(6.8, 1.2), true, false);
        c.QuadraticBezierTo(new Point(3.5, 0.5), new Point(0.0, 1.8), true, false);
        c.QuadraticBezierTo(new Point(-3.5, 0.5), new Point(-6.8, 1.2), true, false);
    });

    // Bunny Head & Long Ears (in Head Space)
    private static readonly Point BunnyPivotL = new Point(-3.2, -3.6);
    private static readonly Point BunnyPivotR = new Point(3.2, -3.6);
    private static readonly StreamGeometry BunnyEarL = Geo(c =>
    {
        c.BeginFigure(new Point(-5.4, -3.5), true, true);
        c.QuadraticBezierTo(new Point(-7.6, -10.8), new Point(-4.2, -14.8), true, false);
        c.QuadraticBezierTo(new Point(-1.4, -12.8), new Point(-1.0, -3.8), true, false);
    });
    private static readonly StreamGeometry BunnyEarInnerL = Geo(c =>
    {
        c.BeginFigure(new Point(-4.7, -3.8), true, true);
        c.QuadraticBezierTo(new Point(-6.2, -9.6), new Point(-3.8, -13.0), true, false);
        c.QuadraticBezierTo(new Point(-1.8, -11.0), new Point(-1.8, -4.0), true, false);
    });
    private static readonly StreamGeometry BunnyEarR = Geo(c =>
    {
        c.BeginFigure(new Point(1.0, -3.8), true, true);
        c.QuadraticBezierTo(new Point(1.4, -12.8), new Point(4.2, -14.8), true, false);
        c.QuadraticBezierTo(new Point(7.6, -10.8), new Point(5.4, -3.5), true, false);
    });
    private static readonly StreamGeometry BunnyEarInnerR = Geo(c =>
    {
        c.BeginFigure(new Point(1.8, -4.0), true, true);
        c.QuadraticBezierTo(new Point(1.8, -11.0), new Point(3.8, -13.0), true, false);
        c.QuadraticBezierTo(new Point(6.2, -9.6), new Point(4.7, -3.8), true, false);
    });
    private static readonly EllipseGeometry BunnyHeadGeo = F(new EllipseGeometry(new Point(0, 0.6), 8.5, 7.1));
    private static readonly Rect BunnyHeadRect = new Rect(-8.5, -6.5, 17.0, 14.2);

    // \U0001F98A Fox Head, Ears & Bushy Tail Geometries
    private static readonly Point FoxPivotL = new Point(-5.2, -3.5);
    private static readonly Point FoxPivotR = new Point(5.2, -3.5);
    private static readonly Point FoxTailBase = new Point(4.4, 7.4);
    private static readonly StreamGeometry FoxTailGeo = Geo(c =>
    {
        c.BeginFigure(new Point(4.2, 7.8), true, true);
        c.BezierTo(new Point(7.8, 8.8), new Point(12.2, 6.2), new Point(12.6, 1.5), true, false);
        c.BezierTo(new Point(10.8, 0.2), new Point(6.6, 2.5), new Point(4.2, 6.4), true, false);
    });
    private static readonly StreamGeometry FoxTailTipGeo = Geo(c =>
    {
        c.BeginFigure(new Point(10.4, 4.4), true, true);
        c.BezierTo(new Point(12.0, 3.8), new Point(12.6, 2.5), new Point(12.6, 1.5), true, false);
        c.BezierTo(new Point(11.4, 0.8), new Point(9.8, 1.6), new Point(9.4, 3.1), true, false);
    });
    private static readonly StreamGeometry FoxEarL = Geo(c =>
    {
        c.BeginFigure(new Point(-8.2, -2.8), true, true);
        c.QuadraticBezierTo(new Point(-9.6, -9.2), new Point(-6.4, -14.2), true, false);
        c.QuadraticBezierTo(new Point(-3.0, -9.2), new Point(-1.6, -4.0), true, false);
    });
    private static readonly StreamGeometry FoxEarInnerL = Geo(c =>
    {
        c.BeginFigure(new Point(-7.0, -3.6), true, true);
        c.LineTo(new Point(-6.2, -11.6), true, false);
        c.LineTo(new Point(-3.0, -4.2), true, false);
    });
    private static readonly StreamGeometry FoxEarR = Geo(c =>
    {
        c.BeginFigure(new Point(1.6, -4.0), true, true);
        c.QuadraticBezierTo(new Point(3.0, -9.2), new Point(6.4, -14.2), true, false);
        c.QuadraticBezierTo(new Point(9.6, -9.2), new Point(8.2, -2.8), true, false);
    });
    private static readonly StreamGeometry FoxEarInnerR = Geo(c =>
    {
        c.BeginFigure(new Point(3.0, -4.2), true, true);
        c.LineTo(new Point(6.2, -11.6), true, false);
        c.LineTo(new Point(7.0, -3.6), true, false);
    });
    private static readonly StreamGeometry FoxHeadGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-8.2, 0.2), true, true);
        c.QuadraticBezierTo(new Point(-11.2, 2.8), new Point(-9.5, 4.8), true, false);
        c.QuadraticBezierTo(new Point(-6.8, 7.6), new Point(0.0, 8.6), true, false);
        c.QuadraticBezierTo(new Point(6.8, 7.6), new Point(9.5, 4.8), true, false);
        c.QuadraticBezierTo(new Point(11.2, 2.8), new Point(8.2, 0.2), true, false);
        c.QuadraticBezierTo(new Point(0.0, -6.2), new Point(-8.2, 0.2), true, false);
    });
    private static readonly StreamGeometry FoxMaskGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-9.2, 3.6), true, true);
        c.QuadraticBezierTo(new Point(-6.5, 7.5), new Point(0.0, 8.5), true, false);
        c.QuadraticBezierTo(new Point(6.5, 7.5), new Point(9.2, 3.6), true, false);
        c.QuadraticBezierTo(new Point(4.6, 1.2), new Point(1.2, 2.2), true, false);
        c.LineTo(new Point(0.0, 0.2), true, false);
        c.LineTo(new Point(-1.2, 2.2), true, false);
        c.QuadraticBezierTo(new Point(-4.6, 1.2), new Point(-9.2, 3.6), true, false);
    });

    // \U0001F43C Panda Eye Patches & Shoulder Saddle
    private static readonly Point PandaPivotL = new Point(-5.8, -4.6);
    private static readonly Point PandaPivotR = new Point(5.8, -4.6);
    private static readonly StreamGeometry PandaSaddleGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-4.3, 0.8), true, true);
        c.QuadraticBezierTo(new Point(-5.6, 2.4), new Point(-5.5, 4.2), true, false);
        c.QuadraticBezierTo(new Point(0.0, 5.2), new Point(5.5, 4.2), true, false);
        c.QuadraticBezierTo(new Point(5.6, 2.4), new Point(4.3, 0.8), true, false);
        c.QuadraticBezierTo(new Point(0.0, 0.1), new Point(-4.3, 0.8), true, false);
    });
    private static readonly StreamGeometry PandaPatchLGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-6.2, -2.6), true, true);
        c.BezierTo(new Point(-7.2, -0.4), new Point(-5.8, 2.2), new Point(-3.2, 1.6), true, false);
        c.BezierTo(new Point(-1.4, 1.0), new Point(-1.4, -2.0), new Point(-3.4, -3.0), true, false);
        c.QuadraticBezierTo(new Point(-5.2, -3.6), new Point(-6.2, -2.6), true, false);
    });
    private static readonly StreamGeometry PandaPatchRGeo = Geo(c =>
    {
        c.BeginFigure(new Point(6.2, -2.6), true, true);
        c.BezierTo(new Point(7.2, -0.4), new Point(5.8, 2.2), new Point(3.2, 1.6), true, false);
        c.BezierTo(new Point(1.4, 1.0), new Point(1.4, -2.0), new Point(3.4, -3.0), true, false);
        c.QuadraticBezierTo(new Point(5.2, -3.6), new Point(6.2, -2.6), true, false);
    });

    // \U0001F432 Dragon Wings, Horns & Spade Tail
    private static readonly Point DragonWingPivotL = new Point(-3.8, 2.8);
    private static readonly Point DragonWingPivotR = new Point(3.8, 2.8);
    private static readonly StreamGeometry DragonWingLGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-3.6, 2.8), true, true);
        c.QuadraticBezierTo(new Point(-8.5, -1.8), new Point(-11.8, 0.6), true, false);
        c.QuadraticBezierTo(new Point(-9.2, 2.6), new Point(-10.2, 5.2), true, false);
        c.QuadraticBezierTo(new Point(-7.2, 4.6), new Point(-4.2, 5.8), true, false);
    });
    private static readonly StreamGeometry DragonWingRGeo = Geo(c =>
    {
        c.BeginFigure(new Point(3.6, 2.8), true, true);
        c.QuadraticBezierTo(new Point(8.5, -1.8), new Point(11.8, 0.6), true, false);
        c.QuadraticBezierTo(new Point(9.2, 2.6), new Point(10.2, 5.2), true, false);
        c.QuadraticBezierTo(new Point(7.2, 4.6), new Point(4.2, 5.8), true, false);
    });
    private static readonly StreamGeometry DragonHornLGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-4.8, -3.8), true, true);
        c.QuadraticBezierTo(new Point(-7.8, -8.5), new Point(-4.6, -12.6), true, false);
        c.QuadraticBezierTo(new Point(-3.8, -8.2), new Point(-2.0, -4.4), true, false);
    });
    private static readonly StreamGeometry DragonHornRGeo = Geo(c =>
    {
        c.BeginFigure(new Point(2.0, -4.4), true, true);
        c.QuadraticBezierTo(new Point(3.8, -8.2), new Point(4.6, -12.6), true, false);
        c.QuadraticBezierTo(new Point(7.8, -8.5), new Point(4.8, -3.8), true, false);
    });
    private static readonly StreamGeometry DragonFrillLGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-7.2, -1.8), true, true);
        c.QuadraticBezierTo(new Point(-11.2, -4.5), new Point(-10.6, -0.5), true, false);
        c.QuadraticBezierTo(new Point(-10.2, 1.8), new Point(-7.6, 1.4), true, false);
    });
    private static readonly StreamGeometry DragonFrillRGeo = Geo(c =>
    {
        c.BeginFigure(new Point(7.2, -1.8), true, true);
        c.QuadraticBezierTo(new Point(11.2, -4.5), new Point(10.6, -0.5), true, false);
        c.QuadraticBezierTo(new Point(10.2, 1.8), new Point(7.6, 1.4), true, false);
    });
    private static readonly StreamGeometry DragonTailSpadeGeo = Geo(c =>
    {
        c.BeginFigure(new Point(0, -1.8), true, true);
        c.LineTo(new Point(1.6, 0.2), true, false);
        c.LineTo(new Point(0, 1.6), true, false);
        c.LineTo(new Point(-1.6, 0.2), true, false);
    });

    // Wearable Accessories Geometries (in Head Space)
    private static readonly StreamGeometry CrownGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-3.8, -4.8), true, true);
        c.LineTo(new Point(-4.4, -9.2), true, false);
        c.LineTo(new Point(-1.8, -6.8), true, false);
        c.LineTo(new Point(0.0, -10.2), true, false);
        c.LineTo(new Point(1.8, -6.8), true, false);
        c.LineTo(new Point(4.4, -9.2), true, false);
        c.LineTo(new Point(3.8, -4.8), true, false);
    });
    private static readonly StreamGeometry WizardHatGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-6.8, -4.4), true, true);
        c.QuadraticBezierTo(new Point(0.0, -3.4), new Point(6.8, -4.4), true, false);
        c.LineTo(new Point(4.2, -5.6), true, false);
        c.QuadraticBezierTo(new Point(2.2, -9.8), new Point(2.8, -13.8), true, false);
        c.QuadraticBezierTo(new Point(-1.5, -11.2), new Point(-4.2, -5.6), true, false);
    });
    private static readonly StreamGeometry HeadphoneBandGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-8.8, 0.2), false, false);
        c.BezierTo(new Point(-9.2, -7.8), new Point(9.2, -7.8), new Point(8.8, 0.2), true, false);
    });
    private static readonly Pen HeadphoneBandPen = MakePen(HeadphoneBandBrush, 1.55);
    private static readonly StreamGeometry SunglassesLGeo = Geo(c =>
    {
        c.BeginFigure(new Point(-6.6, -2.5), true, true);
        c.LineTo(new Point(-1.0, -2.3), true, false);
        c.LineTo(new Point(-1.4, 0.6), true, false);
        c.QuadraticBezierTo(new Point(-4.0, 1.2), new Point(-6.2, 0.4), true, false);
    });
    private static readonly StreamGeometry SunglassesRGeo = Geo(c =>
    {
        c.BeginFigure(new Point(1.0, -2.3), true, true);
        c.LineTo(new Point(6.6, -2.5), true, false);
        c.LineTo(new Point(6.2, 0.4), true, false);
        c.QuadraticBezierTo(new Point(4.0, 1.2), new Point(1.4, 0.6), true, false);
    });
    private static readonly StreamGeometry SproutLeafLGeo = Geo(c =>
    {
        c.BeginFigure(new Point(0, -8.5), true, true);
        c.QuadraticBezierTo(new Point(-3.6, -11.2), new Point(-4.2, -8.4), true, false);
        c.QuadraticBezierTo(new Point(-2.2, -6.8), new Point(0, -8.5), true, false);
    });
    private static readonly StreamGeometry SproutLeafRGeo = Geo(c =>
    {
        c.BeginFigure(new Point(0, -8.5), true, true);
        c.QuadraticBezierTo(new Point(3.6, -11.2), new Point(4.2, -8.4), true, false);
        c.QuadraticBezierTo(new Point(2.2, -6.8), new Point(0, -8.5), true, false);
    });

    // Unit particle shapes (positioned with a matrix)
    private static readonly StreamGeometry HeartGeo = Geo(c =>
    {
        c.BeginFigure(new Point(0, 0.7), true, true);
        c.BezierTo(new Point(-1.0, 0), new Point(-0.8, -0.7), new Point(0, -0.2), true, false);
        c.BezierTo(new Point(0.8, -0.7), new Point(1.0, 0), new Point(0, 0.7), true, false);
    });
    private static readonly StreamGeometry StarGeo = Geo(c =>
    {
        c.BeginFigure(new Point(0, -1), true, true);
        c.QuadraticBezierTo(new Point(0, 0), new Point(1, 0), true, false);
        c.QuadraticBezierTo(new Point(0, 0), new Point(0, 1), true, false);
        c.QuadraticBezierTo(new Point(0, 0), new Point(-1, 0), true, false);
        c.QuadraticBezierTo(new Point(0, 0), new Point(0, -1), true, false);
    });
    private static readonly StreamGeometry DropGeo = Geo(c =>
    {
        c.BeginFigure(new Point(0, -4.2), true, true);
        c.QuadraticBezierTo(new Point(-2.5, 0), new Point(-2.5, 1.8), true, false);
        c.ArcTo(new Point(2.5, 1.8), new Size(2.5, 2.5), 0, false, SweepDirection.Clockwise, true, false);
        c.QuadraticBezierTo(new Point(2.5, 0), new Point(0, -4.2), true, false);
    });

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Runtime state
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private double _time, _lastTime, _lastInvalidatedTime;
    private float _bass, _treble;
    private bool _isNearCursor;
    private double _gazeX, _gazeY, _targetGazeX, _targetGazeY;

    // Squash-and-stretch spring & acrobatic trick flip
    private double _squashOffset, _squashVelocity;
    private bool _isDoingTrick;
    private double _trickStartTime;

    public bool IsAnimatingTrick => _isDoingTrick;

    // Ear & secondary head motion state
    private double _earAng, _earVel, _earTwitch, _earFlick;
    private double _prevOffY, _bodyVelY;
    private double _headBobY, _headTilt, _headShiftX;

    // Pupil dilation 0..1
    private double _dilate = 0.3;

    private readonly Point[] _tail = new Point[CatTailSegs + 1];

    public VirtualPetControl()
    {
        Width = 26;
        Height = 22;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
        ClipToBounds = false;
        RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsNaN(Width) || Width <= 0 ? 26 : Width;
        double h = double.IsNaN(Height) || Height <= 0 ? 22 : Height;
        return new Size(w, h);
    }

    public void PetClickImpulse()
    {
        _squashVelocity = -0.34;
        InvalidateVisual();
    }

    public void TriggerTrick()
    {
        _isDoingTrick = true;
        _trickStartTime = _time;
        _squashVelocity = -0.45;
        InvalidateVisual();
    }

    public void Dance(float bass, float treble, double t)
    {
        _bass = bass;
        _treble = treble;
        _time = t;
        InvalidateVisual();
    }

    public void UpdateTick(double t, bool isNearCursor, double gazeX = 0.0, double gazeY = 0.0)
    {
        _time = t;
        _isNearCursor = isNearCursor;
        _targetGazeX = isNearCursor ? Math.Clamp(gazeX, -1.0, 1.0) : 0.0;
        _targetGazeY = isNearCursor ? Math.Clamp(gazeY, -1.0, 1.0) : 0.0;
        _bass = 0f;
        _treble = 0f;

        // Frame-pace slow sleeping breathing to ~15 FPS when undisturbed, keeping full refresh rate for active moods
        if (Mood == PetMood.Sleeping && !isNearCursor && !IsScampering && !IsPeeking && !_isDoingTrick &&
            Math.Abs(_squashOffset) < 0.002 && Math.Abs(_squashVelocity) < 0.002)
        {
            if (t - _lastInvalidatedTime < 0.065)
                return;
        }

        _lastInvalidatedTime = t;
        InvalidateVisual();
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Physics
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private void StepSquash(double dt)
    {
        const double k = 42.0, c = 8.5;
        int n = Math.Max(1, (int)Math.Ceiling(dt * 120.0));
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            _squashVelocity += (-k * _squashOffset - c * _squashVelocity) * h;
            _squashOffset += _squashVelocity * h;
        }
        if (Math.Abs(_squashOffset) < 0.001 && Math.Abs(_squashVelocity) < 0.001)
        {
            _squashOffset = 0.0;
            _squashVelocity = 0.0;
        }
    }

    private void StepEars(double dt, double target)
    {
        const double k = 180.0, c = 14.0;
        int n = Math.Max(1, (int)Math.Ceiling(dt * 120.0));
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            _earVel += (-k * (_earAng - target) - c * _earVel) * h;
            _earAng += _earVel * h;
        }
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Render
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double t = _time;
        double dt = _lastTime > 0 ? Math.Clamp(t - _lastTime, 0.001, 0.1) : 0.016;
        _lastTime = t;

        var mood = Mood;
        string species = Species ?? "Cat";
        bool bunny = species == "Bunny";

        StepSquash(dt);

        double gk = Math.Min(1.0, dt * 10.0);
        _gazeX += (_targetGazeX - _gazeX) * gk;
        _gazeY += (_targetGazeY - _gazeY) * gk;

        double dilTarget = _isNearCursor ? 1.0 : (mood == PetMood.Happy ? 0.75 : (mood == PetMood.Sleeping ? 0.0 : 0.32));
        _dilate += (dilTarget - _dilate) * Math.Min(1.0, dt * 8.0);

        // Virtual 32 x 28 canvas -> control size
        double actualW = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 32.0);
        double actualH = ActualHeight > 0 ? ActualHeight : (Height > 0 ? Height : 28.0);
        double scale = Math.Min(actualW / 32.0, actualH / 28.0);
        double offsetX = (actualW - 32.0 * scale) / 2.0;
        double offsetY = (actualH - 28.0 * scale) / 2.0;

        const double cx = 16.0, cy = 15.6;

        bool scampering = IsScampering;
        bool peeking = IsPeeking;

        // Mood choreography
        double scaleY = 1.0 + _squashOffset;
        double scaleX = 1.0 - (_squashOffset * 0.65);
        double petOffsetY = 0.0;
        double rotateAngle = 0.0;

        if (scampering)
        {
            double gallop = Math.Sin(t * 24.0);
            petOffsetY = -Math.Abs(gallop) * 1.8;
            scaleY += 0.06 * (1.0 - Math.Abs(gallop));
            scaleX -= 0.04 * (1.0 - Math.Abs(gallop));
            rotateAngle = 6.0 + gallop * 2.8;
        }
        else if (peeking)
        {
            petOffsetY = 1.6 + Math.Sin(t * 2.5) * 0.4;
            scaleY -= 0.05;
            scaleX += 0.05;
            rotateAngle = -3.5 + Math.Sin(t * 3.0) * 1.5;
        }
        else
        {
            switch (mood)
            {
                case PetMood.Sleeping:
                {
                    double breath = Math.Sin(t * 2.1);
                    scaleY += 0.045 * breath;
                    scaleX -= 0.035 * breath;
                    petOffsetY += 0.3;
                    break;
                }
                case PetMood.Dancing:
                {
                    double beat = Math.Sin(t * 11.0);
                    petOffsetY = -Math.Abs(beat) * (2.0 + _bass * 5.0);
                    scaleY += -(_bass * 0.22) + 0.07 * beat;
                    scaleX += (_bass * 0.15) - 0.05 * beat;
                    rotateAngle = Math.Sin(t * 9.0) * (7.0 + _treble * 14.0);
                    break;
                }
                case PetMood.Happy:
                {
                    double hop = Math.Abs(Math.Sin(t * 14.0));
                    petOffsetY = -hop * 3.6;
                    rotateAngle = Math.Sin(t * 12.0) * 7.5;
                    scaleY += 0.08 * (1.0 - hop);
                    scaleX += -0.05 * (1.0 - hop);
                    break;
                }
                case PetMood.Awake:
                {
                    rotateAngle = Math.Sin(t * 1.8) * 2.2;
                    scaleY += 0.02 * Math.Sin(t * 3.0);
                    if (_isNearCursor) { petOffsetY = -0.8; scaleY += 0.035; }
                    break;
                }
                case PetMood.Thirsty:
                {
                    petOffsetY = 0.4 + Math.Sin(t * 3.5) * 0.6;
                    rotateAngle = Math.Sin(t * 2.5) * 2.5;
                    break;
                }
                case PetMood.Stretch:
                {
                    petOffsetY = -1.2;
                    scaleY += 0.14;
                    scaleX -= 0.08;
                    break;
                }
                case PetMood.Eating:
                {
                    double munch = Math.Abs(Math.Sin(t * 18.0));
                    petOffsetY = -munch * 1.4;
                    scaleY += 0.05 * (1.0 - munch);
                    scaleX += 0.04 * munch;
                    rotateAngle = Math.Sin(t * 9.0) * 3.5;
                    break;
                }
            }
        }

        // Acrobatic 360-degree celebratory backflip / spin jump
        if (_isDoingTrick)
        {
            double trickElapsed = t - _trickStartTime;
            const double TrickDuration = 0.65;
            if (trickElapsed >= 0.0 && trickElapsed < TrickDuration)
            {
                double p = trickElapsed / TrickDuration;
                double arc = Math.Sin(p * Math.PI);
                petOffsetY -= arc * 6.2;
                rotateAngle += p * 360.0;
            }
            else
            {
                _isDoingTrick = false;
            }
        }

        // Secondary velocity & ear spring
        double vy = (petOffsetY - _prevOffY) / dt;
        _prevOffY = petOffsetY;
        _bodyVelY += (vy - _bodyVelY) * Math.Min(1.0, dt * 25.0);

        double droop = 0.0;
        if (scampering) droop = -14.0;
        else if (peeking) droop = 6.0;
        else if (mood == PetMood.Sleeping) droop = bunny ? 30.0 : 10.0;
        else if (mood == PetMood.Thirsty) droop = bunny ? 20.0 : 12.0;
        else if (mood == PetMood.Stretch) droop = -6.0;
        else if (mood == PetMood.Awake && _isNearCursor) droop = -5.0;

        StepEars(dt, droop + Math.Clamp(-_bodyVelY * 0.22, -24.0, 24.0));

        _earTwitch = mood switch
        {
            PetMood.Dancing => Math.Sin(t * 12.0) * (5.0 + _treble * 10.0),
            PetMood.Happy => Math.Sin(t * 14.0) * 4.0,
            _ => bunny ? Math.Sin(t * 2.2) * 2.0 : 0.0
        };

        double fp = t % 6.3;
        _earFlick = (mood == PetMood.Awake || mood == PetMood.Thirsty) && fp < 0.3
            ? Math.Sin(fp / 0.3 * Math.PI * 3.0) * 10.0 * (1.0 - fp / 0.3)
            : 0.0;

        // Articulated head secondary motion (head lags slightly behind torso and tracks gaze)
        _headBobY = Math.Clamp(-_bodyVelY * 0.032, -1.1, 1.1) + Math.Sin(t * 3.0 - 0.6) * 0.22;
        if (scampering)
        {
            _headBobY = Math.Sin(t * 24.0) * 0.55;
            _headTilt = rotateAngle * 0.45;
            _headShiftX = 0.4;
        }
        else if (peeking)
        {
            _headBobY = 1.0;
            _headTilt = 7.5 + Math.Sin(t * 3.0) * 2.0;
            _headShiftX = Math.Sin(t * 2.0) * 0.5;
            _targetGazeY = 0.95;
            _targetGazeX = Math.Sin(t * 1.8) * 0.5;
            _dilate = 0.92;
        }
        else if (mood == PetMood.Sleeping)
        {
            _headBobY = 2.2 + Math.Sin(t * 2.1 - 0.4) * 0.25;
            _headTilt = -9.0 + Math.Sin(t * 2.1) * 1.5;
            _headShiftX = -0.5;
        }
        else
        {
            _headTilt = rotateAngle * 0.5 + _gazeX * 8.5 + (_isNearCursor ? Math.Sin(t * 4.0) * 2.0 : 0.0);
            _headShiftX = _gazeX * 0.85;
            if (mood == PetMood.Stretch) _headBobY -= 0.7;
            else if (mood == PetMood.Thirsty) _headBobY += 0.65;
        }

        // â”€â”€ Draw â”€â”€
        Push(dc, new Matrix(scale, 0, 0, scale, offsetX, offsetY));

        // Mood glows
        if (mood == PetMood.Dancing)
        {
            dc.PushOpacity(Math.Clamp(0.35 + _bass * 0.9 + 0.15 * Math.Sin(t * 11.0), 0.0, 1.0));
            dc.DrawEllipse(DanceGlowBrush, null, new Point(cx, cy + 1.5), 15.0 + _bass * 5.0, 13.0 + _bass * 4.0);
            dc.Pop();
        }
        else if (mood == PetMood.Happy)
        {
            dc.PushOpacity(0.5 + 0.2 * Math.Sin(t * 7.0));
            dc.DrawEllipse(HappyGlowBrush, null, new Point(cx, cy + 1.5), 14.0, 12.0);
            dc.Pop();
        }

        // Contact shadow shrinks / fades while airborne
        double lift = Math.Clamp(-petOffsetY / 5.5, 0.0, 1.0);
        dc.PushOpacity(1.0 - 0.5 * lift);
        dc.DrawEllipse(ContactShadowBrush, null, new Point(cx, 25.2), 9.6 * scaleX * (1.0 - 0.22 * lift), 2.3);
        dc.Pop();

        // Body transform: squash & rotate about the FEET so the pet stays planted
        var m = Matrix.Identity;
        m.Translate(0.0, -FootY);
        m.Scale(scaleX, scaleY);
        m.Rotate(rotateAngle);
        m.Translate(cx, cy + petOffsetY + FootY);
        Push(dc, m);

        switch (species)
        {
            case "Dog": RenderDog(dc, mood, t); break;
            case "Bunny": RenderBunny(dc, mood, t); break;
            case "Fox": RenderFox(dc, mood, t); break;
            case "Panda": RenderPanda(dc, mood, t); break;
            case "Dragon": RenderDragon(dc, mood, t); break;
            default: RenderCat(dc, mood, t); break;
        }

        dc.Pop(); // body

        RenderParticles(dc, mood, t, cx, cy, scampering, peeking);

        dc.Pop(); // root
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Shared anatomical helpers
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private static void DrawEar(DrawingContext dc, Geometry outer, Geometry inner, Brush outerBrush, Pen outerPen,
        Brush innerBrush, Point pivot, double angle)
    {
        bool rot = Math.Abs(angle) > 0.05;
        if (rot)
        {
            var m = Matrix.Identity;
            m.RotateAt(angle, pivot.X, pivot.Y);
            Push(dc, m);
        }
        dc.DrawGeometry(outerBrush, outerPen, outer);
        dc.DrawGeometry(innerBrush, null, inner);
        if (rot) dc.Pop();
    }

    private static void DrawBlush(DrawingContext dc, double s)
    {
        dc.DrawEllipse(BlushBrush, null, new Point(-5.3, 2.4), 2.2 * s, 1.5 * s);
        dc.DrawEllipse(BlushBrush, null, new Point(5.3, 2.4), 2.2 * s, 1.5 * s);
    }

    private static void ShadeHead(DrawingContext dc, Geometry clip, Rect rect)
    {
        dc.PushClip(clip);
        dc.DrawRectangle(HeadShadeBrush, null, rect);
        dc.Pop();
    }

    private static void ShadeTorso(DrawingContext dc, Geometry clip)
    {
        dc.PushClip(clip);
        dc.DrawRectangle(BodyShadeBrush, null, TorsoRect);
        dc.Pop();
    }

    private void PushHeadTransform(DrawingContext dc)
    {
        var hm = Matrix.Identity;
        hm.Scale(HeadScale, HeadScale);
        hm.RotateAt(_headTilt, 0.0, 3.8); // pivot near base of skull / neck
        hm.Translate(_headShiftX, -4.1 + _headBobY);
        Push(dc, hm);
    }

    private static void ComputePawOffsets(PetMood mood, double t, bool isScampering, bool isPeeking,
        out double liftL, out double liftR, out double spreadX)
    {
        liftL = 0.0;
        liftR = 0.0;
        spreadX = 0.0;
        if (isScampering)
        {
            double s = Math.Sin(t * 24.0);
            liftL = s * 2.4;
            liftR = -s * 2.4;
            spreadX = 0.55;
            return;
        }
        if (isPeeking)
        {
            liftL = 0.35;
            liftR = 0.35;
            spreadX = 0.68;
            return;
        }
        if (mood == PetMood.Happy)
        {
            double s = Math.Sin(t * 14.0);
            liftL = Math.Max(0.0, s) * -1.8;
            liftR = Math.Max(0.0, -s) * -1.8;
            spreadX = 0.35;
        }
        else if (mood == PetMood.Dancing)
        {
            double s = Math.Sin(t * 11.0);
            liftL = Math.Max(0.0, s) * -2.2;
            liftR = Math.Max(0.0, -s) * -2.2;
            spreadX = 0.45;
        }
        else if (mood == PetMood.Stretch)
        {
            liftL = 0.4;
            liftR = 0.4;
            spreadX = 0.5;
        }
        else if (mood == PetMood.Eating)
        {
            double munch = Math.Sin(t * 18.0) * 0.35;
            liftL = -2.2 + munch;
            liftR = -2.2 - munch;
            spreadX = -0.55; // paws brought inward together to hold the treat
        }
    }

    private static void DrawFrontLegsAndPaws(DrawingContext dc, PetMood mood, double t,
        Brush legBrush, Brush pawBrush, Pen outlinePen, Pen toePen, bool showToeBeans,
        bool isScampering = false, bool isPeeking = false)
    {
        if (mood == PetMood.Sleeping && !isScampering && !isPeeking)
        {
            // Loaf pose: front paws tucked neatly side-by-side under the chin
            dc.DrawEllipse(pawBrush, outlinePen, new Point(-2.1, 8.8), 1.85, 1.15);
            dc.DrawEllipse(pawBrush, outlinePen, new Point(2.1, 8.8), 1.85, 1.15);
            dc.DrawLine(toePen, new Point(-2.5, 8.3), new Point(-2.5, 9.3));
            dc.DrawLine(toePen, new Point(-1.7, 8.3), new Point(-1.7, 9.3));
            dc.DrawLine(toePen, new Point(1.7, 8.3), new Point(1.7, 9.3));
            dc.DrawLine(toePen, new Point(2.5, 8.3), new Point(2.5, 9.3));
            return;
        }

        ComputePawOffsets(mood, t, isScampering, isPeeking, out double liftL, out double liftR, out double spreadX);

        // Left foreleg & paw
        Push(dc, new Matrix(1, 0, 0, 1, -spreadX, liftL * 0.45));
        dc.DrawGeometry(legBrush, outlinePen, FrontLegLGeo);
        dc.Pop();

        double pxL = -2.55 - spreadX;
        double pyL = 8.9 + liftL;
        dc.DrawEllipse(pawBrush, outlinePen, new Point(pxL, pyL), 1.75, 1.15);
        if (showToeBeans && liftL < -0.6)
        {
            dc.DrawEllipse(PawPadBrush, null, new Point(pxL, pyL + 0.15), 0.75, 0.55);
        }
        else
        {
            dc.DrawLine(toePen, new Point(pxL - 0.5, pyL - 0.1), new Point(pxL - 0.5, pyL + 0.85));
            dc.DrawLine(toePen, new Point(pxL + 0.5, pyL - 0.1), new Point(pxL + 0.5, pyL + 0.85));
        }

        // Right foreleg & paw
        Push(dc, new Matrix(1, 0, 0, 1, spreadX, liftR * 0.45));
        dc.DrawGeometry(legBrush, outlinePen, FrontLegRGeo);
        dc.Pop();

        double pxR = 2.55 + spreadX;
        double pyR = 8.9 + liftR;
        dc.DrawEllipse(pawBrush, outlinePen, new Point(pxR, pyR), 1.75, 1.15);
        if (showToeBeans && liftR < -0.6)
        {
            dc.DrawEllipse(PawPadBrush, null, new Point(pxR, pyR + 0.15), 0.75, 0.55);
        }
        else
        {
            dc.DrawLine(toePen, new Point(pxR - 0.5, pyR - 0.1), new Point(pxR - 0.5, pyR + 0.85));
            dc.DrawLine(toePen, new Point(pxR + 0.5, pyR - 0.1), new Point(pxR + 0.5, pyR + 0.85));
        }

        if (mood == PetMood.Eating)
        {
            double bob = Math.Sin(t * 18.0) * 0.25;
            dc.DrawEllipse(TreatSalmonBrush, GoldBellOutlinePen, new Point(0.0, 6.2 + bob), 1.65, 1.15);
            dc.DrawEllipse(WhiteSpecularBrush, null, new Point(-0.5, 5.9 + bob), 0.45, 0.30);
        }
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // \U0001F431 Cat (Full Seated Body + Collar & Bell + Articulated Head)
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private void DrawCatTail(DrawingContext dc, PetMood mood, double t)
    {
        double freq = mood == PetMood.Happy ? 15.0 : (mood == PetMood.Dancing ? 12.0 : 3.0);
        double amp = mood == PetMood.Happy ? 28.0 : (mood == PetMood.Dancing ? 22.0 : (mood == PetMood.Sleeping ? 6.0 : 14.0));
        double phase = t * freq;
        const double segLen = 1.45;

        // Anchored at the lower right rump (4.6, 7.6), curving outward and up beside the hip
        double x = 4.6, y = 7.6;
        _tail[0] = new Point(x, y);
        for (int i = 0; i < CatTailSegs; i++)
        {
            double u = (i + 1.0) / CatTailSegs;
            double baseCurve = -28.0 - i * 8.5;
            double ang = baseCurve + amp * u * Math.Sin(phase - 0.55 * i);
            double rad = ang * Math.PI / 180.0;
            x += segLen * Math.Cos(rad);
            y += segLen * Math.Sin(rad);
            _tail[i + 1] = new Point(x, y);
        }

        for (int i = 0; i < CatTailSegs; i++) dc.DrawLine(CatTailDark[i], _tail[i], _tail[i + 1]);
        for (int i = 0; i < CatTailSegs; i++)
            dc.DrawLine(i % 3 == 2 ? CatTailBand[i] : CatTailMid[i], _tail[i], _tail[i + 1]);
        for (int i = 0; i < CatTailSegs; i++)
        {
            var a = _tail[i]; var b = _tail[i + 1];
            dc.DrawLine(CatTailHi[i], new Point(a.X - 0.3, a.Y - 0.25), new Point(b.X - 0.3, b.Y - 0.25));
        }
        dc.DrawEllipse(CatChestBrush, null, _tail[CatTailSegs], 1.25, 1.25);
    }

}
