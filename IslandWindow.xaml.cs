using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using DynamicIsland.Core;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

public partial class IslandWindow : Window
{
    private readonly record struct PillShape(double Width, double Height, double Radius);

    public const double CanvasWidthDip = 760.0;
    public const double CanvasHeightDip = 660.0;

    /// <summary>
    /// Optical geometry targets tuned with golden-ratio aspect proportions and continuous squircle radii.
    /// </summary>
    private static readonly IReadOnlyDictionary<IslandState, PillShape> Shapes = new Dictionary<IslandState, PillShape>
    {
        [IslandState.Idle]     = new(148, 30, 15),
        [IslandState.Compact]  = new(228, 36, 18),
        [IslandState.Toast]    = new(336, 58, 29),
        [IslandState.Expanded] = new(484, 176, 34),
        [IslandState.Hidden]   = new(120, 24, 12),
    };

    // Unified width (484 DIP) with harmonized vertical rhythm steps (multiples of 8px); Settings uses Left-Rail (536x368)
    private static readonly PillShape SettingsExpandedShape   = new(536, 368, 36);
    private static readonly PillShape ClipboardExpandedShape  = new(588, 296, 36);
    private static readonly PillShape NetworkExpandedShape    = new(484, 196, 34);
    private static readonly PillShape BatteryExpandedShape    = new(484, 216, 34);
    private static readonly PillShape SystemHudExpandedShape  = new(484, 232, 34);
    private static readonly PillShape LyricsExpandedShape     = new(484, 216, 34);
    private static readonly PillShape ConverterExpandedShape  = new(484, 232, 36);
    private static readonly PillShape WidgetsGalleryExpandedShape = new(520, 356, 36);
    private static readonly PillShape LauncherExpandedShape   = new(484, 320, 36);
    private static readonly PillShape TermiExpandedShape      = new(484, 232, 36);
    private static readonly PillShape PhoneBridgeExpandedShape = new(484, 288, 36);
    private static readonly PillShape MechSoundsExpandedShape  = new(484, 220, 34);
    private static readonly PillShape CaptureStudioExpandedShape = new(484, 232, 36);
    private static readonly PillShape CalendarExpandedShape   = new(484, 256, 36);
    private static readonly PillShape NotesExpandedShape      = new(484, 240, 36);
    private static readonly PillShape AudioExpandedShape      = new(484, 340, 36);
    private static readonly PillShape ShelfExpandedShape      = new(588, 268, 36);
    private static readonly PillShape QuickReplyExpandedShape = new(484, 156, 36);
    private static readonly PillShape PetExpandedShape        = new(484, 216, 34);
    private static readonly PillShape MirrorExpandedShape     = new(484, 248, 36);

    // Tactile interaction constants (WWDC Fluid Interfaces spec)
    private const double HoverWidthSwell = 10.0;
    private const double HoverHeightSwell = 2.5;
    private const double PressWidthTension = -6.0;
    private const double PressHeightTension = -1.5;

    private PillShape GetNotificationsExpandedShape()
    {
        int totalCount = _notificationListener?.HistoryCount ?? _notificationHistoryItems.Count;
        int count = _notificationAppGroups.Count;
        if (count <= 0)
        {
            return new PillShape(484, totalCount > 0 ? 228 : 196, 36);
        }

        int expandedExtraItems = _notificationAppGroups.Where(g => g.IsExpanded).Sum(g => Math.Min(g.Notifications.Count - 1, 3));
        int visibleCount = Math.Clamp(count + expandedExtraItems, 1, 4);
        double targetHeight = Math.Clamp(126 + (visibleCount * 82), 224, 412);
        return new PillShape(484, targetHeight, 38);
    }

    private static PillShape GetToastShape(ToastContent? toast)
    {
        if (toast != null && (toast.OnPromptDecision != null || !string.IsNullOrWhiteSpace(toast.PromptActivityId)))
        {
            double w = !string.IsNullOrWhiteSpace(toast.DiffText) ? 416 : 384;
            return new PillShape(w, 58, 29);
        }
        if (toast?.CanReply == true)
        {
            return new PillShape(368, 58, 29);
        }
        return Shapes[IslandState.Toast];
    }

    /// <summary>
    /// Enforces a strict 2-signal visual budget in Idle state so the pill never turns into a cluttered taskbar.
    /// Priority: NetworkSpeed -> VirtualPet/Lottie -> Base Clock.
    /// </summary>
    private PillShape GetIdleShape()
    {
        bool hardwareNotch = _settingsService?.Current?.HardwareNotchMode ?? false;
        bool isDetached = _isDetachedPiP || (_settingsService?.Current.IsDetachedFloating ?? false);
        if (hardwareNotch && !isDetached)
        {
            double notchW = _settingsService?.Current?.HardwareNotchWidth ?? 188.0;
            double notchH = _settingsService?.Current?.HardwareNotchHeight ?? 35.0;
            double notchR = _settingsService?.Current?.HardwareNotchRadius ?? 12.0;
            return new PillShape(notchW, notchH, notchR);
        }

        string style = SettingsService.NormalizeClockStyle(_settingsService?.Current?.ClockStyle);

        double baseW = style switch
        {
            "Seconds"   => 158,
            "DateBadge" => 184,
            "Analog"    => 160,
            "Neon"      => 144,
            "Flip"      => 154,
            "Matrix"    => 176,
            "JumpHour"  => 164,
            _           => 142,
        };

        // Cap concurrent idle complications to prevent visual bloat
        double complicationExtra = 0;
        if (_settingsService?.Current?.ShowNetworkSpeedInIdle == true)
        {
            complicationExtra = 44; // Clean bandwidth pill
        }
        else if (_settingsService?.Current?.EnableVirtualPet == true)
        {
            complicationExtra = 28;
        }

        if (_settingsService?.Current?.ShowCameraLensIcon == false)
        {
            complicationExtra -= 12;
        }

        double finalW = Math.Clamp(baseW + complicationExtra, 104, 244);
        return new PillShape(finalW, 30, 15);
    }

