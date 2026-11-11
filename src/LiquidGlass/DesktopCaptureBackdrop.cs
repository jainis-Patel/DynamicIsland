using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.LiquidGlass;

/// <summary>
/// PATH B: High-performance, zero-CPU-readback desktop capture using Windows.Graphics.Capture.
/// Captures the monitor surface directly into GPU Direct3D 11 textures, excludes the island overlay HWND
/// via WDA_EXCLUDEFROMCAPTURE, crops the region behind the active glass shapes, and delivers frames to the shader.
/// </summary>
public sealed class DesktopCaptureBackdrop : IBackdropProvider
{
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true, ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr hWnd, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr hMonitor, [In] ref Guid iid);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    private static readonly Guid IGraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid ID3D11Texture2DGuid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    private readonly ID3D11Device _d3dDevice;
    private readonly ID3D11DeviceContext _d3dContext;
    private readonly IntPtr _targetHwnd;

    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _captureItem;
    private IDirect3DDevice? _winrtDevice;

    private ID3D11Texture2D? _currentTexture;
    private ID3D11ShaderResourceView? _currentSrv;

    private bool _isCapturing;
    private bool _screenshotMode;
    private int _frameWidth;
    private int _frameHeight;
    private readonly object _lock = new();

    public event EventHandler? FrameReady;

    public ID3D11ShaderResourceView? CurrentFrameSRV => _currentSrv;
    public ID3D11ShaderResourceView? BlurredFrameSRV => null; // Renderer blur chain generates blurred frame
    public bool IsCapturing => _isCapturing;
    public int FrameWidth => _frameWidth;
    public int FrameHeight => _frameHeight;

    /// <summary>
    /// Temporarily enables or disables screenshot visibility. When true, the island overlay
    /// becomes capturable by Snipping Tool and Win+Shift+S (but this also introduces a
    /// capture feedback loop until screenshot mode is turned off again).
    /// </summary>
    public void SetScreenshotMode(bool enabled)
    {
        if (_targetHwnd == IntPtr.Zero) return;
        _screenshotMode = enabled;
        SetWindowDisplayAffinity(_targetHwnd, enabled ? 0u /* WDA_NONE */ : WDA_EXCLUDEFROMCAPTURE);
    }

    public DesktopCaptureBackdrop(ID3D11Device device, ID3D11DeviceContext context, IntPtr targetHwnd)
    {
        _d3dDevice = device;
        _d3dContext = context;
        _targetHwnd = targetHwnd;
    }

    public static bool IsSupported()
    {
        // Windows.Graphics.Capture requires Windows 10 2004 (Build 19041) or later
        return Environment.OSVersion.Version.Major >= 10 &&
               Environment.OSVersion.Version.Build >= 19041 &&
               GraphicsCaptureSession.IsSupported();
    }

    public Task StartAsync()
    {
        if (_isCapturing) return Task.CompletedTask;

        try
        {
            if (!IsSupported())
            {
                App.Log("[DesktopCaptureBackdrop] Windows.Graphics.Capture is not supported on this OS build.");
                return Task.CompletedTask;
            }

            // 1. Exclude the island overlay from its own desktop capture to prevent
            //    recursive feedback artifacts. Note: This also hides the island from
            //    Win+Shift+S and Snipping Tool screenshots. Use ToggleScreenshotMode()
            //    to temporarily restore visibility for user-initiated screenshots.
            if (_targetHwnd != IntPtr.Zero)
            {
                SetWindowDisplayAffinity(_targetHwnd, WDA_EXCLUDEFROMCAPTURE);
            }

            // 2. Initialize WinRT Direct3D Device wrapping our D3D11 device
            using (var dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>())
            {
                int hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr pInspectable);
                if (hr != 0 || pInspectable == IntPtr.Zero)
                {
                    App.Log($"[DesktopCaptureBackdrop] Failed to create WinRT D3D device from DXGI device: 0x{hr:X8}");
                    return Task.CompletedTask;
                }
                _winrtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pInspectable);
            }

            // 3. Create Capture Item for the Monitor containing target window
            IntPtr hMonitor = MonitorFromWindow(_targetHwnd, MONITOR_DEFAULTTOPRIMARY);
            var interop = (IGraphicsCaptureItemInterop)WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
            Guid itemGuid = IGraphicsCaptureItemGuid;
            IntPtr pItem = interop.CreateForMonitor(hMonitor, ref itemGuid);
            if (pItem == IntPtr.Zero)
            {
                App.Log("[DesktopCaptureBackdrop] Failed to create GraphicsCaptureItem for monitor.");
                return Task.CompletedTask;
            }
            _captureItem = WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(pItem);

            _frameWidth = _captureItem.Size.Width;
            _frameHeight = _captureItem.Size.Height;

            // 4. Create Free-Threaded Frame Pool with max 2 buffers for lowest latency
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winrtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                _captureItem.Size);

            _framePool.FrameArrived += OnFrameArrived;

            // 5. Start Capture Session
            _session = _framePool.CreateCaptureSession(_captureItem);

            // Remove yellow capture border on Windows 11 if supported
            try
            {
                var borderProp = typeof(GraphicsCaptureSession).GetProperty("IsBorderRequired");
                if (borderProp != null && borderProp.CanWrite)
                {
                    borderProp.SetValue(_session, false);
                }
            }
            catch
            {
                // Graceful fallback if policy prohibits border removal
            }

            _session.StartCapture();
            _isCapturing = true;
            App.Log($"[DesktopCaptureBackdrop] Desktop capture started successfully ({_frameWidth}x{_frameHeight}).");
        }
        catch (Exception ex)
        {
            App.Log($"[DesktopCaptureBackdrop] Exception starting capture: {ex.Message}");
            Stop();
        }

        return Task.CompletedTask;
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            using var frame = sender.TryGetNextFrame();
            if (frame == null) return;

            // Extract native ID3D11Texture2D from WinRT surface using IDirect3DDxgiInterfaceAccess
            var access = (IDirect3DDxgiInterfaceAccess)(object)frame.Surface;
            Guid texGuid = ID3D11Texture2DGuid;
            IntPtr pTexture = access.GetInterface(ref texGuid);
            if (pTexture == IntPtr.Zero) return;

            using var capturedTex = new ID3D11Texture2D(pTexture);

            lock (_lock)
            {
                // Ensure cached destination texture matches incoming frame resolution
                if (_currentTexture == null || _frameWidth != frame.ContentSize.Width || _frameHeight != frame.ContentSize.Height)
                {
                    _currentSrv?.Dispose();
                    _currentTexture?.Dispose();

                    _frameWidth = frame.ContentSize.Width;
                    _frameHeight = frame.ContentSize.Height;

                    var desc = new Texture2DDescription
                    {
                        Width = (uint)_frameWidth,
                        Height = (uint)_frameHeight,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Default,
                        BindFlags = BindFlags.ShaderResource
                    };
                    _currentTexture = _d3dDevice.CreateTexture2D(desc);
                    _currentSrv = _d3dDevice.CreateShaderResourceView(_currentTexture);
                }

                // Copy captured frame directly on GPU (zero CPU memory transfer)
                _d3dContext.CopyResource(_currentTexture, capturedTex);
            }

            FrameReady?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            App.Log($"[DesktopCaptureBackdrop] Frame processing error: {ex.Message}");
        }
    }

    public void Stop()
    {
        _isCapturing = false;
        try
        {
            if (_framePool != null)
            {
                _framePool.FrameArrived -= OnFrameArrived;
                _framePool.Dispose();
                _framePool = null;
            }

            _session?.Dispose();
            _session = null;

            _captureItem = null;
            _winrtDevice?.Dispose();
            _winrtDevice = null;
        }
        catch (Exception ex)
        {
            App.Log($"[DesktopCaptureBackdrop] Error stopping capture: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();

        // Restore normal display affinity so the window is visible to screenshots again
        if (_targetHwnd != IntPtr.Zero)
        {
            SetWindowDisplayAffinity(_targetHwnd, 0 /* WDA_NONE */);
        }

        lock (_lock)
        {
            _currentSrv?.Dispose();
            _currentTexture?.Dispose();
            _currentSrv = null;
            _currentTexture = null;
        }
    }
}

internal static class WindowsRuntimeMarshal
{
    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, [In] ref Guid iid, out IntPtr factory);

    public static object GetActivationFactory(Type type)
    {
        string? typeName = type.FullName;
        if (string.IsNullOrEmpty(typeName)) throw new InvalidOperationException("Type must have a valid full name.");

        IntPtr hString = CreateHString(typeName);
        try
        {
            Guid iid = typeof(DesktopCaptureBackdrop).GetNestedType("IGraphicsCaptureItemInterop", System.Reflection.BindingFlags.NonPublic)!.GUID;
            int hr = RoGetActivationFactory(hString, ref iid, out IntPtr pFactory);
            if (hr != 0 || pFactory == IntPtr.Zero)
            {
                throw new COMException($"RoGetActivationFactory failed with HRESULT 0x{hr:X8}", hr);
            }
            return WinRT.MarshalInterface<object>.FromAbi(pFactory);
        }
        finally
        {
            DeleteHString(hString);
        }
    }

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    private static IntPtr CreateHString(string s)
    {
        WindowsCreateString(s, s.Length, out IntPtr h);
        return h;
    }

    private static void DeleteHString(IntPtr h)
    {
        if (h != IntPtr.Zero) WindowsDeleteString(h);
    }
}
