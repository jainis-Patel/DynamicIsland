using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Core;
using DynamicIsland.Services;

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
    private Point _lastMousePos;
    private DateTime _lastMouseMoveTime;
    private bool _isPetScampering;
    private double _scamperStartTime;
    private double _nextScamperAllowedTime = 120.0;

    public void TriggerPetScamper()
    {
        if (_isPetScampering || _petService == null || !(_settingsService?.Current.EnableVirtualPet ?? true)) return;
        var state = _machine.State;
        if (state != IslandState.Idle && state != IslandState.Compact) return;

        _isPetScampering = true;
        _scamperStartTime = _lastFrame.TotalSeconds;
        EnsureRendering();
    }

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

        // Apple Fluid Natural Frequency: smooth, silky, continuous (230 * speed^2)
        double stiffness = 230.0 * speed * speed;

        // Damping ratio zeta: 1.00 (critically damped) at 0% down to 0.68 (Apple canonical fluid cushion) at 100% liquid
        double zeta = 1.00 - (0.32 * liquid);
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

        // Harmonious, unified fluid glide: width and height move together with zero stepping
        _height.Stiffness = stiffness * 0.92;
        _height.Damping = damping * 0.96;

        _radius.Stiffness = stiffness * 0.92;
        _radius.Damping = damping * 0.96;

        _dropletOffset.Stiffness = stiffness * 0.92;
        _dropletOffset.Damping = damping * 0.94;
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

        if (showDroplet)
        {
            SolidColorBrush accent = ThemeBrushes.AmberDark;
            string glyph = ActivityResolver.GlyphForActivity(_secondaryCompactHost);
            string tip = ActivityResolver.DescribeSecondaryDroplet(_secondaryCompactHost);

            switch (_secondaryCompactHost)
            {
                case CompactActivityKind.Media:
                    accent = ThemeBrushes.BlueDark;
                    break;
                case CompactActivityKind.Call:
                    accent = ThemeBrushes.GreenDark;
                    break;
                case CompactActivityKind.Recording:
                    accent = ThemeBrushes.RedDark;
                    break;
                case CompactActivityKind.Download:
                    accent = ThemeBrushes.BlueDark;
                    break;
                case CompactActivityKind.LiveActivity:
                    if (_liveActivityHub?.Secondary is { } sec)
                    {
                        glyph = !string.IsNullOrWhiteSpace(sec.Glyph) ? sec.Glyph : "\uE7C3";
                        tip = $"{sec.Title} \u00B7 {sec.Subtitle} (Click to swap)";
                        if (sec.AccentColor != default)
                        {
                            accent = new SolidColorBrush(sec.AccentColor);
                            accent.Freeze();
                        }
                    }
                    break;
                case CompactActivityKind.Countdown:
                case CompactActivityKind.Stopwatch:
                default:
                    accent = ThemeBrushes.AmberDark;
                    break;
            }

            if (SecondaryDropletGlyph != null)
            {
                SecondaryDropletGlyph.Text = glyph;
                SecondaryDropletGlyph.Foreground = accent;
            }
            if (SecondaryDropletRing != null)
            {
                SecondaryDropletRing.Stroke = accent;
            }
            if (SecondaryDroplet != null)
            {
                SecondaryDroplet.ToolTip = tip;
            }
        }
        EnsureRendering();
    }

    /// <summary>
    /// Injects a gooey velocity impulse into the analytical springs, creating a liquid droplet
    /// swell and squash-and-stretch oscillation proportional to <c>LiquidIntensity</c>.
    /// </summary>
    internal void TriggerLiquidImpulse(double widthVel, double heightVel, double radiusVel = 0)
    {
        if (IsReducedMotion) return;
        double liquid = Math.Clamp(_settingsService?.Current.LiquidIntensity ?? 0.65, 0.0, 1.0);
        if (liquid <= 0.01) return;

        double scale = 0.20 + 0.80 * liquid;
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
        bool needsAmbientBass = visible && mediaPlaying &&
            (_settingsService?.Current.EnableAmbientGlow ?? true) &&
            !(_choreographer?.ShouldThrottleVisuals ?? false);

        _audioSpectrum.SetActive(needsCompactBars || needsAmbientBass);
    }

    private bool _isModalDialogOpen;

    private bool ShouldHoldExpandedOpen =>
        _machine.IsPinned ||
        _isModalDialogOpen ||
        _recordingHotkeyAction != null ||
        (FloatingDockHost != null && (FloatingDockHost.IsMouseOver || FloatingDockHost.IsKeyboardFocusWithin)) ||
        (Pill != null && Pill.IsMouseOver) ||
        (CalcInputBox != null && CalcInputBox.IsKeyboardFocused && !string.IsNullOrWhiteSpace(CalcInputBox.Text)) ||
        (ClipboardSearchBox != null && ClipboardSearchBox.IsKeyboardFocused);

    private void OnPointerInsideChanged(bool inside)
    {
        if (_isTucked) return;

        bool dockHovered = FloatingDockHost != null && (FloatingDockHost.IsMouseOver || FloatingDockHost.IsKeyboardFocusWithin);
        bool pillHovered = Pill != null && Pill.IsMouseOver;
        bool effectiveInside = inside || dockHovered || pillHovered;

        if (!effectiveInside && _isGhostVisualActive)
        {
            NotifyGhostPassthroughHover(false);
        }

        bool wasInside = _pointerInside;
        _pointerInside = effectiveInside;
        if (effectiveInside) _memoryManager?.NotifyActivity();
        if (!effectiveInside && _machine.State == IslandState.Expanded && ShouldHoldExpandedOpen)
        {
            _machine.SetPointerInside(true);
        }
        else
        {
            _machine.SetPointerInside(effectiveInside);
        }

        // Tactile magnetic hover swell & press-down tension
        if (_machine.State is IslandState.Idle or IslandState.Compact)
        {
            RefreshTargetGeometry();
        }
    }

    private void OnStateChanged(IslandState previous, IslandState next)
    {
        SyncSpringMotionPreferences();

        if (previous == IslandState.Expanded && next != IslandState.Expanded)
        {
            if (_currentExpandedView == ExpandedView.SystemHud)
            {
                _systemPerfService?.SetHudExpanded(false);
            }
            else if (_currentExpandedView == ExpandedView.Network)
            {
                _networkService?.SetExpanded(false);
            }
            _currentExpandedView = ExpandedView.Media;
            SetKeyboardFocusable(false);
            UpdatePinExpandedUI();
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

        if (next == IslandState.Toast)
        {
            TriggerLiquidImpulse(95, 30, 8);
        }

        if (next == IslandState.Expanded || (previous == IslandState.Expanded && next != IslandState.Hidden))
        {
            TactileFeedback.PlaySnap();
        }

        CrossfadeTo(next);

        if (next == IslandState.Hidden)
        {
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
            _topmostTimer.Stop(); // P1a perf: no need to reassert topmost while hidden
        }
        else if (previous == IslandState.Hidden)
        {
            _topmostTimer.Start(); // P1a perf: resume topmost reassertion when visible
            ReassertTopmost();
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
        }

        if (next == IslandState.Expanded)
        {
            UpdateMediaTimelineUI();
        }

        UpdateSecondaryDropletTarget();
        UpdateSpectrumActivity();
        UpdateClockTimerInterval();
        EnsureRendering();
        _memoryManager?.NotifyStateChanged(next);

        if (next is IslandState.Expanded or IslandState.Idle)
        {
            App.LogMemorySnapshot($"StateChanged: {previous} -> {next}");
        }
    }

    private Color _lastToastAccent;
    private SolidColorBrush? _lastToastFgBrush;
    private SolidColorBrush? _lastToastBgBrush;

    private void OnToastChanged(ToastContent toast, bool replacing)
    {
        _memoryManager?.NotifyActivity();
        ToastGlyph.Text = toast.Glyph;
        if (_lastToastFgBrush == null || _lastToastBgBrush == null || _lastToastAccent != toast.Accent)
        {
            _lastToastAccent = toast.Accent;
            _lastToastFgBrush = ThemeBrushes.CreateFrozen(toast.Accent);
            _lastToastBgBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x33, toast.Accent.R, toast.Accent.G, toast.Accent.B));
        }
        ToastGlyph.Foreground = _lastToastFgBrush;
        ToastIconBg.Background = _lastToastBgBrush;
        ToastTitle.Text = toast.Title;
        ToastSubtitle.Text = toast.Subtitle;
        PillGlowColor.Color = toast.Accent;
        PillGlowSecondaryColor.Color = toast.Accent;

        if (ToastReplyButton != null)
        {
            if (toast.CanReply)
            {
                ToastReplyButton.Visibility = Visibility.Visible;
                ToastReplyButton.Tag = toast.HistoryItem;
                if (ToastReplyGlyph != null) ToastReplyGlyph.Foreground = _lastToastFgBrush;
                if (ToastReplyText != null) ToastReplyText.Foreground = _lastToastFgBrush;
            }
            else
            {
                ToastReplyButton.Visibility = Visibility.Collapsed;
            }
        }

        if (ToastActionsHost != null)
        {
            bool isPrompt = toast.OnPromptDecision != null || !string.IsNullOrWhiteSpace(toast.PromptActivityId);
            if (isPrompt)
            {
                ToastActionsHost.Visibility = Visibility.Visible;
                if (ToastReplyButton != null) ToastReplyButton.Visibility = Visibility.Collapsed;
                if (ToastDiffBtn != null)
                {
                    ToastDiffBtn.Visibility = !string.IsNullOrWhiteSpace(toast.DiffText) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            else
            {
                ToastActionsHost.Visibility = Visibility.Collapsed;
            }
        }

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

        AnimateFloatingDock(state == IslandState.Expanded);
    }

    private void AnimateFloatingDock(bool on)
    {
        if (FloatingDockHost == null) return;
        bool reduced = IsReducedMotion;
        FloatingDockHost.IsHitTestVisible = on;

        if (on)
        {
            FloatingDockHost.Visibility = Visibility.Visible;
        }

        var fade = new DoubleAnimation(on ? 1.0 : 0.0, TimeSpan.FromMilliseconds(on ? 240 : 110))
        {
            BeginTime = on ? TimeSpan.FromMilliseconds(35) : TimeSpan.Zero,
            EasingFunction = on ? SharedQuinticOut : SharedCubicOut,
        };

        if (!on)
        {
            fade.Completed += (_, _) =>
            {
                if (_machine.State != IslandState.Expanded)
                {
                    FloatingDockHost.Visibility = Visibility.Collapsed;
                }
            };
        }

        FloatingDockHost.BeginAnimation(OpacityProperty, fade);

        if (FloatingDockScale != null)
        {
            var grow = new DoubleAnimation
            {
                From = on ? (reduced ? 0.98 : 0.92) : null,
                To = on ? 1.0 : 0.94,
                Duration = TimeSpan.FromMilliseconds(on ? (reduced ? 160 : 340) : 120),
                BeginTime = fade.BeginTime,
                EasingFunction = (on && !reduced) ? SharedPanelBackOut : SharedQuarticOut,
            };
            FloatingDockScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            FloatingDockScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
    }

    private void SetDockActive(Button? btn)
    {
        var inactiveBrush = ThemeBrushes.DockInactiveText;
        var activeBrush = Resources["AccentBrush"] as Brush ?? ThemeBrushes.PrimaryText;

        foreach (var b in _dockButtons)
        {
            b.Background = Brushes.Transparent;
            b.BorderBrush = Brushes.Transparent;
            b.BorderThickness = new Thickness(0);
            b.Foreground = inactiveBrush;
            UpdateDockButtonContentVisual(b, inactiveBrush, isActive: false);
        }

        if (btn != null)
        {
            btn.Background = ThemeBrushes.DockActiveBg;
            btn.BorderBrush = ThemeBrushes.DockActiveBorder;
            btn.BorderThickness = new Thickness(1);
            btn.Foreground = activeBrush;
            UpdateDockButtonContentVisual(btn, activeBrush, isActive: true);

            try
            {
                btn.BringIntoView();
            }
            catch { }
        }
    }

    private static void UpdateDockButtonContentVisual(Button button, Brush targetFg, bool isActive)
    {
        if (button.Content is TextBlock tb)
        {
            tb.Foreground = targetFg;
        }
        else if (button.Content is Panel panel)
        {
            UpdatePanelChildrenColors(panel, targetFg, isActive);
        }
    }

    private static void UpdatePanelChildrenColors(Panel panel, Brush targetFg, bool isActive)
    {
        foreach (UIElement child in panel.Children)
        {
            if (child is TextBlock tb)
            {
                if (tb.Name.Contains("Badge", StringComparison.OrdinalIgnoreCase))
                {
                    tb.Foreground = isActive ? targetFg : ThemeBrushes.SecondaryText;
                }
                else
                {
                    tb.Foreground = targetFg;
                }
            }
            else if (child is Panel innerPanel)
            {
                UpdatePanelChildrenColors(innerPanel, targetFg, isActive);
            }
            else if (child is Border b && b.Child is TextBlock borderTb)
            {
                borderTb.Foreground = targetFg;
            }
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
                    if (target != newContainer)
                        target.Visibility = Visibility.Collapsed;
                };
                target.BeginAnimation(OpacityProperty, fadeOut);
            }
        }

        newContainer.Visibility = Visibility.Visible;
        TranslateTransform tt;
        if (newContainer.RenderTransform is TransformGroup group &&
            group.Children.Count >= 2 &&
            group.Children[0] is ScaleTransform st &&
            group.Children[1] is TranslateTransform groupTt)
        {
            st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            st.ScaleX = 1.0;
            st.ScaleY = 1.0;
            groupTt.BeginAnimation(TranslateTransform.XProperty, null);
            groupTt.X = 0.0;
            tt = groupTt;
        }
        else if (newContainer.RenderTransform is TranslateTransform existingTt)
        {
            tt = existingTt;
        }
        else
        {
            var newGroup = new TransformGroup();
            newGroup.Children.Add(new ScaleTransform(1.0, 1.0));
            tt = new TranslateTransform(0.0, 0.0);
            newGroup.Children.Add(tt);
            newContainer.RenderTransform = newGroup;
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

    private void StartRendering() => EnsureRendering();

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

        // P0 perf: Render one final glass frame at the settled geometry so the
        // at-rest pill has a correct Liquid Glass bitmap even though the FrameReady
        // handler now skips dispatches when _rendering is false.
        if (ThemeManager.IsLiquidGlass && DynamicIsland.LiquidGlass.GlassManager.Instance.IsEnabled)
        {
            UpdatePillLiquidGlass(Pill.ActualWidth > 0 ? Pill.ActualWidth : 148,
                Pill.ActualHeight > 0 ? Pill.ActualHeight : 30,
                15.0f, _dropletOffset?.Value ?? 0);
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return;
        double dt = _lastFrame == TimeSpan.Zero ? 1.0 / 60 : (now - _lastFrame).TotalSeconds;
        _lastFrame = now;

        double liquid = IsReducedMotion ? 0.0 : Math.Clamp(_settingsService?.Current.LiquidIntensity ?? 0.65, 0.0, 1.0);

        _width.Step(dt);
        _height.Step(dt);
        _radius.Step(dt);
        _dropletOffset.Step(dt);

        ApplyGeometry(dt);
        UpdateLiveBars(now.TotalSeconds);
        UpdateVirtualPet(now.TotalSeconds);
        UpdateIslandNatureTheme(now.TotalSeconds);
        bool glowSettled = UpdateAmbientHalo();

        bool petActive = (_petService?.IsEnabled ?? false) && 
            (_isPetScampering || _petService.CurrentMood == PetMood.Dancing || _petService.CurrentMood == PetMood.Happy || _petService.CurrentMood == PetMood.Eating ||
             (IdlePetCompanion?.IsAnimatingTrick ?? false) || (CompactPetCompanion?.IsAnimatingTrick ?? false) || (SettingsHeroPet?.IsAnimatingTrick ?? false) ||
             (_pointerInside && (_machine.State == IslandState.Idle || _machine.State == IslandState.Compact || (SettingsHeroPet?.IsVisible ?? false))));

        bool themeActive = _isNatureThemeActive && !IsReducedMotion && _machine.State != IslandState.Hidden;

        if (_width.IsAtRest && _height.IsAtRest && _radius.IsAtRest && _dropletOffset.IsAtRest &&
            !BarsAnimating && glowSettled && !petActive && !themeActive)
        {
            StopRendering();
        }
    }

    private bool _isNatureThemeActive;

    private void UpdateIslandNatureTheme(double t)
    {
        if (!_isNatureThemeActive) return;
        if (_machine.State == IslandState.Hidden) return;

        double pillW = _appliedWidth > 0 ? _appliedWidth : Pill.Width;
        double pillH = _appliedHeight > 0 ? _appliedHeight : Pill.Height;
        double rootW = Root.ActualWidth > 0 ? Root.ActualWidth : CanvasWidthDip;
        double pillX = (rootW - pillW) / 2.0;
        double pillY = 0.0;
        var pillRect = new Rect(pillX, pillY, pillW, pillH);

        var cur = _settingsService?.Current;
        double density = cur?.NatureDensity ?? 0.70;
        double scale = cur?.NatureScale ?? 0.85;
        double dropLength = cur?.NatureDropLength ?? 60.0;
        int butterflyCount = cur?.NatureButterflyCount ?? 2;
        double bloomDensity = cur?.NatureBloomDensity ?? 0.75;
        bool pollenEnabled = cur?.NaturePollenEnabled ?? true;
        int pollenCount = cur?.NaturePollenCount ?? 14;

        IslandThemeUnderlay?.Step(t, pillRect, IsReducedMotion, density, scale, dropLength, butterflyCount, bloomDensity, pollenEnabled, pollenCount);
        IslandThemeOverlay?.Step(t, pillRect, IsReducedMotion, density, scale, dropLength, butterflyCount, bloomDensity, pollenEnabled, pollenCount);
    }

    private void UpdateVirtualPet(double t)
    {
        if (_petService == null) return;
        bool petEnabled = _settingsService?.Current.EnableVirtualPet ?? true;
        var state = _machine.State;

        if (!petEnabled)
        {
            _isPetScampering = false;
            if (IdlePetTranslate != null) { IdlePetTranslate.X = 0; IdlePetTranslate.Y = 0; }
            if (IdlePetScale != null) IdlePetScale.ScaleX = 1;
            if (CompactPetTranslate != null) { CompactPetTranslate.X = 0; CompactPetTranslate.Y = 0; }
            if (CompactPetScale != null) CompactPetScale.ScaleX = 1;
            if (IdlePetCompanion != null && IdlePetCompanion.Visibility != Visibility.Collapsed)
                IdlePetCompanion.Visibility = Visibility.Collapsed;
            if (CompactPetCompanion != null && CompactPetCompanion.Visibility != Visibility.Collapsed)
                CompactPetCompanion.Visibility = Visibility.Collapsed;
            return;
        }

        bool isMusicPlaying = (_mediaService.Current?.IsPlaying ?? false) && state != IslandState.Hidden;
        float bass = _smoothBands[0];
        float treble = _smoothBands[AudioSpectrum.BandCount - 1];

        _petService.UpdateActivity(isMusicPlaying, bass, _pointerInside);

        var mood = _petService.CurrentMood;
        string species = _petService.PetSpecies;
        string accessory = _petService.PetAccessory;
        string petName = _petService.PetName;
        int affection = _petService.AffectionScore;

        // Check automatic scamper timer (every ~2.5 to 4.5 minutes when Idle / Compact)
        if (!_isPetScampering && t >= _nextScamperAllowedTime && (state == IslandState.Idle || state == IslandState.Compact))
        {
            if (mood != PetMood.Sleeping && mood != PetMood.Thirsty)
            {
                TriggerPetScamper();
            }
            else
            {
                _nextScamperAllowedTime = t + 45.0;
            }
        }

        // Scamper animation computation
        double scamperX = 0.0;
        double scamperY = 0.0;
        double scamperScaleX = 1.0;
        bool isScampering = false;
        bool isPeeking = false;

        if (_isPetScampering)
        {
            if (state != IslandState.Idle && state != IslandState.Compact)
            {
                _isPetScampering = false;
            }
            else
            {
                double elapsed = t - _scamperStartTime;
                const double TotalScamperDuration = 5.2;

                if (elapsed >= TotalScamperDuration)
                {
                    _isPetScampering = false;
                    _nextScamperAllowedTime = t + 150.0 + (Random.Shared.NextDouble() * 120.0); // P2d perf: avoid transient Random allocation
                    if (state == IslandState.Idle) IdlePetCompanion?.PetClickImpulse();
                    else CompactPetCompanion?.PetClickImpulse();
                }
                else
                {
                    double targetDistance = state == IslandState.Idle ? -88.0 : -168.0;

                    if (elapsed < 0.4)
                    {
                        // Phase 1: Crouch & Spring Prep
                        double p = elapsed / 0.4;
                        scamperX = 0;
                        scamperY = p * 1.5;
                        scamperScaleX = -1.0;
                        isScampering = true;
                    }
                    else if (elapsed < 1.8)
                    {
                        // Phase 2: Sprint Across to the Left Edge
                        double p = (elapsed - 0.4) / 1.4;
                        double ease = p < 0.5 ? 2 * p * p : 1.0 - Math.Pow(-2 * p + 2, 2) / 2.0;
                        scamperX = targetDistance * ease;
                        scamperY = 0.5;
                        scamperScaleX = -1.0;
                        isScampering = true;
                    }
                    else if (elapsed < 3.4)
                    {
                        // Phase 3: Peek Over the Edge into the desktop
                        scamperX = targetDistance;
                        scamperY = 3.5;
                        scamperScaleX = -1.0;
                        isPeeking = true;
                    }
                    else if (elapsed < 4.8)
                    {
                        // Phase 4: Turn Around & Sprint Back Home
                        double p = (elapsed - 3.4) / 1.4;
                        double ease = p < 0.5 ? 2 * p * p : 1.0 - Math.Pow(-2 * p + 2, 2) / 2.0;
                        scamperX = targetDistance * (1.0 - ease);
                        scamperY = 3.5 * (1.0 - Math.Min(1.0, p * 2.5));
                        scamperScaleX = 1.0;
                        isScampering = true;
                    }
                    else
                    {
                        // Phase 5: Settle Hop
                        scamperX = 0;
                        scamperY = 0;
                        scamperScaleX = 1.0;
                    }
                }
            }
        }

        double gazeX = 0, gazeY = 0;
        if (_pointerInside)
        {
            try
            {
                var activePet = state == IslandState.Idle ? IdlePetCompanion : CompactPetCompanion;
                if (activePet != null && activePet.IsVisible)
                {
                    Point mousePos = System.Windows.Input.Mouse.GetPosition(activePet);
                    double pcX = activePet.ActualWidth > 0 ? activePet.ActualWidth / 2.0 : 14.0;
                    double pcY = activePet.ActualHeight > 0 ? activePet.ActualHeight / 2.0 : 12.0;
                    gazeX = Math.Clamp((mousePos.X - pcX) / 35.0, -1.0, 1.0);
                    gazeY = Math.Clamp((mousePos.Y - pcY) / 25.0, -1.0, 1.0);
                }
            }
            catch { }
        }

        if (state == IslandState.Idle)
        {
            if (IdlePetCompanion != null)
            {
                if (IdlePetCompanion.Visibility != Visibility.Visible)
                    IdlePetCompanion.Visibility = Visibility.Visible;

                IdlePetCompanion.Species = species;
                IdlePetCompanion.Accessory = accessory;
                IdlePetCompanion.Mood = mood;
                IdlePetCompanion.PetName = petName;
                IdlePetCompanion.Affection = affection;
                IdlePetCompanion.IsScampering = isScampering;
                IdlePetCompanion.IsPeeking = isPeeking;

                if (IdlePetTranslate != null)
                {
                    IdlePetTranslate.X = scamperX;
                    IdlePetTranslate.Y = scamperY;
                }
                if (IdlePetScale != null)
                {
                    IdlePetScale.ScaleX = scamperScaleX;
                }

                if (mood == PetMood.Dancing)
                    IdlePetCompanion.Dance(bass, treble, t);
                else
                    IdlePetCompanion.UpdateTick(t, _pointerInside, gazeX, gazeY);
            }
            if (CompactPetCompanion != null && CompactPetCompanion.Visibility != Visibility.Collapsed)
                CompactPetCompanion.Visibility = Visibility.Collapsed;
        }
        else if (state == IslandState.Compact)
        {
            if (CompactPetCompanion != null)
            {
                if (CompactPetCompanion.Visibility != Visibility.Visible)
                    CompactPetCompanion.Visibility = Visibility.Visible;

                CompactPetCompanion.Species = species;
                CompactPetCompanion.Accessory = accessory;
                CompactPetCompanion.Mood = mood;
                CompactPetCompanion.PetName = petName;
                CompactPetCompanion.Affection = affection;
                CompactPetCompanion.IsScampering = isScampering;
                CompactPetCompanion.IsPeeking = isPeeking;

                if (CompactPetTranslate != null)
                {
                    CompactPetTranslate.X = scamperX;
                    CompactPetTranslate.Y = scamperY;
                }
                if (CompactPetScale != null)
                {
                    CompactPetScale.ScaleX = scamperScaleX;
                }

                if (mood == PetMood.Dancing)
                    CompactPetCompanion.Dance(bass, treble, t);
                else
                    CompactPetCompanion.UpdateTick(t, _pointerInside, gazeX, gazeY);
            }
            if (IdlePetCompanion != null && IdlePetCompanion.Visibility != Visibility.Collapsed)
                IdlePetCompanion.Visibility = Visibility.Collapsed;
        }
        else
        {
            if (IdlePetCompanion != null && IdlePetCompanion.Visibility != Visibility.Collapsed)
                IdlePetCompanion.Visibility = Visibility.Collapsed;
            if (CompactPetCompanion != null && CompactPetCompanion.Visibility != Visibility.Collapsed)
                CompactPetCompanion.Visibility = Visibility.Collapsed;
        }


        // Settings Hero Showcase Pet animation
        if (SettingsHeroPet != null && SettingsHeroPet.IsVisible)
        {
            SettingsHeroPet.Species = species;
            SettingsHeroPet.Accessory = accessory;
            SettingsHeroPet.Mood = mood;
            SettingsHeroPet.PetName = petName;
            SettingsHeroPet.Affection = affection;

            double heroGazeX = 0, heroGazeY = 0;
            bool heroHover = false;
            try
            {
                Point heroMouse = System.Windows.Input.Mouse.GetPosition(SettingsHeroPet);
                heroHover = heroMouse.X >= 0 && heroMouse.X <= SettingsHeroPet.ActualWidth &&
                            heroMouse.Y >= 0 && heroMouse.Y <= SettingsHeroPet.ActualHeight;
                heroGazeX = Math.Clamp((heroMouse.X - (SettingsHeroPet.ActualWidth / 2.0)) / 35.0, -1.0, 1.0);
                heroGazeY = Math.Clamp((heroMouse.Y - (SettingsHeroPet.ActualHeight / 2.0)) / 30.0, -1.0, 1.0);
            }
            catch { }

            if (mood == PetMood.Dancing)
                SettingsHeroPet.Dance(bass, treble, t);
            else
                SettingsHeroPet.UpdateTick(t, heroHover || _pointerInside, heroGazeX, heroGazeY);
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
        bool hardwareNotch = _settingsService?.Current.HardwareNotchMode ?? false;
        double offset = hardwareNotch ? 0 : (_settingsService?.Current.NotchYOffset ?? 0);
        double dropT = _dropletOffset.Value;
        double liquid = IsReducedMotion ? 0.0 : Math.Clamp(_settingsService?.Current.LiquidIntensity ?? 0.65, 0.0, 1.0);

        double vx = Math.Clamp(_width.Velocity / 900.0, -1.0, 1.0);
        double vy = Math.Clamp(_height.Velocity / 750.0, -1.0, 1.0);
        double motionEnergy = Math.Min(1.0, (Math.Abs(vx) + Math.Abs(vy)) * 0.65);

        if (Math.Abs(w - _appliedWidth) < 0.005 &&
            Math.Abs(h - _appliedHeight) < 0.005 &&
            Math.Abs(r - _appliedRadius) < 0.005 &&
            Math.Abs(offset - _appliedOffset) < 0.005 &&
            Math.Abs(dropT - _appliedDroplet) < 0.002)
        {
            return;
        }

        _appliedWidth = w;
        _appliedHeight = h;
        _appliedRadius = r;
        _appliedOffset = offset;
        _appliedDroplet = dropT;

        // UI text, digits, buttons, and icons stay 100% crisp, sharp, unwarped, and legible
        PillDeformScale.ScaleX = 1.0;
        PillDeformScale.ScaleY = 1.0;

        if (SecondaryDroplet != null)
        {
            if (dropT > 0.01)
            {
                SecondaryDroplet.Visibility = Visibility.Visible;
                SecondaryDroplet.Opacity = Math.Clamp(dropT * 1.25, 0.0, 1.0);
                double dropletDiameter = SecondaryDroplet.Width > 0 ? SecondaryDroplet.Width : 30.0;
                SecondaryDropletTranslate.X = (w / 2.0 + 8.0 + (dropletDiameter / 2.0)) * dropT;
                SecondaryDropletTranslate.Y = (h - dropletDiameter) / 2.0;
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
        double effectiveR = Math.Clamp(r + (r * 0.22 * liquid * motionEnergy), 0, Math.Min(w, h) / 2);
        double topR = hardwareNotch
            ? 0
            : Math.Clamp((offset * 1.5) + (effectiveR * 0.18 * liquid * motionEnergy), 0, effectiveR);

        Pill.Width = w;
        Pill.Height = h;

        if (Pill.Margin.Top != 0) Pill.Margin = new Thickness(0);
        if (PillGlow != null && PillGlow.Margin.Top != 0) PillGlow.Margin = new Thickness(0);
        if (FloatingDockTranslate != null)
        {
            FloatingDockTranslate.Y = h + 4;
        }

        var corners = new CornerRadius(topR, topR, effectiveR, effectiveR);
        PillBackground.CornerRadius = corners;
        if (PillGlassSheen != null) PillGlassSheen.CornerRadius = corners;
        PillStroke.CornerRadius = corners;
        PillStroke.BorderThickness = hardwareNotch ? new Thickness(1, 0, 1, 1) : new Thickness(1);
        DropOverlay.CornerRadius = corners;

        if (ThemeManager.IsLiquidGlass && DynamicIsland.LiquidGlass.GlassManager.Instance.IsEnabled)
        {
            UpdatePillLiquidGlass(w, h, (float)effectiveR, dropT);
        }
        else if (_pillGlassBrush != null && PillBackground.Background == _pillGlassBrush)
        {
            PillBackground.SetResourceReference(Border.BackgroundProperty, "PillBackgroundBrush");
        }

        if (NotchLeftEar != null && NotchRightEar != null)
        {
            var earVis = hardwareNotch ? Visibility.Visible : Visibility.Collapsed;
            if (NotchLeftEar.Visibility != earVis)
            {
                NotchLeftEar.Visibility = earVis;
                NotchRightEar.Visibility = earVis;
            }
        }
        if (NotchCameraHost != null)
        {
            bool showCam = _settingsService?.Current?.ShowCameraLensIcon ?? true;
            var camVis = (hardwareNotch && _machine.State == IslandState.Idle && showCam) ? Visibility.Visible : Visibility.Collapsed;
            if (NotchCameraHost.Visibility != camVis)
            {
                NotchCameraHost.Visibility = camVis;
            }
        }

        _contentClip.Rect = new Rect(0, topR > 0 ? 0 : -effectiveR, w, topR > 0 ? h : h + effectiveR);
        _contentClip.RadiusX = effectiveR;
        _contentClip.RadiusY = effectiveR;

        if (_isPetScampering || _machine.State == IslandState.Idle || _machine.State == IslandState.Compact)
        {
            if (ContentHost.Clip != null) ContentHost.Clip = null;
        }
        else
        {
            if (ContentHost.Clip != _contentClip) ContentHost.Clip = _contentClip;
        }

        if (PillGlow != null)
        {
            PillGlow.Width = w + 56;
            PillGlow.Height = h + 32;
            double glowTopR = topR > 0 ? topR + 12 : 0;
            PillGlow.CornerRadius = new CornerRadius(glowTopR, glowTopR, effectiveR + 12, effectiveR + 12);
        }
    }

    private void UpdateLiveBars(double t)
    {
        if (_machine.State != IslandState.Compact) return;

        _audioSpectrum.ReadBands(_bandLevels);
        bool isMusicPlaying = _mediaService.Current?.IsPlaying ?? false;
        bool isTimerRunning = _stopwatch.IsRunning || (_countdownService?.IsRunning ?? false);
        bool isCallActive = !string.IsNullOrWhiteSpace(_privacyService.ActiveCallApp);
        bool isRecording = _voiceMemoService?.IsRecording ?? false;
        string style = _settingsService?.Current?.VisualizerStyle ?? "ClassicBars";
        bool active = isMusicPlaying || isTimerRunning || isCallActive || isRecording;

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

            // Apple iOS Dynamic Island ballistics: fast explosive attack, smooth organic decay
            if (target > _smoothBands[i])
            {
                _smoothBands[i] += (target - _smoothBands[i]) * 0.65f;
            }
            else
            {
                _smoothBands[i] += (target - _smoothBands[i]) * 0.22f;
            }
        }

        switch (style)
        {
            case "Waveform":
                UpdateCompactWaveVisualizer(t, active);
                break;

            case "LiquidBlob":
                UpdateBlobVisualizer(t);
                break;

            case "NeonAurora":
                UpdateAuroraVisualizer(t);
                break;

            case "SiriOrb":
                UpdateSiriOrbVisualizer(t);
                break;

            default:
                UpdateBarsVisualizer(t, active);
                break;
        }
    }

    private void UpdateBarsVisualizer(double t, bool active)
    {
        for (int i = 0; i < _bars.Length; i++)
        {
            if (!active)
            {
                _bars[i].ScaleY = 0.18;
                continue;
            }

            // Dynamic harmonic wave modulation calibrated to Apple iOS Dynamic Island motion
            double wavePhase = 0.88 + 0.12 * Math.Sin(t * 7.2 + i * 1.15);
            double level = Math.Clamp(_smoothBands[i], 0f, 1f) * wavePhase;

            // Bar-specific dynamic sensitivity:
            // Bar 0: sub-bass pulse, Bar 1: kick/snare punch, Bar 2: vocal warmth, Bar 3: treble sparkle
            double multiplier = i switch
            {
                0 => 1.05,
                1 => 1.15,
                2 => 1.00,
                3 => 0.90,
                _ => 1.00
            };

            double scale = 0.20 + 0.80 * Math.Clamp(level * multiplier, 0.0, 1.0);
            _bars[i].ScaleY = scale;
        }
    }

    private void UpdateCompactWaveVisualizer(double t, bool active)
    {
        if (_compactWaveFigure == null) return;
        for (int i = 0; i < 9; i++)
        {
            double x = i * 2.5;
            double y = 7.0;
            if (active)
            {
                int band = Math.Clamp(i * 3 / 8, 0, 3);
                double energy = _smoothBands[band];
                y = 7.0 + Math.Sin(t * 8.0 + i * 0.8) * energy * 5.0;
            }
            if (i == 0)
            {
                _compactWaveFigure.StartPoint = new Point(x, y);
            }
            else if (i - 1 < _compactWaveFigure.Segments.Count && _compactWaveFigure.Segments[i - 1] is LineSegment seg)
            {
                seg.Point = new Point(x, y);
            }
        }
    }

    private void UpdateBlobVisualizer(double t)
    {
        if (BlobScale == null || BlobRotate == null || BlobOuterGlow == null) return;
        double bass = Math.Clamp(_smoothBands[0], 0, 1);
        double mid = Math.Clamp(_smoothBands[1], 0, 1);
        double energy = (bass * 0.72 + mid * 0.28);

        double squash = Math.Sin(t * 9.0) * 0.18 * energy;
        BlobScale.ScaleX = 0.75 + (0.65 * energy) + squash;
        BlobScale.ScaleY = 0.75 + (0.65 * energy) - squash;
        BlobRotate.Angle = Math.Sin(t * 3.5) * 22.0 * (0.25 + energy);
        BlobOuterGlow.Opacity = Math.Clamp(0.20 + 0.75 * energy, 0, 1);
        double glowSize = 13 + (8 * energy);
        BlobOuterGlow.Width = glowSize;
        BlobOuterGlow.Height = glowSize;
    }

    private void UpdateAuroraVisualizer(double t)
    {
        if (AuroraScale0 == null || AuroraScale1 == null || AuroraScale2 == null || AuroraScale3 == null) return;
        double w0 = 0.85 + 0.15 * Math.Sin(t * 6.5);
        double w1 = 0.85 + 0.15 * Math.Sin(t * 6.5 + 1.2);
        double w2 = 0.85 + 0.15 * Math.Sin(t * 6.5 + 2.4);
        double w3 = 0.85 + 0.15 * Math.Sin(t * 6.5 + 3.6);

        AuroraScale0.ScaleY = Math.Clamp((0.18 + 0.82 * Math.Clamp(_smoothBands[0], 0, 1)) * w0, 0.15, 1.25);
        AuroraScale1.ScaleY = Math.Clamp((0.18 + 0.82 * Math.Clamp(_smoothBands[1], 0, 1)) * w1, 0.15, 1.25);
        AuroraScale2.ScaleY = Math.Clamp((0.18 + 0.82 * Math.Clamp(_smoothBands[2], 0, 1)) * w2, 0.15, 1.25);
        AuroraScale3.ScaleY = Math.Clamp((0.18 + 0.82 * Math.Clamp(_smoothBands[3], 0, 1)) * w3, 0.15, 1.25);
    }

    private void UpdateSiriOrbVisualizer(double t)
    {
        if (SiriRotate == null || SiriCoreScale == null || SiriAura == null) return;
        double beat = Math.Clamp(_smoothBands[0] * 0.75 + _smoothBands[1] * 0.45, 0, 1);
        double highs = Math.Clamp(_smoothBands[2] * 0.5 + _smoothBands[3] * 0.5, 0, 1);

        SiriRotate.Angle = (t * 85.0) % 360.0;
        double corePulse = 0.70 + 0.65 * beat;
        SiriCoreScale.ScaleX = corePulse;
        SiriCoreScale.ScaleY = corePulse;
        SiriAura.Opacity = Math.Clamp(0.30 + 0.70 * (beat * 0.6 + highs * 0.4), 0.25, 1.0);
    }

    private void Pill_MouseMove(object sender, MouseEventArgs e)
    {
        _lastMousePos = e.GetPosition(Pill);
        _lastMouseMoveTime = DateTime.UtcNow;
    }

    private void Pill_MouseLeave(object sender, MouseEventArgs e)
    {
    }

    private ImageBrush? _pillGlassBrush;
    private int _lastPillGlassW;
    private int _lastPillGlassH;
    private float _lastPillGlassR;
    private bool _lastHasSecondary;
    private float _lastDropletX;
    private float _lastDropletY;

    internal void UpdatePillLiquidGlass(double w, double h, float radius, double dropT)
    {
        if (!ThemeManager.IsLiquidGlass || !DynamicIsland.LiquidGlass.GlassManager.Instance.IsEnabled)
        {
            if (_pillGlassBrush != null && PillBackground.Background == _pillGlassBrush)
            {
                PillBackground.SetResourceReference(Border.BackgroundProperty, "PillBackgroundBrush");
            }
            return;
        }

        int targetW = (int)Math.Max(w, 20);
        int targetH = (int)Math.Max(h, 20);

        bool hasSecondary = dropT > 0.01 && SecondaryDroplet != null && SecondaryDroplet.Visibility == Visibility.Visible;
        float dropX = (float)(SecondaryDropletTranslate?.X ?? 0);
        float dropY = (float)(SecondaryDropletTranslate?.Y ?? 0);

        if (targetW == _lastPillGlassW && targetH == _lastPillGlassH && Math.Abs(radius - _lastPillGlassR) < 0.5f &&
            hasSecondary == _lastHasSecondary &&
            (!hasSecondary || (Math.Abs(dropX - _lastDropletX) < 0.5f && Math.Abs(dropY - _lastDropletY) < 0.5f)))
        {
            return;
        }

        _lastPillGlassW = targetW;
        _lastPillGlassH = targetH;
        _lastPillGlassR = radius;
        _lastHasSecondary = hasSecondary;
        _lastDropletX = dropX;
        _lastDropletY = dropY;

        float screenX = 0;
        float screenY = 0;
        try
        {
            var screenPos = PillBackground.PointToScreen(new Point(0, 0));
            screenX = (float)screenPos.X;
            screenY = (float)screenPos.Y;
        }
        catch { }

        // P2a perf: Use zero-allocation GlassSettings.Lerp between pre-computed presets
        // instead of Builder pattern that allocates per-frame during morph transitions.
        // Adaptive preset based on current morphing state:
        // Compact idle/toast: Punchy Pill capsule profile
        // Expanding/Expanded: Squircle Card profile with lower refraction thickness
        // to prevent interior text warping, and higher diffusion blur
        var settings = DynamicIsland.LiquidGlass.GlassPresets.Pill;
        if (targetH > 38 || targetW > 200)
        {
            float t = Math.Clamp((float)(targetH - 30) / (176f - 30f), 0.0f, 1.0f);
            settings = DynamicIsland.LiquidGlass.GlassSettings.Lerp(
                DynamicIsland.LiquidGlass.GlassPresets.Pill,
                DynamicIsland.LiquidGlass.GlassPresets.Card,
                t) with { CornerRadius = radius, CornerSmoothing = 0.80f };
        }

        var bmp = DynamicIsland.LiquidGlass.GlassManager.Instance.RenderPillGlass(
            targetW,
            targetH,
            (float)_dpiScale,
            settings,
            radius,
            hasSecondary,
            (float)(SecondaryDropletTranslate?.X ?? 0),
            (float)(SecondaryDropletTranslate?.Y ?? 0),
            18.0f,
            screenX,
            screenY);

        if (bmp != null)
        {
            _pillGlassBrush ??= new ImageBrush { Stretch = Stretch.Fill };
            _pillGlassBrush.ImageSource = bmp;
            if (PillBackground.Background != _pillGlassBrush)
            {
                PillBackground.Background = _pillGlassBrush;
            }
        }
    }
}
