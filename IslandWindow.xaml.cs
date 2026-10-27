using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland;

public partial class IslandWindow : Window
{
    private readonly record struct PillShape(double Width, double Height, double Radius);

    /// <summary>Target geometry (DIPs) for each state, per the spec.</summary>
    private static readonly IReadOnlyDictionary<IslandState, PillShape> Shapes = new Dictionary<IslandState, PillShape>
    {
        [IslandState.Idle] = new(120, 28, 14),
        [IslandState.Compact] = new(220, 34, 17),
        [IslandState.Toast] = new(320, 56, 26),
        [IslandState.Expanded] = new(420, 190, 32),
        [IslandState.Hidden] = new(120, 28, 14),
    };

    /// <summary>Expanded geometry when the Settings view is selected (expands wider and taller).</summary>
    private static readonly PillShape SettingsExpandedShape = new(480, 336, 36);

    /// <summary>Expanded geometry when the Wi-Fi &amp; Connectivity view is selected.</summary>
    private static readonly PillShape NetworkExpandedShape = new(452, 208, 34);

    private PillShape GetTargetShape(IslandState state, ExpandedView? viewOverride = null)
    {
        if (state == IslandState.Expanded)
        {
            var activeView = viewOverride ?? _currentExpandedView;
            if (activeView == ExpandedView.Settings)
            {
                return SettingsExpandedShape;
            }
            if (activeView == ExpandedView.Network)
            {
                return NetworkExpandedShape;
            }
        }
        return Shapes[state];
    }

    private static readonly ToastContent[] DemoToasts =
    {
        new("\uE767", "Volume", "65%", ThemeBrushes.BlueColor, TimeSpan.FromSeconds(2.5), null, ToastPriority.Feedback, "volume"),
        new("\uE702", "AirPods Pro", "Connected · 82%", ThemeBrushes.PurpleColor, TimeSpan.FromSeconds(3.5), "audio", ToastPriority.System),
        new("\uE851", "Battery low", "15% remaining", ThemeBrushes.RedColor, TimeSpan.FromSeconds(4), "battery", ToastPriority.Critical),
        new("\uE709", "Airplane mode", "On", ThemeBrushes.AmberColor, TimeSpan.FromSeconds(2.5), null, ToastPriority.System),
    };

    private readonly ServiceContainer _services = new();
    private readonly IslandStateMachine _machine = new();
    private readonly Spring _width, _height, _radius, _dropletOffset;
    private readonly RectangleGeometry _contentClip = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly CursorTracker _cursor;
    private readonly FullscreenWatcher _fullscreen;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _topmostTimer;
    private readonly FrameworkElement[] _panels;
    private readonly FrameworkElement[] _allContainers;
    private readonly Button[] _dockButtons;
    private readonly IReadOnlyDictionary<ExpandedView, (FrameworkElement Container, Button DockButton)> _viewRegistry;
    private readonly ScaleTransform[] _bars;

    // Observable collections for zero-teardown ItemsControl bindings
    private readonly ObservableCollection<ShelfItem> _shelfItems = new();
    private readonly ObservableCollection<ClipboardItem> _clipboardItems = new();
    private readonly ObservableCollection<AudioEndpointInfo> _audioEndpoints = new();
    private readonly ObservableCollection<AppAudioSessionInfo> _audioAppSessions = new();
    private readonly ObservableCollection<PeripheralDeviceInfo> _peripheralDevices = new();
    private readonly ObservableCollection<NotificationHistoryItem> _notificationHistoryItems = new();

