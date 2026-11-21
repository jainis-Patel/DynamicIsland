using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace DynamicIsland.LiquidGlass;

/// <summary>
/// GPU-based Liquid Glass rendering engine.
/// Manages Direct3D 11 device, shaders, separable blur downsampler, constant buffers,
/// and executes per-pixel optical refraction rendering.
/// </summary>
public sealed class GlassRenderer : IDisposable
{
    public const int MaxShapes = 16;

    [StructLayout(LayoutKind.Sequential, Size = 32)]
    public struct GpuShapeData
    {
        public Vector2 Center;
        public Vector2 HalfSize;
        public float CornerRadius;
        public uint ShapeType;
        public Vector2 Padding;
    }

    [StructLayout(LayoutKind.Sequential, Size = 624)]
    public unsafe struct GpuGlassConstants
    {
        public Vector2 ViewportSize;
        public float DpiScale;
        public uint ShapeCount;

        public float Thickness;
        public float BevelWidth;
        public float IndexOfRefraction;
        public float BlurRadius;

        public float Saturation;
        public float Brightness;
        public float SpecularStrength;
        public float RimPower;

        public Vector4 TintColor;

        public Vector2 LightDirection;
        public float Dispersion;
        public float EdgeFeather;

        public float CornerSmoothing;
        public float MergeSmoothness;
        public Vector2 ScreenOffset;    // element's top-left in monitor pixels

        public Vector2 BackdropSize;    // full monitor capture texture size
        public Vector2 Reserved2;

        public fixed float ShapesData[MaxShapes * 8];
    }

    [StructLayout(LayoutKind.Sequential, Size = 16)]
    private struct GpuBlurConstants
    {
        public Vector2 TexelSize;
        public float Radius;
        public float Padding;
    }

    private readonly ID3D11Device _device = null!;
    private readonly ID3D11DeviceContext _context = null!;

    private readonly ID3D11VertexShader _vertexShader = null!;
    private readonly ID3D11PixelShader _pixelShader = null!;
    private readonly ID3D11PixelShader _blurHorizontalShader = null!;
    private readonly ID3D11PixelShader _blurVerticalShader = null!;

    private readonly ID3D11SamplerState _linearClampSampler = null!;
    private readonly ID3D11BlendState _premultipliedBlendState = null!;
    private readonly ID3D11RasterizerState _rasterizerState = null!;

    private readonly ID3D11Buffer _constantBuffer = null!;
    private readonly ID3D11Buffer _blurConstantBuffer = null!;

    // Downscaled ping-pong blur chain (1/2 or 1/4 resolution)
    private ID3D11Texture2D? _blurTexA;
    private ID3D11RenderTargetView? _blurRtvA;
    private ID3D11ShaderResourceView? _blurSrvA;

    private ID3D11Texture2D? _blurTexB;
    private ID3D11RenderTargetView? _blurRtvB;
    private ID3D11ShaderResourceView? _blurSrvB;
    private int _blurWidth;
    private int _blurHeight;

    // Cached render-target / staging / WriteableBitmap (keyed by last size)
    private ID3D11Texture2D? _cachedRenderTex;
    private ID3D11RenderTargetView? _cachedRtv;
    private ID3D11Texture2D? _cachedStagingTex;
    private WriteableBitmap? _cachedBitmap;
    private int _cachedWidth;
    private int _cachedHeight;
    private float _cachedDpiScale;

    private volatile bool _isDisposed;
    private volatile bool _isDeviceLost;

    public object RenderLock { get; } = new();
    public bool IsDisposed => _isDisposed;
    public bool IsDeviceLost => _isDeviceLost;

    public ID3D11Device Device => _device;
    public ID3D11DeviceContext Context => _context;

