using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Interop;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

public partial class PetFloatWindow : Window
{
    private static readonly Stopwatch MonotonicClock = Stopwatch.StartNew();

    private readonly VirtualPetService _petService;
    private readonly IslandWindow _island;
    private readonly DispatcherTimer _roamHeartbeatTimer;

    private bool _isMouseOverWindow;
    private bool _isRenderingHooked;
    private bool _isClosed;
    private double _lastRenderTime;
    private double _lastFrameTime;

    // Autonomous roaming & drag state
    private bool _isWalking;
    private double _walkDirection = -1.0; // -1 left, +1 right
    private double _walkSpeedDipPerSec = 34.0;
    private double _nextRoamToggleTime;
    private bool _isPointerDownOnPet;
    private bool _isDraggingPet;
    private Point _dragStartScreen;
    private double _dragStartLeft;
    private double _dragStartTop;

    internal PetFloatWindow(VirtualPetService petService, IslandWindow island)
    {
        _petService = petService ?? throw new ArgumentNullException(nameof(petService));
        _island = island ?? throw new ArgumentNullException(nameof(island));
        InitializeComponent();

        _petService.AffectionChanged += OnAffectionChanged;
        _petService.MoodChanged += OnMoodChanged;
        _petService.SpecialActionTriggered += OnSpecialActionTriggered;

        _roamHeartbeatTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _roamHeartbeatTimer.Tick += OnRoamHeartbeatTick;

        IsVisibleChanged += (_, _) => UpdateRenderingSubscription();
        Loaded += PetFloatWindow_Loaded;
    }

    private bool ShouldRunRenderLoop()
    {
        if (!IsVisible || _isClosed) return false;
        if (_island.IsFullscreenSuppressed || !SystemParameters.ClientAreaAnimation) return false;

        return _isMouseOverWindow ||
               _isWalking ||
               _isDraggingPet ||
               FloatPet.IsAnimatingTrick ||
               FloatPet.IsAnimatingSpecialAction ||
               _petService.CurrentMood is PetMood.Dancing or PetMood.Happy or PetMood.Eating or PetMood.Playing;
    }

    private void UpdateRenderingSubscription()
    {
        bool shouldRender = ShouldRunRenderLoop();
        if (shouldRender && !_isRenderingHooked)
        {
            _isRenderingHooked = true;
            _lastFrameTime = MonotonicClock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += OnRenderingFrame;
        }
        else if (!shouldRender && _isRenderingHooked)
        {
            _isRenderingHooked = false;
            CompositionTarget.Rendering -= OnRenderingFrame;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        FloatingWindowHelper.ApplyToolWindowStyles(this, noActivate: true);
    }

    private void PetFloatWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var work = SystemParameters.WorkArea;
        var (savedX, savedY) = _petService.GetFloatPosition();

        if (savedX.HasValue && savedY.HasValue &&
            savedX.Value >= work.Left - 20 && savedX.Value <= work.Right - 40 &&
            savedY.Value >= work.Top - 20 && savedY.Value <= work.Bottom - 20)
        {
            Left = savedX.Value;
            Top = savedY.Value;
        }
        else
        {
            // Perch directly on top of the Windows taskbar on the right side
            Left = Math.Max(work.Left + 24, work.Right - Width - 80);
            Top = work.Bottom - Height + 8;
        }

        _nextRoamToggleTime = MonotonicClock.Elapsed.TotalSeconds + 5.0;
        _roamHeartbeatTimer.Start();
        RefreshUI();
        UpdateRenderingSubscription();
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        _roamHeartbeatTimer.Stop();
        UpdateRenderingSubscription();
        TopToolbar.BeginAnimation(OpacityProperty, null);
        _petService.AffectionChanged -= OnAffectionChanged;
        _petService.MoodChanged -= OnMoodChanged;
        _petService.SpecialActionTriggered -= OnSpecialActionTriggered;
        base.OnClosed(e);
    }

