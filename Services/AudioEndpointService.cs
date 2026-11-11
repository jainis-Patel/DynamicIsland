using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Windows.Devices.Enumeration;

namespace DynamicIsland.Services;

public enum AudioRole : uint
{
    Console = 0,
    Multimedia = 1,
    Communications = 2
}

[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat();
    [PreserveSig] int GetDeviceFormat();
    [PreserveSig] int ResetDeviceFormat();
    [PreserveSig] int SetDeviceFormat();
    [PreserveSig] int GetProcessingPeriod();
    [PreserveSig] int SetProcessingPeriod();
    [PreserveSig] int GetShareMode();
    [PreserveSig] int SetShareMode();
    [PreserveSig] int GetPropertyValue();
    [PreserveSig] int SetPropertyValue();
    [PreserveSig] int SetDefaultEndpoint([In, MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, [In, MarshalAs(UnmanagedType.U4)] AudioRole eRole);
    [PreserveSig] int SetEndpointVisibility();
}

[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class CPolicyConfigClient { }

public sealed class AudioEndpointInfo : INotifyPropertyChanged
{
    private bool _isDefault;
    private float _volume;
    private bool _isMuted;
    private int? _batteryPercent;

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string DeviceType { get; init; } = "Speaker";
    public string Glyph { get; init; } = "\uE7F5";

    public bool IsDefault
    {
        get => _isDefault;
        set { if (_isDefault != value) { _isDefault = value; OnPropertyChanged(); } }
    }

    public float Volume
    {
        get => _volume;
        set { if (Math.Abs(_volume - value) > 0.001f) { _volume = value; OnPropertyChanged(); } }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted != value)
            {
                _isMuted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayGlyph));
            }
        }
    }

    public string DisplayGlyph => _isMuted ? "\uE74F" : Glyph;

    public int? BatteryPercent
    {
        get => _batteryPercent;
        set { if (_batteryPercent != value) { _batteryPercent = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? prop = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

public sealed class AppAudioSessionInfo : INotifyPropertyChanged
{
    private float _volume;
    private bool _isMuted;

    public uint ProcessId { get; init; }
    public string AppName { get; init; } = "";
    public string Subtitle { get; init; } = "App Audio";
    public string Glyph { get; init; } = "\uE8D6";

    public float Volume
    {
        get => _volume;
        set
        {
            if (Math.Abs(_volume - value) > 0.001f)
            {
                _volume = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted != value)
            {
                _isMuted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MuteGlyph));
            }
        }
    }

    public string MuteGlyph => _isMuted ? "\uE74F" : "\uE767";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? prop = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

/// <summary>
/// Monitors Windows CoreAudio playback endpoints (Speakers, Headphones, Bluetooth Earbuds, HDMI),
/// detects active defaults, queries battery telemetry, and allows 1-click output switching via IPolicyConfig.
/// </summary>
internal sealed class AudioEndpointService : IMMNotificationClient, IDisposable
{
    private readonly Dispatcher _ui;
    private readonly MMDeviceEnumerator _enumerator;
    private readonly Dictionary<string, int> _bluetoothBatteryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _refreshLock = new();
    private bool _disposed;
    private string? _lastDefaultId;

    public IReadOnlyList<AudioEndpointInfo> Endpoints { get; private set; } = Array.Empty<AudioEndpointInfo>();
    public IReadOnlyList<AppAudioSessionInfo> AppSessions { get; private set; } = Array.Empty<AppAudioSessionInfo>();
    public AudioEndpointInfo? CurrentDefault { get; private set; }
    public MMDeviceEnumerator SharedEnumerator => _enumerator;

    public event Action<IReadOnlyList<AudioEndpointInfo>>? EndpointsChanged;
    public event Action<IReadOnlyList<AppAudioSessionInfo>>? AppSessionsChanged;
    public event Action<AudioEndpointInfo>? DefaultChanged;
    public event Action? DefaultCaptureChanged;

    public AudioEndpointService(Dispatcher ui)
    {
        _ui = ui;
        _enumerator = new MMDeviceEnumerator();
        try
        {
            _enumerator.RegisterEndpointNotificationCallback(this);
        }
        catch (Exception ex)
        {
            App.Log($"[AudioEndpointService] RegisterEndpointNotificationCallback failed: {ex.Message}");
        }

        RefreshEndpoints(initial: true);
    }

    public void RefreshEndpoints(bool initial = false)
    {
        if (_disposed) return;

        Task.Run(() =>
        {
            lock (_refreshLock)
            {
                if (_disposed) return;

                try
                {
                    string? defaultId = null;
                    try
                    {
                        using var def = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        defaultId = def?.ID;
                    }
                    catch { }

                    var list = new List<AudioEndpointInfo>();
                    var collection = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                    foreach (var dev in collection)
                    {
                        try
                        {
                            string id = dev.ID;
                            string name = dev.FriendlyName;
                            bool isDef = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase);

                            float vol = 1.0f;
                            bool muted = false;
                            try
                            {
                                vol = dev.AudioEndpointVolume.MasterVolumeLevelScalar;
                                muted = dev.AudioEndpointVolume.Mute;
                            }
                            catch { }

                            var (type, glyph) = InferDeviceTypeAndGlyph(name);
                            int? battery = FindBatteryForDevice(name);

                            list.Add(new AudioEndpointInfo
                            {
                                Id = id,
                                Name = name,
                                DeviceType = type,
                                Glyph = glyph,
                                IsDefault = isDef,
                                Volume = vol,
                                IsMuted = muted,
                                BatteryPercent = battery
                            });
                        }
                        finally
                        {
                            try { dev.Dispose(); } catch { }
                        }
                    }

                    Endpoints = list;
                    var currentDef = list.FirstOrDefault(e => e.IsDefault) ?? list.FirstOrDefault();
                    bool changed = !string.Equals(_lastDefaultId, currentDef?.Id, StringComparison.OrdinalIgnoreCase);
                    _lastDefaultId = currentDef?.Id;
                    CurrentDefault = currentDef;

                    _ui.InvokeAsync(() =>
                    {
                        EndpointsChanged?.Invoke(Endpoints);
                        if (changed && !initial && currentDef != null)
                        {
                            DefaultChanged?.Invoke(currentDef);
                        }
                    });
                }
                catch (Exception ex)
                {
                    App.Log($"[AudioEndpointService] RefreshEndpoints error: {ex.Message}");
                }
            }
        });
    }

    public void SetDefaultEndpoint(string deviceId)
    {
        Task.Run(() =>
        {
            try
            {
                var policy = (IPolicyConfig)new CPolicyConfigClient();
                policy.SetDefaultEndpoint(deviceId, AudioRole.Console);
                policy.SetDefaultEndpoint(deviceId, AudioRole.Multimedia);
                policy.SetDefaultEndpoint(deviceId, AudioRole.Communications);

                App.Log($"[AudioEndpointService] SetDefaultEndpoint to {deviceId} completed");
                RefreshEndpoints();
            }
            catch (Exception ex)
            {
                App.Log($"[AudioEndpointService] Failed to set default endpoint {deviceId}: {ex}");
            }
        });
    }

    public void SetEndpointVolume(string deviceId, float volume)
    {
        float clamped = Math.Clamp(volume, 0f, 1f);
        var ep = Endpoints.FirstOrDefault(e => e.Id == deviceId);
        if (ep != null)
        {
            // Update INotifyPropertyChanged property in-place without rebuilding ItemsControl
            ep.Volume = clamped;
        }

        Task.Run(() =>
        {
            try
            {
                using var dev = _enumerator.GetDevice(deviceId);
                if (dev != null)
                {
                    dev.AudioEndpointVolume.MasterVolumeLevelScalar = clamped;
                }
            }
            catch (Exception ex)
            {
                App.Log($"[AudioEndpointService] SetEndpointVolume error: {ex.Message}");
            }
        });
    }

    public void ToggleEndpointMute(string deviceId)
    {
        Task.Run(() =>
        {
            try
            {
                using var dev = _enumerator.GetDevice(deviceId);
                if (dev != null)
                {
                    bool next = !dev.AudioEndpointVolume.Mute;
                    dev.AudioEndpointVolume.Mute = next;
                    _ui.InvokeAsync(() =>
                    {
                        var ep = Endpoints.FirstOrDefault(e => e.Id == deviceId);
                        if (ep != null) ep.IsMuted = next;
                    });
                }
            }
            catch (Exception ex)
            {
                App.Log($"[AudioEndpointService] ToggleEndpointMute error: {ex.Message}");
            }
        });
    }

    public void RefreshAppSessions()
    {
        if (_disposed) return;

        Task.Run(() =>
        {
            try
            {
                using var dev = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (dev == null) return;

                var sessions = dev.AudioSessionManager.Sessions;
                var byPid = new Dictionary<uint, AppAudioSessionInfo>();
                uint selfPid = (uint)Environment.ProcessId;

                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    try
                    {
                        if (s.State == AudioSessionState.AudioSessionStateExpired)
                            continue;

                        uint pid = s.GetProcessID;
                        if (pid == selfPid) continue;

                        float vol = s.SimpleAudioVolume.Volume;
                        bool muted = s.SimpleAudioVolume.Mute;

                        if (s.IsSystemSoundsSession || pid == 0)
                        {
                            if (!byPid.ContainsKey(0))
                            {
                                byPid[0] = new AppAudioSessionInfo
                                {
                                    ProcessId = 0,
                                    AppName = "System Sounds",
                                    Subtitle = "Windows Audio",
                                    Glyph = "\uE777",
                                    Volume = vol,
                                    IsMuted = muted
                                };
                            }
                            continue;
                        }

                        if (byPid.ContainsKey(pid)) continue;

                        string appName = ResolveAppName(pid, s.DisplayName);
                        if (string.IsNullOrWhiteSpace(appName)) continue;

                        byPid[pid] = new AppAudioSessionInfo
                        {
                            ProcessId = pid,
                            AppName = appName,
                            Subtitle = s.State == AudioSessionState.AudioSessionStateActive ? "Playing Audio" : "Idle Session",
                            Glyph = GlyphForAppAudio(appName),
                            Volume = vol,
                            IsMuted = muted
                        };
                    }
                    catch
                    {
                    }
                }

                var result = byPid.Values
                    .OrderByDescending(x => x.Subtitle == "Playing Audio")
                    .ThenBy(x => x.ProcessId == 0)
                    .ThenBy(x => x.AppName)
                    .ToList();

                AppSessions = result;
                _ui.InvokeAsync(() => AppSessionsChanged?.Invoke(AppSessions));
            }
            catch (Exception ex)
            {
                App.Log($"[AudioEndpointService] RefreshAppSessions error: {ex.Message}");
            }
        });
    }

    public void SetAppSessionVolume(uint processId, float volume)
    {
        float clamped = Math.Clamp(volume, 0f, 1f);
        var sessionInfo = AppSessions.FirstOrDefault(a => a.ProcessId == processId);
        if (sessionInfo != null)
        {
            sessionInfo.Volume = clamped;
        }

        Task.Run(() =>
        {
            try
            {
                using var dev = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (dev == null) return;
                var sessions = dev.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    if ((processId == 0 && (s.IsSystemSoundsSession || s.GetProcessID == 0)) ||
                        (processId != 0 && s.GetProcessID == processId))
                    {
                        s.SimpleAudioVolume.Volume = clamped;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log($"[AudioEndpointService] SetAppSessionVolume error: {ex.Message}");
            }
        });
    }

    public void ToggleAppSessionMute(uint processId)
    {
        Task.Run(() =>
        {
            try
            {
                using var dev = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (dev == null) return;
                var sessions = dev.AudioSessionManager.Sessions;
                bool? nextMute = null;

                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    if ((processId == 0 && (s.IsSystemSoundsSession || s.GetProcessID == 0)) ||
                        (processId != 0 && s.GetProcessID == processId))
                    {
                        nextMute ??= !s.SimpleAudioVolume.Mute;
                        s.SimpleAudioVolume.Mute = nextMute.Value;
                    }
                }

                if (nextMute.HasValue)
                {
                    bool finalMute = nextMute.Value;
                    _ui.InvokeAsync(() =>
                    {
                        var info = AppSessions.FirstOrDefault(a => a.ProcessId == processId);
                        if (info != null) info.IsMuted = finalMute;
                    });
                }
            }
            catch (Exception ex)
            {
                App.Log($"[AudioEndpointService] ToggleAppSessionMute error: {ex.Message}");
            }
        });
    }

    private static string ResolveAppName(uint pid, string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(displayName) && !displayName.StartsWith("@%", StringComparison.Ordinal))
        {
            return displayName.Trim();
        }

        try
        {
            using var proc = Process.GetProcessById((int)pid);
            if (!string.IsNullOrWhiteSpace(proc.MainWindowTitle) && proc.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            {
                return proc.MainWindowTitle;
            }
            string raw = proc.ProcessName;
            if (raw.Length > 0)
            {
                return char.ToUpperInvariant(raw[0]) + raw[1..];
            }
        }
        catch
        {
        }
        return "";
    }

    private static string GlyphForAppAudio(string appName)
    {
        string n = appName.ToLowerInvariant();
        if (n.Contains("spotify") || n.Contains("music") || n.Contains("itunes") || n.Contains("tidal")) return "\uE8D6";
        if (n.Contains("chrome") || n.Contains("edge") || n.Contains("firefox") || n.Contains("brave") || n.Contains("opera")) return "\uE774";
        if (n.Contains("discord") || n.Contains("teams") || n.Contains("zoom") || n.Contains("slack") || n.Contains("whatsapp")) return "\uE8BD";
        if (n.Contains("vlc") || n.Contains("mpv") || n.Contains("video")) return "\uE714";
        return "\uE767";
    }

    private static (string Type, string Glyph) InferDeviceTypeAndGlyph(string name)
    {
        string lower = name.ToLowerInvariant();

        if (lower.Contains("headphone") || lower.Contains("headset") || lower.Contains("earbuds") ||
            lower.Contains("airpods") || lower.Contains("buds") || lower.Contains("wh-1000") ||
            lower.Contains("freebuds") || lower.Contains("xm4") || lower.Contains("xm5"))
        {
            return ("Headphones", "\uE7F6");
        }
        if (lower.Contains("bluetooth") || lower.Contains("hands-free") || lower.Contains("wireless"))
        {
            return ("Bluetooth", "\uE702");
        }
        if (lower.Contains("nvidia") || lower.Contains("amd") || lower.Contains("intel") ||
            lower.Contains("hdmi") || lower.Contains("displayport") || lower.Contains("tv") ||
            lower.Contains("monitor") || lower.Contains("screen"))
        {
            return ("Display Audio", "\uE7F4");
        }

        return ("Speakers", "\uE7F5");
    }

    /// <summary>
    /// Updates the Bluetooth battery lookup table from the shared <see cref="PeripheralDeviceService"/> scan,
    /// avoiding duplicate WinRT DeviceInformation enumerations.
    /// </summary>
    public void SyncBluetoothBatteries(IReadOnlyList<PeripheralDeviceInfo> devices)
    {
        bool updated = false;
        lock (_bluetoothBatteryCache)
        {
            foreach (var d in devices)
            {
                if (!d.IsSimulated && d.BatteryPercent.HasValue && !string.IsNullOrWhiteSpace(d.Name))
                {
                    if (!_bluetoothBatteryCache.TryGetValue(d.Name, out int old) || old != d.BatteryPercent.Value)
                    {
                        _bluetoothBatteryCache[d.Name] = d.BatteryPercent.Value;
                        updated = true;
                    }
                }
            }
        }

        if (updated)
        {
            foreach (var ep in Endpoints)
            {
                int? batt = FindBatteryForDevice(ep.Name);
                if (batt != ep.BatteryPercent)
                {
                    ep.BatteryPercent = batt;
                }
            }
        }
    }

    private int? FindBatteryForDevice(string friendlyName)
    {
        lock (_bluetoothBatteryCache)
        {
            foreach (var (key, pct) in _bluetoothBatteryCache)
            {
                if (!string.IsNullOrWhiteSpace(key) &&
                    (friendlyName.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                     key.Contains(friendlyName, StringComparison.OrdinalIgnoreCase)))
                {
                    return pct;
                }
            }
        }
        return null;
    }

    // ═════════════════════════════════════════════════════════════════
    //  IMMNotificationClient Callbacks
    // ═════════════════════════════════════════════════════════════════

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => RefreshEndpoints();
    public void OnDeviceAdded(string pwstrDeviceId) => RefreshEndpoints();
    public void OnDeviceRemoved(string deviceId) => RefreshEndpoints();

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && (role == Role.Multimedia || role == Role.Console))
        {
            RefreshEndpoints();
        }
        else if (flow == DataFlow.Capture && (role == Role.Communications || role == Role.Console))
        {
            _ui.InvokeAsync(() => DefaultCaptureChanged?.Invoke());
        }
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        _disposed = true;
        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(this);
            _enumerator.Dispose();
        }
        catch { }
    }
}
