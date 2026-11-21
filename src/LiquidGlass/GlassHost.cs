using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SharpGen.Runtime;

namespace DynamicIsland.LiquidGlass;

/// <summary>
/// Universal integration host for applying GPU-based Liquid Glass refraction onto any WPF FrameworkElement.
/// Supports both programmatic attachment (<see cref="Attach(FrameworkElement, GlassSettings)"/>) and
/// declarative XAML usage via attached properties (<c>lg:GlassHost.IsGlass="True"</c>).
/// </summary>
public static class GlassHost
{
    public static readonly DependencyProperty IsGlassProperty =
        DependencyProperty.RegisterAttached(
            "IsGlass",
            typeof(bool),
            typeof(GlassHost),
            new PropertyMetadata(false, OnIsGlassChanged));

    public static readonly DependencyProperty SettingsProperty =
        DependencyProperty.RegisterAttached(
            "Settings",
            typeof(GlassSettings),
            typeof(GlassHost),
            new PropertyMetadata(null, OnSettingsChanged));

    public static readonly DependencyProperty PresetProperty =
        DependencyProperty.RegisterAttached(
            "Preset",
            typeof(string),
            typeof(GlassHost),
            new PropertyMetadata("Regular", OnPresetChanged));

    public static readonly DependencyProperty ShapeTypeProperty =
        DependencyProperty.RegisterAttached(
            "ShapeType",
            typeof(GlassShapeType),
            typeof(GlassHost),
            new PropertyMetadata(GlassShapeType.RoundedRect, OnShapeTypeChanged));

    public static bool GetIsGlass(DependencyObject obj) => (bool)obj.GetValue(IsGlassProperty);
    public static void SetIsGlass(DependencyObject obj, bool value) => obj.SetValue(IsGlassProperty, value);

    public static GlassSettings? GetSettings(DependencyObject obj) => (GlassSettings?)obj.GetValue(SettingsProperty);
    public static void SetSettings(DependencyObject obj, GlassSettings? value) => obj.SetValue(SettingsProperty, value);

    public static string GetPreset(DependencyObject obj) => (string)obj.GetValue(PresetProperty);
    public static void SetPreset(DependencyObject obj, string value) => obj.SetValue(PresetProperty, value);

    public static GlassShapeType GetShapeType(DependencyObject obj) => (GlassShapeType)obj.GetValue(ShapeTypeProperty);
    public static void SetShapeType(DependencyObject obj, GlassShapeType value) => obj.SetValue(ShapeTypeProperty, value);

    private static readonly ConcurrentDictionary<FrameworkElement, GlassAttachment> _attachments = new();

