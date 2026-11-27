using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using DynamicIsland.Services;

namespace DynamicIsland.Core.Pet3D;

/// <summary>
/// GPU-accelerated lightweight 3D virtual pet control using native WPF <see cref="Viewport3D"/>.
/// Exposes the exact same public API as <see cref="VirtualPetControl"/> while rendering
/// articulated, fur-textured 3D models with 3-point studio lighting and true 3D gaze tracking.
/// </summary>
public sealed class VirtualPet3DControl : Grid
{
    // ─────────────────────────────────────────────────────────────────────────
    // Dependency Properties (matching VirtualPetControl API)
    // ─────────────────────────────────────────────────────────────────────────
    public static readonly DependencyProperty SpeciesProperty =
        DependencyProperty.Register(nameof(Species), typeof(string), typeof(VirtualPet3DControl),
            new PropertyMetadata("Cat", OnAppearanceChanged));

    public static readonly DependencyProperty AccessoryProperty =
        DependencyProperty.Register(nameof(Accessory), typeof(string), typeof(VirtualPet3DControl),
            new PropertyMetadata("None", OnAccessoryChanged));

    public static readonly DependencyProperty MoodProperty =
        DependencyProperty.Register(nameof(Mood), typeof(PetMood), typeof(VirtualPet3DControl),
            new PropertyMetadata(PetMood.Sleeping));

    public static readonly DependencyProperty PetNameProperty =
        DependencyProperty.Register(nameof(PetName), typeof(string), typeof(VirtualPet3DControl),
            new PropertyMetadata("Mochi"));

    public static readonly DependencyProperty AffectionProperty =
        DependencyProperty.Register(nameof(Affection), typeof(int), typeof(VirtualPet3DControl),
            new PropertyMetadata(25, OnAffectionPropertyChanged));

    public static readonly DependencyProperty IsScamperingProperty =
        DependencyProperty.Register(nameof(IsScampering), typeof(bool), typeof(VirtualPet3DControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsPeekingProperty =
        DependencyProperty.Register(nameof(IsPeeking), typeof(bool), typeof(VirtualPet3DControl),
            new PropertyMetadata(false));

    public string Species { get => (string)GetValue(SpeciesProperty); set => SetValue(SpeciesProperty, value); }
    public string Accessory { get => (string)GetValue(AccessoryProperty); set => SetValue(AccessoryProperty, value); }
    public PetMood Mood { get => (PetMood)GetValue(MoodProperty); set => SetValue(MoodProperty, value); }
    public string PetName { get => (string)GetValue(PetNameProperty); set => SetValue(PetNameProperty, value); }
    public int Affection { get => (int)GetValue(AffectionProperty); set => SetValue(AffectionProperty, value); }
    public bool IsScampering { get => (bool)GetValue(IsScamperingProperty); set => SetValue(IsScamperingProperty, value); }
    public bool IsPeeking { get => (bool)GetValue(IsPeekingProperty); set => SetValue(IsPeekingProperty, value); }

    private static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VirtualPet3DControl ctrl) ctrl.EnsureRig();
    }

