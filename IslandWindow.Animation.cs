using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Core;

namespace DynamicIsland;

public partial class IslandWindow
{
    private static readonly CubicEase SharedCubicOut = CreateFrozenEase(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly BackEase SharedPanelBackOut = CreateFrozenEase(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 });
    private static readonly BackEase SharedContainerBackOut = CreateFrozenEase(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 });

    private bool _rendering;
    private bool _pointerInside;
    private TimeSpan _lastFrame;
    private double _appliedWidth = -1;
    private double _appliedHeight = -1;
    private double _appliedRadius = -1;
    private double _appliedOffset = double.NaN;
    private double _appliedDroplet = -1;

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

        // Stiffness scales with speed^2 so natural frequency omega_0 scales linearly with speed
        double stiffness = 320.0 * speed * speed;

        // Damping ratio zeta ranges from 1.02 (critically damped, zero overshoot) at 0% liquid
        // down to 0.36 (bouncy, gelatinous liquid droplet oscillation) at 100% liquid.
        double zeta = 1.02 - (0.66 * liquid);
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

        _height.Stiffness = stiffness;
        _height.Damping = damping;

        // Slightly softer radius spring creates an organic liquid lag on corner rounding
        _radius.Stiffness = stiffness * 0.86;
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

        if (_machine.State == IslandState.Idle)
        {
            if (inside)
            {
                _width.Target = 136;
                _height.Target = 30;
                _radius.Target = 15;
                TriggerLiquidImpulse(140, 45, 12);
            }
            else
            {
                var idle = Shapes[IslandState.Idle];
                _width.Target = idle.Width;
                _height.Target = idle.Height;
                _radius.Target = idle.Radius;
                TriggerLiquidImpulse(-110, -35, -10);
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
        _width.Target = shape.Width;
        _height.Target = shape.Height;
        _radius.Target = shape.Radius;

        if (next == IslandState.Expanded)
        {
            TriggerLiquidImpulse(290, 210, 40);
        }
        else if (previous == IslandState.Expanded)
        {
            TriggerLiquidImpulse(-240, -170, -30);
        }
        else if (next == IslandState.Toast)
        {
            TriggerLiquidImpulse(250, 95, 30);
        }
        else if (next == IslandState.Compact)
        {
            TriggerLiquidImpulse(180, 50, 18);
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

            var fade = new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 190 : 90))
            {
                BeginTime = on ? TimeSpan.FromMilliseconds(45) : TimeSpan.Zero,
                EasingFunction = SharedCubicOut,
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

            var scale = (ScaleTransform)panel.RenderTransform;
            var grow = new DoubleAnimation
            {
                From = on ? (reduced ? 0.98 : 0.92) : null,
                To = on ? 1 : 0.96,
                Duration = TimeSpan.FromMilliseconds(on ? (reduced ? 180 : 320) : 120),
                BeginTime = fade.BeginTime,
                EasingFunction = (on && !reduced) ? SharedPanelBackOut : SharedCubicOut,
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
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
        ApplyGeometry();
        UpdateLiveBars(now.TotalSeconds);
        bool glowSettled = UpdateAmbientHalo();

        if (_width.IsAtRest && _height.IsAtRest && _radius.IsAtRest && _dropletOffset.IsAtRest && !BarsAnimating && glowSettled)
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

    private void ApplyGeometry()
    {
        double w = Math.Max(1, _width.Value);
        double h = Math.Max(1, _height.Value);
        double r = Math.Clamp(_radius.Value, 0, Math.Min(w, h) / 2);
        double offset = _settingsService?.Current.NotchYOffset ?? 0;
        double dropT = _dropletOffset.Value;
        double liquid = IsReducedMotion ? 0.0 : Math.Clamp(_settingsService?.Current.LiquidIntensity ?? 0.65, 0.0, 1.0);

        // Gooey squash-and-stretch velocity deformation (volume-conserving droplet physics)
        double vx = Math.Clamp(_width.Velocity / 1600.0, -1.0, 1.0);
        double vy = Math.Clamp(_height.Velocity / 1300.0, -1.0, 1.0);
        double deformStrength = 0.14 * liquid;
        double deformX = _width.IsAtRest && _height.IsAtRest ? 1.0 : Math.Clamp(1.0 + deformStrength * (vx - 0.45 * vy), 0.84, 1.20);
        double deformY = _width.IsAtRest && _height.IsAtRest ? 1.0 : Math.Clamp(1.0 + deformStrength * (vy - 0.50 * vx), 0.84, 1.20);

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

        // Smoothly expand/contract ExpandedPanel when morphing between normal Expanded (420x190) and Settings (480x336)
        if (_machine.State == IslandState.Expanded && w >= 360 && h >= 160)
        {
            ExpandedPanel.Width = w;
            ExpandedPanel.Height = h;
        }
        else
        {
            ExpandedPanel.Width = Math.Max(420, w);
            ExpandedPanel.Height = Math.Max(190, h);
        }

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
