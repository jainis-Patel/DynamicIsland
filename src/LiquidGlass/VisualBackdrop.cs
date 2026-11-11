using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace DynamicIsland.LiquidGlass;

/// <summary>
/// PATH A: In-App Backdrop Provider.
/// Renders an internal WPF Visual (e.g. widget panel, canvas, or dialog body) to an offscreen GPU texture.
/// Features aggressive caching: only re-renders to GPU when dirty or when Invalidate() is called.
/// </summary>
public sealed class VisualBackdrop : IBackdropProvider
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly Visual _sourceVisual;

    private ID3D11Texture2D? _texture;
    private ID3D11ShaderResourceView? _srv;
    private bool _isCapturing;
    private bool _isDirty = true;
    private int _width;
    private int _height;
    private double _dpiScale = 1.0;

    public event EventHandler? FrameReady;

    public ID3D11ShaderResourceView? CurrentFrameSRV => _srv;
    public ID3D11ShaderResourceView? BlurredFrameSRV => null;
    public bool IsCapturing => _isCapturing;
    public int FrameWidth => _width;
    public int FrameHeight => _height;

    public VisualBackdrop(ID3D11Device device, ID3D11DeviceContext context, Visual sourceVisual, int width, int height, double dpiScale = 1.0)
    {
        _device = device;
        _context = context;
        _sourceVisual = sourceVisual;
        _width = Math.Max(width, 1);
        _height = Math.Max(height, 1);
        _dpiScale = Math.Max(dpiScale, 0.5);
    }

    public Task StartAsync()
    {
        _isCapturing = true;
        _isDirty = true;
        RenderVisual();
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _isCapturing = false;
    }

    /// <summary>
    /// Marks the visual backdrop as dirty so the next pass captures updated app content.
    /// </summary>
    public void Invalidate()
    {
        _isDirty = true;
        if (_isCapturing)
        {
            RenderVisual();
        }
    }

    public void Resize(int width, int height, double dpiScale)
    {
        if (_width == width && _height == height && Math.Abs(_dpiScale - dpiScale) < 0.001)
            return;

        _width = Math.Max(width, 1);
        _height = Math.Max(height, 1);
        _dpiScale = dpiScale;
        _isDirty = true;
        if (_isCapturing)
        {
            RenderVisual();
        }
    }

    private void RenderVisual()
    {
        if (!_isDirty) return;

        try
        {
            int pixelW = (int)(_width * _dpiScale);
            int pixelH = (int)(_height * _dpiScale);

            var rtb = new RenderTargetBitmap(
                pixelW,
                pixelH,
                96.0 * _dpiScale,
                96.0 * _dpiScale,
                PixelFormats.Pbgra32);

            rtb.Render(_sourceVisual);

            int stride = pixelW * 4;
            byte[] pixels = new byte[stride * pixelH];
            rtb.CopyPixels(pixels, stride, 0);

            UpdateGpuTexture(pixels, pixelW, pixelH, stride);
            _isDirty = false;
            FrameReady?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            App.Log($"[VisualBackdrop] Error rendering WPF visual: {ex.Message}");
        }
    }

    private void UpdateGpuTexture(byte[] pixels, int width, int height, int stride)
    {
        if (_texture == null || _width != width || _height != height)
        {
            _srv?.Dispose();
            _texture?.Dispose();

            var desc = new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource
            };

            unsafe
            {
                fixed (byte* p = pixels)
                {
                    var data = new SubresourceData(new IntPtr(p), (uint)stride, 0);
                    _texture = _device.CreateTexture2D(desc, new[] { data });
                }
            }
            _srv = _device.CreateShaderResourceView(_texture);
        }
        else
        {
            unsafe
            {
                fixed (byte* p = pixels)
                {
                    _context.UpdateSubresource(_texture, 0, null, new IntPtr(p), (uint)stride, 0);
                }
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _srv?.Dispose();
        _texture?.Dispose();
        _srv = null;
        _texture = null;
    }
}
