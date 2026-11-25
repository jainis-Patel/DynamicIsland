using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace DynamicIsland.Services;

/// <summary>
/// Zero-idle pre-call webcam mirror &amp; live microphone level meter.
/// Opens the camera and capture endpoint ONLY while MirrorView is active and releases them immediately on stop.
/// </summary>
internal sealed class WebcamMirrorService : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private MediaCapture? _capture;
    private MediaFrameReader? _frameReader;
    private MMDeviceEnumerator? _micEnumerator;
    private MMDevice? _micDevice;
    private DispatcherTimer? _micTimer;

    private List<DeviceInformation> _cameras = new();
    private int _activeCameraIndex;
    private long _lastFrameTicks;
    private byte[]? _pixelBuffer;
    private WriteableBitmap? _bitmap;
    private int _bitmapWidth;
    private int _bitmapHeight;
    private volatile bool _isRunning;
    private volatile bool _disposed;

    public bool IsRunning => _isRunning;
    public int CameraCount => _cameras.Count;
    public string ActiveCameraName { get; private set; } = "No camera";

    public event Action<WriteableBitmap>? FrameArrived;
    public event Action<float>? MicLevelChanged;
    public event Action<string>? StatusChanged;

    public WebcamMirrorService(Dispatcher ui)
    {
        _ui = ui;
    }

    public async Task StartAsync(int? cameraIndex = null)
    {
        if (_disposed) return;

        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            await StopInternalAsync().ConfigureAwait(false);

            EmitStatus("Starting camera...");
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            _cameras = devices.ToList();
            if (_cameras.Count == 0)
            {
                ActiveCameraName = "No camera";
                EmitStatus("No camera detected");
                StartMicMeter();
                return;
            }

            if (cameraIndex.HasValue)
            {
                _activeCameraIndex = ((cameraIndex.Value % _cameras.Count) + _cameras.Count) % _cameras.Count;
            }
            else if (_activeCameraIndex >= _cameras.Count)
            {
                _activeCameraIndex = 0;
            }

            var chosen = _cameras[_activeCameraIndex];
            ActiveCameraName = string.IsNullOrWhiteSpace(chosen.Name) ? $"Camera {_activeCameraIndex + 1}" : chosen.Name;

            var capture = new MediaCapture();
            var settings = new MediaCaptureInitializationSettings
            {
                VideoDeviceId = chosen.Id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu
            };

            await capture.InitializeAsync(settings);
            _capture = capture;

            var colorSource = capture.FrameSources.Values.FirstOrDefault(
                s => s.Info.SourceKind == MediaFrameSourceKind.Color);

            if (colorSource == null)
            {
                EmitStatus("No color video stream");
                await StopInternalAsync().ConfigureAwait(false);
                return;
            }

            var reader = await capture.CreateFrameReaderAsync(colorSource, MediaEncodingSubtypes.Bgra8);
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            reader.FrameArrived += OnReaderFrameArrived;
            var status = await reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                EmitStatus($"Camera busy ({status})");
                reader.FrameArrived -= OnReaderFrameArrived;
                reader.Dispose();
                await StopInternalAsync().ConfigureAwait(false);
                return;
            }

            _frameReader = reader;
            _isRunning = true;
            EmitStatus(string.Empty);
            StartMicMeter();
        }
        catch (UnauthorizedAccessException)
        {
            EmitStatus("Camera access denied in Windows Privacy Settings");
            await StopInternalAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            App.Log($"[WebcamMirror] Start failed: {ex.Message}");
            EmitStatus("Camera unavailable");
            await StopInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task SwitchCameraAsync()
    {
        if (_disposed || _cameras.Count <= 1) return;
        await StartAsync(_activeCameraIndex + 1).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task StopInternalAsync()
    {
        _isRunning = false;
        StopMicMeter();

        if (_frameReader != null)
        {
            try
            {
                _frameReader.FrameArrived -= OnReaderFrameArrived;
                await _frameReader.StopAsync();
            }
            catch (Exception ex) { App.LogDebug("WebcamMirror.StopReader", ex); }
            try { _frameReader.Dispose(); } catch (Exception ex) { App.LogDebug("WebcamMirror.DisposeReader", ex); }
            _frameReader = null;
        }

        if (_capture != null)
        {
            try { _capture.Dispose(); } catch (Exception ex) { App.LogDebug("WebcamMirror.DisposeCapture", ex); }
            _capture = null;
        }
    }

    private void OnReaderFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (!_isRunning || _disposed) return;

        long now = Environment.TickCount64;
        if ((now - Interlocked.Read(ref _lastFrameTicks)) < 40) return; // ~25 FPS cap
        Interlocked.Exchange(ref _lastFrameTicks, now);

        using var frame = sender.TryAcquireLatestFrame();
        var rawBitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (rawBitmap == null) return;

        SoftwareBitmap? converted = null;
        try
        {
            var src = rawBitmap;
            if (src.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || src.BitmapAlphaMode == BitmapAlphaMode.Straight)
            {
                converted = SoftwareBitmap.Convert(src, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                src = converted;
            }

            int w = src.PixelWidth;
            int h = src.PixelHeight;
            if (w <= 0 || h <= 0) return;

            int stride = w * 4;
            int requiredBytes = stride * h;
            var buf = _pixelBuffer;
            if (buf == null || buf.Length < requiredBytes)
            {
                buf = new byte[requiredBytes];
                _pixelBuffer = buf;
            }

            src.CopyToBuffer(buf.AsBuffer(0, requiredBytes));

            _ = _ui.InvokeAsync(() =>
            {
                if (!_isRunning || _disposed) return;
                if (_bitmap == null || _bitmapWidth != w || _bitmapHeight != h)
                {
                    _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                    _bitmapWidth = w;
                    _bitmapHeight = h;
                }
                _bitmap.WritePixels(new Int32Rect(0, 0, w, h), buf, stride, 0);
                FrameArrived?.Invoke(_bitmap);
            }, DispatcherPriority.Render);
        }
        catch (Exception ex)
        {
            App.LogDebug("WebcamMirror.Frame", ex);
        }
        finally
        {
            converted?.Dispose();
            rawBitmap.Dispose();
        }
    }

    private void StartMicMeter()
    {
        _ = _ui.InvokeAsync(() =>
        {
            if (_disposed) return;
            StopMicMeterOnUi();
            try
            {
                _micEnumerator ??= new MMDeviceEnumerator();
                _micDevice = _micEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                _micTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background, (_, _) =>
                {
                    if (_disposed || _micDevice == null) return;
                    try
                    {
                        float peak = Math.Clamp(_micDevice.AudioMeterInformation.MasterPeakValue, 0f, 1f);
                        MicLevelChanged?.Invoke(peak);
                    }
                    catch (Exception ex) { App.LogDebug("WebcamMirror.MicPeak", ex); }
                }, _ui);
                _micTimer.Start();
            }
            catch (Exception ex)
            {
                App.LogDebug("WebcamMirror.MicInit", ex);
                MicLevelChanged?.Invoke(0f);
            }
        });
    }

    private void StopMicMeter()
    {
        if (_ui.CheckAccess())
            StopMicMeterOnUi();
        else
            _ = _ui.InvokeAsync(StopMicMeterOnUi);
    }

    private void StopMicMeterOnUi()
    {
        _micTimer?.Stop();
        _micTimer = null;
        try { _micDevice?.Dispose(); } catch { }
        _micDevice = null;
        MicLevelChanged?.Invoke(0f);
    }

    private void EmitStatus(string status)
    {
        _ = _ui.InvokeAsync(() => StatusChanged?.Invoke(status));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isRunning = false;
        StopMicMeter();
        try { _frameReader?.Dispose(); } catch { }
        _frameReader = null;
        try { _capture?.Dispose(); } catch { }
        _capture = null;
        try { _micEnumerator?.Dispose(); } catch { }
        _micEnumerator = null;
        _lifecycleLock.Dispose();
    }
}