    private PillShape GetTargetShape(IslandState state, ExpandedView? viewOverride = null)
    {
        PillShape baseShape;

        if (state == IslandState.Expanded)
        {
            var activeView = viewOverride ?? _currentExpandedView;
            baseShape = activeView switch
            {
                ExpandedView.Calendar      => CalendarExpandedShape,
                ExpandedView.Settings      => SettingsExpandedShape,
                ExpandedView.Clipboard     => ClipboardExpandedShape,
                ExpandedView.Shelf         => ShelfExpandedShape,
                ExpandedView.Notes         => NotesExpandedShape,
                ExpandedView.Audio         => AudioExpandedShape,
                ExpandedView.Network       => NetworkExpandedShape,
                ExpandedView.Battery       => BatteryExpandedShape,
                ExpandedView.SystemHud     => SystemHudExpandedShape,
                ExpandedView.Converter     => ConverterExpandedShape,
                ExpandedView.WidgetsGallery => WidgetsGalleryExpandedShape,
                ExpandedView.Launcher      => LauncherExpandedShape,
                ExpandedView.Termi         => TermiExpandedShape,
                ExpandedView.PhoneBridge   => PhoneBridgeExpandedShape,
                ExpandedView.MechSounds    => MechSoundsExpandedShape,
                ExpandedView.CaptureStudio => CaptureStudioExpandedShape,
                ExpandedView.QuickReply    => QuickReplyExpandedShape,
                ExpandedView.Pet           => PetExpandedShape,
                ExpandedView.Mirror        => MirrorExpandedShape,
                ExpandedView.Notifications => GetNotificationsExpandedShape(),
                ExpandedView.Media when _mediaService.Current == null || string.IsNullOrWhiteSpace(_mediaService.Current.Title)
                                           => new PillShape(484, 114, 28),
                ExpandedView.Media when _lyricsService?.HasLyrics == true && _settingsService?.Current?.EnableSyncedLyrics != false && !_preferClockHeaderInMedia
                                           => LyricsExpandedShape,
                ExpandedView.Media         => new PillShape(484, 152, 34),
                _                          => Shapes[IslandState.Expanded]
            };
        }
        else if (state == IslandState.Idle)
        {
            baseShape = GetIdleShape();
        }
        else if (state == IslandState.Toast)
        {
            baseShape = GetToastShape(_machine.ActiveToast);
        }
        else
        {
            baseShape = Shapes[state];
        }

        // Apply tactile magnetic hover swell & press-down tension in collapsed states
        if (state is IslandState.Idle or IslandState.Compact)
        {
            if (_isPillPressed)
            {
                baseShape = new PillShape(
                    baseShape.Width + PressWidthTension,
                    baseShape.Height + PressHeightTension,
                    (baseShape.Height + PressHeightTension) * 0.5);
            }
            else if (_pointerInside)
            {
                baseShape = new PillShape(
                    baseShape.Width + HoverWidthSwell,
                    baseShape.Height + HoverHeightSwell,
                    (baseShape.Height + HoverHeightSwell) * 0.5);
            }
        }

        return baseShape;
    }

    private static readonly ToastContent[] DemoToasts =
    {
        new("\uE767", "Volume", "65%", ThemeBrushes.BlueColor, TimeSpan.FromSeconds(2.2), null, ToastPriority.Feedback, "volume"),
        new("\uE702", "AirPods Pro", "Connected · 82%", ThemeBrushes.PurpleColor, TimeSpan.FromSeconds(3.2), "audio", ToastPriority.System),
        new("\uE851", "Battery Low", "15% remaining", ThemeBrushes.RedColor, TimeSpan.FromSeconds(3.8), "battery", ToastPriority.Critical),
        new("\uE709", "Airplane Mode", "Enabled", ThemeBrushes.AmberColor, TimeSpan.FromSeconds(2.4), null, ToastPriority.System),
    };

    private readonly ServiceContainer _services = new();
    private readonly IslandStateMachine _machine = new();
    private readonly Spring _width, _height, _radius, _dropletOffset;
    private readonly RectangleGeometry _contentClip = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly CursorTracker _cursor = null!;
    private readonly FullscreenWatcher _fullscreen = null!;
    private readonly DispatcherTimer _clockTimer;
    private readonly FrameworkElement[] _panels;
    private readonly List<FrameworkElement> _materializedContainers;
    private readonly Button[] _dockButtons;
    private readonly IReadOnlyDictionary<ExpandedView, (Func<FrameworkElement> ResolveContainer, Button DockButton)> _viewRegistry;
    private readonly List<ExpandedView> _orderedViews;
    private readonly ScaleTransform[] _bars;

    // Lazy-instantiated self-contained expanded widget views (allocated on first navigation)
    private Views.Widgets.CalendarTaskView? _calendarContainer;
    private Views.Widgets.NotesView? _notesContainer;
    private Views.Widgets.ConverterView? _converterContainer;
    private Views.Widgets.WidgetsGalleryView? _widgetsGalleryContainer;
    private Views.Widgets.LauncherView? _launcherContainer;
    private Views.Widgets.TermiNotchView? _termiContainer;
    private Views.Widgets.PhoneBridgeView? _phoneBridgeContainer;
    private Views.Widgets.MechSoundsView? _mechSoundsContainer;
    private Views.Widgets.CaptureStudioView? _captureStudioContainer;
    private Views.Widgets.QuickReplyView? _quickReplyContainer;
    private Views.Widgets.PetSanctuaryView? _petSanctuaryContainer;
    private Views.Widgets.MirrorView? _mirrorContainer;

    internal Views.Widgets.CalendarTaskView? CalendarContainer => _calendarContainer;
    internal Views.Widgets.NotesView? NotesContainer => _notesContainer;
    internal Views.Widgets.ConverterView? ConverterContainer => _converterContainer;
    internal Views.Widgets.WidgetsGalleryView? WidgetsGalleryContainer => _widgetsGalleryContainer;
    internal Views.Widgets.LauncherView? LauncherContainer => _launcherContainer;
    internal Views.Widgets.TermiNotchView? TermiContainer => _termiContainer;
    internal Views.Widgets.PhoneBridgeView? PhoneBridgeContainer => _phoneBridgeContainer;
    internal Views.Widgets.MechSoundsView? MechSoundsContainer => _mechSoundsContainer;
    internal Views.Widgets.CaptureStudioView? CaptureStudioContainer => _captureStudioContainer;
    internal Views.Widgets.QuickReplyView? QuickReplyContainer => _quickReplyContainer;
    internal Views.Widgets.PetSanctuaryView? PetSanctuaryContainer => _petSanctuaryContainer;
    internal Views.Widgets.MirrorView? MirrorContainer => _mirrorContainer;

