using System.IO;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DynamicIsland.Services;

public sealed record VoiceMemoInfo(string FilePath, string FileName, TimeSpan Duration, DateTime CreatedAt)
{
    public string DurationText => Duration.TotalHours >= 1
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"m\:ss");

    public string MetaText => $"{DurationText} \u00B7 {CreatedAt:HH:mm}";
}

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
    private readonly List<VoiceMemoInfo> _recentMemos = new();
    private WasapiCapture? _capture;
    private WaveFileWriter? _writer;
    private WasapiOut? _player;
    private WaveFileReader? _playerReader;
    private string? _currentFilePath;
    private DateTime _recordStartTime;
    private volatile float _currentPeak;
    private volatile bool _disposed;

    public bool IsRecording { get; private set; }
    public bool IsPlaying { get; private set; }
    public string? PlayingFilePath { get; private set; }
    public TimeSpan Elapsed { get; private set; }
    public float PeakLevel => _currentPeak;
    public string? LastSavedFilePath => _currentFilePath;
    public VoiceMemoInfo? LastSavedMemo { get; private set; }
    public IReadOnlyList<VoiceMemoInfo> RecentMemos => _recentMemos;

    public event Action<float, TimeSpan>? RecordingTick;
    public event Action<VoiceMemoInfo>? RecordingCompleted;
    public event Action<bool>? PlaybackStateChanged;
    public event Action? MemosChanged;

    public VoiceMemoService(Dispatcher ui)
    {
        _ui = ui;
        _recordTimer = new DispatcherTimer(DispatcherPriority.Background, _ui)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _recordTimer.Tick += OnTimerTick;

        try
        {
            if (!Directory.Exists(MemoDirectory))
            {
                Directory.CreateDirectory(MemoDirectory);
            }
            else
            {
                _ = Task.Run(LoadMostRecentMemoAsync);
            }
        }
        catch (Exception ex) { App.LogDebug("VoiceMemo.InitDir", ex); }
    }

    private void LoadMostRecentMemoAsync()
    {
        try
        {
            var loaded = new List<VoiceMemoInfo>();
            var files = new DirectoryInfo(MemoDirectory)
                .GetFiles("*.wav")
                .Where(f => f.Exists && f.Length > 44)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(10);

            foreach (var file in files)
            {
                try
                {
                    using var reader = new WaveFileReader(file.FullName);
                    loaded.Add(new VoiceMemoInfo(file.FullName, file.Name, reader.TotalTime, file.CreationTime));
                }
                catch (Exception ex) { App.LogDebug("VoiceMemo.ReadHeader", ex); }
            }

            _ = _ui.InvokeAsync(() =>
            {
                if (_disposed) return;
                _recentMemos.Clear();
                _recentMemos.AddRange(loaded);
                LastSavedMemo = _recentMemos.FirstOrDefault();
                _currentFilePath ??= LastSavedMemo?.FilePath;
                MemosChanged?.Invoke();
            }, DispatcherPriority.Background);
        }
        catch (Exception ex) { App.LogDebug("VoiceMemo.LoadRecent", ex); }
    }

    public bool StartRecording()
    {
        if (_disposed || IsRecording) return false;

        try
        {
            StopPlayback();
            CleanupCapture();

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

            var localReader = new WaveFileReader(filePath);
            var localPlayer = new WasapiOut(AudioClientShareMode.Shared, 200);
            _playerReader = localReader;
            _player = localPlayer;
            localPlayer.Init(localReader);
            localPlayer.PlaybackStopped += (_, _) =>
            {
                if (!ReferenceEquals(_player, localPlayer)) return;
                IsPlaying = false;
                PlayingFilePath = null;
                _ui.InvokeAsync(() => PlaybackStateChanged?.Invoke(false));
                CleanupPlayer();
            };

            localPlayer.Play();
            IsPlaying = true;
            PlayingFilePath = filePath;
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
        var localPlayer = _player;
        var localReader = _playerReader;
        if (!IsPlaying && localPlayer == null && localReader == null) return;

        _player = null;
        _playerReader = null;
        try
        {
            localPlayer?.Stop();
        }
        catch (Exception ex) { App.LogDebug("VoiceMemo.StopPlayback", ex); }
        finally
        {
            try { localPlayer?.Dispose(); } catch { }
            try { localReader?.Dispose(); } catch { }
            IsPlaying = false;
            PlayingFilePath = null;
            PlaybackStateChanged?.Invoke(false);
        }
    }

    public bool DeleteMemo(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        try
        {
            if (IsPlaying && string.Equals(PlayingFilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                StopPlayback();
            }

            _recentMemos.RemoveAll(m => string.Equals(m.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            LastSavedMemo = _recentMemos.FirstOrDefault();
            _currentFilePath = LastSavedMemo?.FilePath;
            MemosChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[VoiceMemoService] DeleteMemo error: {ex.Message}");
            return false;
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
                if (_disposed || _writer == null || _capture == null) return;
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
        catch (Exception ex) { App.LogDebug("VoiceMemo.OnAudioData", ex); }
    }

    private void OnCaptureStopped(object? sender, StoppedEventArgs e)
    {
        var dur = DateTime.Now - _recordStartTime;
        string? savedPath = _currentFilePath;
        _ui.InvokeAsync(() =>
        {
            if (sender != null && !ReferenceEquals(sender, _capture)) return;
            CleanupCapture();
            if (_disposed) return;

            if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath) && dur > TimeSpan.FromMilliseconds(500))
            {
                var info = new VoiceMemoInfo(savedPath, Path.GetFileName(savedPath), dur, DateTime.Now);
                _recentMemos.RemoveAll(m => string.Equals(m.FilePath, savedPath, StringComparison.OrdinalIgnoreCase));
                _recentMemos.Insert(0, info);
                if (_recentMemos.Count > 10)
                {
                    _recentMemos.RemoveRange(10, _recentMemos.Count - 10);
                }
                LastSavedMemo = info;
                MemosChanged?.Invoke();
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
        WasapiCapture? capture;
        WaveFileWriter? writer;
        lock (_writerLock)
        {
            capture = _capture;
            writer = _writer;
            if (capture != null)
            {
                capture.DataAvailable -= OnAudioDataAvailable;
                capture.RecordingStopped -= OnCaptureStopped;
            }
            _writer = null;
            _capture = null;
        }
        try { writer?.Dispose(); } catch { }
        try { capture?.Dispose(); } catch { }
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
