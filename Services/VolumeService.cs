using System.Diagnostics;
using System.Windows.Threading;
using NAudio.CoreAudioApi;

namespace DynamicIsland.Services;

/// <summary>
/// Monitors Windows master audio volume changes (e.g. from keyboard Fn volume keys)
/// and allows smooth scroll-wheel volume stepping over the Dynamic Island.
/// Automatically rebinds when the default render endpoint changes.
/// </summary>
internal sealed class VolumeService : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly MMDeviceEnumerator _enumerator;
    private readonly object _syncLock = new();
    private MMDevice? _device;
    private AudioEndpointVolume? _volume;
    private volatile bool _disposed;
    private float _lastVolume = -1f;
    private bool _lastMute;

    public float CurrentVolume
    {
        get
        {
            lock (_syncLock)
            {
                return _volume?.MasterVolumeLevelScalar ?? (_lastVolume >= 0f ? _lastVolume : 0.5f);
            }
        }
    }

    public bool IsMuted
    {
        get
        {
            lock (_syncLock)
            {
                return _volume?.Mute ?? _lastMute;
            }
        }
    }

    public event Action<float, bool>? VolumeChanged;

    public VolumeService(Dispatcher ui, MMDeviceEnumerator enumerator)
    {
        _ui = ui;
        _enumerator = enumerator;
        AttachEndpoint();
    }

    /// <summary>Rebinds to the new default multimedia output endpoint when the user switches audio devices.</summary>
    public void ReattachDefaultEndpoint()
    {
        if (_disposed) return;
        AttachEndpoint();
    }

    private void AttachEndpoint()
    {
        lock (_syncLock)
        {
            if (_disposed) return;
            try
            {
                DetachEndpointLocked();
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (_device != null)
                {
                    _volume = _device.AudioEndpointVolume;
                    _lastVolume = _volume.MasterVolumeLevelScalar;
                    _lastMute = _volume.Mute;
                    _volume.OnVolumeNotification += OnVolumeNotification;
                }
            }
            catch (Exception ex)
            {
                App.Log($"[VolumeService] Attach failed: {ex.Message}");
            }
        }
    }

    private void DetachEndpointLocked()
    {
        if (_volume != null)
        {
            try { _volume.OnVolumeNotification -= OnVolumeNotification; } catch { }
            try { _volume.Dispose(); } catch { }
            _volume = null;
        }
        if (_device != null)
        {
            try { _device.Dispose(); } catch { }
            _device = null;
        }
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data)
    {
        if (_disposed) return;
        float vol = data.MasterVolume;
        bool mute = data.Muted;

        lock (_syncLock)
        {
            if (_disposed) return;
            if (Math.Abs(vol - _lastVolume) < 0.005f && mute == _lastMute) return;
            _lastVolume = vol;
            _lastMute = mute;
        }

        _ui.InvokeAsync(() =>
        {
            if (!_disposed) VolumeChanged?.Invoke(vol, mute);
        });
    }

    public void SetVolume(float scalar)
    {
        lock (_syncLock)
        {
            if (_disposed) return;
            try
            {
                if (_volume == null) AttachEndpoint();
                if (_volume == null) return;

                float target = Math.Clamp(scalar, 0f, 1f);
                if (_volume.Mute && target > 0) _volume.Mute = false;
                _volume.MasterVolumeLevelScalar = target;
            }
            catch (Exception ex)
            {
                App.Log($"[VolumeService] SetVolume failed: {ex.Message}");
                AttachEndpoint();
            }
        }
    }

    public void StepVolume(float delta)
    {
        lock (_syncLock)
        {
            if (_disposed) return;
            try
            {
                if (_volume == null) AttachEndpoint();
                if (_volume == null) return;

                float current = _volume.MasterVolumeLevelScalar;
                float target = Math.Clamp(current + delta, 0f, 1f);
                if (_volume.Mute && delta > 0) _volume.Mute = false;
                _volume.MasterVolumeLevelScalar = target;
            }
            catch (Exception ex)
            {
                App.Log($"[VolumeService] Step failed: {ex.Message}");
                AttachEndpoint();
            }
        }
    }

    public void ToggleMute()
    {
        lock (_syncLock)
        {
            if (_disposed) return;
            try
            {
                if (_volume == null) AttachEndpoint();
                if (_volume == null) return;
                _volume.Mute = !_volume.Mute;
            }
            catch (Exception ex)
            {
                App.Log($"[VolumeService] ToggleMute failed: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_disposed) return;
            _disposed = true;
            DetachEndpointLocked();
        }
    }
}
