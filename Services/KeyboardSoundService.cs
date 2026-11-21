using System.IO;
using System.Runtime.InteropServices;
using NAudio.Wave;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

public enum MechSoundProfile
{
    CherryBlue,
    TactileBrown,
    RetroTypewriter
}

/// <summary>
/// Mechanical Keyboard Sounds Droplet:
/// Synthesizes realistic mechanical switch acoustics (Cherry Blue clicky, Brown tactile thock, and Typewriter)
/// with micro-pitch jitter for organic feel and ultra-low latency playback via WASAPI.
/// The low-level keyboard hook runs ONLY while this droplet is enabled in DropletRegistry (0% CPU / 0 RAM when disabled).
/// </summary>
internal sealed class KeyboardSoundService : IDisposable
{
    private const int SampleRate = 44100;

    private readonly byte[][] _cherryBlueSamples = new byte[6][];
    private readonly byte[][] _tactileBrownSamples = new byte[6][];
    private readonly byte[][] _typewriterSamples = new byte[6][];

    private readonly Random _rng = new();
    private readonly object _playLock = new();
    private readonly object _sampleInitLock = new();
    private readonly byte[] _scaledBuffer = new byte[8192];

    private IntPtr _hook = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;
    private WasapiOut? _wasapiOut;
    private BufferedWaveProvider? _waveProvider;
    private Timer? _idlePauseTimer;
    private bool _samplesInitialized;

    private MechSoundProfile _profile = MechSoundProfile.CherryBlue;
    private volatile bool _isEnabled;
    private float _volume = 0.5f;
    private volatile bool _disposed;

