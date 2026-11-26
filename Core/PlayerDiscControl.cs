using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DynamicIsland.Services;

namespace DynamicIsland.Core;

/// <summary>
/// Hardware-accelerated, zero-allocation Now Playing artwork surface supporting three styles:
/// <list type="bullet">
///   <item><c>VinylDisc</c>: Grooved obsidian LP platter + 66% center label + hollow spindle + stationary sheen + optional micro-tonearm.</item>
///   <item><c>OpticalDisc</c>: 94% full-bleed circular cover art + iridescent polycarbonate rim + frosted center spindle + stationary sheen.</item>
///   <item><c>StaticSquircle</c>: Classic Apple Dynamic Island static continuous squircle.</item>
/// </list>
/// </summary>
public sealed class PlayerDiscControl : Grid
{
    public static readonly DependencyProperty DiscSizeProperty =
        DependencyProperty.Register(nameof(DiscSize), typeof(double), typeof(PlayerDiscControl),
            new PropertyMetadata(52.0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ShowTonearmProperty =
        DependencyProperty.Register(nameof(ShowTonearm), typeof(bool), typeof(PlayerDiscControl),
            new PropertyMetadata(false, OnLayoutPropertyChanged));

    public static readonly DependencyProperty SquircleRadiusProperty =
        DependencyProperty.Register(nameof(SquircleRadius), typeof(double), typeof(PlayerDiscControl),
            new PropertyMetadata(18.0, OnLayoutPropertyChanged));

    public double DiscSize
    {
        get => (double)GetValue(DiscSizeProperty);
        set => SetValue(DiscSizeProperty, value);
    }

    public bool ShowTonearm
    {
        get => (bool)GetValue(ShowTonearmProperty);
        set => SetValue(ShowTonearmProperty, value);
    }

    public double SquircleRadius
    {
        get => (double)GetValue(SquircleRadiusProperty);
        set => SetValue(SquircleRadiusProperty, value);
    }

    // ── Frozen Shared Brushes & Pens (Zero GC / Render-Thread Safe) ─────
    private static readonly RadialGradientBrush VinylPlatterBrush = CreateFrozenVinylBrush();
    private static readonly LinearGradientBrush OpticalRimBrush = CreateFrozenOpticalRimBrush();
    private static readonly LinearGradientBrush SpecularSheenBrush = CreateFrozenSpecularBrush();
    private static readonly Brush GrooveStrokeOuter = ThemeBrushes.CreateFrozen(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
    private static readonly Brush GrooveStrokeMid = ThemeBrushes.CreateFrozen(Color.FromArgb(0x0E, 0xFF, 0xFF, 0xFF));
    private static readonly Brush GrooveStrokeInner = ThemeBrushes.CreateFrozen(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));
    private static readonly Brush GrooveArcBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF));
    private static readonly Brush TitaniumRimBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
    private static readonly Brush LabelBevelBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
    private static readonly Brush DarkLabelBgBrush = ThemeBrushes.CreateFrozen(Color.FromRgb(0x18, 0x19, 0x20));
    private static readonly Brush DamperRingFill = ThemeBrushes.CreateFrozen(Color.FromArgb(0x55, 0x0A, 0x0A, 0x0E));
    private static readonly Brush DamperRingStroke = ThemeBrushes.CreateFrozen(Color.FromArgb(0x32, 0xFF, 0xFF, 0xFF));
    private static readonly Brush SpindleHoleFill = ThemeBrushes.CreateFrozen(Color.FromRgb(0x07, 0x07, 0x0A));
    private static readonly Brush SpindleRimBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x4C, 0xFF, 0xFF, 0xFF));
    private static readonly Brush SquircleBgBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    private static readonly Brush SquircleBorderBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
    private static readonly Brush TonearmGimbalFill = ThemeBrushes.CreateFrozen(Color.FromRgb(0x24, 0x26, 0x2F));
    private static readonly Brush TonearmGimbalRim = ThemeBrushes.CreateFrozen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
    private static readonly Brush TonearmWandBrush = ThemeBrushes.CreateFrozen(Color.FromRgb(0xC8, 0xCB, 0xD4));
    private static readonly Brush TonearmHeadBrush = ThemeBrushes.CreateFrozen(Color.FromRgb(0x1E, 0x20, 0x28));
    private static readonly Brush DefaultGlyphBrush = ThemeBrushes.CreateFrozen(Color.FromRgb(0x86, 0x86, 0x8B));
    private static readonly FontFamily GlyphFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    // ── Visual Tree Elements ─────────────────────────────────────────────
    private readonly Ellipse _bassHalo = new() { IsHitTestVisible = false, Opacity = 0.0, Visibility = Visibility.Collapsed };
    private readonly Border _squircleHost = new() { ClipToBounds = true, Visibility = Visibility.Collapsed };
    private readonly Image _squircleImage = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _squircleFallback = CreateFallbackGlyph(16);

    private readonly Grid _discStage = new() { ClipToBounds = false };
    private readonly Grid _platterLayer = new() { RenderTransformOrigin = new Point(0.5, 0.5) };
    private readonly RotateTransform _platterRotate = new(0);

    private readonly Ellipse _vinylBase = new();
    private readonly Ellipse _grooveOuter = new() { IsHitTestVisible = false };
    private readonly Ellipse _grooveMid = new() { IsHitTestVisible = false };
    private readonly Ellipse _grooveInner = new() { IsHitTestVisible = false };
    private readonly Path _grooveMotionArc = new() { IsHitTestVisible = false, StrokeThickness = 0.85, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

    private readonly Ellipse _opticalRim = new() { IsHitTestVisible = false };
    private readonly Ellipse _coverCircle = new() { IsHitTestVisible = false };
    private readonly Ellipse _coverBevel = new() { IsHitTestVisible = false };
    private readonly TextBlock _discFallback = CreateFallbackGlyph(14);
    private readonly ImageBrush _coverBrush = new() { Stretch = Stretch.UniformToFill };

    private readonly Ellipse _specularSheen = new() { IsHitTestVisible = false };
    private readonly Ellipse _outerRim = new() { IsHitTestVisible = false };

    private readonly Grid _spindleHost = new() { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse _damperRing = new();
    private readonly Ellipse _spindleHole = new();

    private readonly Grid _tonearmHost = new() { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
    private readonly RotateTransform _tonearmRotate = new(0);
    private readonly Ellipse _tonearmGimbal = new();
    private readonly Path _tonearmArm = new() { StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly Border _tonearmHeadshell = new() { CornerRadius = new CornerRadius(1.2) };
    private readonly Ellipse _tonearmStylusDot = new() { Width = 2.4, Height = 2.4 };

    // ── State & Physics ──────────────────────────────────────────────────
    private string _style = "VinylDisc";
    private ImageSource? _artwork;
    private double _angle;
    private double _angularVelocity;
    private double _tonearmAngle;
    private double _haloOpacity;
    private bool _needsRebuild = true;

    public double Angle => _angle;
    public double AngularVelocity => _angularVelocity;
    public string CurrentStyle => _style;
    internal Border SquircleHost => _squircleHost;

    public bool IsSettled =>
        _style == "StaticSquircle" ||
        (_angularVelocity <= 0.15 && Math.Abs(_tonearmAngle) <= 0.15 && _haloOpacity <= 0.01);

    public PlayerDiscControl()
    {
        ClipToBounds = false;
        Background = Brushes.Transparent;
        RenderOptions.SetBitmapScalingMode(_squircleImage, BitmapScalingMode.HighQuality);
        RenderOptions.SetBitmapScalingMode(_coverBrush, BitmapScalingMode.HighQuality);

        _platterLayer.RenderTransform = _platterRotate;
        _tonearmHost.RenderTransform = _tonearmRotate;

        _bassHalo.SetResourceReference(Shape.FillProperty, "AccentBrush");
        _tonearmStylusDot.SetResourceReference(Shape.FillProperty, "AccentBrush");

        // Squircle static layer
        var squircleGrid = new Grid();
        squircleGrid.Children.Add(_squircleFallback);
        squircleGrid.Children.Add(_squircleImage);
        _squircleHost.Background = SquircleBgBrush;
        _squircleHost.BorderBrush = SquircleBorderBrush;
        _squircleHost.BorderThickness = new Thickness(1);
        _squircleHost.Child = squircleGrid;
        RoundedClip.SetClipChild(_squircleHost, true);

        // Rotating platter layer
        _vinylBase.Fill = VinylPlatterBrush;
        _grooveOuter.Stroke = GrooveStrokeOuter;
        _grooveOuter.StrokeThickness = 0.7;
        _grooveMid.Stroke = GrooveStrokeMid;
        _grooveMid.StrokeThickness = 0.7;
        _grooveInner.Stroke = GrooveStrokeInner;
        _grooveInner.StrokeThickness = 0.7;
        _grooveMotionArc.Stroke = GrooveArcBrush;

        _opticalRim.Stroke = OpticalRimBrush;
        _opticalRim.StrokeThickness = 1.4;
        _coverCircle.Fill = DarkLabelBgBrush;
        _coverBevel.Stroke = LabelBevelBrush;
        _coverBevel.StrokeThickness = 0.85;

        _platterLayer.Children.Add(_vinylBase);
        _platterLayer.Children.Add(_grooveOuter);
        _platterLayer.Children.Add(_grooveMid);
        _platterLayer.Children.Add(_grooveInner);
        _platterLayer.Children.Add(_grooveMotionArc);
        _platterLayer.Children.Add(_opticalRim);
        _platterLayer.Children.Add(_coverCircle);
        _platterLayer.Children.Add(_discFallback);
        _platterLayer.Children.Add(_coverBevel);

        // Stationary sheen & rim
        _specularSheen.Fill = SpecularSheenBrush;
        _outerRim.Stroke = TitaniumRimBrush;
        _outerRim.StrokeThickness = 1.0;

        // Center spindle
        _damperRing.Fill = DamperRingFill;
        _damperRing.Stroke = DamperRingStroke;
        _damperRing.StrokeThickness = 0.75;
        _spindleHole.Fill = SpindleHoleFill;
        _spindleHole.Stroke = SpindleRimBrush;
        _spindleHole.StrokeThickness = 0.75;
        _spindleHost.Children.Add(_damperRing);
        _spindleHost.Children.Add(_spindleHole);

        // Disc stage
        _discStage.HorizontalAlignment = HorizontalAlignment.Left;
        _discStage.VerticalAlignment = VerticalAlignment.Center;
        _discStage.Children.Add(_bassHalo);
        _discStage.Children.Add(_platterLayer);
        _discStage.Children.Add(_specularSheen);
        _discStage.Children.Add(_outerRim);
        _discStage.Children.Add(_spindleHost);

        // Tonearm
        _tonearmGimbal.Fill = TonearmGimbalFill;
        _tonearmGimbal.Stroke = TonearmGimbalRim;
        _tonearmGimbal.StrokeThickness = 0.9;
        _tonearmArm.Stroke = TonearmWandBrush;
        _tonearmHeadshell.Background = TonearmHeadBrush;
        _tonearmHeadshell.BorderBrush = TonearmGimbalRim;
        _tonearmHeadshell.BorderThickness = new Thickness(0.6);

        _tonearmHost.Children.Add(_tonearmArm);
        _tonearmHost.Children.Add(_tonearmHeadshell);
        _tonearmHost.Children.Add(_tonearmStylusDot);
        _tonearmHost.Children.Add(_tonearmGimbal);

        Children.Add(_squircleHost);
        Children.Add(_discStage);
        Children.Add(_tonearmHost);

        RebuildLayout();
        ApplyStyleVisibility();
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PlayerDiscControl ctrl)
        {
            ctrl._needsRebuild = true;
            ctrl.RebuildLayout();
            ctrl.ApplyStyleVisibility();
        }
    }

    public void SetStyle(string? style)
    {
        string normalized = SettingsService.NormalizeAlbumArtStyle(style);
        if (_style == normalized && !_needsRebuild) return;
        _style = normalized;
        if (_style == "StaticSquircle")
        {
            _angularVelocity = 0;
            _tonearmAngle = 0;
            _tonearmRotate.Angle = 0;
            _haloOpacity = 0;
            _bassHalo.Opacity = 0;
        }
        RebuildLayout();
        ApplyStyleVisibility();
    }

    public void SetArtwork(ImageSource? artwork)
    {
        if (ReferenceEquals(_artwork, artwork)) return;
        _artwork = artwork;

        if (artwork != null)
        {
            _coverBrush.ImageSource = artwork;
            _coverCircle.Fill = _coverBrush;
            _squircleImage.Source = artwork;
            _squircleImage.Visibility = Visibility.Visible;
            _squircleFallback.Visibility = Visibility.Collapsed;
            _discFallback.Visibility = Visibility.Collapsed;
        }
        else
        {
            _coverBrush.ImageSource = null;
            _coverCircle.Fill = DarkLabelBgBrush;
            _squircleImage.Source = null;
            _squircleImage.Visibility = Visibility.Collapsed;
            _squircleFallback.Visibility = Visibility.Visible;
            _discFallback.Visibility = Visibility.Visible;
        }
    }

    public void PulseTrackChange()
    {
        if (_style == "StaticSquircle") return;
        // Subtle motor kick on track skip so the platter feels alive
        _angularVelocity = Math.Max(_angularVelocity, 54.0);
        if (_style == "VinylDisc" && ShowTonearm && DiscSize >= 46)
        {
            _tonearmAngle = Math.Max(4.0, _tonearmAngle - 10.0);
            _tonearmRotate.Angle = _tonearmAngle;
        }
    }

    /// <summary>
    /// Advances inertial disc physics and tonearm tracking for one frame.
    /// Returns <c>true</c> when all motion has settled and the render loop can detach.
    /// </summary>
    public bool StepPhysics(double dt, bool isPlaying, float bassLevel, bool reducedMotion)
    {
        if (_style == "StaticSquircle")
        {
            _angularVelocity = 0;
            _haloOpacity = 0;
            if (_bassHalo.Visibility != Visibility.Collapsed)
            {
                _bassHalo.Opacity = 0;
                _bassHalo.Visibility = Visibility.Collapsed;
            }
            return true;
        }

        double targetTonearm = (isPlaying && _style == "VinylDisc" && ShowTonearm && DiscSize >= 46) ? 24.0 : 0.0;

        if (reducedMotion)
        {
            _angularVelocity = 0;
            _tonearmAngle = targetTonearm;
            _tonearmRotate.Angle = _tonearmAngle;
            _haloOpacity = 0;
            if (_bassHalo.Visibility != Visibility.Collapsed)
            {
                _bassHalo.Opacity = 0;
                _bassHalo.Visibility = Visibility.Collapsed;
            }
            return true;
        }

        double clampedDt = Math.Clamp(dt, 0.001, 0.1);

        // 1. Inertial motor spin-up (36 deg/sec = 10s per rev) / magnetic coast-to-stop
        if (isPlaying)
        {
            double spinAlpha = 1.0 - Math.Exp(-4.2 * clampedDt);
            _angularVelocity += (36.0 - _angularVelocity) * spinAlpha;
        }
        else
        {
            _angularVelocity *= Math.Exp(-3.0 * clampedDt);
            if (_angularVelocity < 0.15)
            {
                _angularVelocity = 0.0;
            }
        }

        if (_angularVelocity > 0.0)
        {
            _angle = (_angle + (_angularVelocity * clampedDt)) % 360.0;
            _platterRotate.Angle = _angle;
        }

        // 2. Tonearm gimbal pivot
        if (Math.Abs(_tonearmAngle - targetTonearm) > 0.15)
        {
            double armAlpha = 1.0 - Math.Exp(-9.0 * clampedDt);
            _tonearmAngle += (targetTonearm - _tonearmAngle) * armAlpha;
            if (Math.Abs(_tonearmAngle - targetTonearm) <= 0.15)
            {
                _tonearmAngle = targetTonearm;
            }
            _tonearmRotate.Angle = _tonearmAngle;
        }
        else if (_tonearmAngle != targetTonearm)
        {
            _tonearmAngle = targetTonearm;
            _tonearmRotate.Angle = _tonearmAngle;
        }

        // 3. Sub-bass underlay rim halo
        double targetHalo = isPlaying ? Math.Clamp(0.08 + (bassLevel * 0.44), 0.0, 0.52) : 0.0;
        double haloAlpha = 1.0 - Math.Exp(-(isPlaying ? 14.0 : 8.0) * clampedDt);
        _haloOpacity += (targetHalo - _haloOpacity) * haloAlpha;
        if (!isPlaying && _haloOpacity < 0.01)
        {
            _haloOpacity = 0.0;
        }

        if (_haloOpacity > 0.008)
        {
            if (_bassHalo.Visibility != Visibility.Visible) _bassHalo.Visibility = Visibility.Visible;
            _bassHalo.Opacity = _haloOpacity;
        }
        else if (_bassHalo.Visibility != Visibility.Collapsed)
        {
            _bassHalo.Opacity = 0.0;
            _bassHalo.Visibility = Visibility.Collapsed;
        }

        return !isPlaying &&
               _angularVelocity == 0.0 &&
               Math.Abs(_tonearmAngle - targetTonearm) <= 0.15 &&
               _haloOpacity <= 0.01;
    }

    private void RebuildLayout()
    {
        _needsRebuild = false;
        double size = Math.Max(16.0, DiscSize);
        bool hasTonearm = ShowTonearm && size >= 46.0 && _style == "VinylDisc";

        Width = hasTonearm ? size + 6.0 : size;
        Height = hasTonearm ? size + 2.0 : size;

        // Squircle host
        _squircleHost.Width = size;
        _squircleHost.Height = size;
        _squircleHost.CornerRadius = new CornerRadius(Math.Clamp(SquircleRadius, 2.0, size * 0.45));
        _squircleHost.HorizontalAlignment = HorizontalAlignment.Left;
        _squircleHost.VerticalAlignment = VerticalAlignment.Center;
        _squircleFallback.FontSize = Math.Clamp(size * 0.37, 9.0, 22.0);

        // Disc stage
        _discStage.Width = size;
        _discStage.Height = size;
        _platterLayer.Width = size;
        _platterLayer.Height = size;

        _bassHalo.Width = size + 4.0;
        _bassHalo.Height = size + 4.0;
        _bassHalo.Margin = new Thickness(-2.0);

        _vinylBase.Width = size;
        _vinylBase.Height = size;

        // Micro-groove rings (only visible in VinylDisc >= 28px)
        _grooveOuter.Width = size * 0.90;
        _grooveOuter.Height = size * 0.90;
        _grooveMid.Width = size * 0.81;
        _grooveMid.Height = size * 0.81;
        _grooveInner.Width = size * 0.73;
        _grooveInner.Height = size * 0.73;

        // Rotating track-band specular arc so spin is legible even on uniform/dark covers
        double arcR = size * 0.41;
        double cx = size * 0.5;
        double cy = size * 0.5;
        double a1 = -0.55;
        double a2 = 0.45;
        var arcFig = new PathFigure
        {
            StartPoint = new Point(cx + arcR * Math.Cos(a1), cy + arcR * Math.Sin(a1)),
            IsClosed = false
        };
        arcFig.Segments.Add(new ArcSegment(
            new Point(cx + arcR * Math.Cos(a2), cy + arcR * Math.Sin(a2)),
            new Size(arcR, arcR), 0, false, SweepDirection.Clockwise, true));
        var arcGeo = new PathGeometry();
        arcGeo.Figures.Add(arcFig);
        arcGeo.Freeze();
        _grooveMotionArc.Data = arcGeo;
        _grooveMotionArc.Width = size;
        _grooveMotionArc.Height = size;

        // Cover circle size: 66% for VinylDisc, 94% for OpticalDisc
        double coverRatio = _style == "OpticalDisc" ? 0.94 : 0.66;
        double coverSize = size * coverRatio;
        _coverCircle.Width = coverSize;
        _coverCircle.Height = coverSize;
        _coverBevel.Width = coverSize;
        _coverBevel.Height = coverSize;
        _discFallback.FontSize = Math.Clamp(coverSize * 0.42, 8.0, 18.0);

        _opticalRim.Width = size - 1.0;
        _opticalRim.Height = size - 1.0;
        _specularSheen.Width = size - 1.0;
        _specularSheen.Height = size - 1.0;
        _outerRim.Width = size;
        _outerRim.Height = size;

        // Center spindle assembly
        bool mini = size <= 24.0;
        double damperSize = mini ? 0.0 : Math.Max(7.0, size * 0.22);
        double spindleSize = mini ? 4.5 : Math.Max(4.5, size * 0.105);
        _damperRing.Width = damperSize;
        _damperRing.Height = damperSize;
        _damperRing.Visibility = mini ? Visibility.Collapsed : Visibility.Visible;
        _spindleHole.Width = spindleSize;
        _spindleHole.Height = spindleSize;

        // Micro-tonearm (top-right gimbal, rotates 0° -> +24° onto outer groove)
        if (hasTonearm)
        {
            double armW = 14.0;
            double armH = size * 0.68;
            _tonearmHost.Width = armW;
            _tonearmHost.Height = armH;
            _tonearmHost.Margin = new Thickness(0, 1.0, 0.5, 0);
            _tonearmHost.RenderTransformOrigin = new Point(0.65, 5.0 / armH);

            double gimbalSize = 8.0;
            _tonearmGimbal.Width = gimbalSize;
            _tonearmGimbal.Height = gimbalSize;
            _tonearmGimbal.HorizontalAlignment = HorizontalAlignment.Right;
            _tonearmGimbal.VerticalAlignment = VerticalAlignment.Top;
            _tonearmGimbal.Margin = new Thickness(0, 1.0, 1.0, 0);

            double gx = armW - 5.0;
            double gy = 5.0;
            double elbowX = gx - 1.5;
            double elbowY = armH - 9.0;
            double tipX = 3.5;
            double tipY = armH - 3.0;

            var armFig = new PathFigure { StartPoint = new Point(gx, gy), IsClosed = false };
            armFig.Segments.Add(new LineSegment(new Point(elbowX, elbowY), true));
            armFig.Segments.Add(new LineSegment(new Point(tipX, tipY), true));
            var armGeo = new PathGeometry();
            armGeo.Figures.Add(armFig);
            armGeo.Freeze();
            _tonearmArm.Data = armGeo;

            _tonearmHeadshell.Width = 4.5;
            _tonearmHeadshell.Height = 6.5;
            _tonearmHeadshell.HorizontalAlignment = HorizontalAlignment.Left;
            _tonearmHeadshell.VerticalAlignment = VerticalAlignment.Bottom;
            _tonearmHeadshell.Margin = new Thickness(1.0, 0, 0, 1.0);

            _tonearmStylusDot.HorizontalAlignment = HorizontalAlignment.Left;
            _tonearmStylusDot.VerticalAlignment = VerticalAlignment.Bottom;
            _tonearmStylusDot.Margin = new Thickness(2.0, 0, 0, 2.2);
        }
    }

    private void ApplyStyleVisibility()
    {
        bool isStatic = _style == "StaticSquircle";
        bool isVinyl = _style == "VinylDisc";
        bool isOptical = _style == "OpticalDisc";
        bool showGrooves = isVinyl && DiscSize >= 28.0;

        _squircleHost.Visibility = isStatic ? Visibility.Visible : Visibility.Collapsed;
        _discStage.Visibility = isStatic ? Visibility.Collapsed : Visibility.Visible;

        _vinylBase.Visibility = isVinyl ? Visibility.Visible : Visibility.Collapsed;
        _grooveOuter.Visibility = showGrooves ? Visibility.Visible : Visibility.Collapsed;
        _grooveMid.Visibility = showGrooves ? Visibility.Visible : Visibility.Collapsed;
        _grooveInner.Visibility = showGrooves ? Visibility.Visible : Visibility.Collapsed;
        _grooveMotionArc.Visibility = isVinyl ? Visibility.Visible : Visibility.Collapsed;
        _opticalRim.Visibility = isOptical ? Visibility.Visible : Visibility.Collapsed;

        bool hasTonearm = isVinyl && ShowTonearm && DiscSize >= 46.0;
        _tonearmHost.Visibility = hasTonearm ? Visibility.Visible : Visibility.Collapsed;
    }

    private static TextBlock CreateFallbackGlyph(double fontSize) => new()
    {
        FontFamily = GlyphFont,
        Text = "\uE8D6",
        FontSize = fontSize,
        Foreground = DefaultGlyphBrush,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false
    };

    private static RadialGradientBrush CreateFrozenVinylBrush()
    {
        var b = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.45, 0.45),
            RadiusX = 0.52,
            RadiusY = 0.52
        };
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0x09, 0x09, 0x0D), 0.0));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0x14, 0x15, 0x1C), 0.65));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0x0C, 0x0C, 0x11), 0.88));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0x1B, 0x1C, 0x24), 1.0));
        b.Freeze();
        return b;
    }

    private static LinearGradientBrush CreateFrozenOpticalRimBrush()
    {
        var b = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF), 0.0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x40, 0x64, 0xD2, 0xFF), 0.35));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, 0xBF, 0x5A, 0xF2), 0.70));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF), 1.0));
        b.Freeze();
        return b;
    }

    private static LinearGradientBrush CreateFrozenSpecularBrush()
    {
        var b = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), 0.0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x06, 0xFF, 0xFF, 0xFF), 0.32));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.52));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 0.82));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0));
        b.Freeze();
        return b;
    }
}
