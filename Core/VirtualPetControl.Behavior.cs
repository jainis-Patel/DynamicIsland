using System;
using System.Windows;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Core;

public sealed partial class VirtualPetControl
{
    public void SetInteractiveToyTarget(double normX, double normY)
    {
        _hasInteractiveToyTarget = true;
        _interactiveToyNormX = Math.Clamp(normX, -1.0, 1.0);
        _interactiveToyNormY = Math.Clamp(normY, -1.0, 1.0);
    }

    public void ClearInteractiveToyTarget() => _hasInteractiveToyTarget = false;

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
        if (Mood == PetMood.Sleeping && !isNearCursor && !IsScampering && !IsPeeking && !_isDoingTrick && !_isDoingSpecialAction &&
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
        _activePoolIndex ^= 1;
        _transformPoolIndex = 0;

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
        _isHeroLod = Math.Max(actualW, actualH) >= 44.0;
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
                case PetMood.Playing:
                {
                    double hop = Math.Abs(Math.Sin(t * 16.0));
                    petOffsetY = -hop * 2.8;
                    rotateAngle = Math.Sin(t * 14.0) * 5.5;
                    scaleY += 0.06 * (1.0 - hop);
                    scaleX += -0.04 * (1.0 - hop);
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

        // Species unique signature action choreography
        if (_isDoingSpecialAction)
        {
            double actionElapsed = t - _specialActionStartTime;
            const double ActionDuration = 1.8;
            if (actionElapsed >= 0.0 && actionElapsed < ActionDuration)
            {
                double p = actionElapsed / ActionDuration;
                switch (_specialActionKind)
                {
                    case PetSpecialActionKind.LaserHunt:
                        if (p < 0.45)
                        {
                            petOffsetY += 0.8;
                            scaleX += 0.06;
                            scaleY -= 0.05;
                            _targetGazeX = _hasInteractiveToyTarget ? _interactiveToyNormX : Math.Sin(p * 14.0) * 0.9;
                            _targetGazeY = _hasInteractiveToyTarget ? Math.Clamp(_interactiveToyNormY, -0.4, 0.8) : 0.65;
                        }
                        else
                        {
                            double leap = Math.Sin((p - 0.45) / 0.55 * Math.PI);
                            petOffsetY -= Math.Max(0, leap) * 4.2;
                            rotateAngle = Math.Sin(p * 18.0) * 8.0;
                            _targetGazeX = _hasInteractiveToyTarget ? _interactiveToyNormX : 0.4;
                            _targetGazeY = _hasInteractiveToyTarget ? _interactiveToyNormY : 0.2;
                        }
                        break;
                    case PetSpecialActionKind.BallFetch:
                        double ballArc = Math.Sin(p * Math.PI);
                        petOffsetY -= ballArc * 5.8;
                        rotateAngle = Math.Sin(p * Math.PI * 2.0) * 12.0;
                        _targetGazeX = _hasInteractiveToyTarget ? _interactiveToyNormX : Math.Sin(p * Math.PI) * 0.8;
                        _targetGazeY = _hasInteractiveToyTarget ? _interactiveToyNormY : -0.4;
                        break;
                    case PetSpecialActionKind.BinkyFlip:
                        double binkyArc = Math.Sin(p * Math.PI);
                        petOffsetY -= binkyArc * 8.2;
                        rotateAngle += p * 360.0;
                        scaleY += 0.12 * (1.0 - binkyArc);
                        break;
                    case PetSpecialActionKind.FoxfireDance:
                        double foxArc = Math.Sin(p * Math.PI);
                        petOffsetY -= foxArc * 4.8;
                        rotateAngle = Math.Sin(p * Math.PI * 4.0) * 14.0;
                        _targetGazeX = Math.Cos(p * Math.PI * 2.0) * 0.7;
                        _targetGazeY = Math.Sin(p * Math.PI * 2.0) * 0.5;
                        break;
                    case PetSpecialActionKind.KungFuRoll:
                        double rollArc = Math.Sin(p * Math.PI);
                        petOffsetY -= rollArc * 3.8;
                        rotateAngle -= p * 360.0;
                        break;
                    case PetSpecialActionKind.DragonBreath:
                        petOffsetY = -4.5 + Math.Sin(t * 12.0) * 1.5;
                        if (p < 0.35)
                        {
                            double inh = p / 0.35;
                            scaleX += inh * 0.16;
                            scaleY += inh * 0.12;
                            _targetGazeY = -0.5;
                        }
                        else
                        {
                            scaleX -= 0.08;
                            scaleY -= 0.05;
                            _targetGazeX = 0.8;
                            _targetGazeY = 0.1;
                        }
                        break;
                }
            }
            else
            {
                _isDoingSpecialAction = false;
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

        if (_isDoingSpecialAction)
        {
            RenderSpecialAction(dc, t, cx, cy);
        }

        dc.Pop(); // root
    }

}
