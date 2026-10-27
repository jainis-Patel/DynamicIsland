using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Core;

namespace DynamicIsland;

public partial class IslandWindow
{
    private static readonly CubicEase SharedCubicOut = CreateFrozenEase(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly QuinticEase SharedQuinticOut = CreateFrozenEase(new QuinticEase { EasingMode = EasingMode.EaseOut });
    private static readonly QuarticEase SharedQuarticOut = CreateFrozenEase(new QuarticEase { EasingMode = EasingMode.EaseOut });
    private static readonly BackEase SharedPanelBackOut = CreateFrozenEase(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.16 });
    private static readonly BackEase SharedContainerBackOut = CreateFrozenEase(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.22 });

    private bool _rendering;
    private bool _pointerInside;
    private TimeSpan _lastFrame;
    private double _appliedWidth = -1;
    private double _appliedHeight = -1;
    private double _appliedRadius = -1;
    private double _appliedOffset = double.NaN;
    private double _appliedDroplet = -1;
    private double _smoothDeformX = 1.0;
    private double _smoothDeformY = 1.0;

    private static T CreateFrozenEase<T>(T ease) where T : Freezable
    {
        ease.Freeze();
        return ease;
    }

    private bool IsReducedMotion =>
        (_settingsService?.Current.ReduceMotion ?? false) || !SystemParameters.ClientAreaAnimation;

    /// <summary>
    /// Computes analytical spring stiffness and damping from user-configured gooey/liquid intensity (0..1)
    /// and animation speed multiplier (0.5..1.6).
    /// </summary>
    internal static (double Stiffness, double Damping) ComputeSpringParameters(double liquidIntensity, double animationSpeed, bool reducedMotion)
    {
        double speed = Math.Clamp(animationSpeed, 0.5, 1.6);
        double liquid = reducedMotion ? 0.0 : Math.Clamp(liquidIntensity, 0.0, 1.0);

        // Smooth Apple-tuned natural frequency: softer stiffness (210 * speed^2) yields a
        // graceful, silky ~360ms expansion glide rather than a harsh mechanical snap.
        double stiffness = 210.0 * speed * speed;

        // Damping ratio zeta ranges from 1.00 (critically damped, pure smooth ease-out) at 0% liquid
        // down to 0.56 (plush, velvety single-cushion liquid settle without jittery multi-ringing) at 100% liquid.
        double zeta = 1.00 - (0.44 * liquid);
        double damping = 2.0 * zeta * Math.Sqrt(stiffness);
        return (stiffness, damping);
    }

    private void SyncSpringMotionPreferences()
    {
        var cur = _settingsService?.Current;
        var (stiffness, damping) = ComputeSpringParameters(
            cur?.LiquidIntensity ?? 0.65,
            cur?.AnimationSpeed ?? 1.0,
            IsReducedMotion);

        _width.Stiffness = stiffness;
        _width.Damping = damping;

        // Slightly softer vertical & corner springs so width leads gently and height unfolds smoothly behind it
        _height.Stiffness = stiffness * 0.88;
        _height.Damping = damping * 0.96;

        _radius.Stiffness = stiffness * 0.80;
        _radius.Damping = damping * 0.92;

        _dropletOffset.Stiffness = stiffness * 0.90;
        _dropletOffset.Damping = damping * 0.88;
    }

    private void UpdateSecondaryDropletTarget()
    {
        bool showDroplet = _machine.State == IslandState.Compact && _secondaryCompactHost != CompactActivityKind.None;
        double newTarget = showDroplet ? 1.0 : 0.0;
        if (Math.Abs(_dropletOffset.Target - newTarget) > 0.01)
        {
            _dropletOffset.Target = newTarget;
            TriggerLiquidImpulse(showDroplet ? 140 : -120, showDroplet ? -35 : 30, 12);
        }

        if (showDroplet && SecondaryDropletGlyph != null)
        {
            SecondaryDropletGlyph.Text = ActivityResolver.GlyphForActivity(_secondaryCompactHost);
        }
        EnsureRendering();
    }

    /// <summary>
    /// Injects a gooey velocity impulse into the analytical springs, creating a liquid droplet
    /// swell and squash-and-stretch oscillation proportional to <c>LiquidIntensity</c>.
    /// </summary>
    private void TriggerLiquidImpulse(double widthVel, double heightVel, double radiusVel = 0)
    {
        if (IsReducedMotion) return;
        double liquid = Math.Clamp(_settingsService?.Current.LiquidIntensity ?? 0.65, 0.0, 1.0);
        if (liquid <= 0.01) return;

        double scale = 0.25 + 1.25 * liquid;
        _width.Velocity += widthVel * scale;
        _height.Velocity += heightVel * scale;
        _radius.Velocity += radiusVel * scale;
        _appliedWidth = -1;
        EnsureRendering();
    }

    private bool BarsAnimating =>
        _machine.State == IslandState.Compact &&
        ((_mediaService.Current?.IsPlaying ?? false) ||
         _stopwatch.IsRunning ||
         (_countdownService?.IsRunning ?? false) ||
         !string.IsNullOrWhiteSpace(_privacyService.ActiveCallApp) ||
         (_voiceMemoService?.IsRecording ?? false));

    /// <summary>
    /// Enables WASAPI loopback capture &amp; FFT only when the visual state actually consumes audio spectrum data.
    /// </summary>
    private void UpdateSpectrumActivity()
    {
        bool mediaPlaying = _mediaService.Current?.IsPlaying ?? false;
        bool visible = _machine.State != IslandState.Hidden;
        bool needsCompactBars = _machine.State == IslandState.Compact && mediaPlaying;
        bool needsAmbientBass = visible && mediaPlaying && (_settingsService?.Current.EnableAmbientGlow ?? true);

        _audioSpectrum.SetActive(needsCompactBars || needsAmbientBass);
    }

    private void OnPointerInsideChanged(bool inside)
    {
        _pointerInside = inside;
        _machine.SetPointerInside(inside);

        // Gentle magnetic pre-hover swell in Idle or Compact before full expansion
        // (uses pure spring target change with zero velocity jump so it eases in smoothly from rest)
        if (_machine.State is IslandState.Idle or IslandState.Compact)
        {
            var baseShape = Shapes[_machine.State];
            if (inside)
            {
                _width.Target = baseShape.Width + 18;
                _height.Target = baseShape.Height + 3;
                _radius.Target = baseShape.Radius + 1.5;
            }
            else
            {
                _width.Target = baseShape.Width;
                _height.Target = baseShape.Height;
                _radius.Target = baseShape.Radius;
            }
            EnsureRendering();
        }
    }

    private void OnStateChanged(IslandState previous, IslandState next)
    {
        SyncSpringMotionPreferences();

        if (previous == IslandState.Expanded && next != IslandState.Expanded)
        {
            _currentExpandedView = ExpandedView.Media;
            SetKeyboardFocusable(false);
            SetDockActive(MediaHomeButton);
            foreach (var c in _allContainers)
            {
                bool isMedia = c == MediaContainer;
                c.BeginAnimation(OpacityProperty, null);
                c.Visibility = isMedia ? Visibility.Visible : Visibility.Collapsed;
                c.Opacity = isMedia ? 1 : 0;
            }
        }

        var shape = GetTargetShape(next);
        if (next == IslandState.Expanded)
        {
            ExpandedPanel.Width = shape.Width;
            ExpandedPanel.Height = shape.Height;
        }

        _width.Target = shape.Width;
        _height.Target = shape.Height;
        _radius.Target = shape.Radius;

        // LetExpanded/Idle transitions ease in naturally from current spring momentum (dx/dt continuous)
        // instead of injecting a jarring step-velocity jump; only apply a gentle impulse on Toast alerts.
        if (next == IslandState.Toast)
        {
            TriggerLiquidImpulse(95, 35, 10);
        }

        CrossfadeTo(next);

        if (next == IslandState.Hidden)
        {
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
        }
        else if (previous == IslandState.Hidden)
        {
            ReassertTopmost();
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
        }

        if (next == IslandState.Expanded)
        {
            UpdateMediaTimelineUI();
        }

        _telemetryService.SetActive(next == IslandState.Expanded);
        UpdateSecondaryDropletTarget();
        UpdateSpectrumActivity();
        UpdateClockTimerInterval();
        EnsureRendering();
    }

    private void OnToastChanged(ToastContent toast, bool replacing)
    {
        ToastGlyph.Text = toast.Glyph;
        ToastGlyph.Foreground = ThemeBrushes.CreateFrozen(toast.Accent);
        ToastIconBg.Background = ThemeBrushes.CreateFrozen(Color.FromArgb(0x33, toast.Accent.R, toast.Accent.G, toast.Accent.B));
        ToastTitle.Text = toast.Title;
        ToastSubtitle.Text = toast.Subtitle;
        PillGlowColor.Color = toast.Accent;
        PillGlowSecondaryColor.Color = toast.Accent;

        if (replacing && !IsReducedMotion)
        {
            TriggerLiquidImpulse(280, 105, 30);
        }
    }

    private FrameworkElement PanelFor(IslandState state) => state switch
    {
        IslandState.Compact => CompactPanel,
        IslandState.Toast => ToastPanel,
        IslandState.Expanded => ExpandedPanel,
        _ => IdlePanel,
    };

    private void CrossfadeTo(IslandState state)
    {
        bool reduced = IsReducedMotion;
        var active = PanelFor(state);
        foreach (var panel in _panels)
        {
            bool on = panel == active;
            panel.IsHitTestVisible = on;

            if (on)
            {
                panel.Visibility = Visibility.Visible;
            }
            else if (panel.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            var fade = new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 240 : 105))
            {
                BeginTime = on ? TimeSpan.FromMilliseconds(28) : TimeSpan.Zero,
                EasingFunction = on ? SharedQuinticOut : SharedCubicOut,
            };

            if (!on)
            {
                var captured = panel;
                fade.Completed += (_, _) =>
                {
                    if (captured != PanelFor(_machine.State))
                    {
                        captured.Visibility = Visibility.Collapsed;
                    }
                };
            }

            panel.BeginAnimation(OpacityProperty, fade);

            if (panel.RenderTransform is TransformGroup group &&
                group.Children.Count >= 2 &&
                group.Children[0] is ScaleTransform scale &&
                group.Children[1] is TranslateTransform translate)
            {
                bool isExpandedPanel = panel == ExpandedPanel;
                var grow = new DoubleAnimation
                {
                    From = on ? (reduced ? 0.98 : (isExpandedPanel ? 0.93 : 0.95)) : null,
                    To = on ? 1.0 : (isExpandedPanel ? 0.95 : 0.97),
                    Duration = TimeSpan.FromMilliseconds(on ? (reduced ? 180 : 350) : 130),
                    BeginTime = fade.BeginTime,
                    EasingFunction = (on && !reduced) ? SharedPanelBackOut : SharedQuarticOut,
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

                double fromY = on ? (reduced ? 0 : (isExpandedPanel ? -7 : -3)) : translate.Y;
                double toY = on ? 0 : (reduced ? 0 : -4);
                var slideY = new DoubleAnimation
                {
                    From = on ? fromY : null,
                    To = toY,
                    Duration = TimeSpan.FromMilliseconds(on ? (reduced ? 160 : 330) : 115),
                    BeginTime = fade.BeginTime,
                    EasingFunction = SharedQuinticOut,
                };
                translate.BeginAnimation(TranslateTransform.YProperty, slideY);
            }
        }
    }

    private void SetDockActive(Button? btn)
    {
        foreach (var b in _dockButtons)
        {
            b.Background = ThemeBrushes.DockInactiveBg;
            b.BorderBrush = ThemeBrushes.DockInactiveBg;
        }

        if (btn != null)
        {
            btn.Background = ThemeBrushes.DockActiveBg;
            btn.BorderBrush = ThemeBrushes.DockActiveBorder;
        }
    }

    private void AnimateContainerSwitch(FrameworkElement newContainer)
    {
        bool reduced = IsReducedMotion;
        foreach (var c in _allContainers)
        {
            if (c != newContainer && c.Visibility == Visibility.Visible)
            {
                var target = c;
                var fadeOut = new DoubleAnimation(target.Opacity, 0, TimeSpan.FromMilliseconds(100));
                fadeOut.Completed += (_, _) =>
                {
                    if (target != _allContainers.FirstOrDefault(x => x.Opacity > 0.5))
                        target.Visibility = Visibility.Collapsed;
                };
                target.BeginAnimation(OpacityProperty, fadeOut);
            }
        }

        newContainer.Visibility = Visibility.Visible;
        if (newContainer.RenderTransform is not TranslateTransform tt)
        {
            tt = new TranslateTransform();
            newContainer.RenderTransform = tt;
        }

        tt.Y = reduced ? 0 : 6;
        newContainer.Opacity = 0;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = SharedCubicOut
        };
        var slideIn = new DoubleAnimation(reduced ? 0 : 6, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = reduced ? SharedCubicOut : SharedContainerBackOut
        };

        newContainer.BeginAnimation(OpacityProperty, fadeIn);
        tt.BeginAnimation(TranslateTransform.YProperty, slideIn);
    }

    // ═════════════════════════════════════════════════════════════════
    //  Per-frame spring + audio waveform + ambient glow loop
    // ═════════════════════════════════════════════════════════════════

    private void EnsureRendering()
    {
        if (_rendering) return;
        _rendering = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopRendering()
    {
        if (!_rendering) return;
        CompositionTarget.Rendering -= OnRendering;
        _rendering = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return;
        double dt = _lastFrame == TimeSpan.Zero ? 1.0 / 60 : (now - _lastFrame).TotalSeconds;
        _lastFrame = now;

        _width.Step(dt);
        _height.Step(dt);
        _radius.Step(dt);
        _dropletOffset.Step(dt);
        ApplyGeometry(dt);
        UpdateLiveBars(now.TotalSeconds);
        bool glowSettled = UpdateAmbientHalo();

        if (_width.IsAtRest && _height.IsAtRest && _radius.IsAtRest && _dropletOffset.IsAtRest &&
            Math.Abs(_smoothDeformX - 1.0) < 0.001 && Math.Abs(_smoothDeformY - 1.0) < 0.001 &&
            !BarsAnimating && glowSettled)
        {
            StopRendering();
        }
    }

    private bool UpdateAmbientHalo()
    {
        if (!(_settingsService?.Current.EnableAmbientGlow ?? true))
        {
            if (PillGlow.Visibility != Visibility.Collapsed)
            {
                PillGlow.Opacity = 0;
                PillGlow.Visibility = Visibility.Collapsed;
            }
            return true;
        }

        bool isMusicPlaying = (_mediaService.Current?.IsPlaying ?? false) && _machine.State != IslandState.Hidden;
        double targetGlow;
        double rate;

        if (isMusicPlaying)
        {
            if (_machine.State != IslandState.Compact)
            {
                _audioSpectrum.ReadBands(_bandLevels);
                _smoothBands[0] += (_bandLevels[0] - _smoothBands[0]) * 0.40f;
            }
            double bass = _smoothBands[0];
            targetGlow = 0.22 + 0.58 * Math.Clamp(bass, 0, 1);
            rate = 0.35;
        }
        else if (_machine.State == IslandState.Expanded)
        {
            targetGlow = 0.28;
            rate = 0.22;
        }
        else if (_machine.State == IslandState.Toast)
        {
            targetGlow = 0.35;
            rate = 0.30;
        }
        else if (_machine.State == IslandState.Idle && _pointerInside)
        {
            targetGlow = 0.28;
            rate = 0.25;
        }
        else
        {
            targetGlow = 0.0;
            rate = 0.22;
        }

        double nextOpacity = PillGlow.Opacity + (targetGlow - PillGlow.Opacity) * rate;
        bool settled = !isMusicPlaying && Math.Abs(targetGlow - nextOpacity) < 0.005;
        if (settled)
        {
            nextOpacity = targetGlow;
        }

        PillGlow.Opacity = nextOpacity;
        Visibility desiredVis = nextOpacity > 0.004 ? Visibility.Visible : Visibility.Collapsed;
        if (PillGlow.Visibility != desiredVis)
        {
            PillGlow.Visibility = desiredVis;
        }

        return settled;
    }

    private void ApplyGeometry(double dt = 1.0 / 60.0)
    {
        double w = Math.Max(1, _width.Value);
        double h = Math.Max(1, _height.Value);
        double r = Math.Clamp(_radius.Value, 0, Math.Min(w, h) / 2);
        double offset = _settingsService?.Current.NotchYOffset ?? 0;
        double dropT = _dropletOffset.Value;
        double liquid = IsReducedMotion ? 0.0 : Math.Clamp(_settingsService?.Current.LiquidIntensity ?? 0.65, 0.0, 1.0);

        // Smoothly filtered gooey squash-and-stretch velocity deformation (zero first-frame snap)
        double vx = Math.Clamp(_width.Velocity / 2200.0, -1.0, 1.0);
        double vy = Math.Clamp(_height.Velocity / 1800.0, -1.0, 1.0);
        double deformStrength = 0.085 * liquid;
        double targetDeformX = _width.IsAtRest && _height.IsAtRest ? 1.0 : Math.Clamp(1.0 + deformStrength * (vx - 0.35 * vy), 0.90, 1.12);
        double targetDeformY = _width.IsAtRest && _height.IsAtRest ? 1.0 : Math.Clamp(1.0 + deformStrength * (vy - 0.35 * vx), 0.90, 1.12);

        double smoothFactor = Math.Clamp(dt * 22.0, 0.08, 1.0);
        _smoothDeformX += (targetDeformX - _smoothDeformX) * smoothFactor;
        _smoothDeformY += (targetDeformY - _smoothDeformY) * smoothFactor;
        if (_width.IsAtRest && _height.IsAtRest && Math.Abs(_smoothDeformX - 1.0) < 0.001 && Math.Abs(_smoothDeformY - 1.0) < 0.001)
        {
            _smoothDeformX = 1.0;
            _smoothDeformY = 1.0;
        }
        double deformX = _smoothDeformX;
        double deformY = _smoothDeformY;

        if (Math.Abs(w - _appliedWidth) < 0.005 &&
            Math.Abs(h - _appliedHeight) < 0.005 &&
            Math.Abs(r - _appliedRadius) < 0.005 &&
            Math.Abs(offset - _appliedOffset) < 0.005 &&
            Math.Abs(dropT - _appliedDroplet) < 0.002 &&
            Math.Abs(PillDeformScale.ScaleX - deformX) < 0.002 &&
            Math.Abs(PillDeformScale.ScaleY - deformY) < 0.002)
        {
            return;
        }

        _appliedWidth = w;
        _appliedHeight = h;
        _appliedRadius = r;
        _appliedOffset = offset;
        _appliedDroplet = dropT;

        PillDeformScale.ScaleX = deformX;
        PillDeformScale.ScaleY = deformY;

        if (SecondaryDroplet != null)
        {
            if (dropT > 0.01)
            {
                SecondaryDroplet.Visibility = Visibility.Visible;
                SecondaryDroplet.Opacity = Math.Clamp(dropT * 1.25, 0.0, 1.0);
                SecondaryDropletTranslate.X = (w / 2.0 + 24.0) * dropT;
                SecondaryDropletTranslate.Y = offset;
                double dropVel = Math.Clamp(_dropletOffset.Velocity / 8.0, -0.35, 0.35) * liquid;
                SecondaryDropletScale.ScaleX = Math.Clamp(0.45 + 0.55 * dropT + dropVel, 0.3, 1.28);
                SecondaryDropletScale.ScaleY = Math.Clamp(0.45 + 0.55 * dropT - dropVel * 0.65, 0.3, 1.22);
            }
            else if (SecondaryDroplet.Visibility != Visibility.Collapsed)
            {
                SecondaryDroplet.Visibility = Visibility.Collapsed;
                SecondaryDroplet.Opacity = 0;
            }
        }

        // Dynamic liquid droplet corner-rounding boost during high-velocity morphs
        double motionEnergy = Math.Min(1.0, (Math.Abs(vx) + Math.Abs(vy)) * 0.65);
        double effectiveR = Math.Clamp(r + (r * 0.22 * liquid * motionEnergy), 0, Math.Min(w, h) / 2);
        double topR = Math.Clamp((offset * 1.5) + (effectiveR * 0.18 * liquid * motionEnergy), 0, effectiveR);

        Pill.Width = w;
        Pill.Height = h;

        var corners = new CornerRadius(topR, topR, effectiveR, effectiveR);
        PillBackground.CornerRadius = corners;
        PillStroke.CornerRadius = corners;
        DropOverlay.CornerRadius = corners;

        _contentClip.Rect = new Rect(0, topR > 0 ? 0 : -effectiveR, w, topR > 0 ? h : h + effectiveR);
        _contentClip.RadiusX = effectiveR;
        _contentClip.RadiusY = effectiveR;

        PillGlow.Width = w + 56;
        PillGlow.Height = h + 32;
        double glowTopR = topR > 0 ? topR + 12 : 0;
        PillGlow.CornerRadius = new CornerRadius(glowTopR, glowTopR, effectiveR + 12, effectiveR + 12);
    }

    private void UpdateLiveBars(double t)
    {
        if (_machine.State != IslandState.Compact) return;

        _audioSpectrum.ReadBands(_bandLevels);
        bool isMusicPlaying = _mediaService.Current?.IsPlaying ?? false;
        bool isTimerRunning = _stopwatch.IsRunning || (_countdownService?.IsRunning ?? false);
        bool isCallActive = !string.IsNullOrWhiteSpace(_privacyService.ActiveCallApp);
        bool isRecording = _voiceMemoService?.IsRecording ?? false;

        for (int i = 0; i < _bars.Length; i++)
        {
            float target = _bandLevels[i];

            if (isRecording && _voiceMemoService != null)
            {
                float peak = _voiceMemoService.PeakLevel;
                double wave = peak * (0.65 + 0.35 * Math.Sin(t * 7.5 + i * 1.5));
                target = (float)wave;
            }
            else if (target < 0.08f && (isTimerRunning || isCallActive))
            {
                double wave = (0.5 + 0.5 * Math.Sin(t * (5.5 + i * 1.8) + i * 1.3)) * 0.72;
                target = (float)wave;
            }
            else if (!isMusicPlaying && !isTimerRunning && !isCallActive)
            {
                target = 0f;
            }

            _smoothBands[i] += (target - _smoothBands[i]) * 0.40f;
            _bars[i].ScaleY = 0.20 + 0.80 * Math.Clamp(_smoothBands[i], 0, 1);
        }
    }
}
