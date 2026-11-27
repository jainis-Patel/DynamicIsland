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
    private static readonly Stopwatch NatureClock = Stopwatch.StartNew();
    private DispatcherTimer? _natureThemeTimer;
    private bool _isPetScampering;
    private double _scamperStartTime = -1.0;
    private double _nextScamperAllowedTime = 120.0;

    public void TriggerPetScamper()
    {
        if (_isPetScampering || _petService == null || !(_settingsService?.Current.EnableVirtualPet ?? true)) return;
        var state = _machine.State;
        if (state != IslandState.Idle && state != IslandState.Compact) return;

        _isPetScampering = true;
        _scamperStartTime = -1.0;
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

    private bool _isNatureThemeActive;

    internal void UpdateNatureThemeTimerState()
    {
        bool shouldRun = _isNatureThemeActive
                         && !_isClosing
                         && !IsReducedMotion
                         && !_isSessionLocked
                         && _machine.State != IslandState.Hidden
                         && !(_choreographer?.ShouldThrottleVisuals ?? false);

        if (shouldRun)
        {
            if (_natureThemeTimer == null)
            {
                _natureThemeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(42), DispatcherPriority.Background, (_, _) =>
                {
                    if (_isClosing || _isSessionLocked || _machine.State == IslandState.Hidden || !_isNatureThemeActive)
                    {
                        _natureThemeTimer?.Stop();
                        return;
                    }
                    UpdateIslandNatureTheme(NatureClock.Elapsed.TotalSeconds);
                }, Dispatcher);
            }

            if (!_natureThemeTimer.IsEnabled)
            {
                UpdateIslandNatureTheme(NatureClock.Elapsed.TotalSeconds);
                _natureThemeTimer.Start();
            }
        }
        else
        {
            _natureThemeTimer?.Stop();
            if (_isNatureThemeActive && !_isClosing && !_isSessionLocked && _machine.State != IslandState.Hidden)
            {
                UpdateIslandNatureTheme(NatureClock.Elapsed.TotalSeconds);
            }
        }
    }

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

        bool isFocusTimerActive = (_countdownService?.IsRunning ?? false) || _stopwatch.IsRunning;
        var power = _powerPlanService?.Current;
        bool isLowBattery = power is { HasBattery: true, IsCharging: false } &&
                            power.BatteryPercent <= (_settingsService?.Current.LowBatteryAlertThreshold ?? 20);
        bool isHighLoad = _systemPerfService?.LatestSnapshot.IsHighCpu ?? false;

        _petService.UpdateActivity(
            isMusicPlaying,
            bass,
            _pointerInside,
            isFocusTimerActive,
            isLowBattery,
            isHighLoad,
            DateTime.Now.Hour);

        var mood = _petService.CurrentMood;
        string species = _petService.PetSpecies;
        string accessory = _petService.PetAccessory;
        string renderStyle = _petService.PetRenderStyle;
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
                _scamperStartTime = -1.0;
            }
            else
            {
                if (_scamperStartTime < 0.0)
                {
                    _scamperStartTime = t;
                }
                double elapsed = t - _scamperStartTime;
                const double TotalScamperDuration = 5.2;

                if (elapsed >= TotalScamperDuration)
                {
                    _isPetScampering = false;
                    _scamperStartTime = -1.0;
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

                IdlePetCompanion.RenderStyle = renderStyle;
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

                CompactPetCompanion.RenderStyle = renderStyle;
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
            SettingsHeroPet.RenderStyle = renderStyle;
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

        // Pet Sanctuary Playground Stage Pet animation
        if (PetSanctuaryContainer != null && PetSanctuaryContainer.IsVisible)
        {
            var stagePet = PetSanctuaryContainer.StagePet;
            if (stagePet != null)
            {
                stagePet.RenderStyle = renderStyle;
                double stageGazeX = 0, stageGazeY = 0;
                bool stageHover = false;
                try
                {
                    Point stageMouse = System.Windows.Input.Mouse.GetPosition(stagePet);
                    stageHover = stageMouse.X >= 0 && stageMouse.X <= stagePet.ActualWidth &&
                                 stageMouse.Y >= 0 && stageMouse.Y <= stagePet.ActualHeight;
                    stageGazeX = Math.Clamp((stageMouse.X - (stagePet.ActualWidth / 2.0)) / 45.0, -1.0, 1.0);
                    stageGazeY = Math.Clamp((stageMouse.Y - (stagePet.ActualHeight / 2.0)) / 35.0, -1.0, 1.0);
                }
                catch { }

                if (mood == PetMood.Dancing)
                    stagePet.Dance(bass, treble, t);
                else
                    stagePet.UpdateTick(t, stageHover || _pointerInside, stageGazeX, stageGazeY);
            }
        }
    }
}