    private T MaterializeExpandedView<T>(ref T? slot) where T : FrameworkElement, new()
    {
        if (slot != null) return slot;

        var view = new T
        {
            Visibility = Visibility.Collapsed,
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var tg = new TransformGroup();
        tg.Children.Add(new ScaleTransform(1, 1));
        tg.Children.Add(new TranslateTransform(0, 0));
        view.RenderTransform = tg;

        ExpandedViewsHost.Children.Add(view);
        slot = view;
        _materializedContainers.Add(view);
        return view;
    }

    internal Views.Widgets.TermiNotchView EnsureTermiContainer() => MaterializeExpandedView(ref _termiContainer);
    internal Views.Widgets.QuickReplyView EnsureQuickReplyContainer() => MaterializeExpandedView(ref _quickReplyContainer);
    internal Views.Widgets.NotesView EnsureNotesContainer() => MaterializeExpandedView(ref _notesContainer);

    // Observable collections for zero-teardown ItemsControl bindings
    private readonly ObservableCollection<ShelfItem> _shelfItems = new();
    private readonly ObservableCollection<ClipboardItem> _clipboardItems = new();
    private readonly ObservableCollection<AudioEndpointInfo> _audioEndpoints = new();
    private readonly ObservableCollection<AppAudioSessionInfo> _audioAppSessions = new();
    private readonly ObservableCollection<PeripheralDeviceInfo> _peripheralDevices = new();
    private readonly ObservableCollection<NotificationHistoryItem> _notificationHistoryItems = new();
    private readonly ObservableCollection<NotificationAppGroup> _notificationAppGroups = new();
    private readonly ObservableCollection<WifiNetworkInfo> _wifiNetworks = new();

    // Services
    private readonly MediaSessionService _mediaService;
    private readonly AudioSpectrum _audioSpectrum;
    private readonly PowerPlanService _powerPlanService;
    private readonly BluetoothMonitor _bluetoothMonitor;
    private readonly VolumeService _volumeService;
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
    private readonly LyricsService _lyricsService;
    private MediaSongListService _mediaSongListService => _services.Get<MediaSongListService>();
    private readonly WeatherService _weatherService;
    private readonly CalendarService _calendarService;
    private readonly BrightnessService _brightnessService;
    private readonly VirtualPetService _petService;
    private readonly IslandMemoryManager _memoryManager;
    private readonly SystemPerformanceService _systemPerfService;
    private readonly DropletRegistry _dropletRegistry;
    private readonly LiveActivityHub _liveActivityHub;
    private readonly LiveActivityIpcService _liveActivityIpcService;
    private readonly MeetingModeService _meetingModeService;
    private readonly JiggleBasketService _jiggleBasketService;
    private NotesService _notesService => _services.Get<NotesService>();
    private QuickLauncherService _quickLauncherService => _services.Get<QuickLauncherService>();
    private readonly TouchpadGestureProcessor _touchpadProcessor = new();
    private readonly InlineConfirmGuard _confirmGuard = new();
    private TermiNotchService _termiNotchService => _services.Get<TermiNotchService>();
    private readonly WindowSnapService _windowSnapService;
    private KeyboardSoundService _keyboardSoundService => _services.Get<KeyboardSoundService>();
    private WindowsShareService _windowsShareService => _services.Get<WindowsShareService>();
    private LanQuickShareService _lanQuickShareService => _services.Get<LanQuickShareService>();
    private TaskService _taskService => _services.Get<TaskService>();
    private readonly CommunityDropletService _communityDropletService;
    private readonly UpdateManagerService _updateService;
    private readonly Choreographer _choreographer;
    private Views.MiniPlayerWindow? _miniPlayerWindow;
    private TrayIconService? _trayIcon;
    private bool _isClosing;
    private bool _isFullscreenSuppressed;

    internal bool IsFullscreenSuppressed => _isFullscreenSuppressed;
    internal bool ShouldThrottleVisuals => _choreographer?.ShouldThrottleVisuals ?? false;

    internal SystemPerformanceService? SystemPerfServiceInstance => _systemPerfService;
    internal NetworkService? NetworkServiceInstance => _networkService;
    internal MediaSessionService? MediaServiceInstance => _mediaService;
    internal PowerPlanService? PowerPlanServiceInstance => _powerPlanService;
    internal DropShelfService? DropShelfServiceInstance => _shelfService;
    internal CalendarService? CalendarServiceInstance => _calendarService;
    internal WeatherService? WeatherServiceInstance => _weatherService;
    internal CountdownTimerService? CountdownServiceInstance => _countdownService;
    internal AudioEndpointService? AudioEndpointServiceInstance => _audioEndpointService;
    internal VolumeService? VolumeServiceInstance => _volumeService;
    internal TaskService? TaskServiceInstance => _taskService;
    internal NotesService? NotesServiceInstance => _notesService;
    internal QuickLauncherService? QuickLauncherServiceInstance => _quickLauncherService;
    internal TermiNotchService? TermiNotchServiceInstance => _termiNotchService;
    internal KeyboardSoundService? KeyboardSoundServiceInstance => _keyboardSoundService;
    internal LanQuickShareService LanQuickShare => _lanQuickShareService;
    internal KeyboardSoundService KeyboardSound => _keyboardSoundService;
    internal WebcamMirrorService WebcamMirror => _services.Get<WebcamMirrorService>();
    internal CommunityDropletService CommunityDroplets => _communityDropletService;
    internal DropletRegistry? Droplets => _dropletRegistry;
    internal ClipboardHistoryService ClipboardService => _clipboardService;
    internal DropShelfService Shelf => _shelfService;
    internal SettingsService Settings => _settingsService;
    internal UpdateManagerService UpdateService => _updateService;
    internal void NavigateToView(ExpandedView view) => SwitchExpandedView(view);
    internal void PushToast(ToastContent content) => _machine.PushToast(content);
    internal void ShowToast(string glyph, string title, string message, Color accent, string category)
    {
        _machine.PushToast(new ToastContent(
            glyph,
            title,
            message,
            accent,
            TimeSpan.FromSeconds(2.0),
            TargetView: _currentExpandedView.ToKey(),
            Priority: ToastPriority.Feedback,
            Category: category));
    }

    public void PushNotificationToast(string glyph, string title, string message, Color accentColor)
    {
        _machine.PushToast(new ToastContent(
            glyph,
            title,
            message,
            accentColor,
            TimeSpan.FromSeconds(2.5),
            TargetView: _currentExpandedView.ToKey(),
            Priority: ToastPriority.Feedback,
            Category: "droplet_action"));
        TriggerLiquidImpulse(24, -8, 3);
    }

    private readonly float[] _bandLevels = new float[AudioSpectrum.BandCount];
    private readonly float[] _smoothBands = new float[AudioSpectrum.BandCount];
    private readonly float[] _bandLevels16 = new float[AudioSpectrum.ExpandedBandCount];
    private readonly float[] _smoothBands16 = new float[AudioSpectrum.ExpandedBandCount];
    private readonly float[] _peakLevels16 = new float[AudioSpectrum.ExpandedBandCount];
    private readonly float[] _peakVelocity16 = new float[AudioSpectrum.ExpandedBandCount];
    private readonly long[] _peakHoldTicks16 = new long[AudioSpectrum.ExpandedBandCount];
    private PathFigure? _compactWaveFigure;

    private ExpandedView _currentExpandedView = ExpandedView.Media;
    private CompactActivityKind _activeCompactHost = CompactActivityKind.None;
    private CompactActivityKind _secondaryCompactHost = CompactActivityKind.None;
    private string? _lastTimeText;
    private string? _lastDateShortText;
    private string? _lastTimerText;
    private int _demoToastIndex;
    private bool _isUserSeeking;
    private bool _isPillPressed;
    private Point _dragStartPoint;
    private string? _recordingHotkeyAction;
    private bool _isAudioAppMixerMode;
    private bool _isWifiScannerMode;

    public static IslandWindow? Instance { get; private set; }
    internal VirtualPetService PetService => _petService;

    public IslandWindow()
    {
        Instance = this;
        InitializeComponent();

        var idle = GetIdleShape();
        _width = new Spring(idle.Width);
        _height = new Spring(idle.Height);
        _radius = new Spring(idle.Radius);
        _dropletOffset = new Spring(0) { RestEpsilon = 0.01 };

        ContentHost.Clip = null;

        _panels = new FrameworkElement[] { IdlePanel, CompactPanel, ToastPanel, ExpandedPanel };
        foreach (var panel in _panels)
        {
            bool visible = panel == IdlePanel;
            panel.RenderTransformOrigin = new Point(0.5, 0.0);
            var group = new TransformGroup();
            group.Children.Add(new ScaleTransform(1, 1));
            group.Children.Add(new TranslateTransform(0, 0));
            panel.RenderTransform = group;
            panel.Opacity = visible ? 1 : 0;
            panel.IsHitTestVisible = visible;
            panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        _viewRegistry = new Dictionary<ExpandedView, (Func<FrameworkElement>, Button)>
        {
            [ExpandedView.Media]          = (() => MediaContainer, MediaHomeButton),
            [ExpandedView.Calendar]       = (() => MaterializeExpandedView(ref _calendarContainer), CalendarButton),
            [ExpandedView.Shelf]          = (() => ShelfContainer, ShelfButton),
            [ExpandedView.Notifications]  = (() => NotificationsContainer, NotificationsButton),
            [ExpandedView.WidgetsGallery] = (() => MaterializeExpandedView(ref _widgetsGalleryContainer), WidgetsGalleryButton),
            [ExpandedView.Launcher]       = (() => MaterializeExpandedView(ref _launcherContainer), LauncherButton),
            [ExpandedView.Audio]          = (() => AudioContainer, AudioButton),
            [ExpandedView.Clipboard]      = (() => ClipboardContainer, ShelfButton),
            [ExpandedView.SystemHud]      = (() => SystemHudContainer, WidgetsGalleryButton),
            [ExpandedView.Battery]        = (() => BatteryContainer, WidgetsGalleryButton),
            [ExpandedView.Network]        = (() => NetworkContainer, WidgetsGalleryButton),
            [ExpandedView.Peripherals]    = (() => PeripheralsContainer, WidgetsGalleryButton),
            [ExpandedView.Calc]           = (() => CalcContainer, LauncherButton),
            [ExpandedView.Notes]          = (() => MaterializeExpandedView(ref _notesContainer), LauncherButton),
            [ExpandedView.VoiceMemo]      = (() => VoiceMemoContainer, LauncherButton),
            [ExpandedView.Converter]      = (() => MaterializeExpandedView(ref _converterContainer), LauncherButton),
            [ExpandedView.Termi]          = (() => MaterializeExpandedView(ref _termiContainer), LauncherButton),
            [ExpandedView.PhoneBridge]    = (() => MaterializeExpandedView(ref _phoneBridgeContainer), LauncherButton),
            [ExpandedView.MechSounds]     = (() => MaterializeExpandedView(ref _mechSoundsContainer), LauncherButton),
            [ExpandedView.CaptureStudio]  = (() => MaterializeExpandedView(ref _captureStudioContainer), LauncherButton),
            [ExpandedView.QuickReply]     = (() => MaterializeExpandedView(ref _quickReplyContainer), NotificationsButton),
            [ExpandedView.Pet]            = (() => MaterializeExpandedView(ref _petSanctuaryContainer), WidgetsGalleryButton),
            [ExpandedView.Mirror]         = (() => MaterializeExpandedView(ref _mirrorContainer), LauncherButton),
            [ExpandedView.Settings]       = (() => SettingsContainer, SettingsButton),
        };

        _orderedViews = _viewRegistry.Keys.ToList();
        _materializedContainers = new List<FrameworkElement>
        {
            MediaContainer,
            ShelfContainer,
            ClipboardContainer,
            CalcContainer,
            SettingsContainer,
            AudioContainer,
            BatteryContainer,
            VoiceMemoContainer,
            PeripheralsContainer,
            NetworkContainer,
            NotificationsContainer,
            SystemHudContainer
        };
        _dockButtons = _viewRegistry.Values.Select(v => v.DockButton).ToArray();

        // Prepare each eager expanded container with a TransformGroup for spatial tab transitions
        foreach (var container in _materializedContainers)
        {
            container.RenderTransformOrigin = new Point(0.5, 0.5);
            var tg = new TransformGroup();
            tg.Children.Add(new ScaleTransform(1, 1));
            tg.Children.Add(new TranslateTransform(0, 0));
            container.RenderTransform = tg;
        }

        SetDockActive(MediaHomeButton);

        foreach (var dockBtn in new[] { MediaHomeButton, CalendarButton, ShelfButton, NotificationsButton, WidgetsGalleryButton, LauncherButton, AudioButton, PinExpandedButton, SettingsButton, CollapseButton })
        {
            if (dockBtn != null)
            {
                BentoMotion.AttachInteractiveSpring(dockBtn, hoverScale: 1.12, pressScale: 0.90);
            }
        }

        _bars = new[] { Bar0, Bar1, Bar2, Bar3 }.Select(bar =>
        {
            var scale = new ScaleTransform(1, 0.22);
            bar.RenderTransformOrigin = new Point(0.5, 0.5);
            bar.RenderTransform = scale;
            return scale;
        }).ToArray();

        if (CompactWaveGeometry != null)
        {
            _compactWaveFigure = new PathFigure { StartPoint = new Point(0, 7), IsClosed = false };
            for (int i = 1; i < 9; i++)
            {
                _compactWaveFigure.Segments.Add(new LineSegment(new Point(i * 2.5, 7), isStroked: true));
            }
            CompactWaveGeometry.Figures.Add(_compactWaveFigure);
        }

        ShelfItemsControl.ItemsSource = _shelfItems;
        ClipboardItemsControl.ItemsSource = _clipboardItems;
        AudioEndpointsControl.ItemsSource = _audioEndpoints;
        AudioAppSessionsControl.ItemsSource = _audioAppSessions;
        PeripheralsItemsControl.ItemsSource = _peripheralDevices;
        NotificationsItemsControl.ItemsSource = _notificationAppGroups;
        WifiNetworksItemsControl.ItemsSource = _wifiNetworks;

        _machine.StateChanged += OnStateChanged;
        _machine.ToastChanged += OnToastChanged;

        _cursor = _services.Register(new CursorTracker(GetHotZone));
        _cursor.InsideChanged += OnPointerInsideChanged;
        _cursor.MonitorChanged += OnCursorMonitorChanged;

        _fullscreen = _services.Register(new FullscreenWatcher(() => (_hwnd, GetTargetMonitor())));
        _fullscreen.Changed += fullscreen =>
        {
            _isFullscreenSuppressed = fullscreen;
            _machine.SetSuppressed(fullscreen);
            _systemPerfService?.SetFullscreenSuspended(fullscreen);
            _networkService?.SetSuspended(fullscreen);
            _privacyService?.SetSuspended(fullscreen);
            _peripheralService?.SetSuspended(fullscreen);
            _choreographer?.SetSuspended(fullscreen);
            _cursor?.SetSuspended(fullscreen);
            UpdateNatureThemeTimerState();
        };

        // Register Services
        _settingsService = _services.Register(new SettingsService());
        ApplyExpandTrigger(_settingsService.Current.ExpandTrigger);
        if (_cursor != null) _cursor.EnableFastSwipeGuard = _settingsService.Current.EnableFastSwipeGuard;
        _audioEndpointService = _services.Register(new AudioEndpointService(Dispatcher));
        _mediaService = _services.Register(new MediaSessionService(Dispatcher));
        _audioSpectrum = _services.Register(new AudioSpectrum());
        _powerPlanService = _services.Register(new PowerPlanService(Dispatcher, _settingsService));
        _bluetoothMonitor = _services.Register(new BluetoothMonitor(Dispatcher));
        _volumeService = _services.Register(new VolumeService(Dispatcher, _audioEndpointService.SharedEnumerator));
        _shelfService = _services.Register(new DropShelfService());
        _privacyService = _services.Register(new PrivacyIndicatorService(Dispatcher, _audioEndpointService.SharedEnumerator));
        _notificationListener = _services.Register(new NotificationListenerService(Dispatcher, () => _settingsService.Current));
        _clipboardService = _services.Register(new ClipboardHistoryService(Dispatcher));
        _downloadService = _services.Register(new DownloadTrackerService(Dispatcher));
        _voiceMemoService = _services.Register(new VoiceMemoService(Dispatcher));
        _peripheralService = _services.Register(new PeripheralDeviceService(Dispatcher));
        _countdownService = _services.Register(new CountdownTimerService());
        _networkService = _services.Register(new NetworkService(Dispatcher, () => _settingsService.Current));
        _lyricsService = _services.Register(new LyricsService(Dispatcher));
        _services.RegisterLazy(() => new MediaSongListService(Dispatcher, _mediaService));
        _weatherService = _services.Register(new WeatherService(Dispatcher, () => _settingsService.Current));
        _calendarService = _services.Register(new CalendarService(Dispatcher));
        _brightnessService = _services.Register(new BrightnessService(Dispatcher, GetTargetMonitor));
        _petService = _services.Register(new VirtualPetService(Dispatcher, _settingsService));
        _petService.WellnessAlertTriggered += OnPetWellnessAlert;
        _petService.AffectionChanged += _ => Dispatcher.InvokeAsync(UpdatePetSettingsUI);
        _petService.MoodChanged += _ => Dispatcher.InvokeAsync(UpdatePetSettingsUI);
        _systemPerfService = _services.Register(new SystemPerformanceService(() => _settingsService.Current));
        _systemPerfService.VitalsUpdated += OnSystemVitalsUpdated;
        _systemPerfService.Start();

        _liveActivityHub = new LiveActivityHub();
        _liveActivityHub.Changed += () => Dispatcher.InvokeAsync(EvaluateActivity);
        _liveActivityIpcService = _services.Register(new LiveActivityIpcService(_liveActivityHub));
        _liveActivityIpcService.Start();
        _meetingModeService = _services.Register(new MeetingModeService(Dispatcher, _calendarService, _privacyService, _liveActivityHub));
        _meetingModeService.Start();
        _dropletRegistry = new DropletRegistry(_settingsService);
        _communityDropletService = _services.Register(new CommunityDropletService(_dropletRegistry, _liveActivityHub));
        _communityDropletService.Start();
        _jiggleBasketService = _services.Register(new JiggleBasketService(Dispatcher, _shelfService, () => _dropletRegistry.IsEnabled("jiggle_basket")));
        _services.RegisterLazy(() => new NotesService());
        _services.RegisterLazy(() => new TaskService());
        _services.RegisterLazy(() => new QuickLauncherService());
        _services.RegisterLazy(() => new TermiNotchService());
        _windowSnapService = _services.Register(new WindowSnapService(Dispatcher, () => _hwnd, () => _dropletRegistry.IsEnabled("window_snap")));
        _services.RegisterLazy(() => new KeyboardSoundService());
        _services.RegisterLazy(() => new WindowsShareService(() => _hwnd));
        _services.RegisterLazy(() => new LanQuickShareService(Dispatcher, _shelfService, _clipboardService));
        _services.RegisterLazy(() => new WebcamMirrorService(Dispatcher));
        _updateService = _services.Register(new UpdateManagerService(_settingsService, dispatcher: Dispatcher));
        _updateService.UpdateAvailable += OnUpdateAvailable;
        _updateService.StatusChanged += OnUpdateStatusChanged;
        _updateService.DownloadProgressChanged += OnUpdateDownloadProgressChanged;
        _choreographer = _services.Register(new Choreographer(Dispatcher));
        _weatherService.AttachChoreographer(_choreographer);
        _calendarService.AttachChoreographer(_choreographer);
        _choreographer.PowerModeChanged += (prev, next) => Dispatcher.InvokeAsync(() =>
        {
            UpdateSpectrumActivity();
        });
        Choreographer.EnsureIntegratedGpuPreference();
        _dropletRegistry.DropletStateChanged += (dropletId, enabled) =>
        {
            if (dropletId == "jiggle_basket")
            {
                _jiggleBasketService.SetEnabled(enabled);
            }
            else if (dropletId == "window_snap")
            {
                _windowSnapService.SetEnabled(enabled);
            }
            else if (dropletId == "mech_sounds")
            {
                if (enabled || _services.IsMaterialized<KeyboardSoundService>())
                {
                    _keyboardSoundService.SetEnabled(enabled);
                }
            }
            else if (dropletId == "phone_bridge" && !enabled)
            {
                if (_services.IsMaterialized<LanQuickShareService>())
                {
                    _lanQuickShareService.StopSession();
                }
            }
            else if (dropletId == "caffeine" && !enabled && _isCaffeineActive)
            {
                SetCaffeineMode(CaffeineMode.Off);
            }
            Dispatcher.InvokeAsync(() =>
            {
                UpdateDockVisibilityUI();
                UpdateDropletsUI();
            });
        };

        _memoryManager = _services.Register(new IslandMemoryManager(Dispatcher, () =>
            _machine.State == IslandState.Idle &&
            !_pointerInside &&
            !(_mediaService.Current?.IsPlaying ?? false) &&
            _width.IsAtRest && _height.IsAtRest &&
            !_rendering));

        _mediaService.Changed += OnMediaChanged;
        _mediaService.TrackChanged += OnTrackChanged;
        _powerPlanService.StatusChanged += OnPowerStatusChanged;
        _powerPlanService.ChargingPulseTriggered += OnChargingPulseTriggered;
        _powerPlanService.DisconnectedTriggered += OnPowerDisconnectedTriggered;
        _powerPlanService.LowBatteryTriggered += OnLowBatteryTriggered;
        _bluetoothMonitor.Alert += toast => _machine.PushToast(toast);
        _bluetoothMonitor.DevicesChanged += () => _peripheralService.RefreshAsync().FireAndForget("Bluetooth.DevicesChanged");
        _volumeService.VolumeChanged += OnVolumeChanged;
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
        _voiceMemoService.MemosChanged += () => Dispatcher.InvokeAsync(InitializeVoiceMemoUI);
        _peripheralService.DevicesChanged += OnPeripheralsChanged;
        _peripheralService.DevicesChanged += _audioEndpointService.SyncBluetoothBatteries;
        _peripheralService.LowBatteryAlert += OnPeripheralLowBatteryAlert;
        _countdownService.StateChanged += RefreshStopwatchUI;
        _countdownService.Completed += OnCountdownCompleted;
        _networkService.Updated += OnNetworkUpdated;
        _networkService.WifiNetworksChanged += OnWifiNetworksChanged;
        _networkService.ConnectivityAlert += toast => _machine.PushToast(toast);
        _lyricsService.LyricsLoaded += OnLyricsLoaded;
        _lyricsService.LineChanged += OnLyricsLineChanged;
        _weatherService.Updated += OnWeatherUpdated;
        _calendarService.Updated += OnCalendarUpdated;
        _brightnessService.BrightnessChanged += OnBrightnessChanged;

        _clockTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => UpdateClockAndTimeline(), Dispatcher);
        SetTopmostReassertEnabled(true);

        WireEvents();
        ApplyGeometry();
        UpdateClockAndTimeline();
        UpdateMicMuteUI();
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
        InitializeFloatingState();
        ApplySettings();
        _settingsService.PropertyChanged += OnSettingsPropertyChanged;
        _updateService.StartBackgroundWorker();
        CheckPostUpdateLaunchStatus();
    }

    // ═════════════════════════════════════════════════════════════════
    //  Tactile Interaction & Spatial View Choreography
    // ═════════════════════════════════════════════════════════════════

    private void WireEvents()
    {
        // Phase 1: Press-Down Tension & Tear-Off Anchor
        Pill.MouseLeftButtonDown += (_, e) =>
        {
            if (_machine.State == IslandState.Expanded) return;

            if (e.ClickCount == 2)
            {
                if (_isDetachedPiP)
                {
                    ReDockToBezel();
                    e.Handled = true;
                    return;
                }
                if (_settingsService?.Current.EnableQuickTuck ?? true)
                {
                    TuckIsland();
                    e.Handled = true;
                    return;
                }
            }

            _isPillPressed = true;
            Pill.CaptureMouse();
            try
            {
                StartPillDrag(PointToScreen(e.GetPosition(this)));
            }
            catch { }
            RefreshTargetGeometry();
            e.Handled = true;
        };

        // Phase 2: Release-to-Expand or Settle Floating PiP
        Pill.MouseLeftButtonUp += (_, e) =>
        {
            if (!_isPillPressed) return;
            _isPillPressed = false;
            Pill.ReleaseMouseCapture();

            if (_isPillDragging || _isTearingOff)
            {
                EndPillDrag();
                e.Handled = true;
                return;
            }

            if (!Pill.IsMouseOver)
            {
                RefreshTargetGeometry();
                return;
            }

            if (_machine.State == IslandState.Expanded) return;

            ExpandedView? targetView = null;
            if (_machine.State == IslandState.Toast)
            {
                if (_machine.ActiveToast?.Priority == ToastPriority.Notification)
                {
                    var latest = _notificationListener.History.FirstOrDefault();
                    if (latest != null && NotificationListenerService.ActivateNotification(latest))
                    {
                        _notificationListener.RemoveHistoryItem(latest.Id);
                        _machine.CollapseNow();
                        e.Handled = true;
                        return;
                    }
                }

                if (ExpandedViewExtensions.TryParseKey(_machine.ActiveToast?.TargetView, out var parsed))
                {
                    targetView = parsed;
                }
            }
            else if (_machine.State == IslandState.Compact)
            {
                if (_activeCompactHost == CompactActivityKind.LiveActivity)
                {
                    var primary = _liveActivityHub?.Primary;
                    if (primary != null)
                    {
                        if (primary.Actions != null && primary.Actions.Count > 0 && primary.Id == MeetingModeService.MeetingJoinActivityId)
                        {
                            primary.Actions[0].Callback?.Invoke();
                            e.Handled = true;
                            return;
                        }

                        if (!string.IsNullOrWhiteSpace(primary.TargetViewKey) &&
                            ExpandedViewExtensions.TryParseKey(primary.TargetViewKey, out var actTarget))
                        {
                            targetView = actTarget;
                        }
                    }
                }
                else
                {
                    var compactTarget = ActivityResolver.ResolveCompactTargetView(_activeCompactHost);
                    if (compactTarget != ExpandedView.Media)
                    {
                        targetView = compactTarget;
                    }
                }
            }

            _machine.ExpandNow();
            if (targetView.HasValue)
            {
                SwitchExpandedView(targetView.Value, animateTransition: false);
            }
            e.Handled = true;
        };

        // Right-click in Collapsed State -> Quick Context Switch to Notifications or Media
        Pill.MouseRightButtonUp += (_, e) =>
        {
            if (_machine.State is IslandState.Idle or IslandState.Compact)
            {
                _machine.ExpandNow();
                SwitchExpandedView(
                    _notificationHistoryItems.Count > 0 ? ExpandedView.Notifications : ExpandedView.SystemHud,
                    animateTransition: false);
                e.Handled = true;
            }
        };

        // Horizontal Mouse-Wheel or Tilt-Scroll inside Expanded State -> Fluid Tab Cycling
        Pill.PreviewMouseWheel += (_, e) =>
        {
            if (_machine.State == IslandState.Expanded && Keyboard.Modifiers == ModifierKeys.Shift)
            {
                int currentIndex = _orderedViews.IndexOf(_currentExpandedView);
                if (currentIndex >= 0)
                {
                    int delta = e.Delta < 0 ? 1 : -1;
                    int nextIndex = Math.Clamp(currentIndex + delta, 0, _orderedViews.Count - 1);
                    if (nextIndex != currentIndex)
                    {
                        SwitchExpandedView(_orderedViews[nextIndex], animateTransition: true);
                    }
                }
                e.Handled = true;
            }
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

        MediaHomeButton.Click     += (_, _) => SwitchExpandedView(ExpandedView.Media, true);
        CalendarButton.Click      += (_, _) => SwitchExpandedView(ExpandedView.Calendar, true);
        StartPauseButton.Click    += (_, _) => ToggleStopwatch();
        PomodoroButton.Click      += (_, _) => StartPomodoroTimer();
        ResetButton.Click         += (_, _) => ResetStopwatch();
        ShelfButton.Click         += (_, _) =>
        {
            if (_machine.State == IslandState.Expanded && _currentExpandedView == ExpandedView.Shelf)
                SwitchExpandedView(ExpandedView.Clipboard, true);
            else if (_machine.State == IslandState.Expanded && _currentExpandedView == ExpandedView.Clipboard)
                SwitchExpandedView(ExpandedView.Shelf, true);
            else
                SwitchExpandedView(ExpandedView.Shelf, true);
        };
        ClearShelfButton.Click     += (_, _) => _shelfService.Clear();
        NotificationsButton.Click  += (_, _) => SwitchExpandedView(ExpandedView.Notifications, true);
        WidgetsGalleryButton.Click += (_, _) => SwitchExpandedView(ExpandedView.WidgetsGallery, true);
        LauncherButton.Click       += (_, _) => SwitchExpandedView(ExpandedView.Launcher, true);
        AudioButton.Click          += (_, _) => SwitchExpandedView(ExpandedView.Audio, true);
        SettingsButton.Click       += (_, _) => SwitchExpandedView(ExpandedView.Settings, true);
        PreviewKeyDown            += IslandWindow_PreviewKeyDown;

        if (FloatingDockHost != null)
        {
            FloatingDockHost.MouseEnter += (_, _) =>
            {
                if (_machine.State == IslandState.Expanded)
                {
                    _pointerInside = true;
                    _machine.SetPointerInside(true);
                }
            };
        }
    }

    /// <summary>
    /// Choreographs spatial directional slide + scale transitions between expanded views
    /// while morphing the outer pill height via spring physics.
    /// </summary>
    private void AnimateContainerEntrance(FrameworkElement targetContainer, int direction)
    {
        var ease = SharedCubicOut; // P2b perf: reuse shared frozen instance instead of allocating
        var duration = new Duration(TimeSpan.FromMilliseconds(220));

        targetContainer.Opacity = 0;
        var fadeAnim = new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease };
        targetContainer.BeginAnimation(OpacityProperty, fadeAnim);

        if (targetContainer.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
        {
            if (tg.Children[0] is ScaleTransform scale)
            {
                var scaleAnim = new DoubleAnimation(0.965, 1.0, duration) { EasingFunction = ease };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            }

            if (tg.Children[1] is TranslateTransform translate)
            {
                double startX = direction * 16.0;
                var slideX = new DoubleAnimation(startX, 0.0, duration) { EasingFunction = ease };
                var slideY = new DoubleAnimation(4.0, 0.0, duration) { EasingFunction = ease };
                translate.BeginAnimation(TranslateTransform.XProperty, slideX);
                translate.BeginAnimation(TranslateTransform.YProperty, slideY);
            }
        }
    }

    private void RefreshTargetGeometry()
    {
        var target = GetTargetShape(_machine.State);
        _width.Target = target.Width;
        _height.Target = target.Height;
        _radius.Target = target.Radius;
        StartRendering();
    }

    public void ExpandAndSwitchView(ExpandedView view)
    {
        _machine.ExpandNow();
        SwitchExpandedView(view, animateTransition: false);
    }

    public double DpiScaleValue => _dpiScale > 0 ? _dpiScale : 1.0;
    public double IslandCenterDipX => (_windowRectPx.Left + _windowRectPx.Width / 2.0) / DpiScaleValue;
    public double IslandTopDipY => _windowRectPx.Top / DpiScaleValue;

    public void DetachMiniPlayer()
    {
        if (_miniPlayerWindow != null && _miniPlayerWindow.IsLoaded)
        {
            _miniPlayerWindow.Activate();
            return;
        }

        _miniPlayerWindow = new Views.MiniPlayerWindow(_mediaService, _lyricsService, _volumeService, _mediaSongListService, _audioSpectrum, _settingsService);
        _miniPlayerWindow.Closed += (_, _) => _miniPlayerWindow = null;

        double scale = DpiScaleValue;
        double islandCenterDipX = IslandCenterDipX;
        double islandTopDipY = IslandTopDipY;

        double? savedX = _settingsService.Current.MiniPlayerX;
        double? savedY = _settingsService.Current.MiniPlayerY;
        bool hasSavedPos = savedX.HasValue && savedY.HasValue &&
                           !double.IsNaN(savedX.Value) && !double.IsInfinity(savedX.Value) &&
                           !double.IsNaN(savedY.Value) && !double.IsInfinity(savedY.Value);

        double targetLeft = hasSavedPos ? savedX!.Value : islandCenterDipX - (_miniPlayerWindow.Width / 2.0);
        // Position comfortably right below the collapsed island pill (which is ~36 DIPs tall)
        double targetTop = hasSavedPos ? savedY!.Value : islandTopDipY + 54.0;

        var monitor = GetTargetMonitor();
        if (monitor != IntPtr.Zero)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref mi))
            {
                (targetLeft, targetTop) = WindowPlacementHelper.ClampToWorkArea(
                    targetLeft,
                    targetTop,
                    _miniPlayerWindow.Width,
                    _miniPlayerWindow.Height,
                    mi.rcWork.Left / scale,
                    mi.rcWork.Top / scale,
                    mi.rcWork.Right / scale,
                    mi.rcWork.Bottom / scale,
                    margin: 12.0);
            }
        }