    private static void OnAccessoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VirtualPet3DControl ctrl && ctrl._rig != null)
        {
            Pet3DSpeciesFactory.AttachAccessory(ctrl._rig, ctrl.Accessory ?? "None", ctrl._builtHeroLod);
        }
    }

    private static void OnAffectionPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VirtualPet3DControl ctrl && (int)e.NewValue > (int)e.OldValue)
            ctrl.PetClickImpulse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scene & State
    // ─────────────────────────────────────────────────────────────────────────
    private readonly Viewport3D _viewport;
    private readonly ModelVisual3D _petVisual;
    private readonly Ellipse _contactShadow;
    private readonly ScaleTransform _shadowScale;
    private readonly Pet3DOverlayVisual _overlay;

    private Pet3DRig? _rig;
    private string? _builtSpecies;
    private bool _builtHeroLod;

    // Physics & animation state
    private double _time;
    private double _lastTime;
    private double _lastFrameStepTime = -1.0;
    private double _squashOffset, _squashVelocity;
    private double _earAng, _earVel, _earTwitch, _earFlick;
    private double _prevOffY, _bodyVelY;
    private double _gazeX, _gazeY, _targetGazeX, _targetGazeY;
    private double _dilate = 0.3;
    private bool _isNearCursor;
    private float _bass, _treble;

    private bool _isDoingTrick;
    private double _trickStartTime;
    private bool _isDoingSpecialAction;
    private double _specialActionStartTime;
    private PetSpecialActionKind _specialActionKind = PetSpecialActionKind.None;

    private bool _hasInteractiveToyTarget;
    private double _interactiveToyNormX;
    private double _interactiveToyNormY;

    public bool IsAnimatingTrick => _isDoingTrick;
    public bool IsAnimatingSpecialAction => _isDoingSpecialAction;

    public VirtualPet3DControl()
    {
        Width = 26;
        Height = 22;
        Background = Brushes.Transparent;
        ClipToBounds = false;
        SnapsToDevicePixels = false;

        _shadowScale = new ScaleTransform(1, 1);
        _contactShadow = new Ellipse
        {
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 1),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _shadowScale,
            Fill = CreateFrozenShadowBrush()
        };

        _viewport = new Viewport3D
        {
            IsHitTestVisible = false,
            ClipToBounds = false,
            Camera = new PerspectiveCamera
            {
                Position = new Point3D(0, 0.25, 5.2),
                LookDirection = new Vector3D(0, -0.03, -1),
                UpDirection = new Vector3D(0, 1, 0),
                FieldOfView = 30
            }
        };

        // 3-Point Studio Lighting (Warm Key + Cool Rim + Bounce Fill + Ambient)
        var lightGroup = new Model3DGroup();
        lightGroup.Children.Add(new DirectionalLight(Color.FromRgb(255, 248, 238), new Vector3D(-0.55, -0.70, -0.75)));
        lightGroup.Children.Add(new DirectionalLight(Color.FromRgb(165, 212, 255), new Vector3D(0.75, 0.35, 0.55)));
        lightGroup.Children.Add(new DirectionalLight(Color.FromRgb(95, 82, 76), new Vector3D(0.35, -0.25, -0.80)));
        lightGroup.Children.Add(new AmbientLight(Color.FromRgb(64, 64, 76)));
        _viewport.Children.Add(new ModelVisual3D { Content = lightGroup });

        _petVisual = new ModelVisual3D();
        _viewport.Children.Add(_petVisual);

        _overlay = new Pet3DOverlayVisual { IsHitTestVisible = false };

        Children.Add(_contactShadow);
        Children.Add(_viewport);
        Children.Add(_overlay);

        SizeChanged += (_, _) => EnsureRig();
        EnsureRig();
    }

    private static RadialGradientBrush CreateFrozenShadowBrush()
    {
        var b = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(115, 0, 0, 0), 0.0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(48, 0, 0, 0), 0.55));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0));
        b.Freeze();
        return b;
    }

    private void EnsureRig()
    {
        string sp = Species ?? "Cat";
        double w = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 26);
        double h = ActualHeight > 0 ? ActualHeight : (Height > 0 ? Height : 22);
        bool hardwareCapable = (RenderCapability.Tier >> 16) > 0;
        bool heroLod = Math.Max(w, h) >= 44.0 && hardwareCapable;

        if (_rig != null && _builtSpecies == sp && _builtHeroLod == heroLod)
            return;

        _builtSpecies = sp;
        _builtHeroLod = heroLod;
        _rig = Pet3DSpeciesFactory.BuildRig(sp, Accessory ?? "None", heroLod);
        _petVisual.Content = _rig.RootModel;

        _contactShadow.Width = Math.Max(14, w * 0.62);
        _contactShadow.Height = Math.Max(4, h * 0.14);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public Control API
    // ─────────────────────────────────────────────────────────────────────────
    public void SetInteractiveToyTarget(double normX, double normY)
    {
        _hasInteractiveToyTarget = true;
        _interactiveToyNormX = Math.Clamp(normX, -1.0, 1.0);
        _interactiveToyNormY = Math.Clamp(normY, -1.0, 1.0);
    }

    public void ClearInteractiveToyTarget() => _hasInteractiveToyTarget = false;

    public void PetClickImpulse() => _squashVelocity = -0.34;

    public void TriggerTrick()
    {
        _isDoingTrick = true;
        _trickStartTime = _time;
        _squashVelocity = -0.45;
    }

    public void TriggerSpecialAction(PetSpecialActionKind kind = PetSpecialActionKind.None)
    {
        _specialActionKind = kind != PetSpecialActionKind.None ? kind : (Species switch
        {
            "Dog" => PetSpecialActionKind.BallFetch,
            "Bunny" => PetSpecialActionKind.BinkyFlip,
            "Fox" => PetSpecialActionKind.FoxfireDance,
            "Panda" => PetSpecialActionKind.KungFuRoll,
            "Dragon" => PetSpecialActionKind.DragonBreath,
            _ => PetSpecialActionKind.LaserHunt
        });
        _isDoingSpecialAction = true;
        _specialActionStartTime = _time;
        _squashVelocity = -0.42;
    }

    public void Dance(float bass, float treble, double t)
    {
        _bass = bass;
        _treble = treble;
        _time = t;
        StepAnimation(t);
    }

    public void UpdateTick(double t, bool isNearCursor, double gazeX = 0.0, double gazeY = 0.0)
    {
        _time = t;
        _isNearCursor = isNearCursor;
        _targetGazeX = isNearCursor ? Math.Clamp(gazeX, -1.0, 1.0) : 0.0;
        _targetGazeY = isNearCursor ? Math.Clamp(gazeY, -1.0, 1.0) : 0.0;
        _bass = 0f;
        _treble = 0f;

        if (Mood == PetMood.Sleeping && !isNearCursor && !IsScampering && !IsPeeking && !_isDoingTrick && !_isDoingSpecialAction &&
            Math.Abs(_squashOffset) < 0.002 && Math.Abs(_squashVelocity) < 0.002)
        {
            if (t - _lastFrameStepTime < 0.065)
                return;
        }

        _lastFrameStepTime = t;
        StepAnimation(t);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3D Skeletal & Physics Step (0 bytes/frame allocation)
    // ─────────────────────────────────────────────────────────────────────────
    private void StepAnimation(double t)
    {
        EnsureRig();
        var rig = _rig;
        if (rig == null) return;

        double dt = _lastTime > 0 ? Math.Clamp(t - _lastTime, 0.001, 0.1) : 0.016;
        _lastTime = t;

        StepSquash(dt);

        double gk = Math.Min(1.0, dt * 10.0);
        _gazeX += (_targetGazeX - _gazeX) * gk;
        _gazeY += (_targetGazeY - _gazeY) * gk;

        var mood = Mood;
        string species = Species ?? "Cat";
        bool bunny = species == "Bunny";
        bool scampering = IsScampering;
        bool peeking = IsPeeking;

        double dilTarget = _isNearCursor ? 1.0 : (mood == PetMood.Happy ? 0.75 : (mood == PetMood.Sleeping ? 0.0 : 0.32));
        _dilate += (dilTarget - _dilate) * Math.Min(1.0, dt * 8.0);

        double scaleY = 1.0 + _squashOffset;
        double scaleX = 1.0 - _squashOffset * 0.65;
        double petOffsetY = 0.0;
        double rollDeg = 0.0;
        double flipXDeg = 0.0;
        double yawDeg = 0.0;

        if (scampering)
        {
            double gallop = Math.Sin(t * 24.0);
            petOffsetY = Math.Abs(gallop) * 0.16;
            scaleY += 0.06 * (1.0 - Math.Abs(gallop));
            scaleX -= 0.04 * (1.0 - Math.Abs(gallop));
            rollDeg = -6.0 - gallop * 3.0;
            yawDeg = 18.0;
        }
        else if (peeking)
        {
            petOffsetY = -0.14 + Math.Sin(t * 2.5) * 0.03;
            scaleY -= 0.05;
            scaleX += 0.05;
            rollDeg = 4.0 + Math.Sin(t * 3.0) * 1.5;
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
                    petOffsetY = -0.05;
                    break;
                }
                case PetMood.Dancing:
                {
                    double beat = Math.Sin(t * 11.0);
                    petOffsetY = Math.Abs(beat) * (0.16 + _bass * 0.38);
                    scaleY += -(_bass * 0.20) + 0.07 * beat;
                    scaleX += (_bass * 0.14) - 0.05 * beat;
                    rollDeg = Math.Sin(t * 9.0) * (7.0 + _treble * 14.0);
                    yawDeg = Math.Sin(t * 4.5) * 10.0;
                    break;
                }
                case PetMood.Happy:
                {
                    double hop = Math.Abs(Math.Sin(t * 14.0));
                    petOffsetY = hop * 0.28;
                    rollDeg = Math.Sin(t * 12.0) * 7.5;
                    scaleY += 0.08 * (1.0 - hop);
                    scaleX -= 0.05 * (1.0 - hop);
                    break;
                }
                case PetMood.Awake:
                {
                    rollDeg = Math.Sin(t * 1.8) * 2.0;
                    scaleY += 0.02 * Math.Sin(t * 3.0);
                    if (_isNearCursor) { petOffsetY = 0.06; scaleY += 0.035; }
                    break;
                }
                case PetMood.Thirsty:
                {
                    petOffsetY = -0.04 + Math.Sin(t * 3.5) * 0.04;
                    rollDeg = Math.Sin(t * 2.5) * 2.5;
                    break;
                }
                case PetMood.Stretch:
                {
                    petOffsetY = 0.10;
                    scaleY += 0.14;
                    scaleX -= 0.08;
                    break;
                }
                case PetMood.Eating:
                {
                    double munch = Math.Abs(Math.Sin(t * 18.0));
                    petOffsetY = munch * 0.12;
                    scaleY += 0.05 * (1.0 - munch);
                    scaleX += 0.04 * munch;
                    break;
                }
                case PetMood.Playing:
                {
                    double hop = Math.Abs(Math.Sin(t * 16.0));
                    petOffsetY = hop * 0.22;
                    rollDeg = Math.Sin(t * 14.0) * 5.5;
                    break;
                }
            }
        }

        // 360-degree acrobatic flip
        if (_isDoingTrick)
        {
            double elapsed = t - _trickStartTime;
            const double dur = 0.65;
            if (elapsed >= 0.0 && elapsed < dur)
            {
                double p = elapsed / dur;
                petOffsetY += Math.Sin(p * Math.PI) * 0.48;
                flipXDeg += -p * 360.0;
            }
            else
            {
                _isDoingTrick = false;
            }
        }

        // Species special actions
        if (_isDoingSpecialAction)
        {
            double elapsed = t - _specialActionStartTime;
            const double dur = 1.8;
            if (elapsed >= 0.0 && elapsed < dur)
            {
                double p = elapsed / dur;
                switch (_specialActionKind)
                {
                    case PetSpecialActionKind.LaserHunt:
                        if (p < 0.45)
                        {
                            petOffsetY -= 0.06;
                            scaleX += 0.06; scaleY -= 0.05;
                            _targetGazeX = _hasInteractiveToyTarget ? _interactiveToyNormX : Math.Sin(p * 14.0) * 0.9;
                            _targetGazeY = _hasInteractiveToyTarget ? Math.Clamp(_interactiveToyNormY, -0.4, 0.8) : 0.65;
                        }
                        else
                        {
                            double leap = Math.Sin((p - 0.45) / 0.55 * Math.PI);
                            petOffsetY += Math.Max(0, leap) * 0.34;
                            rollDeg = Math.Sin(p * 18.0) * 8.0;
                        }
                        break;
                    case PetSpecialActionKind.BallFetch:
                        petOffsetY += Math.Sin(p * Math.PI) * 0.46;
                        rollDeg = Math.Sin(p * Math.PI * 2.0) * 12.0;
                        break;
                    case PetSpecialActionKind.BinkyFlip:
                        petOffsetY += Math.Sin(p * Math.PI) * 0.62;
                        yawDeg += p * 360.0;
                        break;
                    case PetSpecialActionKind.FoxfireDance:
                        petOffsetY += Math.Sin(p * Math.PI) * 0.36;
                        yawDeg = Math.Sin(p * Math.PI * 4.0) * 26.0;
                        break;
                    case PetSpecialActionKind.KungFuRoll:
                        petOffsetY += Math.Sin(p * Math.PI) * 0.30;
                        flipXDeg += p * 360.0;
                        break;
                    case PetSpecialActionKind.DragonBreath:
                        petOffsetY = 0.36 + Math.Sin(t * 12.0) * 0.12;
                        if (p < 0.35) { scaleX += (p / 0.35) * 0.14; scaleY += (p / 0.35) * 0.10; }
                        break;
                }
            }
            else
            {
                _isDoingSpecialAction = false;
            }
        }

        // Apply root transforms
        rig.RootScale.ScaleX = scaleX;
        rig.RootScale.ScaleY = scaleY;
        rig.RootScale.ScaleZ = scaleX;
        rig.RootRotateZ.Angle = -rollDeg;
        rig.RootRotateX.Angle = flipXDeg;
        rig.RootRotateY.Angle = yawDeg;
        rig.RootTranslate.OffsetY = petOffsetY;

        // Loaf vs Seated torso
        bool loaf = mood == PetMood.Sleeping && !scampering && !peeking;
        rig.SeatedTorsoScale.ScaleX = rig.SeatedTorsoScale.ScaleY = rig.SeatedTorsoScale.ScaleZ = loaf ? 0 : 1;
        rig.LoafTorsoScale.ScaleX = rig.LoafTorsoScale.ScaleY = rig.LoafTorsoScale.ScaleZ = loaf ? 1 : 0;
        rig.FrontLegsVisibleScale.ScaleX = rig.FrontLegsVisibleScale.ScaleY = rig.FrontLegsVisibleScale.ScaleZ = loaf ? 0 : 1;

        // Contact shadow scaling
        double lift = Math.Clamp(petOffsetY / 0.45, 0.0, 1.0);
        _shadowScale.ScaleX = scaleX * (1.0 - 0.25 * lift);
        _shadowScale.ScaleY = 1.0 - 0.25 * lift;
        _contactShadow.Opacity = 1.0 - 0.55 * lift;

        // Secondary velocity & ear spring
        double vy = (petOffsetY - _prevOffY) / dt;
        _prevOffY = petOffsetY;
        _bodyVelY += (vy - _bodyVelY) * Math.Min(1.0, dt * 25.0);

        double droop = scampering ? -14.0
            : peeking ? 6.0
            : mood == PetMood.Sleeping ? (bunny ? 30.0 : 10.0)
            : mood == PetMood.Thirsty ? (bunny ? 20.0 : 12.0)
            : mood == PetMood.Stretch ? -6.0
            : (mood == PetMood.Awake && _isNearCursor) ? -5.0
            : 0.0;

        StepEars(dt, droop + Math.Clamp(_bodyVelY * 3.0, -24.0, 24.0));

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

        double ea = _earAng + _earTwitch;
        rig.EarLeftRot.Angle = ea + _earFlick;
        rig.EarRightRot.Angle = -ea;

        // True 3D Head Pose & Cursor Gaze
        if (loaf)
        {
            rig.HeadTranslate.OffsetX = 0.0;
            rig.HeadTranslate.OffsetY = 0.46 + Math.Sin(t * 2.1 - 0.4) * 0.025;
            rig.HeadTranslate.OffsetZ = 0.32;
            rig.HeadYaw.Angle = -8.0;
            rig.HeadPitch.Angle = 16.0;
            rig.HeadRoll.Angle = 9.0;
        }
        else
        {
            double idleLookX = _isNearCursor ? 0.0 : Math.Sin(t * 0.9) * 0.25;
            double idleLookY = _isNearCursor ? 0.0 : Math.Sin(t * 1.3 + 1.4) * 0.15;
            double gx = Math.Clamp(_gazeX + idleLookX, -1.0, 1.0);
            double gy = Math.Clamp(_gazeY + idleLookY, -1.0, 1.0);

            rig.HeadTranslate.OffsetX = gx * 0.06;
            rig.HeadTranslate.OffsetY = 0.72 + Math.Clamp(_bodyVelY * 0.08, -0.08, 0.08) + Math.Sin(t * 3.0 - 0.6) * 0.018;
            rig.HeadTranslate.OffsetZ = 0.14;
            rig.HeadYaw.Angle = gx * 24.0;
            rig.HeadPitch.Angle = gy * 16.0 + (mood == PetMood.Thirsty ? 8.0 : 0.0);
            rig.HeadRoll.Angle = -gx * 8.0;
        }

        // Eyes: blink + gaze + closed sleep/happy arcs
        bool eyesClosed = mood == PetMood.Sleeping || mood == PetMood.Happy || mood == PetMood.Eating;
        double ph = t % 3.9;
        double blink = BlinkAmount(ph);
        if (((int)(t / 3.9)) % 3 == 0) blink = Math.Max(blink, BlinkAmount(ph - 0.26));
        if (blink >= 0.75) eyesClosed = true;

        if (eyesClosed)
        {
            rig.EyeLeftOpenScale.ScaleX = rig.EyeLeftOpenScale.ScaleY = rig.EyeLeftOpenScale.ScaleZ = 0;
            rig.EyeRightOpenScale.ScaleX = rig.EyeRightOpenScale.ScaleY = rig.EyeRightOpenScale.ScaleZ = 0;
            rig.ClosedEyesScale.ScaleX = rig.ClosedEyesScale.ScaleY = rig.ClosedEyesScale.ScaleZ = 1;
        }
        else
        {
            double lid = 1.0 - blink * 0.85;
            rig.EyeLeftOpenScale.ScaleX = rig.EyeRightOpenScale.ScaleX = 1.0;
            rig.EyeLeftOpenScale.ScaleY = rig.EyeRightOpenScale.ScaleY = lid;
            rig.EyeLeftOpenScale.ScaleZ = rig.EyeRightOpenScale.ScaleZ = 1.0;
            rig.ClosedEyesScale.ScaleX = rig.ClosedEyesScale.ScaleY = rig.ClosedEyesScale.ScaleZ = 0;

            rig.EyeLeftPupilShift.OffsetX = _gazeX * 0.04;
            rig.EyeLeftPupilShift.OffsetY = -_gazeY * 0.03;
            rig.EyeRightPupilShift.OffsetX = _gazeX * 0.04;
            rig.EyeRightPupilShift.OffsetY = -_gazeY * 0.03;

            double pScale = 0.75 + 0.55 * _dilate;
            rig.PupilScale.ScaleX = pScale;
            rig.PupilScale.ScaleY = 1.0;
            rig.PupilScale.ScaleZ = 1.0;
        }

        // Tongue
        bool showTongue = (mood == PetMood.Happy || mood == PetMood.Dancing || mood == PetMood.Eating) &&
                          species is "Cat" or "Dog" or "Fox" or "Panda" or "Dragon";
        rig.TongueScale.ScaleX = rig.TongueScale.ScaleY = rig.TongueScale.ScaleZ = showTongue ? 1 : 0;
        if (showTongue)
        {
            rig.TongueBob.OffsetY = Math.Sin(t * 14.0) * 0.025;
        }

        // Front legs animation
        if (!loaf)
        {
            if (scampering)
            {
                double gallop = Math.Sin(t * 24.0);
                rig.FrontLegLRot.Angle = gallop * 28.0;
                rig.FrontLegRRot.Angle = -gallop * 28.0;
                rig.FrontLegLShift.OffsetY = Math.Max(0, gallop) * 0.06;
                rig.FrontLegRShift.OffsetY = Math.Max(0, -gallop) * 0.06;
            }
            else if (mood == PetMood.Dancing)
            {
                double d1 = Math.Sin(t * 11.0);
                rig.FrontLegLRot.Angle = -15.0 + d1 * 22.0;
                rig.FrontLegRRot.Angle = -15.0 - d1 * 22.0;
                rig.FrontLegLShift.OffsetY = Math.Max(0, d1) * 0.08;
                rig.FrontLegRShift.OffsetY = Math.Max(0, -d1) * 0.08;
            }
            else if (mood == PetMood.Happy || mood == PetMood.Playing)
            {
                double p = Math.Abs(Math.Sin(t * 14.0));
                rig.FrontLegLRot.Angle = -p * 26.0;
                rig.FrontLegRRot.Angle = -p * 26.0;
                rig.FrontLegLShift.OffsetY = p * 0.07;
                rig.FrontLegRShift.OffsetY = p * 0.07;
            }
            else
            {
                rig.FrontLegLRot.Angle = rig.FrontLegRRot.Angle = 0;
                rig.FrontLegLShift.OffsetY = rig.FrontLegRShift.OffsetY = 0;
            }
        }

        // Tail animation
        double wagFreq = mood == PetMood.Happy ? 15.0 : (mood == PetMood.Dancing ? 11.0 : (scampering ? 18.0 : 2.8));
        double wagAmp = mood == PetMood.Happy ? 18.0 : (mood == PetMood.Dancing ? 14.0 : (loaf ? 3.5 : 8.0));
        for (int i = 0; i < rig.TailSegRots.Length; i++)
        {
            rig.TailSegRots[i].Angle = Math.Sin(t * wagFreq - i * 0.65) * wagAmp;
        }

        // Dragon wings
        if (species == "Dragon")
        {
            double flapFreq = (mood == PetMood.Happy || mood == PetMood.Dancing || scampering) ? 18.0 : (loaf ? 2.1 : 5.5);
            double flapAmp = (mood == PetMood.Happy || mood == PetMood.Dancing || scampering) ? 26.0 : (loaf ? 5.0 : 11.0);
            double flap = Math.Sin(t * flapFreq) * flapAmp;
            rig.WingLeftRot.Angle = flap;
            rig.WingRightRot.Angle = -flap;
        }

        // Collar bell swing
        rig.BellSwingRot.Angle = Math.Clamp(_gazeX * 14.0 + Math.Sin(t * 9.0) * (_bass * 18.0), -22.0, 22.0);

        // Accessory animations (Halo bob, Sprout sway, Headphones bass pulse)
        string acc = Accessory ?? "None";
        if (acc == "Halo")
        {
            rig.AccessoryAnimTranslate.OffsetY = Math.Sin(t * 3.8) * 0.045;
            rig.AccessoryAnimRot.Angle = 0;
            rig.AccessoryAnimScale.ScaleX = rig.AccessoryAnimScale.ScaleY = rig.AccessoryAnimScale.ScaleZ = 1.0;
        }
        else if (acc == "Sprout")
        {
            rig.AccessoryAnimTranslate.OffsetY = 0;
            rig.AccessoryAnimRot.Angle = Math.Sin(t * 4.5) * 9.0 - _gazeX * 6.0;
            rig.AccessoryAnimScale.ScaleX = rig.AccessoryAnimScale.ScaleY = rig.AccessoryAnimScale.ScaleZ = 1.0;
        }
        else if (acc == "Headphones")
        {
            double pulse = 1.0 + _bass * 0.14;
            rig.AccessoryAnimTranslate.OffsetY = 0;
            rig.AccessoryAnimRot.Angle = 0;
            rig.AccessoryAnimScale.ScaleX = rig.AccessoryAnimScale.ScaleY = rig.AccessoryAnimScale.ScaleZ = pulse;
        }
        else
        {
            rig.AccessoryAnimTranslate.OffsetY = 0;
            rig.AccessoryAnimRot.Angle = 0;
            rig.AccessoryAnimScale.ScaleX = rig.AccessoryAnimScale.ScaleY = rig.AccessoryAnimScale.ScaleZ = 1.0;
        }

        // Update 2D particle / special-action toy overlay
        _overlay.UpdateState(
            mood, t, scampering, peeking,
            _isDoingSpecialAction, _specialActionKind, _specialActionStartTime,
            _hasInteractiveToyTarget, _interactiveToyNormX, _interactiveToyNormY);
    }

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

    private static double BlinkAmount(double p)
    {
        if (p < 0.0 || p >= 0.18) return 0.0;
        if (p < 0.07) return p / 0.07;
        if (p < 0.11) return 1.0;
        return 1.0 - (p - 0.11) / 0.07;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Lightweight 2D Particle & Toy Overlay (Zero Per-Frame Allocations)
    // ─────────────────────────────────────────────────────────────────────────
    private sealed class Pet3DOverlayVisual : FrameworkElement
    {
        private const int AlphaLevels = 16;
        private static readonly SolidColorBrush[] ZBrushes = BrushTable(0xFFFFFF, 230);
        private static readonly Pen[] ZPens = PenTable(ZBrushes, 1.15);
        private static readonly SolidColorBrush[] HeartBrushes = BrushTable(0xFF2D55, 245);
        private static readonly SolidColorBrush[] StarBrushes = BrushTable(0xFFD60A, 220);
        private static readonly SolidColorBrush[] NoteBrushes = BrushTable(0xFF9F0A, 235);
        private static readonly Pen[] NotePens = PenTable(NoteBrushes, 1.15);
        private static readonly SolidColorBrush WaterDropBrush = Solid(0x0A84FF);
        private static readonly RadialGradientBrush LaserDotBrush = Radial(0xFFFFFF, 0xFF2D55);
        private static readonly RadialGradientBrush TennisBallBrush = Radial(0xEEFF41, 0x76FF03);
        private static readonly RadialGradientBrush FoxfireBrush = Radial(0xFFFFFF, 0x00F5D4);
        private static readonly RadialGradientBrush DragonFlameBrush = Radial(0xFFD60A, 0xFF453A);
        private static readonly SolidColorBrush SakuraBrush = Solid(0xFFAEC2, 230);
        private static readonly SolidColorBrush BambooBrush = Solid(0x4CAF50, 230);

        private PetMood _mood;
        private double _t;
        private bool _scampering, _peeking;
        private bool _special;
        private PetSpecialActionKind _specialKind;
        private double _specialStart;
        private bool _hasToy;
        private double _toyX, _toyY;

        public void UpdateState(
            PetMood mood, double t, bool scampering, bool peeking,
            bool special, PetSpecialActionKind specialKind, double specialStart,
            bool hasToy, double toyX, double toyY)
        {
            _mood = mood;
            _t = t;
            _scampering = scampering;
            _peeking = peeking;
            _special = special;
            _specialKind = specialKind;
            _specialStart = specialStart;
            _hasToy = hasToy;
            _toyX = toyX;
            _toyY = toyY;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            double cx = w * 0.5;
            double cy = h * 0.52;
            double unit = Math.Min(w, h) / 28.0;

            if (_scampering)
            {
                double laserY = cy + 6.5 * unit + Math.Sin(_t * 32.0) * 1.4 * unit;
                dc.DrawEllipse(LaserDotBrush, null, new Point(cx + 11.5 * unit, laserY), 2.5 * unit, 1.6 * unit);
                return;
            }

            if (_peeking)
            {
                double sc = (_t * 5.0) % 7.0;
                double alpha = 1.0 - sc / 7.0;
                dc.DrawEllipse(StarBrushes[Lvl(alpha * 0.85)], null, new Point(cx + 5.5 * unit, cy - (8.0 + sc * 0.6) * unit), 1.4 * unit, 1.4 * unit);
                return;
            }

            switch (_mood)
            {
                case PetMood.Sleeping:
                {
                    double c1 = (_t * 6.5) % 20.0;
                    double c2 = ((_t * 6.5) + 10.0) % 20.0;
                    DrawZ(dc, cx + (5.5 + c1 * 0.16) * unit, cy - (5.5 + c1 * 0.48) * unit, (1.5 + c1 * 0.04) * unit, 1.0 - c1 / 20.0);
                    DrawZ(dc, cx + (7.0 + c2 * 0.16) * unit, cy - (4.5 + c2 * 0.48) * unit, (1.3 + c2 * 0.04) * unit, 1.0 - c2 / 20.0);
                    break;
                }
                case PetMood.Happy:
                case PetMood.Eating:
                {
                    double c1 = (_t * 14.0) % 20.0;
                    double c2 = ((_t * 14.0) + 10.0) % 20.0;
                    var b1 = HeartBrushes[Lvl(1.0 - c1 / 20.0)];
                    var b2 = HeartBrushes[Lvl(1.0 - c2 / 20.0)];
                    dc.DrawEllipse(b1, null, new Point(cx - (6.0 + Math.Sin(_t * 6.0) * 1.2) * unit, cy - (6.0 + c1 * 0.5) * unit), 1.4 * unit, 1.4 * unit);
                    dc.DrawEllipse(b2, null, new Point(cx + (6.0 + Math.Cos(_t * 6.0) * 1.2) * unit, cy - (5.0 + c2 * 0.5) * unit), 1.2 * unit, 1.2 * unit);
                    break;
                }
                case PetMood.Dancing:
                {
                    double n1 = (_t * 15.0) % 22.0;
                    double n2 = ((_t * 15.0) + 11.0) % 22.0;
                    DrawNote(dc, cx + (7.5 + Math.Sin(_t * 5.0) * 1.4) * unit, cy - (5.0 + n1 * 0.5) * unit, 1.2 * unit, 1.0 - n1 / 22.0);
                    DrawNote(dc, cx - (7.5 + Math.Cos(_t * 5.0) * 1.4) * unit, cy - (4.0 + n2 * 0.5) * unit, 1.1 * unit, 1.0 - n2 / 22.0);
                    break;
                }
                case PetMood.Thirsty:
                {
                    double dy = (_t * 10.0) % 10.0;
                    dc.DrawEllipse(WaterDropBrush, null, new Point(cx + 8.5 * unit, cy - (6.0 - dy * 0.5) * unit), 1.1 * unit, 1.5 * unit);
                    break;
                }
            }

            if (_special)
            {
                double elapsed = _t - _specialStart;
                const double dur = 1.8;
                if (elapsed is >= 0.0 and < dur)
                {
                    double p = elapsed / dur;
                    switch (_specialKind)
                    {
                        case PetSpecialActionKind.LaserHunt:
                        {
                            double lx = _hasToy ? cx + _toyX * cx * 0.8 : cx + Math.Sin(p * 14.0) * 9.0 * unit;
                            double ly = _hasToy ? cy + _toyY * cy * 0.7 : cy + 6.0 * unit;
                            dc.DrawEllipse(LaserDotBrush, null, new Point(lx, ly), 2.8 * unit, 2.0 * unit);
                            break;
                        }
                        case PetSpecialActionKind.BallFetch:
                        {
                            double bx = _hasToy ? cx + _toyX * cx * 0.75 : cx + (-10.0 + p * 20.0) * unit;
                            double by = _hasToy ? cy + _toyY * cy * 0.65 : cy - Math.Abs(Math.Sin(p * Math.PI * 2.0)) * 9.0 * unit;
                            dc.DrawEllipse(TennisBallBrush, null, new Point(bx, by), 2.1 * unit, 2.1 * unit);
                            break;
                        }
                        case PetSpecialActionKind.BinkyFlip:
                        {
                            for (int i = 0; i < 4; i++)
                            {
                                double a = p * 5.0 + i * Math.PI * 0.5;
                                dc.DrawEllipse(SakuraBrush, null, new Point(cx + Math.Cos(a) * 10.0 * unit, cy + Math.Sin(a) * 6.0 * unit), 1.4 * unit, 0.9 * unit);
                            }
                            break;
                        }
                        case PetSpecialActionKind.FoxfireDance:
                        {
                            for (int i = 0; i < 3; i++)
                            {
                                double a = p * Math.PI * 4.0 + i * (Math.PI * 2.0 / 3.0);
                                dc.DrawEllipse(FoxfireBrush, null, new Point(cx + Math.Cos(a) * 10.5 * unit, cy - 2.0 * unit + Math.Sin(a) * 4.5 * unit), 2.4 * unit, 2.4 * unit);
                            }
                            break;
                        }
                        case PetSpecialActionKind.KungFuRoll:
                        {
                            for (int i = 0; i < 4; i++)
                            {
                                double a = p * 4.5 + i * 1.5;
                                dc.DrawEllipse(BambooBrush, null, new Point(cx + Math.Cos(a) * 9.0 * unit, cy - 3.0 * unit + Math.Sin(a) * 5.0 * unit), 1.6 * unit, 0.8 * unit);
                            }
                            break;
                        }
                        case PetSpecialActionKind.DragonBreath:
                        {
                            if (p >= 0.35)
                            {
                                double fp = (p - 0.35) / 0.65;
                                for (int i = 0; i < 3; i++)
                                {
                                    dc.DrawEllipse(DragonFlameBrush, null, new Point(cx + (4.5 + fp * (6.0 + i * 2.0)) * unit, cy - (1.0 + i * 0.8) * unit), 2.4 * unit, 1.8 * unit);
                                }
                            }
                            break;
                        }
                    }
                }
            }
        }

        private static void DrawZ(DrawingContext dc, double x, double y, double sz, double alpha)
        {
            var pen = ZPens[Lvl(alpha)];
            dc.DrawLine(pen, new Point(x - sz, y - sz), new Point(x + sz, y - sz));
            dc.DrawLine(pen, new Point(x + sz, y - sz), new Point(x - sz, y + sz));
            dc.DrawLine(pen, new Point(x - sz, y + sz), new Point(x + sz, y + sz));
        }

        private static void DrawNote(DrawingContext dc, double x, double y, double sz, double alpha)
        {
            int l = Lvl(alpha);
            dc.DrawEllipse(NoteBrushes[l], null, new Point(x, y), sz, sz * 0.72);
            dc.DrawLine(NotePens[l], new Point(x + sz * 0.8, y), new Point(x + sz * 0.8, y - sz * 2.6));
        }

        private static SolidColorBrush Solid(uint rgb, byte a = 255)
        {
            var b = new SolidColorBrush(Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            b.Freeze();
            return b;
        }

        private static RadialGradientBrush Radial(uint inner, uint outer)
        {
            var b = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(245, (byte)(inner >> 16), (byte)(inner >> 8), (byte)inner), 0.0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(200, (byte)(outer >> 16), (byte)(outer >> 8), (byte)outer), 0.55));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, (byte)(outer >> 16), (byte)(outer >> 8), (byte)outer), 1.0));
            b.Freeze();
            return b;
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
            for (int i = 0; i < AlphaLevels; i++)
            {
                var p = new Pen(brushes[i], w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                p.Freeze();
                t[i] = p;
            }
            return t;
        }

        private static int Lvl(double a) => (int)(Math.Clamp(a, 0.0, 1.0) * (AlphaLevels - 1) + 0.5);
    }
}
