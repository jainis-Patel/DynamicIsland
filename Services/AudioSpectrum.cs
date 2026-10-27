using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Dsp;
using NAudio.Wave;

namespace DynamicIsland.Services;

/// <summary>
/// Reads what the speakers are playing via WASAPI loopback (nothing is recorded or stored) and
/// reduces it to 4 log-spaced frequency bands for the compact-pill meter and ambient glow.
/// Capture runs on-demand only while media is actively playing, and auto-restarts when the default output device changes.
/// </summary>
internal sealed class AudioSpectrum : IDisposable
{
    public const int BandCount = 4;

    private const int FftLog2 = 10;
    private const int FftSize = 1 << FftLog2;   // 1024 samples ≈ 21 ms @ 48 kHz
    private const int Hop = FftSize / 2;        // 50 % overlap
    private const double FloorDb = -62;
    private const double CeilingDb = -18;
    private const long StaleAfterMs = 200;      // loopback delivers no packets during silence

    // Bass, low-mid, high-mid, treble. Tilt compensates for music's natural high-frequency roll-off.
    private static readonly (double LowHz, double HighHz, double TiltDb)[] Bands =
    {
        (40, 250, 0),
        (250, 1200, 5),
        (1200, 5000, 10),
        (5000, 14000, 15),
    };

    private readonly object _gate = new();
    private readonly float[] _ring = new float[FftSize];
    private readonly float[] _window = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly float[] _bands = new float[BandCount];

    private WasapiLoopbackCapture? _capture;
    private WaveFormat? _format;
    private int _ringPos;
    private int _sinceFft;
    private long _lastDataTick;
    private bool _wanted;
    private bool _disposed;

    public AudioSpectrum()
    {
        for (int i = 0; i < FftSize; i++)
            _window[i] = (float)FastFourierTransform.HannWindow(i, FftSize);
    }

    /// <summary>
    /// Enables or pauses WASAPI loopback capture based on whether spectrum data is actively needed.
    /// Avoids background audio capture and FFT overhead when idle or paused.
    /// </summary>
    public void SetActive(bool active)
    {
        if (active) Start();
        else Stop();
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _wanted) return;
            _wanted = true;
            if (_capture is null) StartCaptureLocked();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_wanted && _capture is null) return;
            _wanted = false;
            StopCaptureLocked();
        }
        for (int i = 0; i < BandCount; i++)
            Volatile.Write(ref _bands[i], 0f);
    }

    /// <summary>Notifies the spectrum analyzer that the default render endpoint changed so it can rebind.</summary>
    public void NotifyDefaultDeviceChanged()
    {
        ScheduleRestart();
    }

    /// <summary>Copies the latest band levels (0..1) lock-free. Returns zeros when audio has gone silent.</summary>
    public void ReadBands(float[] destination)
    {
        if (!Volatile.Read(ref _wanted) || Environment.TickCount64 - Interlocked.Read(ref _lastDataTick) > StaleAfterMs)
        {
            Array.Clear(destination);
            return;
        }
        for (int i = 0; i < BandCount; i++)
            destination[i] = Volatile.Read(ref _bands[i]);
    }

    private void StartCaptureLocked()
    {
        try
        {
            var capture = new WasapiLoopbackCapture(); // default render endpoint
            _format = capture.WaveFormat;
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            capture.StartRecording();
            _capture = capture;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Audio] Loopback start failed: {ex.Message}");
            _capture = null;
        }
    }

    private void StopCaptureLocked()
    {
        var capture = _capture;
        if (capture is null) return;
        _capture = null;
        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;
        try { capture.Dispose(); } catch { /* device may already be gone */ }
    }

    private void ScheduleRestart()
    {
        Task.Run(async () =>
        {
            await Task.Delay(400).ConfigureAwait(false);
            lock (_gate)
            {
                if (!_wanted || _disposed) return;
                StopCaptureLocked();
                StartCaptureLocked();
            }
        });
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            Debug.WriteLine($"[Audio] Loopback stopped: {e.Exception.Message}");
            ScheduleRestart();
        }
    }

    // ── Capture thread ───────────────────────────────────────────────

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var format = _format;
        if (format is null || e.BytesRecorded == 0) return;

        int bits = format.BitsPerSample;
        if (bits != 32 && bits != 16) return;

        Interlocked.Exchange(ref _lastDataTick, Environment.TickCount64);

        int channels = format.Channels;
        int frameBytes = format.BlockAlign;
        int frames = e.BytesRecorded / frameBytes;
        ReadOnlySpan<byte> raw = e.Buffer.AsSpan(0, frames * frameBytes);
        float invChannels = 1.0f / channels;

        if (bits == 32)
        {
            ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(raw);
            int idx = 0;
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                    sum += samples[idx++];

                _ring[_ringPos] = sum * invChannels;
                _ringPos = (_ringPos + 1) & (FftSize - 1);
                if (++_sinceFft >= Hop)
                {
                    _sinceFft = 0;
                    Analyze(format.SampleRate);
                }
            }
        }
        else
        {
            ReadOnlySpan<short> samples = MemoryMarshal.Cast<byte, short>(raw);
            const float inv16 = 1.0f / 32768f;
            int idx = 0;
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                    sum += samples[idx++] * inv16;

                _ring[_ringPos] = sum * invChannels;
                _ringPos = (_ringPos + 1) & (FftSize - 1);
                if (++_sinceFft >= Hop)
                {
                    _sinceFft = 0;
                    Analyze(format.SampleRate);
                }
            }
        }
    }

    private void Analyze(int sampleRate)
    {
        for (int i = 0; i < FftSize; i++)
        {
            _fft[i].X = _ring[(_ringPos + i) & (FftSize - 1)] * _window[i];
            _fft[i].Y = 0;
        }
        FastFourierTransform.FFT(true, FftLog2, _fft);

        double binHz = (double)sampleRate / FftSize;
        for (int band = 0; band < BandCount; band++)
        {
            var (low, high, tilt) = Bands[band];
            int first = Math.Max(1, (int)(low / binHz));
            int last = Math.Min(FftSize / 2 - 1, (int)(high / binHz));

            double peakSq = 0;
            for (int k = first; k <= last; k++)
            {
                double magSq = _fft[k].X * _fft[k].X + _fft[k].Y * _fft[k].Y;
                if (magSq > peakSq) peakSq = magSq;
            }

            double peak = Math.Sqrt(peakSq);
            double db = 20 * Math.Log10(peak + 1e-9) + tilt;
            float level = (float)Math.Clamp((db - FloorDb) / (CeilingDb - FloorDb), 0, 1);
            Volatile.Write(ref _bands[band], level);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _wanted = false;
            StopCaptureLocked();
        }
    }
}