    // Services (managed via _services container)
    private readonly MediaSessionService _mediaService;
    private readonly AudioSpectrum _audioSpectrum;
    private readonly PowerPlanService _powerPlanService;
    private readonly BluetoothMonitor _bluetoothMonitor;
    private readonly VolumeService _volumeService;
    private readonly SystemTelemetryService _telemetryService;
    private readonly DropShelfService _shelfService;
    private readonly PrivacyIndicatorService _privacyService;
    private readonly NotificationListenerService _notificationListener;
    private readonly ClipboardHistoryService _clipboardService;
    private readonly DownloadTrackerService _downloadService;
    private readonly SettingsService _settingsService;
    private readonly AudioEndpointService _audioEndpointService;
    private readonly VoiceMemoService _voiceMemoService;
    private readonly PeripheralDeviceService _peripheralService;
    private readonly CountdownTimerService _countdownService;
    private readonly NetworkService _networkService;
    private readonly WeatherService _weatherService;
    private readonly CalendarService _calendarService;
    private readonly BrightnessService _brightnessService;
    private TrayIconService? _trayIcon;

    private readonly float[] _bandLevels = new float[AudioSpectrum.BandCount];
    private readonly float[] _smoothBands = new float[AudioSpectrum.BandCount];

    private ExpandedView _currentExpandedView = ExpandedView.Media;
    private CompactActivityKind _activeCompactHost = CompactActivityKind.None;
    private CompactActivityKind _secondaryCompactHost = CompactActivityKind.None;
    private string? _lastTimeText;
    private string? _lastDateShortText;
    private string? _lastTimerText;
    private int _demoToastIndex;
    private bool _isUserSeeking;

