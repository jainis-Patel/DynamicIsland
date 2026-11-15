using System;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using DynamicIsland.Core;
using Vortice.Direct3D11;

namespace DynamicIsland.LiquidGlass;

/// <summary>
/// Operational quality levels for the Liquid Glass engine.
/// Allows dynamic degradation on low-power devices, battery saver, or older OS versions.
/// </summary>
public enum GlassQuality
{
    Off = 0,
    Low = 1,      // Single-pass refraction, reduced blur taps
    Medium = 2,   // Full Snell's law refraction, half-res blur chain
    High = 3      // Full refraction + chromatic dispersion + dual-pass Gaussian blur
}

/// <summary>
/// Centralized singleton managing the GPU Liquid Glass rendering engine,
/// backdrop providers, frame distribution, quality degradation, and lifecycle disposal.
/// </summary>
public sealed class GlassManager : IDisposable
{
    private static readonly Lazy<GlassManager> _instance = new(() => new GlassManager());
    public static GlassManager Instance => _instance.Value;

    private GlassRenderer? _renderer;
    private IBackdropProvider? _sharedBackdrop;
    private GlassQuality _quality = GlassQuality.High;
    private bool _isDisposed;

    public GlassRenderer Renderer => _renderer ??= new GlassRenderer();

    public IBackdropProvider SharedBackdrop
    {
        get
        {
            if (_sharedBackdrop == null)
            {
                InitializeDefaultBackdrop(IntPtr.Zero);
            }
            return _sharedBackdrop!;
        }
    }

    public GlassQuality Quality
    {
        get => _quality;
        set => _quality = value;
    }

    public bool IsEnabled => _quality != GlassQuality.Off && DynamicIsland.Interop.WindowBlurHelper.IsWindowsTransparencyEnabled();

    public event EventHandler? FrameReady;

    // P1b perf: Cached shape arrays to avoid per-frame heap allocations during morphing
    private readonly GlassShape[] _singleShapeBuffer = new GlassShape[1];
    private readonly GlassShape[] _dualShapeBuffer = new GlassShape[2];

    private GlassManager()
    {
    }

    /// <summary>
    /// Initializes the primary backdrop provider for the application HWND.
    /// Prefers Path B (DesktopCaptureBackdrop) for the overlay window, falling back to StaticBackdrop.
    /// </summary>
    public void InitializeDefaultBackdrop(IntPtr overlayHwnd)
    {
        if (_sharedBackdrop != null) return;

        var r = Renderer;
        if (overlayHwnd != IntPtr.Zero && DesktopCaptureBackdrop.IsSupported())
        {
            var desktopCapture = new DesktopCaptureBackdrop(r.Device, r.Context, overlayHwnd);
            _ = desktopCapture.StartAsync();
            desktopCapture.FrameReady += (_, e) => FrameReady?.Invoke(this, e);
            _sharedBackdrop = desktopCapture;
            App.Log("[GlassManager] Initialized PATH B: DesktopCaptureBackdrop for overlay window.");
        }
        else
        {
            var staticBackdrop = new StaticBackdropProvider(r.Device, r.Context);
            _ = staticBackdrop.StartAsync();
            staticBackdrop.FrameReady += (_, e) => FrameReady?.Invoke(this, e);
            _sharedBackdrop = staticBackdrop;
            App.Log("[GlassManager] Initialized PATH A/Static fallback backdrop.");
        }
    }

    /// <summary>
    /// Renders an optical Liquid Glass frame for the main Dynamic Island capsule.
    /// Returns a high-resolution premultiplied-alpha WriteableBitmap for the WPF visual tree.
    /// </summary>
    public WriteableBitmap? RenderPillGlass(
        int width,
        int height,
        float dpiScale,
        GlassSettings settings,
        float cornerRadius,
        bool hasSecondaryDroplet = false,
        float dropletX = 0,
        float dropletY = 0,
        float dropletRadius = 0,
        float screenX = 0,
        float screenY = 0)
    {
        if (!IsEnabled || _sharedBackdrop?.CurrentFrameSRV == null)
            return null;

        int safeW = Math.Max(width, 10);
        int safeH = Math.Max(height, 10);

        int shapeCount = hasSecondaryDroplet ? 2 : 1;
        var shapes = hasSecondaryDroplet ? _dualShapeBuffer : _singleShapeBuffer;

        // Shape 0: Main Dynamic Island Capsule
        shapes[0] = GlassShape.FromBounds(0, 0, safeW, safeH, cornerRadius, settings.Shape);

        // Shape 1: Satellite droplet merging via polynomial smooth-minimum (smin)
        if (hasSecondaryDroplet)
        {
            shapes[1] = GlassShape.FromBounds(
                dropletX, dropletY,
                dropletRadius * 2.0f, dropletRadius * 2.0f,
                dropletRadius,
                GlassShapeType.Circle);
        }

        var screenOffset = new Vector2(screenX, screenY);
        var backdropSize = new Vector2(_sharedBackdrop.FrameWidth, _sharedBackdrop.FrameHeight);

        return Renderer.RenderToBitmap(
            safeW,
            safeH,
            dpiScale,
            settings,
            shapes,
            _sharedBackdrop.CurrentFrameSRV,
            screenOffset,
            backdropSize);
    }

    /// <summary>
    /// Renders Liquid Glass for an arbitrary UI element at a specific screen position.
    /// Returns a WriteableBitmap suitable for use as an ImageBrush source.
    /// </summary>
    public WriteableBitmap? RenderElementGlass(
        int width,
        int height,
        float dpiScale,
        GlassSettings settings,
        float cornerRadius,
        GlassShapeType shapeType,
        float screenX,
        float screenY)
    {
        if (!IsEnabled || _sharedBackdrop?.CurrentFrameSRV == null)
            return null;

        int safeW = Math.Max(width, 10);
        int safeH = Math.Max(height, 10);

        var shape = GlassShape.FromBounds(0, 0, safeW, safeH, cornerRadius, shapeType);
        // P1b perf: Reuse cached single-element array
        _singleShapeBuffer[0] = shape;
        var shapes = _singleShapeBuffer;

        var screenOffset = new Vector2(screenX, screenY);
        var backdropSize = new Vector2(_sharedBackdrop.FrameWidth, _sharedBackdrop.FrameHeight);

        return Renderer.RenderToBitmap(
            safeW,
            safeH,
            dpiScale,
            settings,
            shapes,
            _sharedBackdrop.CurrentFrameSRV,
            screenOffset,
            backdropSize);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _sharedBackdrop?.Dispose();
        _sharedBackdrop = null;

        _renderer?.Dispose();
        _renderer = null;
    }
}