    private void OnRoamHeartbeatTick(object? sender, EventArgs e)
    {
        if (_isClosed || !IsVisible) return;

        double now = MonotonicClock.Elapsed.TotalSeconds;

        // Manage autonomous roaming transitions
        if (!_petService.AutoRoamEnabled || _isMouseOverWindow || _isDraggingPet ||
            _petService.CurrentMood == PetMood.Sleeping)
        {
            if (_isWalking)
            {
                StopWalking();
            }
        }
        else if (now >= _nextRoamToggleTime)
        {
            if (_isWalking)
            {
                StopWalking();
                _nextRoamToggleTime = now + 10.0 + Random.Shared.NextDouble() * 12.0;
            }
            else
            {
                StartWalking();
                _nextRoamToggleTime = now + 4.0 + Random.Shared.NextDouble() * 4.5;
            }
        }

        // When render loop is detached (resting/napping), step gentle ambient breath/blink at 0% GPU cost
        if (!_isRenderingHooked && !_island.IsFullscreenSuppressed && SystemParameters.ClientAreaAnimation)
        {
            FloatPet.UpdateTick(now, false, 0, 0);
        }

        UpdateRenderingSubscription();
    }

    private void StartWalking()
    {
        _isWalking = true;
        FloatPet.IsScampering = true;
        if (Random.Shared.NextDouble() < 0.35)
        {
            _walkDirection *= -1.0;
        }
        FloatPetFlipScale.ScaleX = _walkDirection < 0 ? -1.0 : 1.0;
        UpdateRenderingSubscription();
    }

    private void StopWalking()
    {
        _isWalking = false;
        FloatPet.IsScampering = false;
        FloatPetFlipScale.ScaleX = 1.0;
        PersistFloatPosition();
        UpdateRenderingSubscription();
    }

    private void PersistFloatPosition()
    {
        _petService.SaveFloatPosition(Left, Top);
    }

    private void OnRenderingFrame(object? sender, EventArgs e)
    {
        if (!IsVisible || _isClosed) return;
        if (!ShouldRunRenderLoop())
        {
            UpdateRenderingSubscription();
            return;
        }

        double t = MonotonicClock.Elapsed.TotalSeconds;
        double minInterval = _island.ShouldThrottleVisuals
            ? (1.0 / 10.0)
            : ((_isMouseOverWindow || _isWalking || _isDraggingPet) ? (1.0 / 60.0) : (1.0 / 30.0));
        if ((t - _lastRenderTime) < minInterval) return;

        double dt = Math.Clamp(t - _lastFrameTime, 0.001, 0.08);
        _lastFrameTime = t;
        _lastRenderTime = t;

        // Autonomous horizontal walking along current rail
        if (_isWalking && !_isDraggingPet && !_isMouseOverWindow && _petService.AutoRoamEnabled)
        {
            var work = SystemParameters.WorkArea;
            double minLeft = work.Left + 12.0;
            double maxLeft = Math.Max(minLeft, work.Right - Width - 12.0);
            double nextLeft = Left + (_walkDirection * _walkSpeedDipPerSec * dt);
            if (nextLeft <= minLeft)
            {
                nextLeft = minLeft;
                _walkDirection = 1.0;
                FloatPetFlipScale.ScaleX = 1.0;
            }
            else if (nextLeft >= maxLeft)
            {
                nextLeft = maxLeft;
                _walkDirection = -1.0;
                FloatPetFlipScale.ScaleX = -1.0;
            }
            Left = nextLeft;
        }

        double gazeX = 0, gazeY = 0;
        if (_isMouseOverWindow)
        {
            try
            {
                var pos = Mouse.GetPosition(FloatPet);
                double pcX = FloatPet.ActualWidth > 0 ? FloatPet.ActualWidth / 2.0 : 48.0;
                double pcY = FloatPet.ActualHeight > 0 ? FloatPet.ActualHeight / 2.0 : 40.0;
                gazeX = Math.Clamp((pos.X - pcX) / 35.0, -1.0, 1.0);
                gazeY = Math.Clamp((pos.Y - pcY) / 25.0, -1.0, 1.0);
            }
            catch { }
        }

        FloatPet.UpdateTick(t, _isMouseOverWindow, gazeX, gazeY);
    }

    private void OnAffectionChanged(int affection) => Dispatcher.InvokeAsync(RefreshUI);

    private void OnMoodChanged(PetMood mood) => Dispatcher.InvokeAsync(() =>
    {
        FloatPet.Mood = mood;
        RefreshUI();
        UpdateRenderingSubscription();
    });

