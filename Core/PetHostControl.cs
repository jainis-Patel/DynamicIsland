using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Core.Pet3D;
using DynamicIsland.Services;

namespace DynamicIsland.Core;

/// <summary>
/// Switchable host wrapper that lazily renders either the preserved 2D vector pet
/// (<see cref="VirtualPetControl"/>) or the realistic 3D pet (<see cref="VirtualPet3DControl"/>)
/// based on <see cref="RenderStyle"/> ("Classic2D" | "Realistic3D").
/// </summary>
public sealed class PetHostControl : Grid
{
    public static readonly DependencyProperty RenderStyleProperty =
        DependencyProperty.Register(nameof(RenderStyle), typeof(string), typeof(PetHostControl),
            new PropertyMetadata("Realistic3D", OnRenderStyleChanged));

    public static readonly DependencyProperty SpeciesProperty =
        DependencyProperty.Register(nameof(Species), typeof(string), typeof(PetHostControl),
            new PropertyMetadata("Cat", OnForwardedPropertyChanged));

    public static readonly DependencyProperty AccessoryProperty =
        DependencyProperty.Register(nameof(Accessory), typeof(string), typeof(PetHostControl),
            new PropertyMetadata("None", OnForwardedPropertyChanged));

    public static readonly DependencyProperty MoodProperty =
        DependencyProperty.Register(nameof(Mood), typeof(PetMood), typeof(PetHostControl),
            new PropertyMetadata(PetMood.Sleeping, OnForwardedPropertyChanged));

    public static readonly DependencyProperty PetNameProperty =
        DependencyProperty.Register(nameof(PetName), typeof(string), typeof(PetHostControl),
            new PropertyMetadata("Mochi", OnForwardedPropertyChanged));

    public static readonly DependencyProperty AffectionProperty =
        DependencyProperty.Register(nameof(Affection), typeof(int), typeof(PetHostControl),
            new PropertyMetadata(25, OnForwardedPropertyChanged));

    public static readonly DependencyProperty IsScamperingProperty =
        DependencyProperty.Register(nameof(IsScampering), typeof(bool), typeof(PetHostControl),
            new PropertyMetadata(false, OnForwardedPropertyChanged));

    public static readonly DependencyProperty IsPeekingProperty =
        DependencyProperty.Register(nameof(IsPeeking), typeof(bool), typeof(PetHostControl),
            new PropertyMetadata(false, OnForwardedPropertyChanged));

    public string RenderStyle { get => (string)GetValue(RenderStyleProperty); set => SetValue(RenderStyleProperty, value); }
    public string Species { get => (string)GetValue(SpeciesProperty); set => SetValue(SpeciesProperty, value); }
    public string Accessory { get => (string)GetValue(AccessoryProperty); set => SetValue(AccessoryProperty, value); }
    public PetMood Mood { get => (PetMood)GetValue(MoodProperty); set => SetValue(MoodProperty, value); }
    public string PetName { get => (string)GetValue(PetNameProperty); set => SetValue(PetNameProperty, value); }
    public int Affection { get => (int)GetValue(AffectionProperty); set => SetValue(AffectionProperty, value); }
    public bool IsScampering { get => (bool)GetValue(IsScamperingProperty); set => SetValue(IsScamperingProperty, value); }
    public bool IsPeeking { get => (bool)GetValue(IsPeekingProperty); set => SetValue(IsPeekingProperty, value); }

    private VirtualPetControl? _pet2D;
    private VirtualPet3DControl? _pet3D;
    private string _activeMode = string.Empty;

    public bool IsAnimatingTrick => _pet3D?.IsAnimatingTrick ?? _pet2D?.IsAnimatingTrick ?? false;
    public bool IsAnimatingSpecialAction => _pet3D?.IsAnimatingSpecialAction ?? _pet2D?.IsAnimatingSpecialAction ?? false;

    public PetHostControl()
    {
        Background = Brushes.Transparent;
        ClipToBounds = false;
        SnapsToDevicePixels = false;
        EnsureActiveRenderer();
    }

    private static void OnRenderStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PetHostControl host)
        {
            host.EnsureActiveRenderer();
        }
    }

    private static void OnForwardedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PetHostControl host)
        {
            host.SyncPropertiesToActive();
        }
    }

    private void EnsureActiveRenderer()
    {
        string targetMode = string.Equals(RenderStyle, "Classic2D", StringComparison.OrdinalIgnoreCase)
            ? "Classic2D"
            : "Realistic3D";

        if (string.Equals(_activeMode, targetMode, StringComparison.Ordinal))
        {
            SyncPropertiesToActive();
            return;
        }

        _activeMode = targetMode;
        Children.Clear();

        if (targetMode == "Classic2D")
        {
            _pet3D = null;
            _pet2D ??= new VirtualPetControl();
            Children.Add(_pet2D);
        }
        else
        {
            _pet2D = null;
            _pet3D ??= new VirtualPet3DControl();
            Children.Add(_pet3D);
        }

        SyncPropertiesToActive();
    }

    private void SyncPropertiesToActive()
    {
        if (_pet3D != null)
        {
            _pet3D.Width = Width;
            _pet3D.Height = Height;
            _pet3D.Species = Species;
            _pet3D.Accessory = Accessory;
            _pet3D.Mood = Mood;
            _pet3D.PetName = PetName;
            _pet3D.Affection = Affection;
            _pet3D.IsScampering = IsScampering;
            _pet3D.IsPeeking = IsPeeking;
        }
        else if (_pet2D != null)
        {
            _pet2D.Width = Width;
            _pet2D.Height = Height;
            _pet2D.Species = Species;
            _pet2D.Accessory = Accessory;
            _pet2D.Mood = Mood;
            _pet2D.PetName = PetName;
            _pet2D.Affection = Affection;
            _pet2D.IsScampering = IsScampering;
            _pet2D.IsPeeking = IsPeeking;
        }
    }

    public void PetClickImpulse()
    {
        _pet3D?.PetClickImpulse();
        _pet2D?.PetClickImpulse();
    }

    public void TriggerTrick()
    {
        _pet3D?.TriggerTrick();
        _pet2D?.TriggerTrick();
    }

    public void TriggerSpecialAction(PetSpecialActionKind kind = PetSpecialActionKind.None)
    {
        _pet3D?.TriggerSpecialAction(kind);
        _pet2D?.TriggerSpecialAction(kind);
    }

    public void Dance(float bass, float treble, double t)
    {
        EnsureActiveRenderer();
        _pet3D?.Dance(bass, treble, t);
        _pet2D?.Dance(bass, treble, t);
    }

    public void UpdateTick(double t, bool isNearCursor, double gazeX = 0.0, double gazeY = 0.0)
    {
        EnsureActiveRenderer();
        _pet3D?.UpdateTick(t, isNearCursor, gazeX, gazeY);
        _pet2D?.UpdateTick(t, isNearCursor, gazeX, gazeY);
    }

    public void SetInteractiveToyTarget(double normX, double normY)
    {
        _pet3D?.SetInteractiveToyTarget(normX, normY);
        _pet2D?.SetInteractiveToyTarget(normX, normY);
    }

    public void ClearInteractiveToyTarget()
    {
        _pet3D?.ClearInteractiveToyTarget();
        _pet2D?.ClearInteractiveToyTarget();
    }
}
