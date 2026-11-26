using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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

    private bool IsHighLoadDropletActive =>
        _isHighLoadAlertActive &&
        DateTime.UtcNow >= _highLoadSnoozedUntilUtc &&
        _machine.State is IslandState.Idle or IslandState.Compact;

    private void UpdateSecondaryDropletTarget()
    {
        bool highLoadAlert = IsHighLoadDropletActive;
        bool showSecondaryActivity = _machine.State == IslandState.Compact && _secondaryCompactHost != CompactActivityKind.None;
        bool showDroplet = highLoadAlert || showSecondaryActivity;
        double newTarget = showDroplet ? 1.0 : 0.0;
        if (Math.Abs(_dropletOffset.Target - newTarget) > 0.01)
        {
            _dropletOffset.Target = newTarget;
            TriggerLiquidImpulse(showDroplet ? 140 : -120, showDroplet ? -35 : 30, 12);
        }

        if (highLoadAlert)
        {
            var accent = _highLoadIsCritical ? ThemeBrushes.Red : ThemeBrushes.Amber;
            if (SecondaryDropletIconHost != null)
            {
                SecondaryDropletIconHost.Visibility = Visibility.Collapsed;
            }
            if (SecondaryDropletAlertStack != null)
            {
                SecondaryDropletAlertStack.Visibility = Visibility.Visible;
            }
            if (SecondaryDropletAlertIconBg != null)
            {
                SecondaryDropletAlertIconBg.Background = _highLoadIsCritical ? ThemeBrushes.AlertRedBg : ThemeBrushes.AlertAmberBg;
            }
            if (SecondaryDropletAlertGlyph != null)
            {
                SecondaryDropletAlertGlyph.Text = _highLoadIsRamPriority ? "\uE964" : "\uE950";
                SecondaryDropletAlertGlyph.Foreground = accent;
            }
            if (SecondaryDropletAlertLabel != null)
            {
                SecondaryDropletAlertLabel.Text = _highLoadIsRamPriority ? "RAM" : "CPU";
                SecondaryDropletAlertLabel.Foreground = accent;
            }
            if (SecondaryDropletAlertPercent != null)
            {
                SecondaryDropletAlertPercent.Text = $"{_highLoadPercent}%";
            }
            if (SecondaryDroplet != null)
            {
                SecondaryDroplet.Width = 78;
                SecondaryDroplet.Height = 30;
                SecondaryDroplet.CornerRadius = new CornerRadius(15);
                SecondaryDroplet.BorderBrush = _highLoadIsCritical ? ThemeBrushes.AlertRedBorder : ThemeBrushes.AlertAmberBorder;
                SecondaryDroplet.ToolTip = _highLoadTooltip;
                System.Windows.Automation.AutomationProperties.SetName(
                    SecondaryDroplet,
                    $"High {(_highLoadIsRamPriority ? "RAM" : "CPU")} usage {_highLoadPercent} percent");
            }
            _appliedDroplet = -1;
        }
        else if (showSecondaryActivity)
        {
            if (SecondaryDropletAlertStack != null)
            {
                SecondaryDropletAlertStack.Visibility = Visibility.Collapsed;
            }
            if (SecondaryDropletIconHost != null)
            {
                SecondaryDropletIconHost.Visibility = Visibility.Visible;
            }

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
            if (SecondaryDropletInnerTint != null)
            {
                SecondaryDropletInnerTint.Fill = accent;
            }
            if (SecondaryDroplet != null)
            {
                SecondaryDroplet.Width = 32;
                SecondaryDroplet.Height = 32;
                SecondaryDroplet.CornerRadius = new CornerRadius(16);
                SecondaryDroplet.ClearValue(Border.BorderBrushProperty);
                SecondaryDroplet.ToolTip = tip;
                System.Windows.Automation.AutomationProperties.SetName(SecondaryDroplet, tip);
            }
            _appliedDroplet = -1;
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

    internal void SetModalDialogOpen(bool open)
    {
        _isModalDialogOpen = open;
        if (!open)
        {
            _machine.ExpandNow();
        }
    }

    private bool ShouldHoldExpandedOpen =>
        _machine.IsPinned ||
        _isModalDialogOpen ||
        _recordingHotkeyAction != null ||
        (FloatingDockHost != null && (FloatingDockHost.IsMouseOver || FloatingDockHost.IsKeyboardFocusWithin)) ||
        (Pill != null && Pill.IsMouseOver) ||
        (CalcInputBox != null && CalcInputBox.IsKeyboardFocused && !string.IsNullOrWhiteSpace(CalcInputBox.Text)) ||
        (ClipboardSearchBox != null && ClipboardSearchBox.IsKeyboardFocused) ||
        (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox);

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

        EvaluateAutoHideState();
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
            else if (_currentExpandedView == ExpandedView.Battery)
            {
                _powerPlanService?.SetExpanded(false);
            }
            else if (_currentExpandedView == ExpandedView.Peripherals)
            {
                _peripheralService?.SetExpanded(false);
            }
            else if (_currentExpandedView == ExpandedView.WidgetsGallery)
            {
                WidgetsGalleryContainer?.SetExpanded(false);
            }
            else if (_currentExpandedView == ExpandedView.Mirror)
            {
                MirrorContainer?.SetActive(false);
            }
            _currentExpandedView = ExpandedView.Media;
            SetKeyboardFocusable(false);
            UpdatePinExpandedUI();
            SetDockActive(MediaHomeButton);
            foreach (var c in _materializedContainers)
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
        else if (next == IslandState.Toast)
        {
            ToastPanel.Width = shape.Width;
        }

        _width.Target = shape.Width;
        _height.Target = shape.Height;
        _radius.Target = shape.Radius;
        RepositionIfEdgeDocked();

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
            SetTopmostReassertEnabled(false); // P1a perf: no need to reassert topmost while hidden
        }
        else if (previous == IslandState.Hidden)
        {
            SetTopmostReassertEnabled(true); // P1a perf: resume topmost reassertion when visible
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
        UpdateNatureThemeTimerState();
        EnsureRendering();
        _memoryManager?.NotifyStateChanged(next);
        EvaluateAutoHideState();

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

        var toastShape = GetToastShape(toast);
        ToastPanel.Width = toastShape.Width;
        if (_machine.State == IslandState.Toast)
        {
            _width.Target = toastShape.Width;
            _height.Target = toastShape.Height;
            _radius.Target = toastShape.Radius;
            RepositionIfEdgeDocked();
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
        foreach (var c in _materializedContainers)
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
                (float)(_appliedRadius > 0 ? _appliedRadius : 15.0), _dropletOffset?.Value ?? 0);
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
        UpdateLiveBars(now.TotalSeconds, dt);
        bool discsSettled = UpdatePlayerDiscs(dt);
        UpdateVirtualPet(now.TotalSeconds);
        bool glowSettled = UpdateAmbientHalo(dt);

        bool petActive = !_isSessionLocked &&
            _machine.State != IslandState.Hidden &&
            (_petService?.IsEnabled ?? false) &&
            (_isPetScampering || _petService.CurrentMood == PetMood.Dancing || _petService.CurrentMood == PetMood.Happy || _petService.CurrentMood == PetMood.Eating || _petService.CurrentMood == PetMood.Playing ||
             (IdlePetCompanion?.IsAnimatingTrick ?? false) || (CompactPetCompanion?.IsAnimatingTrick ?? false) || (SettingsHeroPet?.IsAnimatingTrick ?? false) ||
             (PetSanctuaryContainer?.StagePet?.IsAnimatingTrick ?? false) || (PetSanctuaryContainer?.StagePet?.IsAnimatingSpecialAction ?? false) ||
             (_pointerInside && (_machine.State == IslandState.Idle || _machine.State == IslandState.Compact || (SettingsHeroPet?.IsVisible ?? false) || (PetSanctuaryContainer?.IsVisible ?? false))));

        if (_width.IsAtRest && _height.IsAtRest && _radius.IsAtRest && _dropletOffset.IsAtRest &&
            !BarsAnimating && discsSettled && glowSettled && !petActive)
        {
            StopRendering();
        }
    }

    private bool UpdatePlayerDiscs(double dt)
    {
        if (_isSessionLocked || _machine.State == IslandState.Hidden)
        {
            return true;
        }

        string style = SettingsService.NormalizeAlbumArtStyle(_settingsService?.Current.AlbumArtStyle);
        if (style == "StaticSquircle")
        {
            return true;
        }

        bool reduced = IsReducedMotion || (_settingsService?.Current.LowMemoryMode ?? false);
        bool isPlaying = _mediaService.Current?.IsPlaying ?? false;
        float bass = isPlaying ? _smoothBands[0] : 0f;
        bool settled = true;

        if (_machine.State == IslandState.Compact && CompactArtDisc != null)
        {
            settled &= CompactArtDisc.StepPhysics(dt, isPlaying, bass, reduced);
        }
        else if (_machine.State == IslandState.Expanded && _currentExpandedView == ExpandedView.Media && ExpandedArtDisc != null)
        {
            settled &= ExpandedArtDisc.StepPhysics(dt, isPlaying, bass, reduced);
        }
        else if (_machine.State == IslandState.Expanded && _currentExpandedView == ExpandedView.WidgetsGallery && _widgetsGalleryContainer != null)
        {
            settled &= _widgetsGalleryContainer.StepMediaDisc(dt, isPlaying, bass, reduced);
        }

        return settled;
    }


    private bool UpdateAmbientHalo(double dt = 1.0 / 60.0)
    {
        if (!(_settingsService?.Current.EnableAmbientGlow ?? true) ||
            (_choreographer?.ShouldThrottleVisuals ?? false) ||
            _isSessionLocked ||
            _machine.State == IslandState.Hidden)
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
                float bassAlpha = (float)(1.0 - Math.Exp(-0.40 * 60.0 * Math.Clamp(dt, 0.001, 0.1)));
                _smoothBands[0] += (_bandLevels[0] - _smoothBands[0]) * bassAlpha;
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

        double alpha = 1.0 - Math.Exp(-rate * 60.0 * Math.Clamp(dt, 0.001, 0.1));
        double nextOpacity = PillGlow.Opacity + (targetGlow - PillGlow.Opacity) * alpha;
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
                double dropletW = SecondaryDroplet.Width > 0 ? SecondaryDroplet.Width : 32.0;
                double dropletH = SecondaryDroplet.Height > 0 ? SecondaryDroplet.Height : 32.0;
                SecondaryDropletTranslate.X = (w / 2.0 + 8.0 + (dropletW / 2.0)) * dropT;
                SecondaryDropletTranslate.Y = (h - dropletH) / 2.0;
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
        bool isDetached = _isDetachedPiP || (_settingsService?.Current.IsDetachedFloating ?? false);
        double effectiveR = Math.Clamp(r + (r * 0.22 * liquid * motionEnergy), 0, Math.Min(w, h) / 2);
        var (topR, bottomR) = MagneticDockingEngine.ComputeCornerRadii(
            effectiveR,
            isDetached,
            hardwareNotch,
            offset,
            liquid,
            motionEnergy);

        Pill.Width = w;
        Pill.Height = h;

        if (Pill.Margin.Top != 0) Pill.Margin = new Thickness(0);
        if (PillGlow != null && PillGlow.Margin.Top != 0) PillGlow.Margin = new Thickness(0);
        if (FloatingDockTranslate != null)
        {
            FloatingDockTranslate.Y = h + 4;
        }

        var corners = new CornerRadius(topR, topR, bottomR, bottomR);
        PillBackground.CornerRadius = corners;
        if (PillGlassSheen != null) PillGlassSheen.CornerRadius = corners;
        PillStroke.CornerRadius = corners;
        PillStroke.BorderThickness = (hardwareNotch && !isDetached) ? new Thickness(1, 0, 1, 1) : new Thickness(1);
        DropOverlay.CornerRadius = corners;

        if (PillDropShadow != null)
        {
            if (isDetached)
            {
                PillDropShadow.BlurRadius = 36.0;
                PillDropShadow.ShadowDepth = 8.0;
                PillDropShadow.Opacity = 0.65;
            }
            else
            {
                PillDropShadow.BlurRadius = 28.0;
                PillDropShadow.ShadowDepth = 4.0;
                PillDropShadow.Opacity = 0.55;
            }
        }

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
            var earVis = (hardwareNotch && !isDetached) ? Visibility.Visible : Visibility.Collapsed;
            if (NotchLeftEar.Visibility != earVis)
            {
                NotchLeftEar.Visibility = earVis;
                NotchRightEar.Visibility = earVis;
            }
            if (earVis == Visibility.Visible)
            {
                double notchR = _settingsService?.Current.HardwareNotchRadius ?? 12.0;
                UpdateNotchEarGeometry(notchR);
            }
        }
        if (NotchCameraHost != null)
        {
            bool showCam = _settingsService?.Current?.ShowCameraLensIcon ?? true;
            var camVis = (hardwareNotch && !isDetached && _machine.State == IslandState.Idle && showCam) ? Visibility.Visible : Visibility.Collapsed;
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

        if (_isNatureThemeActive && !(_natureThemeTimer?.IsEnabled ?? false) && !_isSessionLocked && _machine.State != IslandState.Hidden)
        {
            UpdateIslandNatureTheme(NatureClock.Elapsed.TotalSeconds);
        }
    }

}