    public MechSoundProfile Profile
    {
        get => _profile;
        set => _profile = value;
    }

    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0.0f, 1.0f);
    }

    public bool IsEnabled => _isEnabled;

    public KeyboardSoundService()
    {
        // Acoustic sample buffers are lazily computed on first enable/click to save startup RAM & CPU.
    }

    private void EnsureSamplesInitialized()
    {
        if (Volatile.Read(ref _samplesInitialized)) return;
        lock (_sampleInitLock)
        {
            if (_samplesInitialized) return;
            for (int i = 0; i < 6; i++)
            {
                double pitchMod = 1.0 + (i - 3) * 0.035; // +/- pitch modulation
                _cherryBlueSamples[i] = GenerateSwitchSound(clickFreq: 2900 * pitchMod, thockFreq: 420 * pitchMod, decay: 75.0, noiseLevel: 0.18, durationMs: 38);
                _tactileBrownSamples[i] = GenerateSwitchSound(clickFreq: 1100 * pitchMod, thockFreq: 240 * pitchMod, decay: 55.0, noiseLevel: 0.22, durationMs: 44);
                _typewriterSamples[i] = GenerateSwitchSound(clickFreq: 3600 * pitchMod, thockFreq: 310 * pitchMod, decay: 85.0, noiseLevel: 0.35, durationMs: 48);
            }
            _samplesInitialized = true;
        }
    }

    private static byte[] GenerateSwitchSound(double clickFreq, double thockFreq, double decay, double noiseLevel, int durationMs)
    {
        int numSamples = (int)(SampleRate * (durationMs / 1000.0));
        short[] pcm = new short[numSamples];
        var rnd = new Random((int)(clickFreq * 100));

        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / SampleRate;
            double envelope = Math.Exp(-decay * t);

            // Resonant click + bottom-out housing thock + mechanical friction noise
            double click = Math.Sin(2.0 * Math.PI * clickFreq * t);
            double thock = Math.Sin(2.0 * Math.PI * thockFreq * t);
            double noise = (rnd.NextDouble() * 2.0 - 1.0) * noiseLevel;

            double sample = (click * 0.55 + thock * 0.45 + noise) * envelope;
            sample = Math.Clamp(sample, -1.0, 1.0);
            pcm[i] = (short)(sample * short.MaxValue * 0.85);
        }

        byte[] bytes = new byte[numSamples * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public void SetEnabled(bool enabled)
    {
        if (_disposed) return;
        _isEnabled = enabled;

        if (enabled)
        {
            EnsureSamplesInitialized();
            InitWasapi();
            InstallHook();
        }
        else
        {
            UninstallHook();
            CleanupWasapi();
        }
    }

    private void InitWasapi()
    {
        lock (_playLock)
        {
            if (_wasapiOut != null) return;

            try
            {
                var waveFormat = new WaveFormat(SampleRate, 16, 1);
                _waveProvider = new BufferedWaveProvider(waveFormat)
                {
                    BufferDuration = TimeSpan.FromSeconds(1),
                    DiscardOnBufferOverflow = true
                };

                _wasapiOut = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 25);
                _wasapiOut.Init(_waveProvider);
                _idlePauseTimer ??= new Timer(_ => PauseWasapiIfIdle(), null, Timeout.Infinite, Timeout.Infinite);
            }
            catch (Exception ex)
            {
                App.Log($"[KeyboardSoundService] InitWasapi failed: {ex.Message}");
            }
        }
    }

    private void PauseWasapiIfIdle()
    {
        lock (_playLock)
        {
            try
            {
                if (_wasapiOut?.PlaybackState == PlaybackState.Playing)
                {
                    _wasapiOut.Pause();
                }
            }
            catch { }
        }
    }

    private void CleanupWasapi()
    {
        lock (_playLock)
        {
            try { _idlePauseTimer?.Dispose(); } catch { }
            _idlePauseTimer = null;
            try { _wasapiOut?.Stop(); } catch { }
            try { _wasapiOut?.Dispose(); } catch { }
            _wasapiOut = null;
            _waveProvider = null;
        }
    }

    private void InstallHook()
    {
        if (_hook != IntPtr.Zero) return;

        try
        {
            _hookProc = LowLevelKeyboardCallback;
            IntPtr hMod = GetModuleHandle(null);
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, hMod, 0);
        }
        catch (Exception ex)
        {
            App.Log($"[KeyboardSoundService] InstallHook failed: {ex.Message}");
        }
    }

    private void UninstallHook()
    {
        if (_hook != IntPtr.Zero)
        {
            try { UnhookWindowsHookEx(_hook); } catch { }
            _hook = IntPtr.Zero;
            _hookProc = null;
        }
    }

    private IntPtr LowLevelKeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isEnabled && !_disposed &&
            (wParam.ToInt32() == WM_KEYDOWN || wParam.ToInt32() == WM_SYSKEYDOWN))
        {
            ThreadPool.UnsafeQueueUserWorkItem(static s => ((KeyboardSoundService)s!).PlayKeyClick(), this);
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void PlayKeyClick()
    {
        if (!_isEnabled || _disposed || _waveProvider == null) return;
        EnsureSamplesInitialized();

        lock (_playLock)
        {
            if (!_isEnabled || _disposed || _waveProvider == null) return;

            byte[][] samplePool = _profile switch
            {
                MechSoundProfile.TactileBrown => _tactileBrownSamples,
                MechSoundProfile.RetroTypewriter => _typewriterSamples,
                _ => _cherryBlueSamples
            };

            int idx = _rng.Next(samplePool.Length);
            byte[] sample = samplePool[idx];

            try
            {
                if (_wasapiOut != null && _wasapiOut.PlaybackState != PlaybackState.Playing)
                {
                    _wasapiOut.Play();
                }

                if (_volume >= 0.98f)
                {
                    _waveProvider.AddSamples(sample, 0, sample.Length);
                }
                else
                {
                    byte[] buf = _scaledBuffer;
                    if (buf.Length < sample.Length)
                    {
                        buf = new byte[sample.Length];
                    }
                    for (int i = 0; i < sample.Length; i += 2)
                    {
                        short val = (short)(sample[i] | (sample[i + 1] << 8));
                        short scaledVal = (short)(val * _volume);
                        buf[i] = (byte)(scaledVal & 0xFF);
                        buf[i + 1] = (byte)((scaledVal >> 8) & 0xFF);
                    }
                    _waveProvider.AddSamples(buf, 0, sample.Length);
                }

                _idlePauseTimer?.Change(3500, Timeout.Infinite);
            }
            catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        SetEnabled(false);
        _disposed = true;
    }
}