        _miniPlayerWindow.Left = targetLeft;
        _miniPlayerWindow.Top = targetTop;
        _miniPlayerWindow.Show();
        _miniPlayerWindow.Activate();

        if (_settingsService.Current.MiniPlayerCompact)
        {
            _miniPlayerWindow.SetCompactModeExternal(true);
        }

        _machine.CollapseNow();
    }

    public bool ShareFilesViaWindows(IEnumerable<string> filePaths, string? title = null) =>
        _windowsShareService.ShareFiles(filePaths, title);

    public LanShareSessionInfo StartPhoneBridgeSession(int durationMinutes = 15, string? password = null) =>
        _lanQuickShareService.StartSession(
            durationMinutes,
            password: password ?? _settingsService.Current.PhoneBridgePassword);

    internal NotesService NotesService => _notesService;
    internal DropShelfService ShelfService => _shelfService;

    private void SetTopmostReassertEnabled(bool enabled)
    {
        if (_isClosing) return;
        if (enabled)
            _choreographer.Register("reassert_topmost", ReassertTopmost, 3, PowerSensitivity.Normal);
        else
            _choreographer.Unregister("reassert_topmost");
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        App.Log("IslandWindow OnClosed called.");
        _clockTimer.Stop();
        _choreographer.Unregister("reassert_topmost");
        _natureThemeTimer?.Stop();
        _caffeineTimer?.Stop();
        _tuckRestoreTimer?.Stop();
        StopRendering();

        try { if (_miniPlayerWindow != null) { _miniPlayerWindow.Close(); _miniPlayerWindow = null; } } catch { }
        try { if (_petFloatWindow != null) { _petFloatWindow.Close(); _petFloatWindow = null; } } catch { }
        try { Views.DesktopWidgetWindow.CloseAll(); } catch { }

        UnregisterAllHotKeys();
        UninstallVolumeKeyHook();
        UnregisterSystemEvents();

        if (_hwnd != IntPtr.Zero)
        {
            try { System.Windows.Interop.HwndSource.FromHwnd(_hwnd)?.RemoveHook(WndProc); } catch { }
        }

        _settingsService.SaveNow();
        _trayIcon?.Dispose();
        _machine.Dispose();
        DynamicIsland.LiquidGlass.GlassManager.Instance.FrameReady -= OnGlassManagerFrameReady;
        DynamicIsland.LiquidGlass.GlassManager.Instance.Dispose();
        _services.Dispose();

        _hwnd = IntPtr.Zero;
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }

        base.OnClosed(e);
    }
}