    public GlassRenderer()
    {
        try
        {
            // 1. Create Direct3D 11 hardware device with BGRA support for DXGI/DirectComposition
            var creationFlags = DeviceCreationFlags.BgraSupport;
            var featureLevels = new[]
            {
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0
            };

            var hr = D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                creationFlags,
                featureLevels,
                out _device!,
                out _context!);

            if (hr.Failure)
            {
                // Fallback to WARP software rasterizer (essential for CI runners, headless VMs, or basic display adapters)
                hr = D3D11.D3D11CreateDevice(
                    null,
                    DriverType.Warp,
                    creationFlags,
                    featureLevels,
                    out _device!,
                    out _context!);
                hr.CheckError();
            }

            // Enable thread protection on the immediate context as defense-in-depth alongside RenderLock
            using (var multithread = _device.QueryInterfaceOrNull<ID3D11Multithread>())
            {
                multithread?.SetMultithreadProtected(true);
            }

            // 2. Load compiled shader bytecode
            byte[] vsBytecode = LoadShaderBytecode("GlassVertexShader.cso");
            byte[] psBytecode = LoadShaderBytecode("GlassPixelShader.cso");
            byte[] blurHBytecode = LoadShaderBytecode("BlurH.cso");
            byte[] blurVBytecode = LoadShaderBytecode("BlurV.cso");

            _vertexShader = _device.CreateVertexShader(vsBytecode);
            _pixelShader = _device.CreatePixelShader(psBytecode);
            _blurHorizontalShader = _device.CreatePixelShader(blurHBytecode);
            _blurVerticalShader = _device.CreatePixelShader(blurVBytecode);

            // 3. Create Sampler State (Linear Clamp)
            _linearClampSampler = _device.CreateSamplerState(SamplerDescription.LinearClamp);

            // 4. Create Premultiplied Alpha Blend State
            var blendDesc = new BlendDescription
            {
                AlphaToCoverageEnable = false,
                IndependentBlendEnable = false
            };
            blendDesc.RenderTarget[0].BlendEnable = true;
            blendDesc.RenderTarget[0].SourceBlend = Blend.One;
            blendDesc.RenderTarget[0].DestinationBlend = Blend.InverseSourceAlpha;
            blendDesc.RenderTarget[0].BlendOperation = BlendOperation.Add;
            blendDesc.RenderTarget[0].SourceBlendAlpha = Blend.One;
            blendDesc.RenderTarget[0].DestinationBlendAlpha = Blend.InverseSourceAlpha;
            blendDesc.RenderTarget[0].BlendOperationAlpha = BlendOperation.Add;
            blendDesc.RenderTarget[0].RenderTargetWriteMask = ColorWriteEnable.All;
            _premultipliedBlendState = _device.CreateBlendState(blendDesc);

            // 5. Create Rasterizer State
            _rasterizerState = _device.CreateRasterizerState(RasterizerDescription.CullNone);

            // 6. Create Constant Buffers
            _constantBuffer = _device.CreateBuffer(new BufferDescription
            {
                ByteWidth = (uint)Marshal.SizeOf<GpuGlassConstants>(),
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                CPUAccessFlags = CpuAccessFlags.Write
            });

            _blurConstantBuffer = _device.CreateBuffer(new BufferDescription
            {
                ByteWidth = 16,
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                CPUAccessFlags = CpuAccessFlags.Write
            });
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static byte[] LoadShaderBytecode(string filename)
    {
        var assembly = Assembly.GetExecutingAssembly();
        string resourceName = $"DynamicIsland.src.LiquidGlass.Shaders.{filename}";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream != null)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        string directPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "src", "LiquidGlass", "Shaders", filename);
        if (File.Exists(directPath))
        {
            return File.ReadAllBytes(directPath);
        }

        string localPath = Path.Combine(Environment.CurrentDirectory, "src", "LiquidGlass", "Shaders", filename);
        if (File.Exists(localPath))
        {
            return File.ReadAllBytes(localPath);
        }

