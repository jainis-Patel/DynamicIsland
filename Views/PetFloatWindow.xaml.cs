using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Core;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Views;

public partial class PetFloatWindow : Window
{
    private static readonly Stopwatch MonotonicClock = Stopwatch.StartNew();

    private readonly VirtualPetService _petService;
    private readonly IslandWindow _island;
    private bool _isMouseOverWindow;
    private bool _isRenderingHooked;
    private bool _isClosed;

    internal PetFloatWindow(VirtualPetService petService, IslandWindow island)
    {
        _petService = petService ?? throw new ArgumentNullException(nameof(petService));
        _island = island ?? throw new ArgumentNullException(nameof(island));
        InitializeComponent();

        _petService.AffectionChanged += OnAffectionChanged;
        _petService.MoodChanged += OnMoodChanged;
        _petService.SpecialActionTriggered += OnSpecialActionTriggered;

        IsVisibleChanged += (_, _) => UpdateRenderingSubscription();
        Loaded += PetFloatWindow_Loaded;
    }

    private void UpdateRenderingSubscription()
    {
        bool shouldRender = IsVisible && !_isClosed;
        if (shouldRender && !_isRenderingHooked)
        {
            _isRenderingHooked = true;
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
        var hwnd = new WindowInteropHelper(this).Handle;
        long exStyle = GetExStyle(hwnd);
        SetExStyle(hwnd, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    private void PetFloatWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Position gracefully near top-right of the current monitor
        try
        {
            var screenW = SystemParameters.PrimaryScreenWidth;
            Left = Math.Max(40, screenW - Width - 60);
            Top = 60;
        }
        catch { }

        RefreshUI();
        UpdateRenderingSubscription();
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        UpdateRenderingSubscription();
        TopToolbar.BeginAnimation(OpacityProperty, null);
        _petService.AffectionChanged -= OnAffectionChanged;
        _petService.MoodChanged -= OnMoodChanged;
        _petService.SpecialActionTriggered -= OnSpecialActionTriggered;
        base.OnClosed(e);
    }

    private void OnRenderingFrame(object? sender, EventArgs e)
    {
        if (!IsVisible || _isClosed) return;
        double t = MonotonicClock.Elapsed.TotalSeconds;

        double gazeX = 0, gazeY = 0;
        if (_isMouseOverWindow)
        {
            try
            {
                var pos = Mouse.GetPosition(FloatPet);
                double pcX = FloatPet.ActualWidth > 0 ? FloatPet.ActualWidth / 2.0 : 49.0;
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
    });

    private void OnSpecialActionTriggered(PetSpecialActionKind kind) => Dispatcher.InvokeAsync(() =>
    {
        FloatPet.TriggerSpecialAction(kind);
        RefreshUI();
    });

    public void RefreshUI()
    {
        string species = _petService.PetSpecies;
        string accessory = _petService.PetAccessory;
        string name = _petService.PetName;
        int affection = _petService.AffectionScore;
        var mood = _petService.CurrentMood;

        FloatPet.Species = species;
        FloatPet.Accessory = accessory;
        FloatPet.Mood = mood;
        FloatPet.PetName = name;
        FloatPet.Affection = affection;

        FloatPetNameText.Text = name;
        FloatPetSpeciesGlyph.Text = species switch
        {
            "Dog" => "🐶",
            "Bunny" => "🐰",
            "Fox" => "🦊",
            "Panda" => "🐼",
            "Dragon" => "🐲",
            _ => "🐱"
        };

        FloatStatusText.Text = $"❤️ {affection} · {mood}";
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _isMouseOverWindow = true;
        TopToolbar.BeginAnimation(OpacityProperty, new DoubleAnimation(TopToolbar.Opacity, 1.0, TimeSpan.FromMilliseconds(120)));
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        _isMouseOverWindow = false;
        TopToolbar.BeginAnimation(OpacityProperty, new DoubleAnimation(TopToolbar.Opacity, 0.3, TimeSpan.FromMilliseconds(200)));
    }

    private void Pet_Click(object sender, MouseButtonEventArgs e)
    {
        string msg = _petService.Pet();
        FloatPet.PetClickImpulse();
        RefreshUI();
        _island.PushToast(new ToastContent(
            "\uE76E",
            _petService.PetName,
            msg,
            ThemeBrushes.PinkColor,
            TimeSpan.FromSeconds(2.5),
            TargetView: null,
            Priority: ToastPriority.Feedback));
        e.Handled = true;
    }

    private void Pet_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        string msg = _petService.PerformTrick();
        FloatPet.TriggerTrick();
        RefreshUI();
        _island.PushToast(new ToastContent(
            "\uE734",
            $"{_petService.PetName} Flip!",
            msg,
            ThemeBrushes.AmberColor,
            TimeSpan.FromSeconds(2.5),
            TargetView: null,
            Priority: ToastPriority.Feedback,
            Category: "pet"));
    }

    private void SpecialAction_Click(object sender, RoutedEventArgs e)
    {
        string msg = _petService.PlaySpecialAction();
        FloatPet.TriggerSpecialAction();
        RefreshUI();
        _island.PushToast(new ToastContent(
            "\uE734",
            $"{_petService.PetName} Playtime!",
            msg,
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(3.0),
            TargetView: null,
            Priority: ToastPriority.Feedback,
            Category: "pet"));
    }

    private void ReturnToIsland_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
