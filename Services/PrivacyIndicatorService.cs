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
    private readonly object _captureLock = new();
    private MMDevice? _captureDevice;
    private int _polling;
    private bool _watchersAttached;
    private bool _isSuspended;
    private volatile bool _disposed;

    private sealed class RegistryWatcherState : IDisposable
    {
        public RegistryKey Key { get; }
        public AutoResetEvent WaitEvent { get; } = new(false);
        public RegisteredWaitHandle? RegisteredWait { get; set; }

        public RegistryWatcherState(RegistryKey key) => Key = key;

        public void Dispose()
        {
            if (RegisteredWait != null)
            {
                using var done = new ManualResetEvent(false);
                try
                {
                    if (RegisteredWait.Unregister(done))
                    {
                        done.WaitOne(200);
                    }
                }
                catch { }
                RegisteredWait = null;
            }
            try { WaitEvent.Dispose(); } catch { }
            try { Key.Dispose(); } catch { }
        }
    }

    public bool IsCameraActive { get; private set; }
    public bool IsMicrophoneActive { get; private set; }
    public bool IsMicrophoneMuted { get; private set; }
    public string? ActiveCallApp { get; private set; }
    public IReadOnlyList<string> ActivePrivacyApps { get; private set; } = Array.Empty<string>();
    public DateTimeOffset? CallStartTime { get; private set; }

    public event Action? StateChanged;

    private static readonly (string Pattern, string FriendlyName)[] KnownCallApps =
    {
        ("whatsapp", "WhatsApp"),
        ("yourphone", "Phone Link"),
        ("phoneexperiencehost", "Phone Link"),
        ("ms-teams", "Teams"),
        ("msteams", "Teams"),
        ("teams", "Teams"),
        ("zoom", "Zoom"),
        ("cpthost", "Zoom"),
        ("discord", "Discord"),
        ("telegram", "Telegram"),
        ("unigram", "Telegram"),
        ("signal", "Signal"),
        ("slack", "Slack"),
        ("skype", "Skype"),
        ("ciscocollabhost", "Webex"),
        ("webex", "Webex"),
        ("atmgr", "Webex"),
        ("messenger", "Messenger"),
        ("viber", "Viber"),
        ("wechat", "WeChat"),
        ("facetime", "FaceTime"),
    };

    private static readonly string[] BrowserProcesses =
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc"
    };

    public PrivacyIndicatorService(Dispatcher ui, MMDeviceEnumerator enumerator)
    {
        _ui = ui;
        _enumerator = enumerator;
        InitCaptureDevice();

        _fallbackTimer = new DispatcherTimer(DispatcherPriority.Background, _ui)
        {
            Interval = TimeSpan.FromSeconds(4)
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
        lock (_captureLock)
        {
            if (_disposed) return;
            try
            {
                if (_captureDevice != null)
                {
                    try { _captureDevice.AudioEndpointVolume.OnVolumeNotification -= OnCaptureVolumeNotification; } catch { }
                    try { _captureDevice.Dispose(); } catch { }
                    _captureDevice = null;
                }

                _captureDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                if (_captureDevice != null)
                {
                    IsMicrophoneMuted = _captureDevice.AudioEndpointVolume.Mute;
                    _captureDevice.AudioEndpointVolume.OnVolumeNotification += OnCaptureVolumeNotification;
                }
            }
            catch (Exception ex) { App.LogDebug("PrivacyIndicator", ex); }
        }
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
        bool camAttached = TryAttachRegistryWatcher("webcam");
        bool micAttached = TryAttachRegistryWatcher("microphone");
        _watchersAttached = camAttached && micAttached;
        if (!_watchersAttached && !_isSuspended)
        {
            _fallbackTimer.Interval = TimeSpan.FromSeconds(10);
            _fallbackTimer.Start();
        }
        TriggerPoll();
    }

    public void Stop()
    {
        _fallbackTimer.Stop();
        DisposeWatchers();
    }

    public void SetSuspended(bool suspended)
    {
        if (_disposed || _isSuspended == suspended) return;
        _isSuspended = suspended;
        if (suspended)
        {
            _fallbackTimer.Stop();
        }
        else
        {
            if (!_watchersAttached || IsCameraActive || IsMicrophoneActive)
            {
                _fallbackTimer.Start();
            }
            TriggerPoll();
        }
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
        catch (Exception ex)
        {
            App.LogDebug("PrivacyIndicator", ex);
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
                REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET | REG_NOTIFY_THREAD_AGNOSTIC,
                state.WaitEvent.SafeWaitHandle.DangerousGetHandle(),
                fAsynchronous: true);
            return res == 0;
        }
        catch (Exception ex)
        {
            App.LogDebug("PrivacyIndicator", ex);
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
        lock (_captureLock)
        {
            if (_disposed) return IsMicrophoneMuted;
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
    }

    private void TriggerPoll()
    {
        if (_disposed || Interlocked.CompareExchange(ref _polling, 1, 0) != 0)
            return;

        Task.Run(() =>
        {
            try
            {
                var activeApps = new List<string>();
                bool cam = CheckConsentStore("webcam", activeApps);
                bool mic = CheckConsentStore("microphone", activeApps);

                if (!mic)
                {
                    mic = CheckActiveWasapiCaptureSessions(activeApps);
                }

                string? meetingApp = (mic || cam)
                    ? ResolveFriendlyCallAppName(activeApps)
                    : null;

                var cleanPrivacyApps = new List<string>();
                foreach (var rawApp in activeApps)
                {
                    if (string.IsNullOrWhiteSpace(rawApp) ||
                        rawApp.Contains("DynamicIsland", StringComparison.OrdinalIgnoreCase) ||
                        rawApp.Contains("svchost", StringComparison.OrdinalIgnoreCase) ||
                        rawApp.Contains("audiodg", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string clean = ExtractCleanAppName(rawApp);
                    if (!string.IsNullOrWhiteSpace(clean) && !cleanPrivacyApps.Contains(clean, StringComparer.OrdinalIgnoreCase))
                    {
                        cleanPrivacyApps.Add(clean);
                    }
                }
                string[] privacyAppsSnapshot = cleanPrivacyApps.Count > 0 ? cleanPrivacyApps.ToArray() : Array.Empty<string>();

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

                    bool appsChanged = !ActivePrivacyApps.SequenceEqual(privacyAppsSnapshot, StringComparer.OrdinalIgnoreCase);
                    ActivePrivacyApps = privacyAppsSnapshot;

                    if (cam != IsCameraActive || mic != IsMicrophoneActive || meetingApp != ActiveCallApp || appsChanged)
                    {
                        IsCameraActive = cam;
                        IsMicrophoneActive = mic;
                        ActiveCallApp = meetingApp;
                        StateChanged?.Invoke();
                    }

                    // Keep fallback timer running ONLY while cam/mic are active or kernel registry watchers failed
                    if (!_isSuspended && (cam || mic || !_watchersAttached))
                    {
                        var desiredInterval = (cam || mic) ? TimeSpan.FromSeconds(4) : TimeSpan.FromSeconds(10);
                        if (_fallbackTimer.Interval != desiredInterval)
                        {
                            _fallbackTimer.Interval = desiredInterval;
                        }
                        if (!_fallbackTimer.IsEnabled)
                        {
                            _fallbackTimer.Start();
                        }
                    }
                    else
                    {
                        _fallbackTimer.Stop();
                    }
                });
            }
            catch (Exception ex) { App.LogDebug("PrivacyIndicator", ex); }
            finally
            {
                Interlocked.Exchange(ref _polling, 0);
            }
        });
    }

    internal static string FormatCompactCallText(string callApp, TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        string timer = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");
        return $"{callApp} \u00B7 {timer}";
    }

    internal static string? ResolveFriendlyCallAppName(IReadOnlyList<string> activeAppIdentifiers)
    {
        if (activeAppIdentifiers == null || activeAppIdentifiers.Count == 0)
            return null;

        // Pass 1: Match dedicated communication/calling apps (WhatsApp, Phone Link, Teams, Zoom, Discord, Telegram, etc.)
        foreach (var raw in activeAppIdentifiers)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (raw.Contains("DynamicIsland", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var (pattern, friendly) in KnownCallApps)
            {
                if (raw.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return friendly;
                }
            }
        }

        // Pass 2: Check if a browser is hosting a web call (WhatsApp Web, Google Meet, Discord Web, Teams Web, Zoom Web)
        foreach (var raw in activeAppIdentifiers)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            foreach (var browser in BrowserProcesses)
            {
                if (raw.Contains(browser, StringComparison.OrdinalIgnoreCase))
                {
                    string? webCallTitle = TryDetectBrowserCallTitle(browser);
                    return webCallTitle ?? "Web Call";
                }
            }
        }

        // Pass 3: Fallback to cleaned executable or package name for any other app using mic/camera
        foreach (var raw in activeAppIdentifiers)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (raw.Contains("DynamicIsland", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("svchost", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("audiodg", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string clean = ExtractCleanAppName(raw);
            if (!string.IsNullOrWhiteSpace(clean))
            {
                return clean;
            }
        }

        return null;
    }

    private static string? TryDetectBrowserCallTitle(string browserProcName)
    {
        Process[]? procs = null;
        try
        {
            procs = Process.GetProcessesByName(browserProcName);
            foreach (var p in procs)
            {
                try
                {
                    string title = p.MainWindowTitle;
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    if (title.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)) return "WhatsApp Web";
                    if (title.Contains("Meet", StringComparison.OrdinalIgnoreCase)) return "Google Meet";
                    if (title.Contains("Discord", StringComparison.OrdinalIgnoreCase)) return "Discord";
                    if (title.Contains("Teams", StringComparison.OrdinalIgnoreCase)) return "Teams";
                    if (title.Contains("Zoom", StringComparison.OrdinalIgnoreCase)) return "Zoom";
                    if (title.Contains("Messenger", StringComparison.OrdinalIgnoreCase)) return "Messenger";
                    if (title.Contains("Slack", StringComparison.OrdinalIgnoreCase)) return "Slack";
                    if (title.Contains("Telegram", StringComparison.OrdinalIgnoreCase)) return "Telegram";
                }
                catch (Exception ex) { App.LogDebug("PrivacyIndicator", ex); }
            }
        }
        catch (Exception ex) { App.LogDebug("PrivacyIndicator", ex); }
        finally
        {
            if (procs != null)
            {
                foreach (var p in procs)
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }
        return null;
    }

    internal static string ExtractCleanAppName(string rawSubKey)
    {
        // NonPackaged format: C:#Program Files#WindowsApps#...#WhatsApp.exe
        string normalized = rawSubKey.Replace('#', '\\');
        string leaf = System.IO.Path.GetFileNameWithoutExtension(normalized);
        if (!string.IsNullOrWhiteSpace(leaf) && !leaf.Contains('_'))
        {
            return leaf;
        }

        // Packaged UWP/MSIX format: 5319275A.WhatsAppDesktop_cv1g1gvanyjgm
        int underscore = rawSubKey.IndexOf('_');
        string pkg = underscore > 0 ? rawSubKey[..underscore] : rawSubKey;
        int dot = pkg.LastIndexOf('.');
        if (dot >= 0 && dot + 1 < pkg.Length)
        {
            pkg = pkg[(dot + 1)..];
        }
        return pkg.Replace("Desktop", "", StringComparison.OrdinalIgnoreCase).Trim();
    }

    private bool CheckActiveWasapiCaptureSessions(List<string> activeApps)
    {
        lock (_captureLock)
        {
            try
            {
                if (_disposed || _captureDevice == null) return false;
                var sessionMgr = _captureDevice.AudioSessionManager;
                try
                {
                    var sessions = sessionMgr.Sessions;
                    bool anyActive = false;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var s = sessions[i];
                        try
                        {
                            if (s.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive)
                            {
                                uint pid = s.GetProcessID;
                                if (pid == 0 || pid == Environment.ProcessId) continue;
                                try
                                {
                                    using var proc = Process.GetProcessById((int)pid);
                                    if (!string.IsNullOrWhiteSpace(proc.ProcessName) &&
                                        !proc.ProcessName.Equals("DynamicIsland", StringComparison.OrdinalIgnoreCase) &&
                                        !proc.ProcessName.Equals("audiodg", StringComparison.OrdinalIgnoreCase) &&
                                        !proc.ProcessName.Equals("svchost", StringComparison.OrdinalIgnoreCase))
                                    {
                                        activeApps.Add(proc.ProcessName);
                                        anyActive = true;
                                    }
                                }
                                catch (Exception ex) { App.LogDebug("PrivacyIndicator", ex); }
                            }
                        }
                        finally
                        {
                            try { s.Dispose(); } catch { }
                        }
                    }
                    return anyActive;
                }
                finally
                {
                    try { (sessionMgr as IDisposable)?.Dispose(); } catch { }
                }
            }
            catch (Exception ex)
            {
                App.LogDebug("PrivacyIndicator", ex);
                return false;
            }
        }
    }

    public static bool FocusCallAppWindow(string? callAppName)
    {
        if (string.IsNullOrWhiteSpace(callAppName)) return false;
        Process[]? procs = null;
        try
        {
            string search = callAppName.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase) ? "WhatsApp"
                : callAppName.Contains("Phone Link", StringComparison.OrdinalIgnoreCase) ? "PhoneExperienceHost"
                : callAppName.Contains("Teams", StringComparison.OrdinalIgnoreCase) ? "ms-teams"
                : callAppName;

            procs = Process.GetProcesses();
            foreach (var proc in procs)
            {
                try
                {
                    if (proc.MainWindowHandle == IntPtr.Zero) continue;
                    if (proc.ProcessName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        proc.MainWindowTitle.Contains(callAppName, StringComparison.OrdinalIgnoreCase) ||
                        (callAppName.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase) &&
                         proc.MainWindowTitle.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)))
                    {
                        SetForegroundWindow(proc.MainWindowHandle);
                        return true;
                    }
                }
                catch (Exception ex) { App.LogDebug("PrivacyIndicator", ex); }
            }
        }
        catch (Exception ex) { App.LogDebug("PrivacyIndicator", ex); }
        finally
        {
            if (procs != null)
            {
                foreach (var proc in procs)
                {
                    try { proc.Dispose(); } catch { }
                }
            }
        }
        return false;
    }

    private static bool CheckConsentStore(string capability, List<string> activeApps)
    {
        string basePath = $@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{capability}";
        using var root = Registry.CurrentUser.OpenSubKey(basePath, false);
        if (root == null) return false;

        bool foundAny = false;

        // Check desktop NonPackaged apps first
        using (var nonPackaged = root.OpenSubKey("NonPackaged", false))
        {
            if (nonPackaged != null)
            {
                foreach (var subKeyName in nonPackaged.GetSubKeyNames())
                {
                    if (subKeyName.Contains("DynamicIsland", StringComparison.OrdinalIgnoreCase)) continue;
                    using var appKey = nonPackaged.OpenSubKey(subKeyName, false);
                    if (appKey != null && IsActive(appKey))
                    {
                        activeApps.Add(subKeyName);
                        foundAny = true;
                    }
                }
            }
        }

        // Check packaged UWP/MSIX apps (e.g. 5319275A.WhatsAppDesktop_..., MSTeams_..., Microsoft.YourPhone_...)
        foreach (var subKeyName in root.GetSubKeyNames())
        {
            if (string.Equals(subKeyName, "NonPackaged", StringComparison.OrdinalIgnoreCase)) continue;
            if (subKeyName.Contains("DynamicIsland", StringComparison.OrdinalIgnoreCase)) continue;
            using var appKey = root.OpenSubKey(subKeyName, false);
            if (appKey != null && IsActive(appKey))
            {
                activeApps.Add(subKeyName);
                foundAny = true;
            }
        }

        return foundAny;
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
        lock (_captureLock)
        {
            if (_captureDevice != null)
            {
                try { _captureDevice.AudioEndpointVolume.OnVolumeNotification -= OnCaptureVolumeNotification; } catch { }
                try { _captureDevice.Dispose(); } catch { }
                _captureDevice = null;
            }
        }
    }
}