    public IslandWindow()
    {
        InitializeComponent();

        var idle = Shapes[IslandState.Idle];
        _width = new Spring(idle.Width);
        _height = new Spring(idle.Height);
        _radius = new Spring(idle.Radius);
        _dropletOffset = new Spring(0);

        ContentHost.Clip = _contentClip;

        _panels = new FrameworkElement[] { IdlePanel, CompactPanel, ToastPanel, ExpandedPanel };
        foreach (var panel in _panels)
        {
            bool visible = panel == IdlePanel;
            panel.RenderTransformOrigin = new Point(0.5, 0);
            panel.RenderTransform = new ScaleTransform(1, 1);
            panel.Opacity = visible ? 1 : 0;
            panel.IsHitTestVisible = visible;
            panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        _viewRegistry = new Dictionary<ExpandedView, (FrameworkElement, Button)>
        {
            [ExpandedView.Media] = (MediaContainer, MediaHomeButton),
            [ExpandedView.Shelf] = (ShelfContainer, ShelfButton),
            [ExpandedView.Clipboard] = (ClipboardContainer, ClipboardButton),
            [ExpandedView.Calc] = (CalcContainer, CalcButton),
            [ExpandedView.Settings] = (SettingsContainer, SettingsButton),
            [ExpandedView.Audio] = (AudioContainer, AudioButton),
            [ExpandedView.Battery] = (BatteryContainer, BatteryButton),
            [ExpandedView.VoiceMemo] = (VoiceMemoContainer, RecordButton),
            [ExpandedView.Peripherals] = (PeripheralsContainer, PeripheralsButton),
            [ExpandedView.Network] = (NetworkContainer, NetworkButton),
            [ExpandedView.Notifications] = (NotificationsContainer, NotificationsButton),
        };

        _allContainers = _viewRegistry.Values.Select(v => v.Container).ToArray();
        _dockButtons = _viewRegistry.Values.Select(v => v.DockButton).ToArray();
        SetDockActive(MediaHomeButton);

        _bars = new[] { Bar0, Bar1, Bar2, Bar3 }.Select(bar =>
        {
            var scale = new ScaleTransform(1, 0.25);
            bar.RenderTransformOrigin = new Point(0.5, 0.5);
            bar.RenderTransform = scale;
            return scale;
        }).ToArray();

        // Bind ItemsControls once to ObservableCollections
        ShelfItemsControl.ItemsSource = _shelfItems;
        ClipboardItemsControl.ItemsSource = _clipboardItems;
        AudioEndpointsControl.ItemsSource = _audioEndpoints;
        AudioAppSessionsControl.ItemsSource = _audioAppSessions;
        PeripheralsItemsControl.ItemsSource = _peripheralDevices;
        NotificationsItemsControl.ItemsSource = _notificationHistoryItems;

        _machine.StateChanged += OnStateChanged;
        _machine.ToastChanged += OnToastChanged;

        _cursor = _services.Register(new CursorTracker(GetHotZone));
        _cursor.InsideChanged += OnPointerInsideChanged;

        _fullscreen = _services.Register(new FullscreenWatcher(() => (_hwnd, GetTargetMonitor())));
        _fullscreen.Changed += fullscreen => _machine.SetSuppressed(fullscreen);

        // Register Services in _services container (sharing a single CoreAudio MMDeviceEnumerator)
        _settingsService = _services.Register(new SettingsService());
        _audioEndpointService = _services.Register(new AudioEndpointService(Dispatcher));
        _mediaService = _services.Register(new MediaSessionService(Dispatcher));
        _audioSpectrum = _services.Register(new AudioSpectrum());
        _powerPlanService = _services.Register(new PowerPlanService(Dispatcher));
        _bluetoothMonitor = _services.Register(new BluetoothMonitor(Dispatcher));
        _volumeService = _services.Register(new VolumeService(Dispatcher, _audioEndpointService.SharedEnumerator));
        _telemetryService = _services.Register(new SystemTelemetryService());
        _shelfService = _services.Register(new DropShelfService());
        _privacyService = _services.Register(new PrivacyIndicatorService(Dispatcher, _audioEndpointService.SharedEnumerator));
        _notificationListener = _services.Register(new NotificationListenerService(Dispatcher, () => _settingsService.Current));
        _clipboardService = _services.Register(new ClipboardHistoryService(Dispatcher));
        _downloadService = _services.Register(new DownloadTrackerService(Dispatcher));
        _voiceMemoService = _services.Register(new VoiceMemoService(Dispatcher));
        _peripheralService = _services.Register(new PeripheralDeviceService(Dispatcher));
        _countdownService = _services.Register(new CountdownTimerService());
        _networkService = _services.Register(new NetworkService(Dispatcher));
        _weatherService = _services.Register(new WeatherService(Dispatcher, () => _settingsService.Current));
        _calendarService = _services.Register(new CalendarService(Dispatcher));
        _brightnessService = _services.Register(new BrightnessService(Dispatcher, GetTargetMonitor));

        _mediaService.Changed += OnMediaChanged;
        _mediaService.TrackChanged += OnTrackChanged;
        _powerPlanService.StatusChanged += OnPowerStatusChanged;
        _powerPlanService.ChargingPulseTriggered += OnChargingPulseTriggered;
        _powerPlanService.DisconnectedTriggered += OnPowerDisconnectedTriggered;
        _powerPlanService.LowBatteryTriggered += OnLowBatteryTriggered;
        _bluetoothMonitor.Alert += toast =>
        {
            _machine.PushToast(toast);
            _ = _peripheralService.RefreshAsync();
        };
        _volumeService.VolumeChanged += OnVolumeChanged;
        _telemetryService.Updated += OnTelemetryUpdated;
        _shelfService.Changed += OnShelfChanged;
        _privacyService.StateChanged += OnPrivacyStateChanged;
        _notificationListener.NotificationReceived += toast => _machine.PushToast(toast);
        _notificationListener.HistoryChanged += () => Dispatcher.InvokeAsync(OnNotificationHistoryChanged);
        _clipboardService.Changed += OnClipboardChanged;
        _downloadService.ActiveDownloadsChanged += OnDownloadsChanged;
        _downloadService.DownloadCompleted += OnDownloadCompleted;
        _audioEndpointService.EndpointsChanged += OnAudioEndpointsChanged;
        _audioEndpointService.AppSessionsChanged += OnAudioAppSessionsChanged;
        _audioEndpointService.DefaultChanged += OnDefaultAudioEndpointChanged;
        _audioEndpointService.DefaultCaptureChanged += () => _privacyService.ReattachCaptureDevice();
        _voiceMemoService.RecordingTick += OnVoiceMemoTick;
        _voiceMemoService.RecordingCompleted += OnVoiceMemoCompleted;
        _voiceMemoService.PlaybackStateChanged += OnMemoPlaybackStateChanged;
        _peripheralService.DevicesChanged += OnPeripheralsChanged;
        _peripheralService.DevicesChanged += _audioEndpointService.SyncBluetoothBatteries;
        _peripheralService.LowBatteryAlert += OnPeripheralLowBatteryAlert;
        _countdownService.StateChanged += RefreshStopwatchUI;
        _countdownService.Completed += OnCountdownCompleted;
        _networkService.Updated += OnNetworkUpdated;
        _networkService.ConnectivityAlert += toast => _machine.PushToast(toast);
        _weatherService.Updated += OnWeatherUpdated;
        _calendarService.Updated += OnCalendarUpdated;
        _brightnessService.BrightnessChanged += OnBrightnessChanged;

        _clockTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => UpdateClockAndTimeline(), Dispatcher);
        _topmostTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background,
            (_, _) => ReassertTopmost(), Dispatcher);