        throw new FileNotFoundException($"Compiled HLSL shader bytecode not found: {filename}");
    }

    /// <summary>
    /// Executes the separable Gaussian blur chain, downsampling the backdrop to half resolution
    /// and running 1 horizontal pass and 1 vertical pass into a dedicated texture.
    /// </summary>
    public ID3D11ShaderResourceView RunBlurPipeline(ID3D11ShaderResourceView sourceSrv, int srcWidth, int srcHeight, float blurRadius)
    {
        lock (RenderLock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            try
            {
                return RunBlurPipelineCore(sourceSrv, srcWidth, srcHeight, blurRadius);
            }
            catch (SharpGenException ex) when (IsDeviceLostError(ex))
            {
                _isDeviceLost = true;
                throw;
            }
        }
    }

    private ID3D11ShaderResourceView RunBlurPipelineCore(ID3D11ShaderResourceView sourceSrv, int srcWidth, int srcHeight, float blurRadius)
    {
        int targetW = Math.Max(srcWidth / 2, 64);
        int targetH = Math.Max(srcHeight / 2, 64);

        EnsureBlurBuffers(targetW, targetH);

        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.PSSetSampler(0, _linearClampSampler);
        _context.RSSetState(_rasterizerState);
        _context.OMSetBlendState((ID3D11BlendState?)null);

        var viewport = new Viewport(0, 0, targetW, targetH, 0.0f, 1.0f);
        _context.RSSetViewport(viewport);

        // Pass 1: Horizontal Blur (Source -> BlurTexA)
        _context.OMSetRenderTargets(_blurRtvA!, null);
        _context.PSSetShader(_blurHorizontalShader);
        _context.PSSetShaderResource(0, sourceSrv);

        var blurConst = new GpuBlurConstants
        {
            TexelSize = new Vector2(1.0f / targetW, 1.0f / targetH),
            Radius = Math.Clamp(blurRadius * 0.15f, 0.5f, 5.0f)
        };
        UpdateBuffer(_blurConstantBuffer, blurConst);
        _context.PSSetConstantBuffer(0, _blurConstantBuffer);

        _context.Draw(3, 0);

        // Unbind texture from slot 0
        _context.PSSetShaderResource(0, null!);

        // Pass 2: Vertical Blur (BlurTexA -> BlurTexB)
        _context.OMSetRenderTargets(_blurRtvB!, null);
        _context.PSSetShader(_blurVerticalShader);
        _context.PSSetShaderResource(0, _blurSrvA!);

        _context.Draw(3, 0);

        // Reset
        _context.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
        _context.PSSetShaderResource(0, null!);

        return _blurSrvB!;
    }

    private void EnsureBlurBuffers(int width, int height)
    {
        if (_blurTexA != null && _blurWidth == width && _blurHeight == height)
            return;

        _blurSrvA?.Dispose();
        _blurRtvA?.Dispose();
        _blurTexA?.Dispose();

        _blurSrvB?.Dispose();
        _blurRtvB?.Dispose();
        _blurTexB?.Dispose();

        _blurWidth = width;
        _blurHeight = height;

        var desc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource
        };

        _blurTexA = _device.CreateTexture2D(desc);
        _blurRtvA = _device.CreateRenderTargetView(_blurTexA);
        _blurSrvA = _device.CreateShaderResourceView(_blurTexA);

        _blurTexB = _device.CreateTexture2D(desc);
        _blurRtvB = _device.CreateRenderTargetView(_blurTexB);
        _blurSrvB = _device.CreateShaderResourceView(_blurTexB);
    }

    /// <summary>
    /// Renders one or more Liquid Glass shapes in a single draw pass over the backdrop.
    /// </summary>
    public void Render(
        ID3D11RenderTargetView targetRtv,
        int targetWidth,
        int targetHeight,
        float dpiScale,
        GlassSettings settings,
        ReadOnlySpan<GlassShape> shapes,
        ID3D11ShaderResourceView backdropSrv,
        ID3D11ShaderResourceView? blurredBackdropSrv = null,
        Vector2 screenOffset = default,
        Vector2 backdropSize = default)
    {
        lock (RenderLock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            try
            {
                RenderCore(targetRtv, targetWidth, targetHeight, dpiScale, settings, shapes, backdropSrv, blurredBackdropSrv, screenOffset, backdropSize);
            }
            catch (SharpGenException ex) when (IsDeviceLostError(ex))
            {
                _isDeviceLost = true;
                throw;
            }
        }
    }

    private unsafe void RenderCore(
        ID3D11RenderTargetView targetRtv,
        int targetWidth,
        int targetHeight,
        float dpiScale,
        GlassSettings settings,
        ReadOnlySpan<GlassShape> shapes,
        ID3D11ShaderResourceView backdropSrv,
        ID3D11ShaderResourceView? blurredBackdropSrv = null,
        Vector2 screenOffset = default,
        Vector2 backdropSize = default)
    {
        int blurSrcW = backdropSize.X > 1.0f ? (int)backdropSize.X : targetWidth;
        int blurSrcH = backdropSize.Y > 1.0f ? (int)backdropSize.Y : targetHeight;
        ID3D11ShaderResourceView blurSrv = blurredBackdropSrv ??
            RunBlurPipelineCore(backdropSrv, blurSrcW, blurSrcH, settings.BlurRadius);

        var viewport = new Viewport(0, 0, targetWidth, targetHeight, 0.0f, 1.0f);
        _context.RSSetViewport(viewport);
        _context.OMSetRenderTargets(targetRtv, null);

        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(_pixelShader);
        _context.PSSetSampler(0, _linearClampSampler);
        _context.RSSetState(_rasterizerState);
        _context.OMSetBlendState(_premultipliedBlendState);

        var constants = new GpuGlassConstants
        {
            ViewportSize = new Vector2(targetWidth, targetHeight),
            DpiScale = dpiScale,
            ShapeCount = (uint)Math.Clamp(shapes.Length, 1, MaxShapes),
            Thickness = settings.Thickness * Core.LiquidGlassTheme.GlobalThicknessScale,
            BevelWidth = settings.BevelWidth * dpiScale,
            IndexOfRefraction = settings.IndexOfRefraction,
            BlurRadius = settings.BlurRadius * Core.LiquidGlassTheme.GlobalBlurScale,
            Saturation = settings.Saturation,
            Brightness = settings.Brightness,
            SpecularStrength = settings.SpecularStrength * Core.LiquidGlassTheme.GlobalSpecularScale,
            RimPower = settings.RimPower,
            TintColor = new Vector4(
                settings.TintColor.R / 255.0f,
                settings.TintColor.G / 255.0f,
                settings.TintColor.B / 255.0f,
                settings.TintColor.A / 255.0f),
            LightDirection = settings.LightDirection,
            Dispersion = settings.Dispersion,
            EdgeFeather = settings.EdgeFeather,
            CornerSmoothing = settings.CornerSmoothing,
            MergeSmoothness = settings.MergeSmoothness * dpiScale,
            ScreenOffset = screenOffset,
            BackdropSize = backdropSize
        };

        for (int i = 0; i < constants.ShapeCount; i++)
        {
            var s = shapes[i];
            int offset = i * 8;
            constants.ShapesData[offset + 0] = s.CenterX;
            constants.ShapesData[offset + 1] = s.CenterY;
            constants.ShapesData[offset + 2] = s.HalfWidth;
            constants.ShapesData[offset + 3] = s.HalfHeight;
            constants.ShapesData[offset + 4] = s.CornerRadius;
            constants.ShapesData[offset + 5] = BitConverter.UInt32BitsToSingle((uint)s.ShapeType);
            constants.ShapesData[offset + 6] = 0.0f;
            constants.ShapesData[offset + 7] = 0.0f;
        }

        UpdateBuffer(_constantBuffer, constants);
        _context.PSSetConstantBuffer(0, _constantBuffer);

        _context.PSSetShaderResources(0, new[] { backdropSrv, blurSrv });
        _context.Draw(3, 0);

        _context.PSSetShaderResource(0, null!);
        _context.PSSetShaderResource(1, null!);
        _context.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
    }

    /// <summary>
    /// Ensures the cached render target and staging texture match the requested size.
    /// Disposes and recreates them when the size or DPI changes.
    /// </summary>
    private void EnsureCachedResources(int width, int height, float dpiScale, bool allocateBitmap)
    {
        if (_cachedRenderTex != null && _cachedWidth == width && _cachedHeight == height && _cachedDpiScale == dpiScale)
        {
            if (allocateBitmap && _cachedBitmap == null)
            {
                _cachedBitmap = new WriteableBitmap(width, height, (int)(96.0 * dpiScale), (int)(96.0 * dpiScale), System.Windows.Media.PixelFormats.Pbgra32, null);
            }
            return;
        }

        // Dispose old resources
        _cachedRtv?.Dispose();
        _cachedRenderTex?.Dispose();
        _cachedStagingTex?.Dispose();
        _cachedRtv = null;
        _cachedRenderTex = null;
        _cachedStagingTex = null;
        // WriteableBitmap is managed — no Dispose needed, just drop the reference
        _cachedBitmap = null;

        _cachedWidth = width;
        _cachedHeight = height;
        _cachedDpiScale = dpiScale;

        var renderDesc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget
        };

        _cachedRenderTex = _device.CreateTexture2D(renderDesc);
        _cachedRtv = _device.CreateRenderTargetView(_cachedRenderTex);

        var stagingDesc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read
        };

        _cachedStagingTex = _device.CreateTexture2D(stagingDesc);
        if (allocateBitmap)
        {
            _cachedBitmap = new WriteableBitmap(width, height, (int)(96.0 * dpiScale), (int)(96.0 * dpiScale), System.Windows.Media.PixelFormats.Pbgra32, null);
        }
    }

    /// <summary>
    /// Renders Liquid Glass to an offscreen BGRA32 bitmap for golden-image testing or WPF ImageSource presentation.
    /// Uses pooled render target, staging texture, and WriteableBitmap to avoid per-frame allocation.
    /// </summary>
    public unsafe WriteableBitmap RenderToBitmap(
        int width,
        int height,
        float dpiScale,
        GlassSettings settings,
        ReadOnlySpan<GlassShape> shapes,
        ID3D11ShaderResourceView backdropSrv,
        Vector2 screenOffset = default,
        Vector2 backdropSize = default)
    {
        lock (RenderLock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            try
            {
                EnsureCachedResources(width, height, dpiScale, allocateBitmap: true);

                _context.ClearRenderTargetView(_cachedRtv!, new Color4(0, 0, 0, 0));

                RenderCore(_cachedRtv!, width, height, dpiScale, settings, shapes, backdropSrv,
                       screenOffset: screenOffset, backdropSize: backdropSize);

                _context.CopyResource(_cachedStagingTex!, _cachedRenderTex!);

                var wb = _cachedBitmap!;
                wb.Lock();

                var mapped = _context.Map(_cachedStagingTex!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    for (int y = 0; y < height; y++)
                    {
                        IntPtr srcRow = IntPtr.Add(mapped.DataPointer, (int)(y * mapped.RowPitch));
                        IntPtr dstRow = IntPtr.Add(wb.BackBuffer, y * wb.BackBufferStride);
                        NativeMemory.Copy((void*)srcRow, (void*)dstRow, (nuint)(width * 4));
                    }
                    wb.AddDirtyRect(new System.Windows.Int32Rect(0, 0, width, height));
                }
                finally
                {
                    _context.Unmap(_cachedStagingTex!, 0);
                    wb.Unlock();
                }

                return wb;
            }
            catch (SharpGenException ex) when (IsDeviceLostError(ex))
            {
                _isDeviceLost = true;
                throw;
            }
        }
    }

    /// <summary>
    /// Renders Liquid Glass into an EXISTING WriteableBitmap, avoiding bitmap allocation entirely.
    /// The caller owns the WriteableBitmap lifetime.
    /// </summary>
    public unsafe void RenderToExistingBitmap(
        WriteableBitmap target,
        int width,
        int height,
        float dpiScale,
        GlassSettings settings,
        ReadOnlySpan<GlassShape> shapes,
        ID3D11ShaderResourceView backdropSrv,
        Vector2 screenOffset = default,
        Vector2 backdropSize = default)
    {
        if (target == null || width <= 0 || height <= 0)
            return;

        if (target.PixelWidth < width || target.PixelHeight < height ||
            (target.Format != System.Windows.Media.PixelFormats.Bgra32 &&
             target.Format != System.Windows.Media.PixelFormats.Pbgra32))
        {
            return;
        }

        lock (RenderLock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            try
            {
                EnsureCachedResources(width, height, dpiScale, allocateBitmap: false);

                _context.ClearRenderTargetView(_cachedRtv!, new Color4(0, 0, 0, 0));

                RenderCore(_cachedRtv!, width, height, dpiScale, settings, shapes, backdropSrv,
                       screenOffset: screenOffset, backdropSize: backdropSize);

                _context.CopyResource(_cachedStagingTex!, _cachedRenderTex!);

                target.Lock();

                var mapped = _context.Map(_cachedStagingTex!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    int rowBytes = width * 4;
                    if (target.BackBufferStride >= rowBytes)
                    {
                        for (int y = 0; y < height; y++)
                        {
                            IntPtr srcRow = IntPtr.Add(mapped.DataPointer, (int)(y * mapped.RowPitch));
                            IntPtr dstRow = IntPtr.Add(target.BackBuffer, y * target.BackBufferStride);
                            NativeMemory.Copy((void*)srcRow, (void*)dstRow, (nuint)rowBytes);
                        }
                        target.AddDirtyRect(new System.Windows.Int32Rect(0, 0, width, height));
                    }
                }
                finally
                {
                    _context.Unmap(_cachedStagingTex!, 0);
                    target.Unlock();
                }
            }
            catch (SharpGenException ex) when (IsDeviceLostError(ex))
            {
                _isDeviceLost = true;
                throw;
            }
        }
    }

    public static bool IsDeviceLostError(SharpGenException ex)
    {
        const int DXGI_ERROR_DEVICE_REMOVED = unchecked((int)0x887A0005);
        const int DXGI_ERROR_DEVICE_RESET = unchecked((int)0x887A0007);
        const int DXGI_ERROR_ACCESS_LOST = unchecked((int)0x887A0026);
        int code = ex.ResultCode.Code;
        return code == DXGI_ERROR_DEVICE_REMOVED ||
               code == DXGI_ERROR_DEVICE_RESET ||
               code == DXGI_ERROR_ACCESS_LOST;
    }

    private unsafe void UpdateBuffer<T>(ID3D11Buffer buffer, T data) where T : unmanaged
    {
        var mapped = _context.Map(buffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        *(T*)mapped.DataPointer = data;
        _context.Unmap(buffer, 0);
    }

    public void Dispose()
    {
        lock (RenderLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // Cached render-to-bitmap resources
            _cachedRtv?.Dispose();
            _cachedRenderTex?.Dispose();
            _cachedStagingTex?.Dispose();
            _cachedRtv = null;
            _cachedRenderTex = null;
            _cachedStagingTex = null;
            _cachedBitmap = null;

            _blurSrvA?.Dispose();
            _blurRtvA?.Dispose();
            _blurTexA?.Dispose();
            _blurSrvA = null;
            _blurRtvA = null;
            _blurTexA = null;

            _blurSrvB?.Dispose();
            _blurRtvB?.Dispose();
            _blurTexB?.Dispose();
            _blurSrvB = null;
            _blurRtvB = null;
            _blurTexB = null;

            _constantBuffer?.Dispose();
            _blurConstantBuffer?.Dispose();

            _rasterizerState?.Dispose();
            _premultipliedBlendState?.Dispose();
            _linearClampSampler?.Dispose();

            _blurVerticalShader?.Dispose();
            _blurHorizontalShader?.Dispose();
            _pixelShader?.Dispose();
            _vertexShader?.Dispose();

            _context?.Dispose();
            _device?.Dispose();
        }
    }
}
