using System;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using DynamicIsland.Core;
using SharpGen.Runtime;
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
    private const int MaxConsecutiveDeviceFailures = 3;

    private static readonly Lazy<GlassManager> _instance = new(() => new GlassManager());
    public static GlassManager Instance => _instance.Value;

    private readonly object _syncLock = new();
    private GlassRenderer? _renderer;
    private IBackdropProvider? _sharedBackdrop;
    private GlassQuality _quality = GlassQuality.High;
    private volatile bool _isDisposed;
    private int _consecutiveDeviceFailures;
    private IntPtr _lastOverlayHwnd;

    public bool IsDisposed => _isDisposed;

    public GlassRenderer Renderer
    {
        get
        {
            lock (_syncLock)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                return _renderer ??= new GlassRenderer();
            }
        }
    }

    public IBackdropProvider SharedBackdrop
    {
        get
        {
            lock (_syncLock)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                if (_sharedBackdrop == null)
                {
                    InitializeDefaultBackdropLocked(_lastOverlayHwnd);
                }
                return _sharedBackdrop!;
            }
        }
    }

    public GlassQuality Quality
    {
        get => _quality;
        set => _quality = value;
    }

    public bool IsEnabled =>
        !_isDisposed &&
        _consecutiveDeviceFailures < MaxConsecutiveDeviceFailures &&
        _quality != GlassQuality.Off &&
        DynamicIsland.Interop.WindowBlurHelper.IsWindowsTransparencyEnabled();

    public event EventHandler? FrameReady;

    // P1b perf: Cached shape arrays to avoid per-frame heap allocations during morphing
    private readonly GlassShape[] _singleShapeBuffer = new GlassShape[1];
    private readonly GlassShape[] _dualShapeBuffer = new GlassShape[2];
    private WriteableBitmap? _pillBitmap;

    private GlassManager()
    {
    }

    private void OnBackdropFrameReady(object? sender, EventArgs e)
    {
        if (_isDisposed) return;
        FrameReady?.Invoke(this, e);
    }

    /// <summary>
    /// Initializes the primary backdrop provider for the application HWND.
    /// Prefers Path B (DesktopCaptureBackdrop) for the overlay window, falling back to StaticBackdrop.
    /// </summary>
    public void InitializeDefaultBackdrop(IntPtr overlayHwnd)
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            if (overlayHwnd != IntPtr.Zero)
            {
                _lastOverlayHwnd = overlayHwnd;
            }
            if (_sharedBackdrop != null) return;
            // Defer D3D11 + WGC startup until Liquid Glass theme is actually active.
            if (!ThemeManager.IsLiquidGlass || !IsEnabled) return;
            InitializeDefaultBackdropLocked(overlayHwnd);
        }
    }

    private void InitializeDefaultBackdropLocked(IntPtr overlayHwnd)
    {
        if (_isDisposed || _sharedBackdrop != null) return;

        var r = _renderer ??= new GlassRenderer();
        if (overlayHwnd != IntPtr.Zero && DesktopCaptureBackdrop.IsSupported())
        {
            var desktopCapture = new DesktopCaptureBackdrop(r.Device, r.Context, overlayHwnd, r.RenderLock);
            desktopCapture.FrameReady += OnBackdropFrameReady;
            _ = desktopCapture.StartAsync();
            _sharedBackdrop = desktopCapture;
            App.Log("[GlassManager] Initialized PATH B: DesktopCaptureBackdrop for overlay window.");
        }
        else
        {
            var staticBackdrop = new StaticBackdropProvider(r.Device, r.Context);
            staticBackdrop.FrameReady += OnBackdropFrameReady;
            _ = staticBackdrop.StartAsync();
            _sharedBackdrop = staticBackdrop;
            App.Log("[GlassManager] Initialized PATH A/Static fallback backdrop.");
        }
    }

    internal void NotifyDeviceFailure(Exception ex)
    {
        App.Log($"[GlassManager] D3D11 device/render failure ({_consecutiveDeviceFailures + 1}/{MaxConsecutiveDeviceFailures}): {ex.Message}");
        lock (_syncLock)
        {
            _consecutiveDeviceFailures++;
            ResetGraphicsPipelineLocked();
        }
    }

    private void ResetGraphicsPipelineLocked()
    {
        if (_sharedBackdrop != null)
        {
            try
            {
                _sharedBackdrop.FrameReady -= OnBackdropFrameReady;
                _sharedBackdrop.Dispose();
            }
            catch { }
            _sharedBackdrop = null;
        }

        if (_renderer != null)
        {
            try { _renderer.Dispose(); } catch { }
            _renderer = null;
        }
        _pillBitmap = null;
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
        if (!IsEnabled)
            return null;

        GlassRenderer? renderer;
        IBackdropProvider? backdrop;
        lock (_syncLock)
        {
            if (!IsEnabled) return null;
            if (_sharedBackdrop == null && _lastOverlayHwnd != IntPtr.Zero)
            {
                try { InitializeDefaultBackdropLocked(_lastOverlayHwnd); }
                catch (Exception ex)
                {
                    NotifyDeviceFailure(ex);
                    return null;
                }
            }
            renderer = _renderer;
            backdrop = _sharedBackdrop;
        }

        if (renderer == null || backdrop == null || renderer.IsDisposed || renderer.IsDeviceLost)
        {
            if (renderer?.IsDeviceLost == true) NotifyDeviceFailure(new InvalidOperationException("D3D11 device lost"));
            return null;
        }

        if (backdrop is DesktopCaptureBackdrop { IsDeviceLost: true })
        {
            NotifyDeviceFailure(new InvalidOperationException("Capture device lost"));
            return null;
        }

        if (backdrop is DesktopCaptureBackdrop monitorCheck && _lastOverlayHwnd != IntPtr.Zero)
        {
            monitorCheck.EnsureTargetMonitor(_lastOverlayHwnd);
        }

        try
        {
            lock (renderer.RenderLock)
            {
                if (_isDisposed || renderer.IsDisposed)
                    return null;

                var srv = backdrop.CurrentFrameSRV;
                if (srv == null || backdrop is DesktopCaptureBackdrop { IsDisposed: true })
                    return null;

                int safeW = Math.Max(width, 10);
                int safeH = Math.Max(height, 10);
                float safeScale = Math.Max(dpiScale, 0.5f);
                int pxW = Math.Max((int)Math.Round(safeW * safeScale), 10);
                int pxH = Math.Max((int)Math.Round(safeH * safeScale), 10);

                var shapes = hasSecondaryDroplet ? _dualShapeBuffer : _singleShapeBuffer;

                // Shape 0: Main Dynamic Island Capsule (physical pixels)
                shapes[0] = GlassShape.FromBounds(0, 0, pxW, pxH, cornerRadius * safeScale, settings.Shape);

                // Shape 1: Satellite droplet merging via polynomial smooth-minimum (smin)
                if (hasSecondaryDroplet)
                {
                    shapes[1] = GlassShape.FromBounds(
                        dropletX * safeScale, dropletY * safeScale,
                        dropletRadius * 2.0f * safeScale, dropletRadius * 2.0f * safeScale,
                        dropletRadius * safeScale,
                        GlassShapeType.Circle);
                }

                float originX = backdrop is DesktopCaptureBackdrop dcb ? dcb.MonitorOriginX : 0f;
                float originY = backdrop is DesktopCaptureBackdrop dcb2 ? dcb2.MonitorOriginY : 0f;
                var screenOffset = new Vector2(screenX - originX, screenY - originY);
                var backdropSize = new Vector2(backdrop.FrameWidth, backdrop.FrameHeight);

                if (_pillBitmap == null || _pillBitmap.PixelWidth != pxW || _pillBitmap.PixelHeight != pxH ||
                    Math.Abs(_pillBitmap.DpiX - 96.0 * safeScale) > 0.5)
                {
                    _pillBitmap = new WriteableBitmap(
                        pxW,
                        pxH,
                        96.0 * safeScale,
                        96.0 * safeScale,
                        System.Windows.Media.PixelFormats.Pbgra32,
                        null);
                }

                renderer.RenderToExistingBitmap(
                    _pillBitmap,
                    pxW,
                    pxH,
                    safeScale,
                    settings,
                    shapes,
                    srv,
                    screenOffset,
                    backdropSize);

                _consecutiveDeviceFailures = 0;
                return _pillBitmap;
            }
        }
        catch (SharpGenException ex)
        {
            NotifyDeviceFailure(ex);
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
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
        if (!IsEnabled)
            return null;

        GlassRenderer? renderer;
        IBackdropProvider? backdrop;
        lock (_syncLock)
        {
            if (!IsEnabled) return null;
            if (_sharedBackdrop == null)
            {
                try { InitializeDefaultBackdropLocked(_lastOverlayHwnd); }
                catch (Exception ex)
                {
                    NotifyDeviceFailure(ex);
                    return null;
                }
            }
            renderer = _renderer;
            backdrop = _sharedBackdrop;
        }

        if (renderer == null || backdrop == null || renderer.IsDisposed || renderer.IsDeviceLost)
            return null;

        try
        {
            lock (renderer.RenderLock)
            {
                if (_isDisposed || renderer.IsDisposed)
                    return null;

                var srv = backdrop.CurrentFrameSRV;
                if (srv == null || backdrop is DesktopCaptureBackdrop { IsDisposed: true })
                    return null;

                int safeW = Math.Max(width, 10);
                int safeH = Math.Max(height, 10);
                float safeScale = Math.Max(dpiScale, 0.5f);
                int pxW = Math.Max((int)Math.Round(safeW * safeScale), 10);
                int pxH = Math.Max((int)Math.Round(safeH * safeScale), 10);

                var shape = GlassShape.FromBounds(0, 0, pxW, pxH, cornerRadius * safeScale, shapeType);
                // P1b perf: Reuse cached single-element array
                _singleShapeBuffer[0] = shape;
                var shapes = _singleShapeBuffer;

                float originX = backdrop is DesktopCaptureBackdrop dcb ? dcb.MonitorOriginX : 0f;
                float originY = backdrop is DesktopCaptureBackdrop dcb2 ? dcb2.MonitorOriginY : 0f;
                var screenOffset = new Vector2(screenX - originX, screenY - originY);
                var backdropSize = new Vector2(backdrop.FrameWidth, backdrop.FrameHeight);

                var bmp = renderer.RenderToBitmap(
                    pxW,
                    pxH,
                    safeScale,
                    settings,
                    shapes,
                    srv,
                    screenOffset,
                    backdropSize);

                _consecutiveDeviceFailures = 0;
                return bmp;
            }
        }
        catch (SharpGenException ex)
        {
            NotifyDeviceFailure(ex);
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            FrameReady = null;
            ResetGraphicsPipelineLocked();
        }
    }
}
