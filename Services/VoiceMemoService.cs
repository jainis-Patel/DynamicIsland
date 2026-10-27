using System.IO;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DynamicIsland.Services;

public sealed record VoiceMemoInfo(string FilePath, string FileName, TimeSpan Duration, DateTime CreatedAt);

/// <summary>
/// 1-Click voice memo audio recorder using WASAPI Capture.
/// Streams microphone audio to a timestamped .wav file, computes live peak RMS for animated waveforms,
/// and auto-deposits finished recordings straight into the Drop Shelf.
/// </summary>
internal sealed class VoiceMemoService : IDisposable
{
    private static readonly string MemoDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        "DynamicIsland_Memos");

    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _recordTimer;
    private readonly object _writerLock = new();
    private WasapiCapture? _capture;
    private WaveFileWriter? _writer;
    private WasapiOut? _player;
    private WaveFileReader? _playerReader;
    private string? _currentFilePath;
    private DateTime _recordStartTime;
    private volatile float _currentPeak;
    private bool _disposed;

    public bool IsRecording { get; private set; }
    public bool IsPlaying { get; private set; }
    public TimeSpan Elapsed { get; private set; }
    public float PeakLevel => _currentPeak;
    public string? LastSavedFilePath => _currentFilePath;
    public VoiceMemoInfo? LastSavedMemo { get; private set; }

    public event Action<float, TimeSpan>? RecordingTick;
    public event Action<VoiceMemoInfo>? RecordingCompleted;
    public event Action<bool>? PlaybackStateChanged;

    public VoiceMemoService(Dispatcher ui)
    {
        _ui = ui;
        _recordTimer = new DispatcherTimer(DispatcherPriority.Render, _ui)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _recordTimer.Tick += OnTimerTick;

        try
        {
            if (!Directory.Exists(MemoDirectory))
                Directory.CreateDirectory(MemoDirectory);
            else
                LoadMostRecentMemo();
        }
        catch { }
    }

    private void LoadMostRecentMemo()
    {
        try
        {
            var latest = new DirectoryInfo(MemoDirectory)
                .GetFiles("*.wav")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latest is { Exists: true, Length: > 44 })
            {
                _currentFilePath = latest.FullName;
                using var reader = new WaveFileReader(latest.FullName);
                LastSavedMemo = new VoiceMemoInfo(latest.FullName, latest.Name, reader.TotalTime, latest.CreationTime);
            }
        }
        catch { }
    }

    public bool StartRecording()
    {
        if (_disposed || IsRecording) return false;

        try
        {
            StopPlayback();

            if (!Directory.Exists(MemoDirectory))
                Directory.CreateDirectory(MemoDirectory);

            string fileName = $"VoiceMemo_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
            _currentFilePath = Path.Combine(MemoDirectory, fileName);

            var capture = new WasapiCapture();
            lock (_writerLock)
            {
                _capture = capture;
                _writer = new WaveFileWriter(_currentFilePath, capture.WaveFormat);
            }

            capture.DataAvailable += OnAudioDataAvailable;
            capture.RecordingStopped += OnCaptureStopped;

            capture.StartRecording();
            _recordStartTime = DateTime.Now;
            Elapsed = TimeSpan.Zero;
            IsRecording = true;
            _recordTimer.Start();

            App.Log($"[VoiceMemoService] Started recording: {_currentFilePath}");
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[VoiceMemoService] Failed to start recording: {ex.Message}");
            CleanupCapture();
            return false;
        }
    }

    public void StopRecording()
    {
        if (!IsRecording) return;

        try
        {
            _recordTimer.Stop();
            IsRecording = false;
            _capture?.StopRecording();
            App.Log("[VoiceMemoService] Stopping recording...");
        }
        catch (Exception ex)
        {
            App.Log($"[VoiceMemoService] Error stopping recording: {ex.Message}");
            CleanupCapture();
        }
    }

    public bool ToggleRecord()
    {
        if (IsRecording)
        {
            StopRecording();
            return false;
        }
        return StartRecording();
    }

    public void PlayMemo(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            StopPlayback();

            _playerReader = new WaveFileReader(filePath);
            _player = new WasapiOut(AudioClientShareMode.Shared, 200);
            _player.Init(_playerReader);
            _player.PlaybackStopped += (_, _) =>
            {
                IsPlaying = false;
                _ui.InvokeAsync(() => PlaybackStateChanged?.Invoke(false));
                CleanupPlayer();
            };

            _player.Play();
            IsPlaying = true;
            PlaybackStateChanged?.Invoke(true);
        }
        catch (Exception ex)
        {
            App.Log($"[VoiceMemoService] Playback error: {ex.Message}");
            CleanupPlayer();
        }
    }

    public void StopPlayback()
    {
        if (!IsPlaying) return;
        try
        {
            _player?.Stop();
        }
        catch { }
        finally
        {
            CleanupPlayer();
            IsPlaying = false;
            PlaybackStateChanged?.Invoke(false);
        }
    }

    private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        try
        {
            int bitsPerSample;
            lock (_writerLock)
            {
                if (_writer == null || _capture == null) return;
                _writer.Write(e.Buffer, 0, e.BytesRecorded);
                bitsPerSample = _capture.WaveFormat.BitsPerSample;
            }

            float max = 0f;
            int bytesPerSample = bitsPerSample == 32 ? 4 : 2;
            int sampleCount = e.BytesRecorded / bytesPerSample;

            if (bytesPerSample == 4)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    float abs = Math.Abs(BitConverter.ToSingle(e.Buffer, i * 4));
                    if (abs > max) max = abs;
                }
            }
            else
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    float abs = Math.Abs(BitConverter.ToInt16(e.Buffer, i * 2)) / 32768f;
                    if (abs > max) max = abs;
                }
            }

            _currentPeak = Math.Clamp(max * 1.8f, 0.05f, 1.0f);
        }
        catch { }
    }

    private void OnCaptureStopped(object? sender, StoppedEventArgs e)
    {
        _ui.InvokeAsync(() =>
        {
            var dur = Elapsed;
            string? savedPath = _currentFilePath;
            CleanupCapture();

            if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath) && dur > TimeSpan.FromMilliseconds(500))
            {
                var info = new VoiceMemoInfo(savedPath, Path.GetFileName(savedPath), dur, DateTime.Now);
                LastSavedMemo = info;
                RecordingCompleted?.Invoke(info);
            }
        });
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!IsRecording) return;
        Elapsed = DateTime.Now - _recordStartTime;
        RecordingTick?.Invoke(_currentPeak, Elapsed);
    }

    private void CleanupCapture()
    {
        lock (_writerLock)
        {
            if (_capture != null)
            {
                _capture.DataAvailable -= OnAudioDataAvailable;
                _capture.RecordingStopped -= OnCaptureStopped;
            }
            try { _writer?.Dispose(); } catch { }
            try { _capture?.Dispose(); } catch { }
            _writer = null;
            _capture = null;
        }
        _currentPeak = 0f;
    }

    private void CleanupPlayer()
    {
        try { _player?.Dispose(); } catch { }
        try { _playerReader?.Dispose(); } catch { }
        _player = null;
        _playerReader = null;
    }

    public void Dispose()
    {
        _disposed = true;
        _recordTimer.Stop();
        StopRecording();
        StopPlayback();
        CleanupCapture();
        CleanupPlayer();
    }
}
