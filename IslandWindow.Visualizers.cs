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
    private void UpdateLiveBars(double t, double dt = 1.0 / 60.0)
    {
        if (_machine.State != IslandState.Compact) return;

        _audioSpectrum.ReadBands(_bandLevels);
        bool isMusicPlaying = _mediaService.Current?.IsPlaying ?? false;
        bool isTimerRunning = _stopwatch.IsRunning || (_countdownService?.IsRunning ?? false);
        bool isCallActive = !string.IsNullOrWhiteSpace(_privacyService.ActiveCallApp);
        bool isRecording = _voiceMemoService?.IsRecording ?? false;
        string style = _settingsService?.Current?.VisualizerStyle ?? "ClassicBars";
        bool active = isMusicPlaying || isTimerRunning || isCallActive || isRecording;

        double clampedDt = Math.Clamp(dt, 0.001, 0.1);
        float attackAlpha = (float)(1.0 - Math.Exp(-0.65 * 60.0 * clampedDt));
        float decayAlpha = (float)(1.0 - Math.Exp(-0.22 * 60.0 * clampedDt));

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
            float alpha = target > _smoothBands[i] ? attackAlpha : decayAlpha;
            _smoothBands[i] += (target - _smoothBands[i]) * alpha;
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

        if (_isPillPressed && e.LeftButton == MouseButtonState.Pressed && _machine.State != IslandState.Expanded)
        {
            try
            {
                ProcessPillDrag(PointToScreen(e.GetPosition(this)));
            }
            catch (Exception ex) { App.LogDebug("IslandAnimation.PillDrag", ex); }
        }
    }

    private void Pill_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isPillPressed && e.LeftButton != MouseButtonState.Pressed)
        {
            _isPillPressed = false;
            EndPillDrag();
        }
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
