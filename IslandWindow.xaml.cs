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

    private static readonly ToastContent[] DemoToasts =
    {
        new("\uE767", "Volume", "65%", ThemeBrushes.BlueColor, TimeSpan.FromSeconds(2.5)),
        new("\uE702", "AirPods Pro", "Connected · 82%", ThemeBrushes.PurpleColor, TimeSpan.FromSeconds(3.5)),
        new("\uE851", "Battery low", "15% remaining", ThemeBrushes.RedColor, TimeSpan.FromSeconds(4)),
        new("\uE709", "Airplane mode", "On", ThemeBrushes.AmberColor, TimeSpan.FromSeconds(2.5)),
    };

    private readonly IslandStateMachine _machine = new();
    private readonly Spring _width, _height, _radius;
    private readonly RectangleGeometry _contentClip = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly CursorTracker _cursor;
    private readonly FullscreenWatcher _fullscreen;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _topmostTimer;
    private readonly FrameworkElement[] _panels;
    private readonly FrameworkElement[] _allContainers;
    private readonly Button[] _dockButtons;
    private readonly ScaleTransform[] _bars;

    // Observable collections for zero-teardown ItemsControl bindings
    private readonly ObservableCollection<ShelfItem> _shelfItems = new();
    private readonly ObservableCollection<ClipboardItem> _clipboardItems = new();
    private readonly ObservableCollection<AudioEndpointInfo> _audioEndpoints = new();
    private readonly ObservableCollection<PeripheralDeviceInfo> _peripheralDevices = new();

    // Services
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
    private TrayIconService? _trayIcon;

    private readonly float[] _bandLevels = new float[AudioSpectrum.BandCount];
    private readonly float[] _smoothBands = new float[AudioSpectrum.BandCount];

    private string _currentExpandedView = "media";
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

        _allContainers = new FrameworkElement[]
        {
            MediaContainer, ShelfContainer, ClipboardContainer, CalcContainer,
            SettingsContainer, AudioContainer, BatteryContainer, VoiceMemoContainer,
            PeripheralsContainer
        };

        _dockButtons = new Button[]
        {
            MediaHomeButton, ShelfButton, ClipboardButton, CalcButton, AudioButton,
            BatteryButton, RecordButton, PeripheralsButton, SettingsButton
        };
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
        PeripheralsItemsControl.ItemsSource = _peripheralDevices;

        _machine.StateChanged += OnStateChanged;
        _machine.ToastChanged += OnToastChanged;

        _cursor = new CursorTracker(GetHotZone);
        _cursor.InsideChanged += OnPointerInsideChanged;

        _fullscreen = new FullscreenWatcher(() => (_hwnd, GetTargetMonitor()));
        _fullscreen.Changed += fullscreen => _machine.SetSuppressed(fullscreen);

        // Initialize Services (sharing a single CoreAudio MMDeviceEnumerator)
        _audioEndpointService = new AudioEndpointService(Dispatcher);
        _mediaService = new MediaSessionService(Dispatcher);
        _audioSpectrum = new AudioSpectrum();
        _powerPlanService = new PowerPlanService(Dispatcher);
        _bluetoothMonitor = new BluetoothMonitor(Dispatcher);
        _volumeService = new VolumeService(Dispatcher, _audioEndpointService.SharedEnumerator);
        _telemetryService = new SystemTelemetryService();
        _shelfService = new DropShelfService();
        _privacyService = new PrivacyIndicatorService(Dispatcher, _audioEndpointService.SharedEnumerator);
        _notificationListener = new NotificationListenerService(Dispatcher);
        _clipboardService = new ClipboardHistoryService(Dispatcher);
        _downloadService = new DownloadTrackerService(Dispatcher);
        _settingsService = new SettingsService();
        _voiceMemoService = new VoiceMemoService(Dispatcher);
        _peripheralService = new PeripheralDeviceService(Dispatcher);

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
        _clipboardService.Changed += OnClipboardChanged;
        _downloadService.ActiveDownloadsChanged += OnDownloadsChanged;
        _downloadService.DownloadCompleted += OnDownloadCompleted;
        _audioEndpointService.EndpointsChanged += OnAudioEndpointsChanged;
        _audioEndpointService.DefaultChanged += OnDefaultAudioEndpointChanged;
        _audioEndpointService.DefaultCaptureChanged += () => _privacyService.ReattachCaptureDevice();
        _voiceMemoService.RecordingTick += OnVoiceMemoTick;
        _voiceMemoService.RecordingCompleted += OnVoiceMemoCompleted;
        _voiceMemoService.PlaybackStateChanged += OnMemoPlaybackStateChanged;
        _peripheralService.DevicesChanged += OnPeripheralsChanged;
        _peripheralService.DevicesChanged += _audioEndpointService.SyncBluetoothBatteries;
        _peripheralService.LowBatteryAlert += OnPeripheralLowBatteryAlert;

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
        OnAudioEndpointsChanged(_audioEndpointService.Endpoints);
        OnPowerStatusChanged(_powerPlanService.Current);
        OnPeripheralsChanged(_peripheralService.Devices);
        InitializeVoiceMemoUI();
        ApplySettings();
        _settingsService.Changed += OnSettingsChanged;
    }

    private void WireEvents()
    {
        Pill.MouseLeftButtonDown += (_, e) =>
        {
            if (_machine.State == IslandState.Expanded) return;

            string? targetView = null;
            if (_machine.State == IslandState.Toast)
            {
                targetView = _machine.ActiveToast?.TargetView;
            }
            else if (_machine.State == IslandState.Compact)
            {
                if (_voiceMemoService.IsRecording) targetView = "voicememo";
                else if (_downloadService.HasActiveDownloads && !(_mediaService.Current?.IsPlaying ?? false)) targetView = "shelf";
            }

            _machine.ExpandNow();
            if (!string.IsNullOrEmpty(targetView))
            {
                SwitchExpandedView(targetView);
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

        MediaHomeButton.Click += (_, _) => SwitchExpandedView("media");
        StartPauseButton.Click += (_, _) => ToggleStopwatch();
        ResetButton.Click += (_, _) => ResetStopwatch();
        ShelfButton.Click += (_, _) => ToggleShelfView();
        ClearShelfButton.Click += (_, _) => _shelfService.Clear();
        ClipboardButton.Click += (_, _) => ToggleClipboardView();
        ClearClipboardButton.Click += (_, _) => _clipboardService.Clear();
        CalcButton.Click += (_, _) => ToggleCalcView();
        AudioButton.Click += (_, _) => ToggleAudioView();
        BatteryButton.Click += (_, _) => ToggleBatteryView();
        RecordButton.Click += (_, _) => ToggleVoiceMemoView();
        PeripheralsButton.Click += (_, _) => TogglePeripheralsView();
        SettingsButton.Click += (_, _) => ToggleSettingsView();
        MicMuteButton.Click += (_, _) => ToggleMicMute();
        PreviewKeyDown += IslandWindow_PreviewKeyDown;
    }

    // ═════════════════════════════════════════════════════════════════
    //  Activity & Clock/Timeline Coordination
    // ═════════════════════════════════════════════════════════════════

    private void EvaluateActivity()
    {
        bool mediaPlaying = _mediaService.Current?.IsPlaying ?? false;
        bool timerActive = _stopwatch.IsRunning || _stopwatch.Elapsed > TimeSpan.Zero;
        bool callActive = !string.IsNullOrWhiteSpace(_privacyService.ActiveCallApp);
        bool downloading = _downloadService.HasActiveDownloads;
        bool recording = _voiceMemoService?.IsRecording ?? false;
        _machine.SetActivity(mediaPlaying || timerActive || callActive || downloading || recording);

        // Update Compact mode hosts priority: Recording > Media > Call > Download > Timer
        CompactRecordHost.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        CompactMediaHost.Visibility = !recording && mediaPlaying ? Visibility.Visible : Visibility.Collapsed;
        CompactCallHost.Visibility = !recording && !mediaPlaying && callActive ? Visibility.Visible : Visibility.Collapsed;
        CompactDownloadHost.Visibility = !recording && !mediaPlaying && !callActive && downloading ? Visibility.Visible : Visibility.Collapsed;
        CompactTimerHost.Visibility = !recording && !mediaPlaying && !callActive && !downloading ? Visibility.Visible : Visibility.Collapsed;

        UpdateSpectrumActivity();
        UpdateClockTimerInterval();
    }

    private void UpdateClockTimerInterval()
    {
        bool needsSubSecond = _machine.State == IslandState.Expanded ||
                              _stopwatch.IsRunning ||
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

        // Stopwatch formatting (only update TextBlocks when text changes)
        if (_stopwatch.IsRunning || _lastTimerText == null)
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
        _cursor.Dispose();
        _fullscreen.Dispose();
        _powerPlanService.Dispose();
        _bluetoothMonitor.Dispose();
        _audioSpectrum.Dispose();
        _mediaService.Dispose();
        _volumeService.Dispose();
        _telemetryService.Dispose();
        _privacyService.Dispose();
        _notificationListener.Dispose();
        _clipboardService.Dispose();
        _downloadService.Dispose();
        _audioEndpointService.Dispose();
        _voiceMemoService.Dispose();
        _peripheralService.Dispose();

        base.OnClosed(e);
    }
}
