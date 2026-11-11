using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace DynamicIsland.LiquidGlass;

/// <summary>
/// Provides a static or procedurally synthesized high-contrast test pattern backdrop for the D3D11 glass shader.
/// Used in unit tests, golden-image tests, and the interactive Glass Playground.
/// </summary>
public sealed class StaticBackdropProvider : IBackdropProvider
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private ID3D11Texture2D? _texture;
    private ID3D11ShaderResourceView? _srv;
    private bool _isCapturing;

    public event EventHandler? FrameReady;

    public ID3D11ShaderResourceView? CurrentFrameSRV => _srv;
    public ID3D11ShaderResourceView? BlurredFrameSRV => _srv; // Fallback to same SRV if unblurred
    public bool IsCapturing => _isCapturing;
    public int FrameWidth { get; private set; }
    public int FrameHeight { get; private set; }

    public StaticBackdropProvider(ID3D11Device device, ID3D11DeviceContext context)
    {
        _device = device;
        _context = context;
    }

    public Task StartAsync()
    {
        _isCapturing = true;
        if (_texture == null)
        {
            GenerateTestPattern(1280, 720);
        }
        FrameReady?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _isCapturing = false;
    }

    /// <summary>
    /// Procedurally synthesizes a high-contrast test pattern with fine grid lines, diagonal stripes,
    /// vibrant color bars, and typography blocks to visually verify optical refraction.
    /// </summary>
    public void GenerateTestPattern(int width, int height)
    {
        FrameWidth = width;
        FrameHeight = height;

        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        // Fill background pattern
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * stride + x * 4;

                // 1. Grid check (every 32 pixels)
                bool isGrid = (x % 32 == 0) || (y % 32 == 0);
                // 2. High-frequency stripe check (every 16 pixels diagonal)
                bool isStripe = ((x + y) / 12) % 2 == 0;

                // 3. Color bar zone at bottom 20%
                if (y > height * 0.75)
                {
                    int zone = (x * 6) / width;
                    byte r = (byte)(zone == 0 || zone == 3 || zone == 4 ? 240 : 20);
                    byte g = (byte)(zone == 1 || zone == 3 || zone == 5 ? 240 : 20);
                    byte b = (byte)(zone == 2 || zone == 4 || zone == 5 ? 240 : 20);
                    pixels[idx + 0] = b;
                    pixels[idx + 1] = g;
                    pixels[idx + 2] = r;
                    pixels[idx + 3] = 255;
                    continue;
                }

                if (isGrid)
                {
                    // Bright Cyan Grid Line
                    pixels[idx + 0] = 0xD0; // B
                    pixels[idx + 1] = 0xAA; // G
                    pixels[idx + 2] = 0x00; // R
                    pixels[idx + 3] = 0xFF;
                }
                else if (isStripe)
                {
                    // Dark navy stripe
                    pixels[idx + 0] = 0x38;
                    pixels[idx + 1] = 0x1A;
                    pixels[idx + 2] = 0x14;
                    pixels[idx + 3] = 0xFF;
                }
                else
                {
                    // Deep slate background
                    pixels[idx + 0] = 0x1C;
                    pixels[idx + 1] = 0x14;
                    pixels[idx + 2] = 0x10;
                    pixels[idx + 3] = 0xFF;
                }
            }
        }

        UploadPixels(pixels, width, height, stride);
    }

    /// <summary>
    /// Loads a static bitmap (e.g. wallpaper or test card) into the GPU texture.
    /// </summary>
    public void LoadBitmap(BitmapSource bitmap)
    {
        var formatted = new FormatConvertedBitmap(bitmap, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int width = formatted.PixelWidth;
        int height = formatted.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        formatted.CopyPixels(pixels, stride, 0);

        FrameWidth = width;
        FrameHeight = height;
        UploadPixels(pixels, width, height, stride);
    }

    private void UploadPixels(byte[] pixels, int width, int height, int stride)
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
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None
        };

        unsafe
        {
            fixed (byte* pPixels = pixels)
            {
                var subresource = new SubresourceData(new IntPtr(pPixels), (uint)stride, 0);
                _texture = _device.CreateTexture2D(desc, new[] { subresource });
            }
        }

        _srv = _device.CreateShaderResourceView(_texture);
        FrameReady?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _srv?.Dispose();
        _texture?.Dispose();
        _srv = null;
        _texture = null;
    }
}