    private void OnSpecialActionTriggered(PetSpecialActionKind kind) => Dispatcher.InvokeAsync(() =>
    {
        FloatPet.TriggerSpecialAction(kind);
        RefreshUI();
        UpdateRenderingSubscription();
    });

    public void RefreshUI()
    {
        string species = _petService.PetSpecies;
        string accessory = _petService.PetAccessory;
        string renderStyle = _petService.PetRenderStyle;
        string name = _petService.PetName;
        int affection = _petService.AffectionScore;
        var mood = _petService.CurrentMood;

        FloatPet.RenderStyle = renderStyle;
        FloatPet.Species = species;
        FloatPet.Accessory = accessory;
        FloatPet.Mood = mood;
        FloatPet.PetName = name;
        FloatPet.Affection = affection;

        FloatPetNameText.Text = name;
        if (AutoRoamGlyph != null)
        {
            AutoRoamGlyph.Foreground = _petService.AutoRoamEnabled
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("SecondaryBrush");
        }
        FloatPet.ToolTip = $"{name} (❤️ {affection} · {_petService.CurrentContextCaption})\nClick to pet · Right-click for flip · Drag to move";
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _isMouseOverWindow = true;
        if (_isWalking) StopWalking();
        TopToolbar.IsHitTestVisible = true;
        TopToolbar.BeginAnimation(OpacityProperty, new DoubleAnimation(TopToolbar.Opacity, 1.0, TimeSpan.FromMilliseconds(140)));
        UpdateRenderingSubscription();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        _isMouseOverWindow = false;
        TopToolbar.IsHitTestVisible = false;
        TopToolbar.BeginAnimation(OpacityProperty, new DoubleAnimation(TopToolbar.Opacity, 0.0, TimeSpan.FromMilliseconds(200)));
        UpdateRenderingSubscription();
    }

    private void Pet_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPointerDownOnPet = true;
        _isDraggingPet = false;
        _dragStartScreen = PointToScreen(e.GetPosition(this));
        _dragStartLeft = Left;
        _dragStartTop = Top;
        FloatPet.CaptureMouse();
        e.Handled = true;
    }

    private void Pet_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPointerDownOnPet || e.LeftButton != MouseButtonState.Pressed) return;
        Point curScreen = PointToScreen(e.GetPosition(this));
        double dx = curScreen.X - _dragStartScreen.X;
        double dy = curScreen.Y - _dragStartScreen.Y;

        if (!_isDraggingPet && (Math.Abs(dx) > 5.0 || Math.Abs(dy) > 5.0))
        {
            _isDraggingPet = true;
            if (_isWalking) StopWalking();
            UpdateRenderingSubscription();
        }

        if (_isDraggingPet)
        {
            double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            Left = _dragStartLeft + (dx / Math.Max(1.0, dpiScale));
            Top = _dragStartTop + (dy / Math.Max(1.0, dpiScale));
        }
    }

    private void Pet_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPointerDownOnPet) return;
        _isPointerDownOnPet = false;
        if (FloatPet.IsMouseCaptured) FloatPet.ReleaseMouseCapture();

        if (_isDraggingPet)
        {
            _isDraggingPet = false;
            PersistFloatPosition();
            FloatPet.PetClickImpulse();
            UpdateRenderingSubscription();
        }
        else
        {
            _petService.Pet();
            FloatPet.PetClickImpulse();
            RefreshUI();
            UpdateRenderingSubscription();
        }
        e.Handled = true;
    }

    private void Pet_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _petService.PerformTrick();
        FloatPet.TriggerTrick();
        RefreshUI();
        UpdateRenderingSubscription();
    }

    private void SpecialAction_Click(object sender, RoutedEventArgs e)
    {
        _petService.PlaySpecialAction();
        FloatPet.TriggerSpecialAction();
        RefreshUI();
        UpdateRenderingSubscription();
    }

    private void AutoRoamToggle_Click(object sender, RoutedEventArgs e)
    {
        _petService.ToggleAutoRoam();
        if (!_petService.AutoRoamEnabled && _isWalking)
        {
            StopWalking();
        }
        RefreshUI();
    }

    private void ReturnToIsland_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