        WireEvents();
        ApplyGeometry();
        UpdateClockAndTimeline();
        UpdateMicMuteUI();
        OnTelemetryUpdated();
        OnShelfChanged();
        OnClipboardChanged();
        OnDownloadsChanged();
        OnNotificationHistoryChanged();
        OnAudioEndpointsChanged(_audioEndpointService.Endpoints);
        OnPowerStatusChanged(_powerPlanService.Current);
        OnPeripheralsChanged(_peripheralService.Devices);
        OnNetworkUpdated(_networkService.Current);
        OnWeatherUpdated(_weatherService.Current);
        OnCalendarUpdated(_calendarService.NextEvent);
        OnBrightnessChanged(_brightnessService.BrightnessPercent);
        InitializeVoiceMemoUI();
        ApplySettings();
        _settingsService.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void WireEvents()
    {
        Pill.MouseLeftButtonDown += (_, e) =>
        {
            if (_machine.State == IslandState.Expanded) return;

            ExpandedView? targetView = null;
            if (_machine.State == IslandState.Toast)
            {
                if (ExpandedViewExtensions.TryParseKey(_machine.ActiveToast?.TargetView, out var parsed))
                {
                    targetView = parsed;
                }
            }
            else if (_machine.State == IslandState.Compact)
            {
                var compactTarget = ActivityResolver.ResolveCompactTargetView(_activeCompactHost);
                if (compactTarget != ExpandedView.Media)
                {
                    targetView = compactTarget;
                }
            }

            _machine.ExpandNow();
            if (targetView.HasValue)
            {
                SwitchExpandedView(targetView.Value);
            }
            e.Handled = true;
        };

        // Media transport controls
        PlayPauseButton.Click += (_, _) => _mediaService.TogglePlayPause();
        PrevButton.Click += (_, _) => _mediaService.Previous();
        NextButton.Click += (_, _) => _mediaService.Next();

        // Timeline scrubber
        TimelineSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _isUserSeeking = true));
        TimelineSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
        {
            _isUserSeeking = false;
            CommitSeek();
        }));
        TimelineSlider.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_isUserSeeking)
            {
                _isUserSeeking = false;
                CommitSeek();
            }
        };

        MediaHomeButton.Click += (_, _) => SwitchExpandedView(ExpandedView.Media);
        StartPauseButton.Click += (_, _) => ToggleStopwatch();
        PomodoroButton.Click += (_, _) => StartPomodoroTimer();
        ResetButton.Click += (_, _) => ResetStopwatch();
        ShelfButton.Click += (_, _) => SwitchExpandedView(ExpandedView.Shelf);
        ClearShelfButton.Click += (_, _) => _shelfService.Clear();
        ClipboardButton.Click += (_, _) => SwitchExpandedView(ExpandedView.Clipboard);
        ClearClipboardButton.Click += (_, _) => _clipboardService.Clear();
        CalcButton.Click += (_, _) => SwitchExpandedView(ExpandedView.Calc);
        AudioButton.Click += (_, _) => SwitchExpandedView(ExpandedView.Audio);
        BatteryButton.Click += (_, _) => SwitchExpandedView(ExpandedView.Battery);
        RecordButton.Click += (_, _) => SwitchExpandedView(ExpandedView.VoiceMemo);
        PeripheralsButton.Click += (_, _) => SwitchExpandedView(ExpandedView.Peripherals);
        NetworkButton.Click += (_, _) => SwitchExpandedView(ExpandedView.Network);
        NotificationsButton.Click += (_, _) => SwitchExpandedView(ExpandedView.Notifications);
        SettingsButton.Click += (_, _) => SwitchExpandedView(ExpandedView.Settings);
        MicMuteButton.Click += (_, _) => ToggleMicMute();
        PreviewKeyDown += IslandWindow_PreviewKeyDown;
    }

    // ═════════════════════════════════════════════════════════════════
    //  Activity & Clock/Timeline Coordination
    // ═════════════════════════════════════════════════════════════════

    private void EvaluateActivity()
    {
        bool mediaPlaying = _mediaService.Current?.IsPlaying ?? false;
        bool stopwatchActive = _stopwatch.IsRunning || _stopwatch.Elapsed > TimeSpan.Zero;
        bool countdownRunning = _countdownService?.IsActive ?? false;
        bool callActive = !string.IsNullOrWhiteSpace(_privacyService.ActiveCallApp);
        bool downloading = _downloadService.HasActiveDownloads;
        bool recording = _voiceMemoService?.IsRecording ?? false;

        var (hasActivity, host, secondaryHost) = ActivityResolver.ResolveWithSecondary(
            recording,
            mediaPlaying,
            callActive,
            countdownRunning,
            downloading,
            stopwatchActive);

        _activeCompactHost = host;
        _secondaryCompactHost = secondaryHost;
        _machine.SetActivity(hasActivity);

        bool showTimerHost = host is CompactActivityKind.Countdown or CompactActivityKind.Stopwatch or CompactActivityKind.None;
        CompactRecordHost.Visibility = host == CompactActivityKind.Recording ? Visibility.Visible : Visibility.Collapsed;
        CompactMediaHost.Visibility = host == CompactActivityKind.Media ? Visibility.Visible : Visibility.Collapsed;
        CompactCallHost.Visibility = host == CompactActivityKind.Call ? Visibility.Visible : Visibility.Collapsed;
        CompactDownloadHost.Visibility = host == CompactActivityKind.Download ? Visibility.Visible : Visibility.Collapsed;
        CompactTimerHost.Visibility = showTimerHost ? Visibility.Visible : Visibility.Collapsed;

        UpdateSecondaryDropletTarget();
        UpdateSpectrumActivity();
        UpdateClockTimerInterval();
    }

    private void UpdateClockTimerInterval()
    {
        bool needsSubSecond = _machine.State == IslandState.Expanded ||
                              _stopwatch.IsRunning ||
                              (_countdownService?.IsRunning ?? false) ||
                              _privacyService.CallStartTime.HasValue;
        var targetInterval = needsSubSecond ? TimeSpan.FromMilliseconds(200) : TimeSpan.FromSeconds(1);
        if (_clockTimer != null && _clockTimer.Interval != targetInterval)
        {
            _clockTimer.Interval = targetInterval;
        }
    }

    private void UpdateClockAndTimeline()
    {
        var now = DateTime.Now;
        string time = now.ToString("t", CultureInfo.CurrentCulture);
        if (time != _lastTimeText)
        {
            _lastTimeText = time;
            IdleClock.Text = time;
            ExpandedBigClock.Text = time;
            ExpandedMiniClock.Text = time;

            string dateShort = now.ToString("ddd, MMM d", CultureInfo.CurrentCulture);
            if (dateShort != _lastDateShortText)
            {
                _lastDateShortText = dateShort;
                ExpandedDate.Text = dateShort;
                ExpandedBigDate.Text = now.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);
            }
        }

        // Poll Pomodoro / Countdown completion
        _countdownService?.PollCompletion();

        // Stopwatch or Countdown formatting (only update TextBlocks when text changes)
        bool isCountdown = _countdownService is { IsActive: true } || (_countdownService?.TargetDuration > TimeSpan.Zero && !_stopwatch.IsRunning);
        if (isCountdown && _countdownService != null)
        {
            string formatted = _countdownService.FormatRemaining();
            if (formatted != _lastTimerText)
            {
                _lastTimerText = formatted;
                CompactTimer.Text = formatted;
                ExpandedTimer.Text = formatted;
            }
        }
        else if (_stopwatch.IsRunning || _lastTimerText == null)
        {
            var elapsed = _stopwatch.Elapsed;
            string formattedTimer = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"mm\:ss");
            if (formattedTimer != _lastTimerText)
            {
                _lastTimerText = formattedTimer;
                CompactTimer.Text = formattedTimer;
                ExpandedTimer.Text = formattedTimer;
            }
        }

        // Call duration timer
        if (_privacyService.CallStartTime.HasValue)
        {
            var callSpan = DateTimeOffset.Now - _privacyService.CallStartTime.Value;
            ExpandedCallTimer.Text = callSpan.TotalHours >= 1 ? callSpan.ToString(@"h\:mm\:ss") : callSpan.ToString(@"mm\:ss");
        }

        // Media Scrubber timeline updates (only compute while Expanded or when media changes)
        if (_machine.State == IslandState.Expanded)
        {
            UpdateMediaTimelineUI();
        }
    }

    private void UpdateMediaTimelineUI()
    {
        var media = _mediaService.Current;
        if (media is not null && media.Duration > TimeSpan.Zero)
        {
            var pos = media.GetPosition(DateTimeOffset.Now);
            PositionText.Text = FormatTime(pos);
            DurationText.Text = FormatTime(media.Duration);

            if (!_isUserSeeking)
            {
                double pct = Math.Clamp((pos.TotalSeconds / media.Duration.TotalSeconds) * 100.0, 0, 100);
                TimelineSlider.Value = pct;
            }
        }
        else
        {
            PositionText.Text = "0:00";
            DurationText.Text = "0:00";
            if (!_isUserSeeking) TimelineSlider.Value = 0;
        }
    }

    private static string FormatTime(TimeSpan span)
    {
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{span.Minutes}:{span.Seconds:D2}";
    }

    /// <summary>
    /// Incrementally synchronizes an <see cref="ObservableCollection{T}"/> with <paramref name="source"/>
    /// without calling <c>Clear()</c> when items are added/removed at the head or modified in-place,
    /// avoiding full WPF <see cref="ItemsControl"/> container teardown.
    /// </summary>
    internal static void SyncObservableCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        if (source.Count == 0)
        {
            if (target.Count > 0) target.Clear();
            return;
        }

        // Fast-path: single head insertion (e.g., new clipboard item or shelf drop)
        if (source.Count == target.Count + 1 && target.Count > 0 && EqualityComparer<T>.Default.Equals(source[1], target[0]))
        {
            target.Insert(0, source[0]);
            return;
        }

        // Remove items no longer in source
        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!source.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        // Insert or reorder to match source exactly
        for (int i = 0; i < source.Count; i++)
        {
            var desired = source[i];
            if (i < target.Count)
            {
                if (!EqualityComparer<T>.Default.Equals(target[i], desired))
                {
                    int existingIdx = target.IndexOf(desired);
                    if (existingIdx > i)
                        target.Move(existingIdx, i);
                    else
                        target.Insert(i, desired);
                }
            }
            else
            {
                target.Add(desired);
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        App.Log("IslandWindow OnClosed called.");
        _clockTimer.Stop();
        _topmostTimer.Stop();
        StopRendering();
        UnregisterAllHotKeys();

        _settingsService.SaveNow();
        _trayIcon?.Dispose();
        _services.Dispose();

        base.OnClosed(e);
    }
}