    private static void OnIsGlassChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            if ((bool)e.NewValue)
            {
                var settings = GetSettings(element) ?? ResolvePreset(GetPreset(element));
                Attach(element, settings);
            }
            else
            {
                Detach(element);
            }
        }
    }

    private static void OnSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && _attachments.TryGetValue(element, out var attachment))
        {
            if (e.NewValue is GlassSettings newSettings)
            {
                attachment.UpdateSettings(newSettings);
            }
        }
    }

    private static void OnPresetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && _attachments.TryGetValue(element, out var attachment))
        {
            string? presetName = e.NewValue as string;
            attachment.UpdateSettings(ResolvePreset(presetName));
        }
    }

    private static void OnShapeTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && _attachments.TryGetValue(element, out var attachment))
        {
            var shapeType = (GlassShapeType)e.NewValue;
            attachment.UpdateShapeType(shapeType);
        }
    }

    public static GlassSettings ResolvePreset(string? presetName)
    {
        return presetName?.ToLowerInvariant() switch
        {
            "clear" => GlassPresets.Clear,
            "dark" => GlassPresets.Dark,
            "pill" => GlassPresets.Pill,
            "capsulebutton" or "capsule" => GlassPresets.CapsuleButton,
            "card" => GlassPresets.Card,
            "dock" => GlassPresets.Dock,
            _ => GlassPresets.Regular
        };
    }

    /// <summary>
    /// Attaches the GPU Liquid Glass rendering engine to the target element.
    /// Replaces the background fill with live refractive optical glass while keeping children/text intact.
    /// </summary>
    public static void Attach(FrameworkElement element, GlassSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(element);

        var activeSettings = settings ?? GlassPresets.Regular;
        _attachments.AddOrUpdate(element,
            el => new GlassAttachment(el, activeSettings),
            (el, existing) =>
            {
                existing.UpdateSettings(activeSettings);
                return existing;
            });
    }

    /// <summary>
    /// Detaches Liquid Glass from the target element, restoring original background brush.
    /// </summary>
    public static void Detach(FrameworkElement element)
    {
        if (_attachments.TryRemove(element, out var attachment))
        {
            attachment.Dispose();
        }
    }

    /// <summary>
    /// Forces an immediate re-render of the Liquid Glass surface on the given element,
    /// bypassing the size-deduplication check.
    /// </summary>
    public static void ForceRender(FrameworkElement element)
    {
        if (_attachments.TryGetValue(element, out var attachment))
        {
            attachment.ForceRender();
        }
    }

    /// <summary>
    /// Internal attachment controller coordinating D3D11 rendering, frame caching, and background brushing.
    /// </summary>
    private sealed class GlassAttachment : IDisposable
    {
        private readonly FrameworkElement _element;
        private GlassSettings _settings;
        private readonly Brush? _originalBackground;
        private readonly ImageBrush _glassBrush = new();
        private readonly GlassShape[] _shapeBuffer = new GlassShape[1];
        private System.Windows.Media.Imaging.WriteableBitmap? _targetBitmap;
        private bool _isDisposed;
        private int _lastWidth = -1;
        private int _lastHeight = -1;
        private GlassShapeType _shapeType = GlassShapeType.RoundedRect;
        private DateTime _lastFrameRender = DateTime.MinValue;
        private static readonly TimeSpan FrameThrottle = TimeSpan.FromMilliseconds(33); // ~30fps

        public GlassAttachment(FrameworkElement element, GlassSettings settings)
        {
            _element = element;
            _settings = settings;

            if (_element is Border b)
            {
                _originalBackground = b.Background;
                b.Background = _glassBrush;
            }
            else if (_element is Panel p)
            {
                _originalBackground = p.Background;
                p.Background = _glassBrush;
            }
            else if (_element is Control c)
            {
                _originalBackground = c.Background;
                c.Background = _glassBrush;
            }

            _glassBrush.Stretch = Stretch.Fill;

            _element.SizeChanged += OnElementSizeChanged;
            _element.Loaded += OnElementLoaded;
            _element.Unloaded += OnElementUnloaded;

            if (_element.IsLoaded)
            {
                GlassManager.Instance.FrameReady += OnBackdropFrameReady;
                RequestRender();
            }
        }

        public void UpdateSettings(GlassSettings settings)
        {
            _settings = settings;
            RequestRender();
        }

        public void UpdateShapeType(GlassShapeType shapeType)
        {
            _shapeType = shapeType;
            RequestRender();
        }

        private void OnElementLoaded(object sender, RoutedEventArgs e)
        {
            if (_isDisposed || GlassManager.Instance.IsDisposed) return;
            GlassManager.Instance.FrameReady -= OnBackdropFrameReady;
            GlassManager.Instance.FrameReady += OnBackdropFrameReady;
            RequestRender();
        }

        private void OnElementUnloaded(object sender, RoutedEventArgs e)
        {
            // Pause rendering and detach singleton handler when element is unloaded
            GlassManager.Instance.FrameReady -= OnBackdropFrameReady;
        }

        private void OnElementSizeChanged(object sender, SizeChangedEventArgs e) => RequestRender();

        public void ForceRender()
        {
            if (_isDisposed || !_element.IsLoaded) return;
            _lastWidth = -1; // Reset to force re-render
            _lastHeight = -1;
            RequestRender();
        }

        private void OnBackdropFrameReady(object? sender, EventArgs e)
        {
            if (_isDisposed || !_element.IsLoaded) return;
            var now = DateTime.UtcNow;
            if (now - _lastFrameRender < FrameThrottle) return;
            _lastFrameRender = now;

            _element.Dispatcher.InvokeAsync(() =>
            {
                if (_isDisposed || !_element.IsLoaded) return;
                _lastWidth = -1; // Force re-render
                _lastHeight = -1;
                RequestRender();
            }, DispatcherPriority.Background);
        }

        public void RequestRender()
        {
            if (_isDisposed || !_element.IsLoaded || GlassManager.Instance.IsDisposed || !GlassManager.Instance.IsEnabled)
                return;

            int w = (int)Math.Max(_element.ActualWidth, 1);
            int h = (int)Math.Max(_element.ActualHeight, 1);

            if (w == _lastWidth && h == _lastHeight) return;

            _lastWidth = w;
            _lastHeight = h;

            // Compute screen position for backdrop sampling
            Vector2 screenOffset = default;
            try
            {
                var screenPos = _element.PointToScreen(new System.Windows.Point(0, 0));
                screenOffset = new Vector2((float)screenPos.X, (float)screenPos.Y);
            }
            catch { }

            _element.Dispatcher.InvokeAsync(() =>
            {
                if (_isDisposed || !_element.IsLoaded || GlassManager.Instance.IsDisposed || !GlassManager.Instance.IsEnabled)
                    return;

                try
                {
                    var renderer = GlassManager.Instance.Renderer;
                    var backdrop = GlassManager.Instance.SharedBackdrop;

                    lock (renderer.RenderLock)
                    {
                        if (_isDisposed || renderer.IsDisposed || renderer.IsDeviceLost)
                            return;

                        var srv = backdrop.CurrentFrameSRV;
                        if (srv == null || backdrop is DesktopCaptureBackdrop { IsDisposed: true })
                            return;

                        // Compute proper DPI scale
                        float dpiScale = 1.0f;
                        var source = PresentationSource.FromVisual(_element);
                        if (source?.CompositionTarget != null)
                            dpiScale = (float)source.CompositionTarget.TransformToDevice.M11;

                        float radius = _settings.CornerRadius;
                        if (_element is Border border)
                        {
                            radius = (float)Math.Max(border.CornerRadius.TopLeft, border.CornerRadius.BottomRight);
                        }

                        float safeScale = Math.Max(dpiScale, 0.5f);
                        int pxW = Math.Max((int)Math.Round(w * safeScale), 1);
                        int pxH = Math.Max((int)Math.Round(h * safeScale), 1);

                        _shapeBuffer[0] = GlassShape.FromBounds(0, 0, pxW, pxH, radius * safeScale, _shapeType);

                        float originX = backdrop is DesktopCaptureBackdrop dcb ? dcb.MonitorOriginX : 0f;
                        float originY = backdrop is DesktopCaptureBackdrop dcb2 ? dcb2.MonitorOriginY : 0f;
                        var relativeOffset = new Vector2(screenOffset.X - originX, screenOffset.Y - originY);
                        var backdropSize = new Vector2(backdrop.FrameWidth, backdrop.FrameHeight);

                        if (_targetBitmap == null || _targetBitmap.PixelWidth != pxW || _targetBitmap.PixelHeight != pxH ||
                            Math.Abs(_targetBitmap.DpiX - 96.0 * safeScale) > 0.5)
                        {
                            _targetBitmap = new System.Windows.Media.Imaging.WriteableBitmap(
                                pxW,
                                pxH,
                                96.0 * safeScale,
                                96.0 * safeScale,
                                PixelFormats.Pbgra32,
                                null);
                        }

                        renderer.RenderToExistingBitmap(_targetBitmap, pxW, pxH, safeScale, _settings, _shapeBuffer, srv, relativeOffset, backdropSize);
                        if (_glassBrush.ImageSource != _targetBitmap)
                        {
                            _glassBrush.ImageSource = _targetBitmap;
                        }
                    }
                }
                catch (SharpGenException ex)
                {
                    GlassManager.Instance.NotifyDeviceFailure(ex);
                }
                catch (ObjectDisposedException)
                {
                }
                catch (Exception ex)
                {
                    App.Log($"[GlassHost] Render error on element {_element.GetType().Name}: {ex.Message}");
                }
            }, DispatcherPriority.Background);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _targetBitmap = null;

            _element.SizeChanged -= OnElementSizeChanged;
            _element.Loaded -= OnElementLoaded;
            _element.Unloaded -= OnElementUnloaded;
            GlassManager.Instance.FrameReady -= OnBackdropFrameReady;

            if (_element is Border b) b.Background = _originalBackground;
            else if (_element is Panel p) p.Background = _originalBackground;
            else if (_element is Control c) c.Background = _originalBackground;
        }
    }
}
