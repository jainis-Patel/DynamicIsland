using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>
/// Monitors active webcam and microphone usage across all Windows apps (mirroring iOS/macOS privacy dots),
/// detects active meeting sessions (Zoom, Teams, Discord), and provides 1-click global microphone mute.
/// Uses kernel-level <c>RegNotifyChangeKeyValue</c> notifications for 0% idle CPU, with fallback polling only if needed.
/// </summary>
internal sealed class PrivacyIndicatorService : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _fallbackTimer;
    private readonly MMDeviceEnumerator _enumerator;
    private readonly List<RegistryWatcherState> _registryWatchers = new();
    private MMDevice? _captureDevice;
    private int _polling;
    private bool _disposed;

    private sealed class RegistryWatcherState : IDisposable
    {
        public RegistryKey Key { get; }
        public AutoResetEvent WaitEvent { get; } = new(false);
        public RegisteredWaitHandle? RegisteredWait { get; set; }

        public RegistryWatcherState(RegistryKey key) => Key = key;

        public void Dispose()
        {
            try { RegisteredWait?.Unregister(null); } catch { }
            try { WaitEvent.Dispose(); } catch { }
            try { Key.Dispose(); } catch { }
        }
    }

    public bool IsCameraActive { get; private set; }
    public bool IsMicrophoneActive { get; private set; }
    public bool IsMicrophoneMuted { get; private set; }
    public string? ActiveCallApp { get; private set; }
    public DateTimeOffset? CallStartTime { get; private set; }

    public event Action? StateChanged;

    private static readonly string[] MeetingProcesses =
    {
        "zoom", "CptHost", "Teams", "ms-teams", "Discord", "Slack", "WhatsApp", "Telegram", "Skype"
    };

    public PrivacyIndicatorService(Dispatcher ui, MMDeviceEnumerator enumerator)
    {
        _ui = ui;
        _enumerator = enumerator;
        InitCaptureDevice();

        _fallbackTimer = new DispatcherTimer(DispatcherPriority.Background, _ui)
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _fallbackTimer.Tick += (_, _) => TriggerPoll();
    }

    public void ReattachCaptureDevice()
    {
        if (_disposed) return;
        InitCaptureDevice();
        StateChanged?.Invoke();
    }

    private void InitCaptureDevice()
    {
        try
        {
            try { _captureDevice?.Dispose(); } catch { }
            _captureDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            if (_captureDevice != null)
            {
                IsMicrophoneMuted = _captureDevice.AudioEndpointVolume.Mute;
                _captureDevice.AudioEndpointVolume.OnVolumeNotification += OnCaptureVolumeNotification;
            }
        }
        catch { }
    }

    private void OnCaptureVolumeNotification(AudioVolumeNotificationData data)
    {
        if (_disposed) return;
        _ui.InvokeAsync(() =>
        {
            if (IsMicrophoneMuted != data.Muted)
            {
                IsMicrophoneMuted = data.Muted;
                StateChanged?.Invoke();
            }
        });
    }

    public void Start()
    {
        bool camWatched = TryAttachRegistryWatcher("webcam");
        bool micWatched = TryAttachRegistryWatcher("microphone");

        if (!camWatched || !micWatched)
        {
            _fallbackTimer.Start();
        }

        TriggerPoll();
    }

    public void Stop()
    {
        _fallbackTimer.Stop();
        DisposeWatchers();
    }

    private bool TryAttachRegistryWatcher(string capability)
    {
        try
        {
            string basePath = $@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{capability}";
            var key = Registry.CurrentUser.OpenSubKey(basePath, false);
            if (key == null) return false;

            var state = new RegistryWatcherState(key);
            if (!ArmRegistryNotification(state))
            {
                state.Dispose();
                return false;
            }

            state.RegisteredWait = ThreadPool.RegisterWaitForSingleObject(
                state.WaitEvent,
                (ctx, _) =>
                {
                    if (_disposed || ctx is not RegistryWatcherState s) return;
                    TriggerPoll();
                    ArmRegistryNotification(s);
                },
                state,
                Timeout.Infinite,
                executeOnlyOnce: false);

            _registryWatchers.Add(state);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool ArmRegistryNotification(RegistryWatcherState state)
    {
        if (_disposed) return false;
        try
        {
            int res = RegNotifyChangeKeyValue(
                state.Key.Handle.DangerousGetHandle(),
                bWatchSubtree: true,
                REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET,
                state.WaitEvent.SafeWaitHandle.DangerousGetHandle(),
                fAsynchronous: true);
            return res == 0;
        }
        catch
        {
            return false;
        }
    }

    private void DisposeWatchers()
    {
        foreach (var w in _registryWatchers)
        {
            w.Dispose();
        }
        _registryWatchers.Clear();
    }

    public bool ToggleMicrophoneMute()
    {
        try
        {
            if (_captureDevice == null) InitCaptureDevice();
            if (_captureDevice != null)
            {
                bool nextMute = !_captureDevice.AudioEndpointVolume.Mute;
                _captureDevice.AudioEndpointVolume.Mute = nextMute;
                IsMicrophoneMuted = nextMute;
                StateChanged?.Invoke();
                return nextMute;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[PrivacyService] ToggleMute failed: {ex.Message}");
            InitCaptureDevice();
        }
        return false;
    }

    private void TriggerPoll()
    {
        if (_disposed || Interlocked.CompareExchange(ref _polling, 1, 0) != 0)
            return;

        Task.Run(() =>
        {
            try
            {
                bool cam = CheckConsentStore("webcam", out _);
                bool mic = CheckConsentStore("microphone", out string? activeApp);

                string? meetingApp = null;
                if ((mic || cam) && !string.IsNullOrWhiteSpace(activeApp))
                {
                    foreach (var proc in MeetingProcesses)
                    {
                        if (activeApp.Contains(proc, StringComparison.OrdinalIgnoreCase))
                        {
                            meetingApp = proc.ToUpperInvariant();
                            break;
                        }
                    }
                }

                if (_disposed) return;

                _ui.InvokeAsync(() =>
                {
                    if (_disposed) return;

                    if (meetingApp != null && ActiveCallApp == null)
                    {
                        CallStartTime = DateTimeOffset.Now;
                    }
                    else if (meetingApp == null)
                    {
                        CallStartTime = null;
                    }

                    if (cam != IsCameraActive || mic != IsMicrophoneActive || meetingApp != ActiveCallApp)
                    {
                        IsCameraActive = cam;
                        IsMicrophoneActive = mic;
                        ActiveCallApp = meetingApp;
                        StateChanged?.Invoke();
                    }
                });
            }
            catch { }
            finally
            {
                Interlocked.Exchange(ref _polling, 0);
            }
        });
    }

    private static bool CheckConsentStore(string capability, out string? activeAppName)
    {
        activeAppName = null;
        string basePath = $@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{capability}";
        using var root = Registry.CurrentUser.OpenSubKey(basePath, false);
        if (root == null) return false;

        // Check desktop NonPackaged apps first (most common for Zoom/Discord/Teams/browsers)
        using (var nonPackaged = root.OpenSubKey("NonPackaged", false))
        {
            if (nonPackaged != null)
            {
                foreach (var subKeyName in nonPackaged.GetSubKeyNames())
                {
                    using var appKey = nonPackaged.OpenSubKey(subKeyName, false);
                    if (appKey != null && IsActive(appKey))
                    {
                        activeAppName = subKeyName;
                        return true;
                    }
                }
            }
        }

        // Check packaged UWP/MSIX apps
        foreach (var subKeyName in root.GetSubKeyNames())
        {
            if (string.Equals(subKeyName, "NonPackaged", StringComparison.OrdinalIgnoreCase)) continue;
            using var appKey = root.OpenSubKey(subKeyName, false);
            if (appKey != null && IsActive(appKey))
            {
                activeAppName = subKeyName;
                return true;
            }
        }

        return false;
    }

    private static bool IsActive(RegistryKey key)
    {
        return key.GetValue("LastUsedTimeStop") is long stopTime && stopTime == 0 &&
               key.GetValue("LastUsedTimeStart") is long startTime && startTime > 0;
    }

    public void Dispose()
    {
        _disposed = true;
        _fallbackTimer.Stop();
        DisposeWatchers();
        if (_captureDevice != null)
        {
            try { _captureDevice.AudioEndpointVolume.OnVolumeNotification -= OnCaptureVolumeNotification; } catch { }
            try { _captureDevice.Dispose(); } catch { }
        }
    }
}
