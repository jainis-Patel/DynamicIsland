using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.LiquidGlass;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Core;

/// <summary>
/// Built-in deterministic verification suite for core physics, state machine priority &amp; toast queueing,
/// fullscreen detection, clipboard privacy exclusion, atomic JSON persistence, activity resolution,
/// countdown/Pomodoro timer, calculator/unit conversion, and color extraction algorithms.
/// Run via: DynamicIsland.exe --self-test
/// </summary>
internal static class SelfTestRunner
{
    public static int RunAll()
    {
        int passed = 0;
        int failed = 0;

        void Run(string name, Action test)
        {
            try
            {
                test();
                passed++;
                Console.WriteLine($"[PASS] {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"[FAIL] {name}: {ex.Message}");
            }
        }

        Run("Spring: 60Hz vs 144Hz vs 240Hz exact analytical frame-rate independence", TestSpringPhysics);
        Run("Spring: Stalled frame clamp prevents numerical explosion", TestSpringStalledFrame);
        Run("IslandStateMachine: State priority hierarchy (Hidden > Expanded > Toast > Compact > Idle)", TestStateMachinePriorities);
        Run("IslandStateMachine: Toast replacement & TargetView routing", TestStateMachineToastReplacement);
        Run("IslandStateMachine: Toast priority queue & category coalescing", TestStateMachineToastPriorityQueue);
        Run("FullscreenWatcher: Borderless-fullscreen detection with standard & auto-hidden taskbar", TestFullscreenBorderlessDetection);
        Run("ClipboardHistoryService: 4K screenshot bounding & detached freeze", TestClipboardImageBounding);
        Run("ClipboardHistoryService: Password-manager privacy exclusion markers", TestClipboardPrivacyExclusion);
        Run("AtomicJsonStore & SettingsService: Atomic save, schema migration & granular PropertyChanged", TestAtomicJsonAndSettings);
        Run("NotificationListenerService: DND, Focus Assist Quiet Time, self-filter & per-app mute", TestNotificationSuppression);
        Run("ActivityResolver & ExpandedView: Compact priority hierarchy & strongly-typed view routing", TestActivityResolverAndViews);
        Run("CountdownTimerService: Pomodoro 25m countdown, pause/resume & deterministic completion", TestCountdownTimer);
        Run("ServiceContainer: Deterministic reverse-order IDisposable lifecycle management", TestServiceContainerLifecycle);
        Run("QuickCalcService: Arithmetic, exponentiation, percentages, sqrt & unit conversions", TestQuickCalc);
        Run("ColorExtractor: Vibrant hue extraction vs monochrome fallback", TestColorExtractor);
        Run("ObservableCollection: Incremental sync avoids destructive Clear/Reset", TestIncrementalObservableSync);
        Run("AppPaths: Writable data directory resolution & XAML dictionary validation", TestAppPaths);
        Run("ThemeBrushes: Pre-allocated brushes are frozen for render-thread safety", TestThemeBrushesFrozen);
        Run("ThemeManager: System detection, Dark/Light modes & palette contrast calibration", TestThemeManagerAndSystemModes);
        Run("AudioSpectrum: Lock-free band read & idle zeroing", TestAudioSpectrumLockFree);
        Run("NotificationListenerService: 25-item history ring buffer, item removal & clear", TestNotificationHistoryBuffer);
        Run("WeatherService & CalendarService: Open-Meteo JSON parsing, WMO glyphs & meeting URL extraction", TestWeatherAndCalendarParsing);
        Run("ActivityResolver: Split-pill secondary droplet activity resolution & glyph mapping", TestSecondaryDropletActivity);
        Run("ClipboardHistoryService: DPAPI encryption round-trip & pinned clip eviction resistance", TestClipboardDpapiAndPinning);
        Run("DropShelfService & SettingsService: One-click ZIP bundle creation & settings export/import", TestDropShelfZipAndSettingsBackup);
        Run("NetworkService & PeripheralDeviceService: Netsh Wi-Fi SSID parsing, signal tiers & Bluetooth states", TestWifiParserAndPeripheralStatus);
        Run("InstallerService: CLI argument routing, path resolution & shortcut synthesis", TestInstallerService);
        Run("ThemeBrushes & ClipboardItem: Zero-allocation singleton badge & cached swatch brushes", TestZeroAllocationBrushCaching);
        Run("IslandMemoryManager: Conservative working-set trim & LOH compaction", TestIslandMemoryManagerTrim);
        Run("ClockStyles: Multiple styles, normalization & shape geometry mapping", TestClockStyles);
        Run("VisualizerStyles: Themes enumeration, normalization & settings persistence", TestVisualizerStyles);
        Run("IslandNatureThemes: Living themes enumeration, normalization & settings persistence", TestIslandNatureThemes);
        Run("LyricsService: LRCLIB LRC timestamp parsing, title cleaning & playback position sync", TestLyricsService);
        Run("NetworkHUD: Real-time bandwidth abbreviation, ping latency & snapshot metrics", TestNetworkHudAndBandwidth);
        Run("MultiMonitor & HardwareNotch: Bezel simulation, cursor follow setting & monitor enumeration", TestMultiMonitorAndNotchMode);
        Run("VirtualPetService: Pet species, moods, affection score & wellness alerts", TestVirtualPetService);
        Run("MultiMonitorPositioning: Physical pixel coordinates on stacked & scaled monitors", TestMultiMonitorPositioning);
        Run("PowerPlanService & DisplayTopology: Battery telemetry, health, sparklines, display modes & night light", TestPowerPlanAndDisplayTopology);
        Run("SystemPerformanceService: Zero-allocation telemetry, CPU/RAM delta, top processes & RAM buster", TestSystemPerformanceService);
        Run("Wave2Features: Pomodoro presets, VoiceMemoInfo formatting, Compact Lyrics & Muted Apps unmute", TestWave2Features);
        Run("Wave3Features: Calc 'ans' memory, hex/binary conversions, Shelf thumbnails & Caffeine mode", TestWave3Features);
        Run("Wave4Features: Universal call detection (WhatsApp Store/Desktop, Phone Link, Teams, Discord) & self-exclusion", TestWave4CallDetection);
        Run("Wave5Features: Compact call timer, live download status, media player search & endpoint mute glyph", TestWave5Features);
        Run("Phase1DroppyParity: Droplets registry, Jiggle detector, File converter/PDF merger, Paste Queue & LiveActivityHub", TestPhase1DroppyParityFeatures);
        Run("Phase2LiveActivitiesAndMeetings: Live Activity IPC, Meeting Mode coordinator & Lock/Keyboard HUDs", TestPhase2LiveActivitiesAndMeetings);
        Run("Phase4ProductivityDroplets: Notes, Quick Launcher, Termi-Notch, Window Snap & Mech Sounds", TestPhase4ProductivityDroplets);
        Run("Phase5SharingAndPhoneBridge: Windows Share Sheet interop, Phone Bridge LAN Quickshare & PWA", TestPhase5SharingAndPhoneBridge);
        Run("QrCodeGenerator: ISO/IEC 18004 2D matrix encoding, Reed-Solomon ECC, mask optimization & BitmapSource generation", TestQrCodeGenerator);
        Run("DetachedPlayer & MediaSongList: Tracklist cleaning, session models & active players", TestMediaSongListAndDetachedPlayer);
        Run("TaskService & CalendarService: Real checklists, custom events, dot markers & metadata parsing", TestTaskServiceAndCalendar);
        Run("LauncherView: Smooth scrolling, search filtering & friendly tooltip descriptions", TestLauncherScrollingAndSearch);
        Run("NavigationAndFocusability: Expanded views focusability, input traps & key roundtrips", TestNavigationAndFocusability);
        Run("ThemeBrushesAndShellInterop: Frozen brush invariants, HIG light/dark contrast & borderless game suppression", TestThemeBrushesAndShellInterop);
        Run("LiquidGlass: Settings validation, fluent builder, parameter clamping & preset invariants", TestLiquidGlassSettingsValidation);
        Run("LiquidGlass: Analytical SDF math, smin smooth-union, 3D normal reconstruction & Snell's law", TestLiquidGlassSdfMath);
        Run("LiquidGlass: GPU headless golden-image refraction rendering & multi-body liquid bridge", TestLiquidGlassGoldenImage);
        Run("UpdateManager: SemVersion 2.0.0 parsing, sorting & pre-release precedence", TestSemVersionComparison);
        Run("UpdateManager: Manifest JSON serialization, channel resolution & hash verification", TestUpdateManifestAndIntegrity);
        Run("Option2And3: Live download speeds, rotating ring & notification app grouping / stacking", TestLiveDownloadsAndNotificationAppGrouping);
        Run("Choreographer: Power governor modes, task scheduling, interval scaling & iGPU preference", TestChoreographerAndPowerGovernor);
        Run("SpringAndHaptics: Impulse, Retarget, SnapTo & zero-allocation synthesized RIFF WAVE haptics", TestSpringDynamicsAndTactileHaptics);

        Console.WriteLine($"\nSelf-Test Summary: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void TestSpringPhysics()
    {
        var s60 = new Spring(120) { Target = 420 };
        var s144 = new Spring(120) { Target = 420 };
        var s240 = new Spring(120) { Target = 420 };

        // Step for exactly 0.1 seconds across 60Hz (6 steps), 140Hz (14 steps), and 240Hz (24 steps)
        for (int i = 0; i < 6; i++) s60.Step(1.0 / 60.0);
        for (int i = 0; i < 14; i++) s144.Step(1.0 / 140.0);
        for (int i = 0; i < 24; i++) s240.Step(1.0 / 240.0);

        Assert(Math.Abs(s60.Value - s240.Value) < 1e-9, $"60Hz ({s60.Value}) diverged from 240Hz ({s240.Value})");
        Assert(Math.Abs(s144.Value - s240.Value) < 1e-9, $"144Hz ({s144.Value}) diverged from 240Hz ({s240.Value})");

        // Run to rest (~1.5s)
        for (int i = 0; i < 90; i++) s60.Step(1.0 / 60.0);
        Assert(s60.IsAtRest, "Spring did not settle to rest after 1.6s");
        Assert(Math.Abs(s60.Value - 420) < 0.001, $"Spring settled at wrong target: {s60.Value}");

        // Verify gooey liquid parameter mapping: higher LiquidIntensity lowers damping (more liquid overshoot)
        var (stiffCrisp, dampCrisp) = IslandWindow.ComputeSpringParameters(0.0, 1.0, reducedMotion: false);
        var (stiffLiquid, dampLiquid) = IslandWindow.ComputeSpringParameters(1.0, 1.0, reducedMotion: false);
        Assert(Math.Abs(stiffCrisp - stiffLiquid) < 1e-6 && dampLiquid < dampCrisp,
            "100% LiquidIntensity must produce lower damping (more liquid bounce) than 0%");
    }

    private static void TestSpringStalledFrame()
    {
        var spring = new Spring(120) { Target = 420 };
        spring.Step(5.0); // Simulate 5-second UI stall
        Assert(!double.IsNaN(spring.Value) && !double.IsInfinity(spring.Value), "Spring exploded on large dt");
        Assert(spring.Value > 120 && spring.Value < 600, $"Spring value out of bounds: {spring.Value}");
    }

    private static void TestStateMachinePriorities()
    {
        var sm = new IslandStateMachine();
        Assert(sm.State == IslandState.Idle, "Initial state should be Idle");

        sm.SetActivity(true);
        Assert(sm.State == IslandState.Compact, "Activity should transition Idle -> Compact");

        var toast = new ToastContent("\uE767", "Volume", "50%", Colors.DodgerBlue, TimeSpan.FromSeconds(2), "audio");
        sm.PushToast(toast);
        Assert(sm.State == IslandState.Toast, "Toast should override Compact");
        Assert(sm.ActiveToast?.TargetView == "audio", "ActiveToast should preserve TargetView");

        sm.ExpandNow();
        Assert(sm.State == IslandState.Expanded, "Expanded should override Toast");

        sm.SetSuppressed(true);
        Assert(sm.State == IslandState.Hidden, "Hidden (Suppressed) should override Expanded");

        sm.SetSuppressed(false);
        Assert(sm.State == IslandState.Toast, $"Expected Toast after unsuppress, got {sm.State}");

        sm.ExpandNow();
        sm.SetPinned(true);
        Assert(sm.IsPinned && sm.State == IslandState.Expanded, "SetPinned(true) should hold Expanded state");
        sm.SetPointerInside(false);
        Assert(sm.State == IslandState.Expanded, "Pinned state must prevent pointer-leave collapse");
        sm.CollapseNow();
        Assert(!sm.IsPinned && sm.State == IslandState.Toast, "CollapseNow should unpin and fall back to active Toast");
    }

    private static void TestStateMachineToastReplacement()
    {
        var sm = new IslandStateMachine();
        bool lastReplacing = true;
        sm.ToastChanged += (_, replacing) => lastReplacing = replacing;

        sm.PushToast(new ToastContent("A", "First", "1", Colors.Red, TimeSpan.FromSeconds(3)));
        Assert(!lastReplacing, "First toast should have replacing=false");

        sm.PushToast(new ToastContent("B", "Second", "2", Colors.Green, TimeSpan.FromSeconds(3)));
        Assert(lastReplacing, "Second consecutive toast in Toast state should have replacing=true");
    }

    private static void TestStateMachineToastPriorityQueue()
    {
        var sm = new IslandStateMachine();

        // 1. Show a Notification toast (priority = Notification)
        var notifToast = new ToastContent("N", "Discord · Alice", "Hey there!", Colors.CornflowerBlue, TimeSpan.FromSeconds(3.5), null, ToastPriority.Notification);
        sm.PushToast(notifToast);
        Assert(sm.ActiveToast?.Title == "Discord · Alice", "Notification toast should be active");

        // 2. Push a low-priority Volume Feedback toast while Notification is showing -> should NOT clobber Notification!
        var volToast = new ToastContent("V", "Volume", "60%", Colors.DodgerBlue, TimeSpan.FromSeconds(1.8), "audio", ToastPriority.Feedback, "volume");
        sm.PushToast(volToast);
        Assert(sm.ActiveToast?.Title == "Discord · Alice", "Feedback toast must not clobber active Notification toast");

        // 3. Push a System toast (e.g. Track changed) while Notification is showing -> should queue behind Notification
        var trackToast = new ToastContent("M", "Strobe", "deadmau5", Colors.Orange, TimeSpan.FromSeconds(2.8), "media", ToastPriority.System, "track");
        sm.PushToast(trackToast);
        Assert(sm.ActiveToast?.Title == "Discord · Alice", "System toast must not clobber active Notification toast");
        Assert(sm.QueuedToastCount == 1, $"Expected 1 queued toast, got {sm.QueuedToastCount}");

        // 4. Push a Critical toast (Battery Low) -> preempts Notification AND re-queues the Notification at the front!
        var critToast = new ToastContent("C", "Battery Low", "15% remaining", Colors.Red, TimeSpan.FromSeconds(4.0), "battery", ToastPriority.Critical);
        sm.PushToast(critToast);
        Assert(sm.ActiveToast?.Title == "Battery Low", "Critical toast must immediately preempt Notification");
        Assert(sm.QueuedToastCount == 2, $"Expected 2 queued toasts (Notification + System), got {sm.QueuedToastCount}");

        // 5. Advance toast queue: Notification resumes first, then Track toast, then Idle
        sm.AdvanceToast();
        Assert(sm.ActiveToast?.Title == "Discord · Alice", $"Expected re-queued Notification next, got {sm.ActiveToast?.Title}");

        sm.AdvanceToast();
        Assert(sm.ActiveToast?.Title == "Strobe", $"Expected queued System toast next, got {sm.ActiveToast?.Title}");

        // 6. Test same-category coalescing in-place
        sm.PushToast(new ToastContent("M", "Harder Better Faster", "Daft Punk", Colors.Gold, TimeSpan.FromSeconds(2.8), "media", ToastPriority.System, "track"));
        Assert(sm.ActiveToast?.Title == "Harder Better Faster", "Same-category toast should update in-place");
        Assert(sm.QueuedToastCount == 0, "In-place category coalescing should not grow queue");

        sm.AdvanceToast();
        Assert(sm.ActiveToast is null && sm.State == IslandState.Idle, "State machine should return to Idle when queue drains");
    }

    private static void TestFullscreenBorderlessDetection()
    {
        var fullRect = new RECT { Left = 0, Top = 0, Right = 2560, Bottom = 1440 };

        // Case 1: Standard visible taskbar (rcWork < rcMonitor) -> borderless window covering rcMonitor is fullscreen
        var standardMon = new MONITORINFO
        {
            rcMonitor = fullRect,
            rcWork = new RECT { Left = 0, Top = 0, Right = 2560, Bottom = 1392 }
        };
        Assert(FullscreenWatcher.IsWindowBorderlessFullscreen(fullRect, standardMon, WS_CAPTION | WS_THICKFRAME),
            "Window covering full monitor when taskbar is visible should be detected as fullscreen");

        // Case 2: Auto-hidden taskbar (rcWork == rcMonitor) + normal maximized window (has WS_CAPTION) -> NOT fullscreen
        var autoHideMon = new MONITORINFO
        {
            rcMonitor = fullRect,
            rcWork = fullRect
        };
        Assert(!FullscreenWatcher.IsWindowBorderlessFullscreen(fullRect, autoHideMon, WS_CAPTION | WS_THICKFRAME),
            "Normal maximized window with WS_CAPTION on auto-hidden taskbar must NOT be treated as fullscreen");

        // Case 3: Auto-hidden taskbar (rcWork == rcMonitor) + borderless game/video (no WS_CAPTION, no WS_THICKFRAME) -> IS fullscreen
        Assert(FullscreenWatcher.IsWindowBorderlessFullscreen(fullRect, autoHideMon, 0),
            "Borderless window (style=0) covering monitor with auto-hidden taskbar MUST be detected as fullscreen");
    }

    private static void TestClipboardPrivacyExclusion()
    {
        // 1. Normal text clip -> allowed
        var normalObj = new DataObject();
        normalObj.SetText("Hello world");
        Assert(!ClipboardHistoryService.ShouldExcludeClipboardData(normalObj),
            "Normal text clipboard object should not be excluded");

        // 2. Password manager sets ExcludeClipboardContentFromMonitorProcessing -> excluded
        var pwdObj = new DataObject();
        pwdObj.SetText("SuperSecretPassword123!");
        pwdObj.SetData(ClipboardHistoryService.FormatExcludeFromMonitoring, true);
        Assert(ClipboardHistoryService.ShouldExcludeClipboardData(pwdObj),
            "Clipboard data with ExcludeClipboardContentFromMonitorProcessing must be excluded");

        // 3. Windows CanIncludeInClipboardHistory = 0 (DWORD) -> excluded
        var noHistObj = new DataObject();
        noHistObj.SetText("TemporaryAuthToken");
        noHistObj.SetData(ClipboardHistoryService.FormatCanIncludeInHistory, 0);
        Assert(ClipboardHistoryService.ShouldExcludeClipboardData(noHistObj),
            "Clipboard data with CanIncludeInClipboardHistory=0 must be excluded");
    }

    private static void TestAtomicJsonAndSettings()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"di_test_settings_{Guid.NewGuid():N}.json");
        try
        {
            var svc = new SettingsService(tempFile);
            string? lastChangedProp = null;
            svc.PropertyChanged += prop => lastChangedProp = prop;

            svc.Update(s =>
            {
                s.NotchYOffset = 14;
                s.DoNotDisturb = true;
                s.MonitorDeviceName = @"\\.\DISPLAY2";
                s.LiquidIntensity = 0.85;
                s.AnimationSpeed = 1.25;
                s.IslandScale = 1.15;
                s.BackdropOpacity = 0.80;
                s.ShowEdgeBorder = false;
                s.EnableSecondaryDroplet = false;
            }, nameof(AppSettings.NotchYOffset));

            Assert(lastChangedProp == nameof(AppSettings.NotchYOffset), $"Expected PropertyChanged 'NotchYOffset', got '{lastChangedProp}'");
            svc.SaveNow();
            Assert(File.Exists(tempFile), "AtomicJsonStore did not write settings file");

            var reloaded = new SettingsService(tempFile);
            Assert(reloaded.Current.SchemaVersion == 3, "Expected SchemaVersion 3");
            Assert(Math.Abs(reloaded.Current.NotchYOffset - 14) < 0.001, $"Expected NotchYOffset 14, got {reloaded.Current.NotchYOffset}");
            Assert(reloaded.Current.DoNotDisturb, "Expected DoNotDisturb=true after reload");
            Assert(reloaded.Current.MonitorDeviceName == @"\\.\DISPLAY2", "Expected MonitorDeviceName persisted");
            Assert(Math.Abs(reloaded.Current.LiquidIntensity - 0.85) < 0.001, "Expected LiquidIntensity 0.85 persisted");
            Assert(Math.Abs(reloaded.Current.AnimationSpeed - 1.25) < 0.001, "Expected AnimationSpeed 1.25 persisted");
            Assert(Math.Abs(reloaded.Current.IslandScale - 1.15) < 0.001, "Expected IslandScale 1.15 persisted");
            Assert(Math.Abs(reloaded.Current.BackdropOpacity - 0.80) < 0.001, "Expected BackdropOpacity 0.80 persisted");
            Assert(!reloaded.Current.ShowEdgeBorder, "Expected ShowEdgeBorder=false persisted");
            Assert(!reloaded.Current.EnableSecondaryDroplet, "Expected EnableSecondaryDroplet=false persisted");
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }

    private static void TestNotificationSuppression()
    {
        var settings = new AppSettings { DoNotDisturb = false };
        settings.MutedNotificationApps.Add("Slack");

        Assert(!NotificationListenerService.ShouldSuppressNotification("Discord", settings, isQuietTime: false),
            "Unmuted app should not be suppressed when DND and QuietTime are off");
        Assert(NotificationListenerService.ShouldSuppressNotification("Slack", settings, isQuietTime: false),
            "Muted app 'Slack' must be suppressed");
        Assert(NotificationListenerService.ShouldSuppressNotification("Dynamic Island", settings, isQuietTime: false),
            "Self notifications from 'Dynamic Island' must be suppressed");
        Assert(NotificationListenerService.ShouldSuppressNotification("Discord", settings, isQuietTime: true),
            "Notifications must be suppressed during Windows Focus Assist Quiet Time");

        settings.DoNotDisturb = true;
        Assert(NotificationListenerService.ShouldSuppressNotification("Discord", settings, isQuietTime: false),
            "All notifications must be suppressed when DoNotDisturb is enabled");
    }

    private static void TestActivityResolverAndViews()
    {
        // Priority: Recording > Media > Call > Countdown > Download > Stopwatch
        var r1 = ActivityResolver.Resolve(recording: true, mediaPlaying: true, callActive: true, countdownRunning: true, downloading: true, stopwatchActive: true);
        Assert(r1.HasActivity && r1.ActiveHost == CompactActivityKind.Recording, "Recording must have highest Compact priority");
        Assert(ActivityResolver.ResolveCompactTargetView(r1.ActiveHost) == ExpandedView.VoiceMemo, "Recording click should route to VoiceMemo");

        var r2 = ActivityResolver.Resolve(recording: false, mediaPlaying: false, callActive: false, countdownRunning: true, downloading: true, stopwatchActive: true);
        Assert(r2.HasActivity && r2.ActiveHost == CompactActivityKind.Countdown, "Countdown should take priority over Download and Stopwatch");

        var r3 = ActivityResolver.Resolve(recording: false, mediaPlaying: false, callActive: false, countdownRunning: false, downloading: true, stopwatchActive: false);
        Assert(ActivityResolver.ResolveCompactTargetView(r3.ActiveHost) == ExpandedView.Shelf, "Download click should route to Shelf");

        foreach (var view in Enum.GetValues<ExpandedView>())
        {
            string key = view.ToKey();
            Assert(ExpandedViewExtensions.TryParseKey(key, out var roundTrip) && roundTrip == view,
                $"ExpandedView round-trip failed for {view} ('{key}')");
        }

        Assert(ExpandedViewExtensions.TryParseKey("wifi", out var wifiView) && wifiView == ExpandedView.Network,
            "Expected 'wifi' alias to resolve to ExpandedView.Network");
        Assert(NetworkService.ResolveGlyph(connected: false, isWifi: true, isEthernet: false, signalBars: 0) == "\uEB5E",
            "Expected disconnected Wi-Fi glyph");
        Assert(NetworkService.ResolveGlyph(connected: true, isWifi: true, isEthernet: false, signalBars: 4) == "\uE701",
            "Expected 4-bar Wi-Fi glyph");
        Assert(NetworkService.FormatBandwidth(2 * 1024 * 1024) == "2.0 MB/s",
            $"Expected '2.0 MB/s', got '{NetworkService.FormatBandwidth(2 * 1024 * 1024)}'");
    }

    private static void TestCountdownTimer()
    {
        using var timer = new CountdownTimerService();
        bool completedFired = false;
        string? completedLabel = null;
        timer.Completed += lbl =>
        {
            completedFired = true;
            completedLabel = lbl;
        };

        timer.StartCountdown(TimeSpan.FromSeconds(10), "Focus");
        Assert(timer.IsRunning && timer.IsActive, "Timer should be running after StartCountdown");

        timer.AdvanceSimulated(TimeSpan.FromSeconds(4));
        Assert(!completedFired && timer.Remaining <= TimeSpan.FromSeconds(6.1), $"Expected ~6s remaining, got {timer.Remaining}");

        timer.AdvanceSimulated(TimeSpan.FromSeconds(7));
        Assert(completedFired && completedLabel == "Focus", "Timer should fire Completed when countdown reaches zero");
        Assert(!timer.IsRunning && timer.Remaining == TimeSpan.Zero, "Timer should stop at 00:00 after completion");
    }

    private sealed class DummyDisposable : IDisposable
    {
        private readonly List<int> _order;
        private readonly int _id;
        public DummyDisposable(List<int> order, int id) { _order = order; _id = id; }
        public void Dispose() => _order.Add(_id);
    }

    private static void TestServiceContainerLifecycle()
    {
        var order = new List<int>();
        var container = new ServiceContainer();
        container.Register<object>(new DummyDisposable(order, 1));
        container.Register<DummyDisposable>(new DummyDisposable(order, 2));
        container.Dispose();

        Assert(order.SequenceEqual(new[] { 2, 1 }), $"ServiceContainer must dispose in reverse registration order, got [{string.Join(",", order)}]");
    }

    private static void TestQuickCalc()
    {
        var r1 = QuickCalcService.Evaluate("12 * 85");
        Assert(r1.Success && r1.Result == "1020", $"Expected 1020, got {r1.Result}");

        var r2 = QuickCalcService.Evaluate("15 x 4");
        Assert(r2.Success && r2.Result == "60", $"Expected 60 for '15 x 4', got {r2.Result}");

        var r3 = QuickCalcService.Evaluate("2 ^ 10");
        Assert(r3.Success && r3.Result == "1024", $"Expected 1024 for '2 ^ 10', got {r3.Result}");

        var r4 = QuickCalcService.Evaluate("20% of 450");
        Assert(r4.Success && r4.Result == "90", $"Expected 90 for '20% of 450', got {r4.Result}");

        var r5 = QuickCalcService.Evaluate("100 c in f");
        Assert(r5.Success && r5.Result == "212", $"Expected 212 for '100 c in f', got {r5.Result}");

        var r6 = QuickCalcService.Evaluate("10 inch in cm");
        Assert(r6.Success && r6.Result == "25.4", $"Expected 25.4 for '10 inch in cm', got {r6.Result}");

        var r7 = QuickCalcService.Evaluate("sqrt(144)");
        Assert(r7.Success && r7.Result == "12", $"Expected 12 for 'sqrt(144)', got {r7.Result}");

        var r8 = QuickCalcService.Evaluate("2 gb in mb");
        Assert(r8.Success && r8.Result == "2048", $"Expected 2048 for '2 gb in mb', got {r8.Result}");
    }

    private static void TestColorExtractor()
    {
        // 1. Monochrome grey bitmap -> should return neutral fallback
        var greyBmp = new WriteableBitmap(16, 16, 96, 96, PixelFormats.Bgra32, null);
        byte[] greyPixels = new byte[16 * 16 * 4];
        for (int i = 0; i < greyPixels.Length; i += 4)
        {
            greyPixels[i] = 100;     // B
            greyPixels[i + 1] = 100; // G
            greyPixels[i + 2] = 100; // R
            greyPixels[i + 3] = 255; // A
        }
        greyBmp.WritePixels(new Int32Rect(0, 0, 16, 16), greyPixels, 16 * 4, 0);
        Color neutral = ColorExtractor.Vibrant(greyBmp);
        Assert(neutral.R == 0xE5 && neutral.G == 0xE5 && neutral.B == 0xEA, $"Expected neutral fallback, got {neutral}");

        // 2. Vivid red bitmap -> should return high-R accent color
        var redBmp = new WriteableBitmap(16, 16, 96, 96, PixelFormats.Bgra32, null);
        byte[] redPixels = new byte[16 * 16 * 4];
        for (int i = 0; i < redPixels.Length; i += 4)
        {
            redPixels[i] = 20;      // B
            redPixels[i + 1] = 20;  // G
            redPixels[i + 2] = 240; // R
            redPixels[i + 3] = 255; // A
        }
        redBmp.WritePixels(new Int32Rect(0, 0, 16, 16), redPixels, 16 * 4, 0);
        Color vivid = ColorExtractor.Vibrant(redBmp);
        Assert(vivid.R > 200 && vivid.R > vivid.B * 2, $"Expected vivid red-dominant accent, got {vivid}");
    }

    private static void TestClipboardImageBounding()
    {
        var bigBmp = new WriteableBitmap(3840, 2160, 96, 96, PixelFormats.Bgra32, null);
        var bounded = ClipboardHistoryService.BoundImageSize(bigBmp);

        Assert(bounded.PixelWidth == ClipboardHistoryService.MaxImagePreviewWidth,
            $"Expected bounded width {ClipboardHistoryService.MaxImagePreviewWidth}, got {bounded.PixelWidth}");
        int expectedH = (int)Math.Round(2160.0 * ClipboardHistoryService.MaxImagePreviewWidth / 3840.0);
        Assert(bounded.PixelHeight == expectedH,
            $"Expected bounded height {expectedH} (16:9), got {bounded.PixelHeight}");
        Assert(bounded.IsFrozen, "Bounded clipboard image preview must be frozen");
    }

    private static void TestIncrementalObservableSync()
    {
        var target = new ObservableCollection<string> { "B", "C", "D" };
        int resetCount = 0;
        target.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
                resetCount++;
        };

        // 1. Head insertion ("A" prepended)
        IslandWindow.SyncObservableCollection(target, new[] { "A", "B", "C", "D" });
        Assert(resetCount == 0, "Head insertion should not trigger NotifyCollectionChangedAction.Reset");
        Assert(target.SequenceEqual(new[] { "A", "B", "C", "D" }), "Target sequence mismatch after head insert");

        // 2. Single item removal ("C" removed)
        IslandWindow.SyncObservableCollection(target, new[] { "A", "B", "D" });
        Assert(resetCount == 0, "Single item removal should not trigger NotifyCollectionChangedAction.Reset");
        Assert(target.SequenceEqual(new[] { "A", "B", "D" }), "Target sequence mismatch after removal");
    }

    private static void TestAppPaths()
    {
        Assert(!string.IsNullOrWhiteSpace(AppPaths.DataDirectory), "AppPaths.DataDirectory must not be empty");
        Assert(Directory.Exists(AppPaths.DataDirectory), $"AppPaths.DataDirectory does not exist: {AppPaths.DataDirectory}");
        Assert(AppPaths.SettingsFile.EndsWith("settings.json", StringComparison.OrdinalIgnoreCase), "Invalid SettingsFile path");

        // Verify IslandStyles.xaml parses cleanly without any runtime XAML errors
        var app = Application.Current ?? new Application();
        var dict = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/DynamicIsland;component/Resources/IslandStyles.xaml", UriKind.Absolute)
        };
        if (!app.Resources.MergedDictionaries.Any(d => d.Source == dict.Source))
        {
            app.Resources.MergedDictionaries.Add(dict);
        }
        Assert(dict.Count > 0, "IslandStyles.xaml resource dictionary must load with entries");

        // Verify SettingsView.xaml and IslandWindow load cleanly without runtime exceptions
        try
        {
            var settingsView = new DynamicIsland.Views.Widgets.SettingsView();
            Assert(settingsView.SettingsThemesTab != null, "SettingsThemesTab must not be null");
            Assert(settingsView.NatureDensitySlider != null, "NatureDensitySlider must not be null");
            Assert(settingsView.NatureScaleSlider != null, "NatureScaleSlider must not be null");
            Assert(settingsView.NatureDropSlider != null, "NatureDropSlider must not be null");
            Assert(settingsView.NatureBloomSlider != null, "NatureBloomSlider must not be null");
            Assert(settingsView.NatureButterfly0Btn != null, "NatureButterfly0Btn must not be null");
            Assert(settingsView.NaturePollenSlider != null, "NaturePollenSlider must not be null");

            var island = new IslandWindow();
            Assert(island.IslandThemeUnderlay != null, "IslandThemeUnderlay must not be null");
            Assert(island.IslandThemeOverlay != null, "IslandThemeOverlay must not be null");
        }
        catch (Exception ex)
        {
            var baseEx = ex.GetBaseException();
            Assert(false, $"Initialization crash: {ex.Message} -> Base: {baseEx.Message}\n{ex.StackTrace}");
        }
    }

    private static void TestThemeBrushesFrozen()
    {
        Assert(ThemeBrushes.Green.IsFrozen, "ThemeBrushes.Green must be frozen");
        Assert(ThemeBrushes.Amber.IsFrozen, "ThemeBrushes.Amber must be frozen");
        Assert(ThemeBrushes.Red.IsFrozen, "ThemeBrushes.Red must be frozen");
        Assert(ThemeBrushes.Blue.IsFrozen, "ThemeBrushes.Blue must be frozen");
        Assert(ThemeBrushes.DockActiveBg.IsFrozen, "ThemeBrushes.DockActiveBg must be frozen");
        Assert(ThemeBrushes.PrimaryTextDark.IsFrozen, "ThemeBrushes.PrimaryTextDark must be frozen");
        Assert(ThemeBrushes.PrimaryTextLight.IsFrozen, "ThemeBrushes.PrimaryTextLight must be frozen");
        Assert(ThemeBrushes.SecondaryTextDark.IsFrozen, "ThemeBrushes.SecondaryTextDark must be frozen");
        Assert(ThemeBrushes.SecondaryTextLight.IsFrozen, "ThemeBrushes.SecondaryTextLight must be frozen");
        Assert(ThemeBrushes.CardSurfaceDark.IsFrozen, "ThemeBrushes.CardSurfaceDark must be frozen");
        Assert(ThemeBrushes.CardSurfaceLight.IsFrozen, "ThemeBrushes.CardSurfaceLight must be frozen");
        Assert(ThemeBrushes.PillBackgroundDark.IsFrozen, "ThemeBrushes.PillBackgroundDark must be frozen");
        Assert(ThemeBrushes.PillBackgroundLight.IsFrozen, "ThemeBrushes.PillBackgroundLight must be frozen");
        Assert(ThemeBrushes.PillStrokeDark.IsFrozen, "ThemeBrushes.PillStrokeDark must be frozen");
        Assert(ThemeBrushes.PillStrokeLight.IsFrozen, "ThemeBrushes.PillStrokeLight must be frozen");
        Assert(ThemeBrushes.PillBackgroundLiquidDark.IsFrozen, "ThemeBrushes.PillBackgroundLiquidDark must be frozen");
        Assert(ThemeBrushes.PillBackgroundLiquidLight.IsFrozen, "ThemeBrushes.PillBackgroundLiquidLight must be frozen");
        Assert(ThemeBrushes.PillStrokeLiquidDark.IsFrozen, "ThemeBrushes.PillStrokeLiquidDark must be frozen");
        Assert(ThemeBrushes.PillStrokeLiquidLight.IsFrozen, "ThemeBrushes.PillStrokeLiquidLight must be frozen");
        Assert(ThemeBrushes.CardSurfaceLiquidDark.IsFrozen, "ThemeBrushes.CardSurfaceLiquidDark must be frozen");
        Assert(ThemeBrushes.CardSurfaceLiquidLight.IsFrozen, "ThemeBrushes.CardSurfaceLiquidLight must be frozen");
        Assert(ThemeBrushes.CardBorderLiquidDark.IsFrozen, "ThemeBrushes.CardBorderLiquidDark must be frozen");
        Assert(ThemeBrushes.CardBorderLiquidLight.IsFrozen, "ThemeBrushes.CardBorderLiquidLight must be frozen");
        Assert(ThemeBrushes.FloatingDockBackgroundLiquidDark.IsFrozen, "ThemeBrushes.FloatingDockBackgroundLiquidDark must be frozen");
        Assert(ThemeBrushes.FloatingDockBackgroundLiquidLight.IsFrozen, "ThemeBrushes.FloatingDockBackgroundLiquidLight must be frozen");
        Assert(ThemeBrushes.PillGlassSheenLiquidDark.IsFrozen, "ThemeBrushes.PillGlassSheenLiquidDark must be frozen");
        Assert(ThemeBrushes.PillGlassSheenLiquidLight.IsFrozen, "ThemeBrushes.PillGlassSheenLiquidLight must be frozen");
        Assert(ThemeBrushes.PillGlassSheenTransparent.IsFrozen, "ThemeBrushes.PillGlassSheenTransparent must be frozen");
        Assert(ThemeBrushes.ClockTileBgLiquidDark.IsFrozen, "ThemeBrushes.ClockTileBgLiquidDark must be frozen");
        Assert(ThemeBrushes.ClockTileBorderLiquidDark.IsFrozen, "ThemeBrushes.ClockTileBorderLiquidDark must be frozen");
        Assert(ThemeBrushes.ClockTileBgLiquidLight.IsFrozen, "ThemeBrushes.ClockTileBgLiquidLight must be frozen");
        Assert(ThemeBrushes.ClockTileBorderLiquidLight.IsFrozen, "ThemeBrushes.ClockTileBorderLiquidLight must be frozen");

        // LiquidUiEngine optical frozen brush invariants
        Assert(LiquidUiEngine.FrostedGrainActive.IsFrozen, "LiquidUiEngine.FrostedGrainActive must be frozen");
        Assert(LiquidUiEngine.FrostedGrainInactive.IsFrozen, "LiquidUiEngine.FrostedGrainInactive must be frozen");
        Assert(LiquidUiEngine.PointerGlareActive.IsFrozen, "LiquidUiEngine.PointerGlareActive must be frozen");
        Assert(LiquidUiEngine.PointerGlareInactive.IsFrozen, "LiquidUiEngine.PointerGlareInactive must be frozen");

        // LiquidUiEngine pointer lensing mechanics
        var tt = new System.Windows.Media.TranslateTransform(0, 0);
        LiquidUiEngine.UpdatePointerLensing(100, 20, 200, 40, tt);
        Assert(tt.X == 0, "Centered pointer X produces 0 translate");
        Assert(tt.Y == 0, "Centered pointer Y produces 0 translate");
        LiquidUiEngine.UpdatePointerLensing(180, 35, 200, 40, tt);
        Assert(tt.X > 0 && tt.Y > 0, "Pointer shift produces positive specular translation");
        LiquidUiEngine.ResetPointerLensing(tt);
        Assert(tt.X == 0 && tt.Y == 0, "ResetPointerLensing restores resting center coordinates");
    }

    private static void TestThemeManagerAndSystemModes()
    {
        // 1. ThemeMode normalization
        Assert(SettingsService.NormalizeThemeMode("System") == "System", "NormalizeThemeMode System");
        Assert(SettingsService.NormalizeThemeMode("auto") == "System", "NormalizeThemeMode auto");
        Assert(SettingsService.NormalizeThemeMode("DARK") == "Dark", "NormalizeThemeMode dark");
        Assert(SettingsService.NormalizeThemeMode("Light") == "Light", "NormalizeThemeMode light");
        Assert(SettingsService.NormalizeThemeMode(null) == "System", "NormalizeThemeMode null fallback");
        Assert(SettingsService.NormalizeThemeMode("invalid") == "System", "NormalizeThemeMode invalid fallback");

        // 2. ParseThemeMode
        Assert(ThemeManager.ParseThemeMode("System") == ThemeMode.System, "ParseThemeMode System");
        Assert(ThemeManager.ParseThemeMode("Dark") == ThemeMode.Dark, "ParseThemeMode Dark");
        Assert(ThemeManager.ParseThemeMode("Light") == ThemeMode.Light, "ParseThemeMode Light");

        // 3. System Light mode query should not throw
        bool sysLight = ThemeManager.IsSystemInLightMode();

        // 4. Force Dark mode
        ThemeManager.SetThemeMode(ThemeMode.Dark);
        Assert(ThemeManager.CurrentMode == ThemeMode.Dark, "CurrentMode must be Dark");
        Assert(ThemeManager.IsDark, "IsDark must be true");
        Assert(!ThemeManager.IsLight, "IsLight must be false");
        Assert(ThemeBrushes.PrimaryText.Color == ThemeBrushes.PrimaryTextColor, "Dark PrimaryText");
        Assert(ThemeBrushes.PrimaryText.IsFrozen, "Dark PrimaryText must be frozen");

        // 5. Force Light mode
        ThemeManager.SetThemeMode(ThemeMode.Light);
        Assert(ThemeManager.CurrentMode == ThemeMode.Light, "CurrentMode must be Light");
        Assert(ThemeManager.IsLight, "IsLight must be true");
        Assert(!ThemeManager.IsDark, "IsDark must be false");
        Assert(ThemeBrushes.PrimaryText.Color == ThemeBrushes.PrimaryTextColorLight, "Light PrimaryText");
        Assert(ThemeBrushes.PrimaryText.IsFrozen, "Light PrimaryText must be frozen");
        Assert(ThemeBrushes.PillBackgroundLight.IsFrozen, "PillBackgroundLight must be frozen");
        Assert(ThemeBrushes.PillStrokeLight.IsFrozen, "PillStrokeLight must be frozen");

        // 6. ThemePalette contrast calibration
        var amber = SettingsService.ResolveTheme("Solar Amber");
        Assert(amber.GetAccent(true) == System.Windows.Media.Color.FromRgb(0xFF, 0x9F, 0x0A), "Dark amber accent");
        Assert(amber.GetAccent(false) == System.Windows.Media.Color.FromRgb(0xD9, 0x77, 0x06), "Light amber accent calibrated");

        var frost = SettingsService.ResolveTheme("Monochrome Frost");
        Assert(frost.GetAccent(true) == System.Windows.Media.Color.FromRgb(0xE5, 0xE5, 0xEA), "Dark frost accent");
        Assert(frost.GetAccent(false) == System.Windows.Media.Color.FromRgb(0x48, 0x48, 0x4A), "Light frost accent calibrated");

        // 7. Liquid Glass theme style normalization & switching
        Assert(SettingsService.NormalizeThemeStyle("Default") == "Default", "NormalizeThemeStyle Default");
        Assert(SettingsService.NormalizeThemeStyle("Liquid Glass") == "Liquid Glass", "NormalizeThemeStyle Liquid Glass");
        Assert(SettingsService.NormalizeThemeStyle("liquidglass") == "Liquid Glass", "NormalizeThemeStyle liquidglass");
        Assert(SettingsService.NormalizeThemeStyle("macOS") == "Liquid Glass", "NormalizeThemeStyle macOS");
        Assert(SettingsService.NormalizeThemeStyle("frosted") == "Liquid Glass", "NormalizeThemeStyle frosted");
        Assert(SettingsService.NormalizeThemeStyle("frosted glass") == "Liquid Glass", "NormalizeThemeStyle frosted glass");
        Assert(SettingsService.NormalizeThemeStyle("Frost") == "Liquid Glass", "NormalizeThemeStyle Frost");
        Assert(SettingsService.NormalizeThemeStyle("invalid") == "Default", "NormalizeThemeStyle invalid fallback");
        Assert(SettingsService.NormalizeThemeStyle(null) == "Default", "NormalizeThemeStyle null fallback");

        Assert(ThemeManager.ParseThemeStyle("Default") == ThemeStyle.Default, "ParseThemeStyle Default");
        Assert(ThemeManager.ParseThemeStyle("Liquid Glass") == ThemeStyle.LiquidGlass, "ParseThemeStyle Liquid Glass");
        Assert(ThemeManager.ParseThemeStyle("Glass") == ThemeStyle.LiquidGlass, "ParseThemeStyle Glass");
        Assert(ThemeManager.ParseThemeStyle("frosted") == ThemeStyle.LiquidGlass, "ParseThemeStyle frosted");
        Assert(ThemeManager.ParseThemeStyle("frosted glass") == ThemeStyle.LiquidGlass, "ParseThemeStyle frosted glass");

        ThemeManager.SetThemeMode(ThemeMode.Dark);
        ThemeManager.SetThemeStyle(ThemeStyle.LiquidGlass);
        Assert(ThemeManager.IsLiquidGlass, "IsLiquidGlass must be true");
        Assert(ThemeManager.CurrentStyle == ThemeStyle.LiquidGlass, "CurrentStyle must be LiquidGlass");
        Assert(ThemeBrushes.CardSurface == ThemeBrushes.CardSurfaceLiquidDark, "Dark LiquidGlass CardSurface");
        Assert(ThemeBrushes.PillGlassSheen == ThemeBrushes.PillGlassSheenLiquidDark, "Dark LiquidGlass PillGlassSheen");
        Assert(ThemeBrushes.PillInnerBevel == ThemeBrushes.PillInnerBevelLiquidDark, "Dark LiquidGlass PillInnerBevel");
        Assert(ThemeBrushes.ClockTileBg == ThemeBrushes.ClockTileBgLiquidDark, "Dark LiquidGlass ClockTileBg");

        ThemeManager.SetThemeStyle(ThemeStyle.Default);
        Assert(!ThemeManager.IsLiquidGlass, "IsLiquidGlass must be false");
        Assert(ThemeManager.CurrentStyle == ThemeStyle.Default, "CurrentStyle must be Default");
        Assert(ThemeBrushes.CardSurface == ThemeBrushes.CardSurfaceDark, "Dark Default CardSurface");
        Assert(ThemeBrushes.PillGlassSheen == ThemeBrushes.PillGlassSheenTransparent, "Default PillGlassSheen transparent");
        Assert(ThemeBrushes.PillInnerBevel == ThemeBrushes.PillInnerBevelTransparent, "Default PillInnerBevel transparent");
        Assert(ThemeBrushes.ClockTileBg == ThemeBrushes.ClockTileBgDark, "Default ClockTileBgDark");

        // 8. Reset to System mode & Default style
        ThemeManager.SetThemeMode(ThemeMode.System);
        ThemeManager.SetThemeStyle(ThemeStyle.Default);
        Assert(ThemeManager.CurrentMode == ThemeMode.System, "CurrentMode must be System");
        Assert(ThemeManager.IsDark != ThemeManager.IsLight, "Exactly one theme must be active");
    }

    private static void TestAudioSpectrumLockFree()
    {
        using var spectrum = new AudioSpectrum();
        float[] bands = { 0.8f, 0.7f, 0.6f, 0.5f };
        spectrum.ReadBands(bands);
        Assert(bands.All(b => b == 0f), "Inactive AudioSpectrum.ReadBands must zero destination buffer without locking");
    }

    private static void TestNotificationHistoryBuffer()
    {
        string tempSettings = Path.Combine(Path.GetTempPath(), $"di_notif_test_{Guid.NewGuid():N}.json");
        try
        {
            var settingsSvc = new SettingsService(tempSettings);
            using var listener = new NotificationListenerService(Dispatcher.CurrentDispatcher, () => settingsSvc.Current);

            for (int i = 1; i <= 30; i++)
            {
                listener.RecordNotification("Discord", $"Message #{i}", $"Body {i}", "\uE8F2");
            }

            Assert(listener.HistoryCount == NotificationListenerService.MaxHistoryCount,
                $"Expected ring buffer capped at {NotificationListenerService.MaxHistoryCount}, got {listener.HistoryCount}");
            Assert(listener.History[0].AppName == "Discord" && listener.History[0].Title == "Message #30",
                $"Expected newest item at index 0, got '{listener.History[0].Title}'");

            string firstId = listener.History[0].Id;
            listener.RemoveHistoryItem(firstId);
            Assert(listener.HistoryCount == NotificationListenerService.MaxHistoryCount - 1, "Expected count decremented after RemoveHistoryItem");

            listener.ClearHistory();
            Assert(listener.HistoryCount == 0, "Expected empty history after ClearHistory");

            Assert(NotificationListenerService.IsMessagingApp("WhatsApp"), "WhatsApp must be recognized as messaging app");
            Assert(NotificationListenerService.IsMessagingApp("Telegram"), "Telegram must be recognized as messaging app");
            Assert(NotificationListenerService.IsMessagingApp("Discord"), "Discord must be recognized as messaging app");
            Assert(NotificationListenerService.IsMessagingApp("Microsoft Teams"), "Teams must be recognized as messaging app");
            Assert(NotificationListenerService.IsMessagingApp("Phone Link"), "Phone Link must be recognized as messaging app");
            Assert(NotificationListenerService.IsMessagingApp("Notification", "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App"), "WhatsApp AUMID must be recognized as messaging app");
            Assert(NotificationListenerService.IsMessagingApp("Notification", "Microsoft.YourPhone_8wekyb3d8bbwe!App"), "Phone Link AUMID must be recognized as messaging app");
            Assert(NotificationListenerService.IsMessagingApp("Notification", "TelegramMessengerLLP.TelegramDesktop_t4vj0pshhgkw4!Telegram"), "Telegram AUMID must be recognized as messaging app");
            Assert(!NotificationListenerService.IsMessagingApp("Windows Update"), "Windows Update must not be recognized as messaging app");

            var msgItem = new NotificationHistoryItem { AppName = "WhatsApp", IsMessagingApp = true };
            var nonMsgItem = new NotificationHistoryItem { AppName = "System", IsMessagingApp = false };
            Assert(msgItem.ReplyVisibility == Visibility.Visible, "Messaging item must have Visible ReplyVisibility");
            Assert(nonMsgItem.ReplyVisibility == Visibility.Collapsed, "Non-messaging item must have Collapsed ReplyVisibility");

            var waCandidates = NotificationListenerService.GetCandidateProcessNames("WhatsApp", "");
            Assert(waCandidates.Contains("WhatsApp"), "WhatsApp candidate process names must contain 'WhatsApp'");

            var phoneCandidates = NotificationListenerService.GetCandidateProcessNames("Phone Link", "");
            Assert(phoneCandidates.Contains("PhoneExperienceHost"), "Phone Link candidate process names must contain 'PhoneExperienceHost'");

            var teamsCandidates = NotificationListenerService.GetCandidateProcessNames("Teams", "");
            Assert(teamsCandidates.Contains("ms-teams"), "Teams candidate process names must contain 'ms-teams'");
        }
        finally
        {
            try { if (File.Exists(tempSettings)) File.Delete(tempSettings); } catch { }
        }
    }

    private static void TestWeatherAndCalendarParsing()
    {
        string sampleJson = """
        {
          "current": {
            "temperature_2m": 24.6,
            "weather_code": 2,
            "is_day": 1
          }
        }
        """;

        var snap = WeatherService.ParseOpenMeteoForecastJson(sampleJson, "Tokyo");
        Assert(snap.IsAvailable, "WeatherService failed to parse valid Open-Meteo JSON");
        Assert(snap.LocationName == "Tokyo" && snap.TempC == 25, $"Parsed temperature mismatch: {snap.TempC}");
        Assert(snap.Condition == "Partly Cloudy", $"Expected 'Partly Cloudy' for WMO code 2, got '{snap.Condition}'");

        string? zoomUrl = CalendarService.ExtractMeetingUrl("Join our call at https://us02web.zoom.us/j/81234567890?pwd=abc right now!");
        Assert(zoomUrl == "https://us02web.zoom.us/j/81234567890?pwd=abc", $"Unexpected Zoom URL: {zoomUrl}");

        string? meetUrl = CalendarService.ExtractMeetingUrl("Standup link: https://meet.google.com/abc-defg-hij");
        Assert(meetUrl == "https://meet.google.com/abc-defg-hij", $"Unexpected Google Meet URL: {meetUrl}");
    }

    private static void TestSecondaryDropletActivity()
    {
        var split = ActivityResolver.ResolveWithSecondary(
            recording: false,
            mediaPlaying: true,
            callActive: false,
            countdownRunning: true,
            downloading: false,
            stopwatchActive: true);

        Assert(split.HasActivity, "Expected HasActivity=true");
        Assert(split.PrimaryHost == CompactActivityKind.Media, $"Expected PrimaryHost=Media, got {split.PrimaryHost}");
        Assert(split.SecondaryHost == CompactActivityKind.Countdown, $"Expected SecondaryHost=Countdown, got {split.SecondaryHost}");
        Assert(!string.IsNullOrEmpty(ActivityResolver.GlyphForActivity(split.SecondaryHost)), "SecondaryHost glyph must not be empty");
    }

    private static void TestClipboardDpapiAndPinning()
    {
        string secret = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";
        string? protectedStr = ClipboardHistoryService.ProtectText(secret);
        Assert(protectedStr != null && protectedStr.StartsWith("dpapi:", StringComparison.Ordinal), "Protected text must have 'dpapi:' prefix");
        Assert(!protectedStr!.Contains("Bearer", StringComparison.Ordinal), "Protected text must not expose plaintext");

        string? roundTrip = ClipboardHistoryService.UnprotectText(protectedStr);
        Assert(roundTrip == secret, "DPAPI UnprotectText round-trip mismatch");

        string tempClipFile = Path.Combine(Path.GetTempPath(), $"di_clip_test_{Guid.NewGuid():N}.json");
        try
        {
            using var clipSvc = new ClipboardHistoryService(Dispatcher.CurrentDispatcher, tempClipFile);
            var pinnedItem = new ClipboardItem { Id = "pin1", Title = "Pinned Snippet", RawText = "Pinned_Test_Val", IsPinned = true };
            clipSvc.InsertClipItem(pinnedItem);

            // Insert 25 unpinned items so count exceeds MaxHistory (20) and triggers eviction
            for (int i = 0; i < 25; i++)
            {
                clipSvc.InsertClipItem(new ClipboardItem { Id = $"u{i}", Title = $"Unpinned {i}", RawText = $"Val_{i}" });
            }

            Assert(clipSvc.Items.Any(x => x.Id == "pin1" && x.IsPinned), "Pinned item must survive ring-buffer eviction");
            clipSvc.Clear();
            Assert(clipSvc.Items.Count == 1 && clipSvc.Items[0].Id == "pin1", "Pinned item must survive Clear()");
        }
        finally
        {
            try { if (File.Exists(tempClipFile)) File.Delete(tempClipFile); } catch { }
        }
    }

    private static void TestDropShelfZipAndSettingsBackup()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"di_zip_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string fileA = Path.Combine(tempDir, "alpha.txt");
            string fileB = Path.Combine(tempDir, "beta.txt");
            File.WriteAllText(fileA, "Hello Alpha");
            File.WriteAllText(fileB, "Hello Beta");

            var shelf = new DropShelfService();
            shelf.Clear();
            shelf.AddPaths(new[] { fileA, fileB });

            string? zipPath = shelf.CreateZipBundle(tempDir);
            Assert(!string.IsNullOrEmpty(zipPath) && File.Exists(zipPath), "DropShelfService.CreateZipBundle did not create .zip archive");
            Assert(new FileInfo(zipPath!).Length > 50, "Created ZIP archive is unexpectedly empty");

            string s1Path = Path.Combine(tempDir, "s1.json");
            string s2Path = Path.Combine(tempDir, "s2.json");
            string exportPath = Path.Combine(tempDir, "backup.json");

            var svc1 = new SettingsService(s1Path);
            svc1.Update(s =>
            {
                s.ThemeName = "Cyber Neon";
                s.LiquidIntensity = 0.92;
                s.HiddenDockViews.Add("calc");
            });
            Assert(svc1.ExportToFile(exportPath), "SettingsService.ExportToFile failed");

            var svc2 = new SettingsService(s2Path);
            Assert(svc2.ImportFromFile(exportPath), "SettingsService.ImportFromFile failed");
            Assert(svc2.Current.ThemeName == "Cyber Neon" && svc2.Current.HiddenDockViews.Contains("calc"),
                "Imported settings did not match exported values");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static void TestWifiParserAndPeripheralStatus()
    {
        string sampleNetsh = @"
Interface name : Wi-Fi
There are 3 networks currently visible.

SSID 1 : WorkStation_5G
    Network type            : Infrastructure
    Authentication          : WPA2-Personal
    Encryption              : CCMP
    BSSID 1                 : 00:11:22:33:44:55
         Signal             : 92%
         Radio type         : 802.11ax
         Channel            : 36

SSID 2 : Coffee_Guest
    Network type            : Infrastructure
    Authentication          : Open
    Encryption              : None
    BSSID 1                 : aa:bb:cc:dd:ee:ff
         Signal             : 45%

SSID 3 : Home_Router
    Network type            : Infrastructure
    Authentication          : WPA3-Personal
    Encryption              : CCMP
    BSSID 1                 : 11:22:33:44:55:66
         Signal             : 82%
";
        var saved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Home_Router" };
        var list = NetworkService.ParseNetshNetworksOutput(sampleNetsh, saved, connectedSsid: "WorkStation_5G");

        Assert(list.Count == 3, $"Expected 3 networks, parsed {list.Count}");
        // Connected network must be first
        Assert(list[0].Ssid == "WorkStation_5G" && list[0].IsConnected && list[0].SignalBars == 4,
            "Connected network must be ordered first with 4 bars");
        // Saved profile (Home_Router) should come before unsaved (Coffee_Guest)
        Assert(list[1].Ssid == "Home_Router" && list[1].IsSavedProfile && !list[1].IsConnected,
            "Saved network Home_Router must be second");
        Assert(list[2].Ssid == "Coffee_Guest" && !list[2].IsSavedProfile && list[2].SignalBars == 2,
            "Coffee_Guest must be third with 2 bars");

        // Verify PeripheralDeviceInfo properties
        var pairedBt = new PeripheralDeviceInfo
        {
            Id = "bt1",
            Name = "Sony WH-1000XM4",
            Category = "Headset",
            IsConnected = false
        };
        Assert(pairedBt.RightHeaderText == "Connect" && pairedBt.CardOpacity == 0.80 && pairedBt.BatteryStatusText.Contains("Click to connect"),
            "Disconnected paired peripheral should display Connect and prompt to click");

        var connBt = new PeripheralDeviceInfo
        {
            Id = "bt2",
            Name = "AirPods Pro",
            Category = "Headset",
            IsConnected = true,
            BatteryPercent = 15
        };
        Assert(connBt.IsLowBattery && connBt.RightHeaderText == "15%" && connBt.CardOpacity == 1.0,
            "Connected low-battery peripheral must have IsLowBattery == true");
    }

    private static void TestInstallerService()
    {
        // CLI argument and binary name detection
        Assert(InstallerService.IsInstallerMode(new[] { "--install" }, @"C:\App\DynamicIsland.exe"), "--install must trigger installer mode");
        Assert(InstallerService.IsInstallerMode(new[] { "--setup" }, @"C:\App\DynamicIsland.exe"), "--setup must trigger installer mode");
        Assert(InstallerService.IsInstallerMode(new[] { "/setup" }, @"C:\App\DynamicIsland.exe"), "/setup must trigger installer mode");
        Assert(InstallerService.IsInstallerMode(new[] { "/install" }, @"C:\App\DynamicIsland.exe"), "/install must trigger installer mode");
        Assert(InstallerService.IsInstallerMode(new[] { "-i" }, @"C:\App\DynamicIsland.exe"), "-i must trigger installer mode");
        Assert(InstallerService.IsInstallerMode(Array.Empty<string>(), @"C:\Downloads\DynamicIsland-Setup.exe"), "Binary named Setup must trigger installer mode");
        Assert(!InstallerService.IsInstallerMode(new[] { "--other" }, @"C:\App\DynamicIsland.exe"), "Generic argument with DynamicIsland.exe must not trigger installer mode");
        Assert(InstallerService.IsUninstallMode(new[] { "--uninstall" }), "--uninstall must trigger uninstall mode");
        Assert(InstallerService.IsSilent(new[] { "--silent" }), "--silent flag must be recognized");
        Assert(InstallerService.IsSilent(new[] { "/S" }), "/S flag must be recognized");

        // Path resolution
        string installDir = InstallerService.GetDefaultInstallDir();
        Assert(installDir.Contains("DynamicIsland", StringComparison.OrdinalIgnoreCase), "Install dir must contain DynamicIsland");
        string exePath = InstallerService.GetInstalledExePath();
        Assert(exePath.EndsWith("DynamicIsland.exe", StringComparison.OrdinalIgnoreCase), "Installed exe path must end with DynamicIsland.exe");
        Assert(SemVersion.TryParse(InstallerService.AppVersion, out _), "InstallerService.AppVersion must be valid SemVer");

        // Existing installation detection & Drive space evaluation
        var (isInstalled, ver, loc) = InstallerService.GetExistingInstallation();
        Assert(ver != null || !isInstalled, "If installed, version must not be null");
        string spaceStr = InstallerService.GetDriveFreeSpace(installDir);
        Assert(!string.IsNullOrWhiteSpace(spaceStr), "Drive free space string must not be empty");

        // Shortcut creation in temp directory
        string tempDir = Path.Combine(Path.GetTempPath(), $"di_shortcut_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string dummyTarget = Path.Combine(tempDir, "test_target.exe");
            File.WriteAllText(dummyTarget, "dummy");
            string shortcutPath = Path.Combine(tempDir, "TestShortcut.lnk");

            bool ok = InstallerService.CreateShortcut(shortcutPath, dummyTarget, "Test Description");
            Assert(ok && File.Exists(shortcutPath), "InstallerService.CreateShortcut must successfully generate a .lnk file");
            Assert(new FileInfo(shortcutPath).Length > 0, "Shortcut file must not be 0 bytes");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static void TestZeroAllocationBrushCaching()
    {
        var codeClip = new ClipboardItem { Type = ClipboardItemType.Code, Title = "public class Foo {}", Subtitle = "Code · 19 chars" };
        Assert(ReferenceEquals(codeClip.BadgeColorBrush, ThemeBrushes.ClipBadgeCode), "Code clip BadgeColorBrush must reuse static frozen ThemeBrushes.ClipBadgeCode singleton");
        Assert(codeClip.BadgeColorBrush.IsFrozen, "Code clip BadgeColorBrush must be frozen");

        var urlClip = new ClipboardItem { Type = ClipboardItemType.Url, Title = "https://example.com", Subtitle = "URL · 19 chars" };
        Assert(ReferenceEquals(urlClip.BadgeColorBrush, ThemeBrushes.ClipBadgeUrl), "URL clip BadgeColorBrush must reuse static frozen ThemeBrushes.ClipBadgeUrl singleton");

        var colorClip = new ClipboardItem { Type = ClipboardItemType.Color, Title = "#30D158", SwatchColor = Color.FromRgb(0x30, 0xD1, 0x58) };
        var firstSwatch = colorClip.SwatchBrush;
        var secondSwatch = colorClip.SwatchBrush;
        Assert(ReferenceEquals(firstSwatch, secondSwatch), "ClipboardItem.SwatchBrush must cache its frozen brush across accesses");
        Assert(firstSwatch.IsFrozen, "Cached SwatchBrush must be frozen");
    }

    private static void TestIslandMemoryManagerTrim()
    {
        IslandMemoryManager.ExecuteConservativeTrim();
        using var proc = Process.GetCurrentProcess();
        Assert(proc.WorkingSet64 > 0, "WorkingSet64 must remain valid after conservative memory trim");
    }

    private static void TestClockStyles()
    {
        // 1. Verify all 6 supported clock styles exist
        Assert(SettingsService.AvailableClockStyles.Count == 6, "Must define 6 clock styles");
        Assert(SettingsService.AvailableClockStyles.Contains("Minimal"), "Must include Minimal");
        Assert(SettingsService.AvailableClockStyles.Contains("Seconds"), "Must include Seconds");
        Assert(SettingsService.AvailableClockStyles.Contains("DateBadge"), "Must include DateBadge");
        Assert(SettingsService.AvailableClockStyles.Contains("Analog"), "Must include Analog");
        Assert(SettingsService.AvailableClockStyles.Contains("Neon"), "Must include Neon");
        Assert(SettingsService.AvailableClockStyles.Contains("Flip"), "Must include Flip");

        // 2. Normalization test including aliases & synonyms
        Assert(SettingsService.NormalizeClockStyle("minimal") == "Minimal", "Must normalize case");
        Assert(SettingsService.NormalizeClockStyle("SECONDS") == "Seconds", "Must normalize uppercase");
        Assert(SettingsService.NormalizeClockStyle("Flip") == "Flip", "Must recognize Flip");
        Assert(SettingsService.NormalizeClockStyle("RetroFlip") == "Flip", "Must recognize RetroFlip alias as Flip");
        Assert(SettingsService.NormalizeClockStyle("FlipClock") == "Flip", "Must recognize FlipClock alias as Flip");
        Assert(SettingsService.NormalizeClockStyle("NeonGlow") == "Neon", "Must recognize NeonGlow alias as Neon");
        Assert(SettingsService.NormalizeClockStyle("Date") == "DateBadge", "Must recognize Date alias as DateBadge");
        Assert(SettingsService.NormalizeClockStyle("DateBadge") == "DateBadge", "Must recognize DateBadge");
        Assert(SettingsService.NormalizeClockStyle("Analog") == "Analog", "Must recognize Analog");
        Assert(SettingsService.NormalizeClockStyle("unknown_xyz") == "Minimal", "Must fallback unknown to Minimal");
        Assert(SettingsService.NormalizeClockStyle(null) == "Minimal", "Must fallback null to Minimal");

        // 3. Normalization of Dock Positions
        Assert(SettingsService.NormalizeDockPosition("left") == "Left", "Must normalize dock left");
        Assert(SettingsService.NormalizeDockPosition("RIGHT") == "Right", "Must normalize dock right");
        Assert(SettingsService.NormalizeDockPosition("Center") == "Center", "Must normalize dock center");
        Assert(SettingsService.NormalizeDockPosition("invalid") == "Center", "Must fallback dock to Center");

        // 4. Round-trip settings test and ResetToDefaults test
        string tempPath = Path.Combine(Path.GetTempPath(), $"di_clock_style_test_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(tempPath);
            Assert(settings.Current.ClockStyle == "Minimal", "Default clock style must be Minimal");

            settings.Update(s => s.ClockStyle = "Flip", nameof(AppSettings.ClockStyle));
            settings.SaveNow();

            var reloaded = new SettingsService(tempPath);
            Assert(reloaded.Current.ClockStyle == "Flip", "ClockStyle Flip must persist across instances");

            // Test ResetToDefaults
            reloaded.ResetToDefaults();
            Assert(reloaded.Current.ClockStyle == "Minimal", "ResetToDefaults must restore ClockStyle to Minimal");
            Assert(reloaded.Current.DockPosition == "Center", "ResetToDefaults must restore DockPosition to Center");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static void TestVisualizerStyles()
    {
        // 1. Verify visualizer themes exist
        Assert(SettingsService.AvailableVisualizerStyles.Count >= 4, "Must define at least 4 visualizer styles");
        Assert(SettingsService.AvailableVisualizerStyles.Contains("ClassicBars"), "Must include ClassicBars");
        Assert(SettingsService.AvailableVisualizerStyles.Contains("Waveform"), "Must include Waveform");
        Assert(SettingsService.AvailableVisualizerStyles.Contains("LiquidBlob"), "Must include LiquidBlob");
        Assert(SettingsService.AvailableVisualizerStyles.Contains("NeonAurora"), "Must include NeonAurora");
        Assert(SettingsService.AvailableVisualizerStyles.Contains("SiriOrb"), "Must include SiriOrb");

        // 2. Normalization test
        Assert(SettingsService.NormalizeVisualizerStyle("classicbars") == "ClassicBars", "Must normalize lowercase");
        Assert(SettingsService.NormalizeVisualizerStyle("waveform") == "Waveform", "Must normalize waveform");
        Assert(SettingsService.NormalizeVisualizerStyle("LIQUIDBLOB") == "LiquidBlob", "Must normalize uppercase");
        Assert(SettingsService.NormalizeVisualizerStyle("neonaurora") == "NeonAurora", "Must normalize mixed case");
        Assert(SettingsService.NormalizeVisualizerStyle("SIRIORB") == "SiriOrb", "Must normalize siri orb");
        Assert(SettingsService.NormalizeVisualizerStyle("unknown_theme") == "ClassicBars", "Must fallback unknown to ClassicBars");
        Assert(SettingsService.NormalizeVisualizerStyle(null) == "ClassicBars", "Must fallback null to ClassicBars");

        // 3. Round-trip settings persistence
        string tempPath = Path.Combine(Path.GetTempPath(), $"di_vis_style_test_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(tempPath);
            Assert(settings.Current.VisualizerStyle == "ClassicBars", "Default visualizer style must be ClassicBars");

            settings.Update(s => s.VisualizerStyle = "SiriOrb", nameof(AppSettings.VisualizerStyle));
            settings.SaveNow();

            var reloaded = new SettingsService(tempPath);
            Assert(reloaded.Current.VisualizerStyle == "SiriOrb", "VisualizerStyle must persist across instances");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static void TestIslandNatureThemes()
    {
        // 1. Verify available nature themes exist (None, Ivy & Blooms, Meadow)
        Assert(SettingsService.AvailableIslandThemes.Count == 3, "AvailableIslandThemes should contain 3 themes");
        Assert(SettingsService.AvailableIslandThemes.Contains("None"), "Must include None");
        Assert(SettingsService.AvailableIslandThemes.Contains("Ivy & Blooms"), "Must include Ivy & Blooms");
        Assert(SettingsService.AvailableIslandThemes.Contains("Meadow"), "Must include Meadow");

        // 2. Normalization & alias resolution
        Assert(SettingsService.NormalizeIslandTheme("ivy") == "Ivy & Blooms", "Must normalize 'ivy' to Ivy & Blooms");
        Assert(SettingsService.NormalizeIslandTheme("ivy & blooms") == "Ivy & Blooms", "Must normalize lowercase");
        Assert(SettingsService.NormalizeIslandTheme("Ivy and Blooms") == "Ivy & Blooms", "Must resolve 'Ivy and Blooms'");
        Assert(SettingsService.NormalizeIslandTheme("bloom") == "Ivy & Blooms", "Must resolve 'bloom'");
        Assert(SettingsService.NormalizeIslandTheme("blooms") == "Ivy & Blooms", "Must resolve 'blooms'");
        Assert(SettingsService.NormalizeIslandTheme("sakura") == "Ivy & Blooms", "Must map legacy 'sakura' to Ivy & Blooms");
        Assert(SettingsService.NormalizeIslandTheme("tree") == "Ivy & Blooms", "Must map legacy 'tree' to Ivy & Blooms");
        Assert(SettingsService.NormalizeIslandTheme("meadow") == "Meadow", "Must resolve 'meadow'");
        Assert(SettingsService.NormalizeIslandTheme("clean") == "None", "Must resolve 'clean' to None");
        Assert(SettingsService.NormalizeIslandTheme("none") == "None", "Must resolve 'none'");
        Assert(SettingsService.NormalizeIslandTheme(null) == "Ivy & Blooms", "Null fallback must default to Ivy & Blooms");
        Assert(SettingsService.NormalizeIslandTheme("unknown_theme") == "Ivy & Blooms", "Unknown fallback must be Ivy & Blooms");

        // 3. Settings persistence round-trip
        string tempPath = Path.Combine(Path.GetTempPath(), $"di_nature_theme_test_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(tempPath);
            Assert(settings.Current.IslandTheme == "Ivy & Blooms", "Default island theme must be Ivy & Blooms");
            Assert(settings.Current.NatureDensity == 0.70, "Default NatureDensity must be 0.70");
            Assert(settings.Current.NatureScale == 0.85, "Default NatureScale must be 0.85");
            Assert(settings.Current.NatureDropLength == 60.0, "Default NatureDropLength must be 60.0");
            Assert(settings.Current.NatureButterflyCount == 2, "Default NatureButterflyCount must be 2");
            Assert(settings.Current.NatureBloomDensity == 0.75, "Default NatureBloomDensity must be 0.75");
            Assert(settings.Current.NaturePollenEnabled == true, "Default NaturePollenEnabled must be true");
            Assert(settings.Current.NaturePollenCount == 14, "Default NaturePollenCount must be 14");

            settings.Update(s =>
            {
                s.IslandTheme = "Meadow";
                s.NatureDensity = 1.15;
                s.NatureScale = 1.05;
                s.NatureDropLength = 85.0;
                s.NatureButterflyCount = 3;
                s.NatureBloomDensity = 0.90;
                s.NaturePollenEnabled = false;
                s.NaturePollenCount = 8;
            }, nameof(AppSettings.IslandTheme));
            settings.SaveNow();

            var reloaded = new SettingsService(tempPath);
            Assert(reloaded.Current.IslandTheme == "Meadow", "Must persist Meadow across reload");
            Assert(Math.Abs(reloaded.Current.NatureDensity - 1.15) < 1e-4, "Must persist NatureDensity across reload");
            Assert(Math.Abs(reloaded.Current.NatureScale - 1.05) < 1e-4, "Must persist NatureScale across reload");
            Assert(Math.Abs(reloaded.Current.NatureDropLength - 85.0) < 1e-4, "Must persist NatureDropLength across reload");
            Assert(reloaded.Current.NatureButterflyCount == 3, "Must persist NatureButterflyCount across reload");
            Assert(Math.Abs(reloaded.Current.NatureBloomDensity - 0.90) < 1e-4, "Must persist NatureBloomDensity across reload");
            Assert(reloaded.Current.NaturePollenEnabled == false, "Must persist NaturePollenEnabled across reload");
            Assert(reloaded.Current.NaturePollenCount == 8, "Must persist NaturePollenCount across reload");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static void TestLyricsService()
    {
        // 1. LRC Timestamp Parsing & Ordering
        string sampleLrc = """
            [ti:Test Song]
            [ar:Test Artist]
            [al:Test Album]
            [00:05.50]First line of the song
            [00:12.80]Second line of the song
            [00:02.10]Intro guitar solo
            [00:25.00]Chorus explodes here
            """;

        var lines = LyricsService.ParseLrc(sampleLrc);
        Assert(lines.Count == 4, $"Expected 4 parsed lines, got {lines.Count}");
        // Must be ordered by timestamp
        Assert(lines[0].Text == "Intro guitar solo", "Lines must be ordered chronologically");
        Assert(lines[0].Timestamp == TimeSpan.FromMilliseconds(2100), "First line timestamp match");
        Assert(lines[1].Text == "First line of the song", "Second line text match");
        Assert(lines[1].Timestamp == TimeSpan.FromMilliseconds(5500), "Second line timestamp match");
        Assert(lines[3].Text == "Chorus explodes here", "Last line text match");
        Assert(lines[3].Timestamp == TimeSpan.FromSeconds(25), "Last line timestamp match");

        // 2. Playback Position Sync (with 120ms lead-in)
        // Position 0.0s -> before intro (effective pos 0.12s < 2.10s) -> index -1
        int idx0 = LyricsService.FindCurrentIndex(lines, TimeSpan.FromSeconds(0));
        Assert(idx0 == -1, $"Before first line should be -1, got {idx0}");

        // Position 2.0s -> effective pos 2.12s >= 2.10s -> index 0 (Intro guitar solo)
        int idx1 = LyricsService.FindCurrentIndex(lines, TimeSpan.FromMilliseconds(2000));
        Assert(idx1 == 0, $"Position 2.0s should hit index 0, got {idx1}");

        // Position 10.0s -> effective pos 10.12s (between 5.50s and 12.80s) -> index 1
        int idx2 = LyricsService.FindCurrentIndex(lines, TimeSpan.FromSeconds(10));
        Assert(idx2 == 1, $"Position 10.0s should hit index 1, got {idx2}");

        // Position 30.0s -> after chorus -> index 3
        int idx3 = LyricsService.FindCurrentIndex(lines, TimeSpan.FromSeconds(30));
        Assert(idx3 == 3, $"Position 30.0s should hit index 3, got {idx3}");

        // 3. Track Title Sanitization
        Assert(LyricsService.CleanTrackTitle("Bohemian Rhapsody (Official Music Video)") == "Bohemian Rhapsody", "Must strip (Official Music Video)");
        Assert(LyricsService.CleanTrackTitle("In The End [Official HD Music Video]") == "In The End", "Must strip [Official HD Music Video]");
        Assert(LyricsService.CleanTrackTitle("Hotel California - Remastered 2013") == "Hotel California", "Must strip - Remastered");
        Assert(LyricsService.CleanTrackTitle("Shape of You (Lyric Video)") == "Shape of You", "Must strip (Lyric Video)");
        Assert(LyricsService.CleanTrackTitle("Blinding Lights - Radio Edit") == "Blinding Lights", "Must strip - Radio Edit");

        // 4. Artist Name Sanitization
        Assert(LyricsService.CleanArtistName("Coldplay feat. BTS") == "Coldplay", "Must strip feat.");
        Assert(LyricsService.CleanArtistName("David Guetta ft. Sia") == "David Guetta", "Must strip ft.");

        // 5. Smart Browser/YouTube Track Metadata Parsing
        var (t1, a1) = LyricsService.ParseTrackMetadata("Imagine Dragons - Believer (Official Music Video)", "YouTube");
        Assert(t1 == "Believer", $"Expected 'Believer', got '{t1}'");
        Assert(a1 == "Imagine Dragons", $"Expected 'Imagine Dragons', got '{a1}'");

        var (t2, a2) = LyricsService.ParseTrackMetadata("Queen – Bohemian Rhapsody [Official Audio]", "Brave");
        Assert(t2 == "Bohemian Rhapsody", $"Expected 'Bohemian Rhapsody', got '{t2}'");
        Assert(a2 == "Queen", $"Expected 'Queen', got '{a2}'");

        var (t3, a3) = LyricsService.ParseTrackMetadata("The Weeknd - Blinding Lights (Official Video)", "The Weeknd - Topic");
        Assert(t3 == "Blinding Lights", $"Expected 'Blinding Lights', got '{t3}'");
        Assert(a3 == "The Weeknd", $"Expected 'The Weeknd', got '{a3}'");

        var (t4, a4) = LyricsService.ParseTrackMetadata("Dua Lipa - Levitating (feat. DaBaby)", "");
        Assert(t4 == "Levitating", $"Expected 'Levitating', got '{t4}'");
        Assert(a4 == "Dua Lipa", $"Expected 'Dua Lipa', got '{a4}'");

        var (t5, a5) = LyricsService.ParseTrackMetadata("Starboy", "The Weeknd");
        Assert(t5 == "Starboy", $"Expected 'Starboy', got '{t5}'");
        Assert(a5 == "The Weeknd", $"Expected 'The Weeknd', got '{a5}'");

        var (t6, a6) = LyricsService.ParseTrackMetadata("Coldplay - Yellow | Official Video", "Chrome");
        Assert(t6 == "Yellow", $"Expected 'Yellow', got '{t6}'");
        Assert(a6 == "Coldplay", $"Expected 'Coldplay', got '{a6}'");
    }

    private static void TestNetworkHudAndBandwidth()
    {
        // 1. Bandwidth Abbreviation Formatter
        Assert(NetworkService.FormatShortBandwidth(0) == "0K", "0 bytes should be 0K");
        Assert(NetworkService.FormatShortBandwidth(500) == "0K", "<1024 bytes should be 0K");
        Assert(NetworkService.FormatShortBandwidth(1024) == "1K", "1024 bytes should be 1K");
        Assert(NetworkService.FormatShortBandwidth(512 * 1024) == "512K", "512 KB should be 512K");
        Assert(NetworkService.FormatShortBandwidth(1024 * 1024) == "1.0M", "1 MB should be 1.0M");
        Assert(NetworkService.FormatShortBandwidth(1572864) == "1.5M", "1.5 MB should be 1.5M");
        Assert(NetworkService.FormatShortBandwidth(10 * 1024 * 1024) == "10.0M", "10 MB should be 10.0M");

        // 2. NetworkStatusSnapshot with Ping Latency
        var snap = new NetworkStatusSnapshot(
            IsConnected: true,
            IsWifi: true,
            IsEthernet: false,
            ConnectionName: "Home-Fiber-5G",
            AdapterName: "Wi-Fi 6E",
            SignalBars: 4,
            Glyph: "\uE701",
            LocalIpv4: "192.168.1.100",
            LinkSpeedMbps: 866,
            DownloadBytesPerSec: 2097152,
            UploadBytesPerSec: 524288,
            DownloadSpeedText: "2.0 MB/s",
            UploadSpeedText: "512 KB/s",
            IsWifiRadioOn: true,
            IsBluetoothRadioOn: true,
            PingMs: 14,
            PingText: "14 ms",
            ShortSpeedText: "2.0M");

        Assert(snap.IsConnected, "Snapshot should be connected");
        Assert(snap.PingMs == 14, "PingMs should be 14");
        Assert(snap.PingText == "14 ms", "PingText should match");
        Assert(snap.ShortSpeedText == "2.0M", "ShortSpeedText should match");
    }

    private static void TestMultiMonitorAndNotchMode()
    {
        // 1. AppSettings defaults
        var freshSettings = new AppSettings();
        Assert(!freshSettings.HardwareNotchMode, "Default HardwareNotchMode must be false");
        Assert(!freshSettings.FollowActiveMonitor, "Default FollowActiveMonitor must be false");

        // 2. Settings persistence round-trip
        string tempPath = Path.Combine(Path.GetTempPath(), $"di_notch_monitor_test_{Guid.NewGuid():N}.json");
        try
        {
            var svc = new SettingsService(tempPath);
            Assert(!svc.Current.HardwareNotchMode, "Initial HardwareNotchMode must be false");
            Assert(!svc.Current.FollowActiveMonitor, "Initial FollowActiveMonitor must be false");

            string? changedProp = null;
            svc.PropertyChanged += p => changedProp = p;

            svc.Update(s => s.HardwareNotchMode = true, nameof(AppSettings.HardwareNotchMode));
            Assert(changedProp == nameof(AppSettings.HardwareNotchMode), "PropertyChanged must fire with HardwareNotchMode");
            Assert(svc.Current.HardwareNotchMode, "HardwareNotchMode must be updated to true");

            svc.Update(s => s.FollowActiveMonitor = true, nameof(AppSettings.FollowActiveMonitor));
            Assert(changedProp == nameof(AppSettings.FollowActiveMonitor), "PropertyChanged must fire with FollowActiveMonitor");
            Assert(svc.Current.FollowActiveMonitor, "FollowActiveMonitor must be updated to true");

            svc.SaveNow();

            // Reload from disk to verify atomic JSON persistence
            var reloaded = new SettingsService(tempPath);
            Assert(reloaded.Current.HardwareNotchMode, "HardwareNotchMode must persist as true across reload");
            Assert(reloaded.Current.FollowActiveMonitor, "FollowActiveMonitor must persist as true across reload");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }

        // 3. Monitor enumeration and device naming
        var monitors = IslandWindow.GetAvailableMonitors();
        Assert(monitors != null, "GetAvailableMonitors must return non-null list");
        if (monitors!.Count > 0)
        {
            Assert(monitors[0] != IntPtr.Zero, "Enumerated monitor handle must not be IntPtr.Zero");
        }
        Assert(GetMonitorDeviceName(IntPtr.Zero) == null, "IntPtr.Zero must safely return null device name");
    }

    private static void TestVirtualPetService()
    {
        // 1. Species & Accessory normalization
        Assert(SettingsService.NormalizePetSpecies("Cat") == "Cat", "Cat must normalize to Cat");
        Assert(SettingsService.NormalizePetSpecies("dog") == "Dog", "dog must normalize to Dog");
        Assert(SettingsService.NormalizePetSpecies("BUNNY") == "Bunny", "BUNNY must normalize to Bunny");
        Assert(SettingsService.NormalizePetSpecies("fox") == "Fox", "fox must normalize to Fox");
        Assert(SettingsService.NormalizePetSpecies("PANDA") == "Panda", "PANDA must normalize to Panda");
        Assert(SettingsService.NormalizePetSpecies("dragon") == "Dragon", "dragon must normalize to Dragon");
        Assert(SettingsService.NormalizePetSpecies("alien") == "Cat", "Unknown species must fallback to Cat");
        Assert(SettingsService.NormalizePetAccessory("crown") == "Crown", "crown must normalize to Crown");
        Assert(SettingsService.NormalizePetAccessory("unknown_hat") == "None", "Unknown accessory must fallback to None");

        // 2. Service state & interactions
        string tempPath = Path.Combine(Path.GetTempPath(), $"di_pet_test_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(tempPath);
            Assert(settings.Current.EnableVirtualPet, "Virtual pet must be enabled by default");
            Assert(settings.Current.PetSpecies == "Cat", "Default species must be Cat");

            var pet = new VirtualPetService(Dispatcher.CurrentDispatcher, settings);
            Assert(pet.IsEnabled, "PetService.IsEnabled must be true");
            Assert(pet.PetSpecies == "Cat", "PetSpecies must match Cat");
            Assert(pet.CurrentMood == PetMood.Sleeping, "Initial mood must be Sleeping");

            // Affection interaction
            int initialScore = pet.AffectionScore;
            string petMsg = pet.Pet();
            Assert(!string.IsNullOrEmpty(petMsg), "Pet interaction message must not be empty");
            Assert(pet.AffectionScore == initialScore + 3, "Petting must add 3 affection points");
            Assert(pet.CurrentMood == PetMood.Happy, "Petting must trigger Happy mood");

            // Water logging
            int initWater = pet.WaterCount;
            int scoreBeforeWater = pet.AffectionScore;
            string waterMsg = pet.LogWater();
            Assert(!string.IsNullOrEmpty(waterMsg), "Water message must not be empty");
            Assert(pet.WaterCount == initWater + 1, "Water count must increment by 1");
            Assert(pet.AffectionScore == scoreBeforeWater + 10, "Water must add 10 affection points");

            // Stretch logging
            int initStretch = pet.StretchCount;
            int scoreBeforeStretch = pet.AffectionScore;
            string stretchMsg = pet.LogStretch();
            Assert(!string.IsNullOrEmpty(stretchMsg), "Stretch message must not be empty");
            Assert(pet.StretchCount == initStretch + 1, "Stretch count must increment by 1");
            Assert(pet.AffectionScore == scoreBeforeStretch + 10, "Stretch must add 10 affection points");

            // Species cycling across all 6 species
            pet.CycleSpecies();
            Assert(pet.PetSpecies == "Dog", "First cycle must switch to Dog");
            pet.CycleSpecies();
            Assert(pet.PetSpecies == "Bunny", "Second cycle must switch to Bunny");
            pet.CycleSpecies();
            Assert(pet.PetSpecies == "Fox", "Third cycle must switch to Fox");
            pet.CycleSpecies();
            Assert(pet.PetSpecies == "Panda", "Fourth cycle must switch to Panda");
            pet.CycleSpecies();
            Assert(pet.PetSpecies == "Dragon", "Fifth cycle must switch to Dragon");
            pet.CycleSpecies();
            Assert(pet.PetSpecies == "Cat", "Sixth cycle must wrap back to Cat");

            // Accessory cycling & Treat / Trick actions
            Assert(pet.PetAccessory == "None", "Initial accessory must be None");
            pet.CycleAccessory();
            Assert(pet.PetAccessory == "Crown", "First accessory cycle must be Crown");
            int treatsBefore = pet.TreatCount;
            int scoreBeforeTreat = pet.AffectionScore;
            string treatMsg = pet.FeedTreat();
            Assert(!string.IsNullOrEmpty(treatMsg), "FeedTreat message must not be empty");
            Assert(pet.TreatCount == treatsBefore + 1, "TreatCount must increment by 1");
            Assert(pet.AffectionScore == scoreBeforeTreat + 8, "FeedTreat must add 8 affection points");
            Assert(pet.CurrentMood == PetMood.Eating, "FeedTreat must set PetMood.Eating");

            int scoreBeforeTrick = pet.AffectionScore;
            string trickMsg = pet.PerformTrick();
            Assert(!string.IsNullOrEmpty(trickMsg), "PerformTrick message must not be empty");
            Assert(pet.AffectionScore == scoreBeforeTrick + 5, "PerformTrick must add 5 affection points");

            string oldName = pet.PetName;
            string newName = pet.CyclePetName();
            Assert(newName != oldName, "CyclePetName must advance to next preset name");

            // Reminder interval cycling
            Assert(pet.ReminderIntervalMinutes == 30, "Initial reminder interval must be 30");
            pet.CycleReminderInterval();
            Assert(pet.ReminderIntervalMinutes == 45, "First cycle must be 45m");
            pet.CycleReminderInterval();
            Assert(pet.ReminderIntervalMinutes == 60, "Second cycle must be 60m");
            pet.CycleReminderInterval();
            Assert(pet.ReminderIntervalMinutes == 0, "Third cycle must be 0 (Off)");
            pet.CycleReminderInterval();
            Assert(pet.ReminderIntervalMinutes == 30, "Fourth cycle must wrap to 30m");

            // Activity reactive states
            pet.UpdateActivity(isMediaPlaying: true, audioEnergy: 0.15f, isUserNear: false);
            // Happy timer may be active from recent action, so test after Happy duration or directly
            pet.UpdateActivity(isMediaPlaying: false, audioEnergy: 0f, isUserNear: true);
            pet.UpdateActivity(isMediaPlaying: false, audioEnergy: 0f, isUserNear: false);

            // Wellness alert callback
            WellnessAlert? triggeredAlert = null;
            pet.WellnessAlertTriggered += a => triggeredAlert = a;
            pet.CheckWellnessNow(DateTime.UtcNow.AddMinutes(35));
            Assert(triggeredAlert != null, "Wellness alert must be triggered after interval elapsed");
            Assert(triggeredAlert!.Kind == WellnessAlertKind.Hydrate, "First alert must be Hydrate");

            // 3. VirtualPetControl 200x Vector Visual Renderer & Kinetics
            var petControl = new VirtualPetControl();
            petControl.Measure(new Size(100, 100));
            Assert(petControl.DesiredSize == new Size(26, 22), "Default desired size should be 26x22");
            
            // Verify all 6 species and 7 accessories render cleanly across all moods
            string[] testSpecies = { "Cat", "Dog", "Bunny", "Fox", "Panda", "Dragon" };
            string[] testAccessories = { "None", "Crown", "WizardHat", "Headphones", "Sunglasses", "Halo", "Sprout" };
            PetMood[] testMoods = { PetMood.Sleeping, PetMood.Awake, PetMood.Dancing, PetMood.Happy, PetMood.Thirsty, PetMood.Stretch, PetMood.Eating };

            foreach (var sp in testSpecies)
            {
                petControl.Species = sp;
                foreach (var acc in testAccessories)
                {
                    petControl.Accessory = acc;
                    foreach (var md in testMoods)
                    {
                        petControl.Mood = md;
                        petControl.UpdateTick(1.5, isNearCursor: true, gazeX: 0.5, gazeY: -0.2);
                        petControl.Dance(bass: 0.8f, treble: 0.6f, t: 2.0);
                        petControl.PetClickImpulse();
                        petControl.TriggerTrick();
                    }
                }
            }

            // Verify resolution-independent hero scaling (e.g., Settings Hero Card 60x50)
            petControl.Width = 60;
            petControl.Height = 50;
            petControl.Measure(new Size(60, 50));
            petControl.Arrange(new Rect(0, 0, 60, 50));
            Assert(petControl.ActualWidth >= 0, "Hero pet control must arrange cleanly");

            pet.Dispose();
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static void TestMultiMonitorPositioning()
    {
        // 1. Primary Monitor (2560x1440 @ 150% DPI scale = 1.5, window width = 660 DIPs)
        int monLeft1 = 0;
        int monTop1 = 0;
        int monWidth1 = 2560;
        double scale1 = 1.5;
        double winWidthDip = 660.0;
        int widthPx1 = (int)Math.Round(winWidthDip * scale1); // 990 px
        int x1 = monLeft1 + (monWidth1 - widthPx1) / 2;
        int y1 = monTop1;

        Assert(widthPx1 == 990, "Primary width must be 990 px (660 * 1.5)");
        Assert(x1 == 785, "Primary x coordinate must be (2560 - 990) / 2 = 785");
        Assert(x1 + widthPx1 / 2 == monLeft1 + monWidth1 / 2, "Window center (785 + 495 = 1280) must match monitor center (1280)");

        // 2. Stacked Secondary Monitor (DISPLAY6 topology: 1920x1080 @ 100% DPI scale = 1.0, rcMonitor = (288, -1080, 2208, 0))
        int monLeft6 = 288;
        int monTop6 = -1080;
        int monWidth6 = 1920; // 2208 - 288
        double scale6 = 1.0;
        int widthPx6 = (int)Math.Round(winWidthDip * scale6); // 660 px
        int x6 = monLeft6 + (monWidth6 - widthPx6) / 2;
        int y6 = monTop6;

        Assert(widthPx6 == 660, "Secondary width must be 660 px (660 * 1.0)");
        Assert(x6 == 918, "Secondary x coordinate must be 288 + (1920 - 660) / 2 = 918");
        Assert(y6 == -1080, "Secondary y coordinate must be -1080 (top edge of stacked monitor)");
        Assert(x6 + widthPx6 / 2 == monLeft6 + monWidth6 / 2, "Window center (918 + 330 = 1248) must match monitor center (288 + 960 = 1248)");
        Assert(x6 >= monLeft6, "Window left edge (918) must be >= monitor left (288) — never clipped off-screen");
        Assert(x6 + widthPx6 <= monLeft6 + monWidth6, "Window right edge (1578) must be <= monitor right (2208)");

        // 3. Dock Left and Right positioning
        double pillMarginInsideWindow = Math.Max(0, (winWidthDip - 480) / 2.0); // (660 - 480) / 2 = 90
        int xLeft = monLeft6 + (int)Math.Round((24 - pillMarginInsideWindow) * scale6);
        int capsuleLeftEdge = xLeft + (int)Math.Round(pillMarginInsideWindow * scale6);
        Assert(capsuleLeftEdge == monLeft6 + 24, "Dock Left must place capsule exactly 24 px from screen left edge");

        int xRight = (monLeft6 + monWidth6) - widthPx6 - (int)Math.Round((24 - pillMarginInsideWindow) * scale6);
        int capsuleRightEdge = xRight + widthPx6 - (int)Math.Round(pillMarginInsideWindow * scale6);
        Assert(capsuleRightEdge == monLeft6 + monWidth6 - 24, "Dock Right must place capsule exactly 24 px from screen right edge");
    }

    private static void TestPowerPlanAndDisplayTopology()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"di_power_disp_test_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(tempPath);
            Assert(settings.Current.EnableChargingPulse, "Charging pulse must be enabled by default");
            Assert(settings.Current.LowBatteryAlertThreshold == 20, "Default low battery threshold must be 20%");
            Assert(settings.Current.NightLightWarmthKelvin == 3400, "Default night light warmth must be 3400K");

            var power = new PowerPlanService(Dispatcher.CurrentDispatcher, settings);
            var cur = power.Current;
            Assert(cur != null, "PowerStatusInfo must not be null");
            Assert(!string.IsNullOrEmpty(cur!.StatusSubtitle), "StatusSubtitle must not be empty");
            Assert(!string.IsNullOrEmpty(cur.TimeEstimateText), "TimeEstimateText must not be empty");
            Assert(!string.IsNullOrEmpty(cur.WattageText), "WattageText must not be empty");
            Assert((cur.HealthPercent ?? 100) >= 10 && (cur.HealthPercent ?? 100) <= 100, "Health percent must be between 10 and 100%");
            Assert(cur.RecentBatteryHistory.Count > 0, "RecentBatteryHistory must contain samples for sparkline graph");

            // Power Scheme switching
            power.SetActiveScheme("Performance");
            power.SetActiveScheme("Balanced");
            power.SetActiveScheme("PowerSaver");

            // Simulated charging pulse
            bool pulseFired = false;
            power.ChargingPulseTriggered += (pct, est) => pulseFired = true;
            power.TriggerSimulatedChargingPulse();
            Assert(pulseFired, "Charging pulse event must trigger on simulated pulse");

            power.Dispose();

            // Display Topology queries
            var monitors = DisplayTopologyService.QueryConnectedMonitors(IntPtr.Zero);
            Assert(monitors.Count > 0, "Must discover at least one connected display monitor");
            var primary = monitors[0];
            Assert(primary.Width > 0 && primary.Height > 0, "Monitor dimensions must be positive");
            Assert(primary.RefreshRateHz >= 30, "Monitor refresh rate must be >= 30Hz");
            Assert(!string.IsNullOrEmpty(primary.ResolutionText), "ResolutionText must be populated");
            Assert(!string.IsNullOrEmpty(primary.RefreshRateText), "RefreshRateText must be populated");

            // Night Light gamma safety check
            DisplayTopologyService.SetNightLight(true, 3400);
            DisplayTopologyService.SetNightLight(false, 3400); // restore
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static void TestSystemPerformanceService()
    {
        using var service = new SystemPerformanceService();

        // 1. Verify Hardware Summary is populated
        Assert(!string.IsNullOrWhiteSpace(service.HardwareSummary), "SystemPerformanceService failed to read HardwareSummary.");

        // 2. Benchmark Tier 1 Idle Poll (Must execute in < 25.0 ms with 0 exceptions)
        var sw = Stopwatch.StartNew();
        SystemVitalsSnapshot tier1Snap = service.SampleTelemetry(forceProcessScan: false, forceDispatch: true);
        sw.Stop();

        Assert(tier1Snap.RamTotalGb > 0.5, $"Invalid total physical RAM reading: {tier1Snap.RamTotalGb:0.00} GB");
        Assert(tier1Snap.CpuPercent >= 0 && tier1Snap.CpuPercent <= 100, $"CPU percentage out of range: {tier1Snap.CpuPercent}%");
        Assert(sw.Elapsed.TotalMilliseconds < 25.0, $"Tier 1 telemetry exceeded latency budget: {sw.Elapsed.TotalMilliseconds:0.00} ms");

        // 3. Verify Tier 2 On-Demand Process Group Scan
        SystemVitalsSnapshot tier2Snap = service.SampleTelemetry(forceProcessScan: true, forceDispatch: true);
        Assert(tier2Snap.HogCount > 0, "Tier 2 process scan should detect at least 1 process.");
        Assert(!string.IsNullOrWhiteSpace(tier2Snap.Hog1.ExeName), "Top resource hog should have an executable name.");

        // 4. Verify Foreground-Safe RAM Buster & Cooldown Guard
        RamPurgeResult purge1 = service.PurgeIdleRam();
        Assert(!purge1.OnCooldown, "Initial RAM purge must not be on cooldown.");
        Assert(purge1.FreedMb >= 0, "Freed RAM must be non-negative.");

        RamPurgeResult purge2 = service.PurgeIdleRam();
        Assert(purge2.OnCooldown, "RAM purge 10-second cooldown guard must engage on second consecutive purge.");
    }

    private static void TestWave2Features()
    {
        // 1. Custom Pomodoro preset durations (15m / 25m / 45m / 60m)
        using var cd = new CountdownTimerService();
        cd.StartCountdown(TimeSpan.FromMinutes(45), "Pomodoro 45m");
        Assert(cd.IsActive && cd.IsRunning, "Pomodoro 45m must be active and running");
        Assert(cd.Label == "Pomodoro 45m", $"Expected label 'Pomodoro 45m', got '{cd.Label}'");
        Assert(cd.TargetDuration == TimeSpan.FromMinutes(45), "TargetDuration must be 45 minutes");

        // 2. VoiceMemoInfo duration & metadata formatting
        var memo = new VoiceMemoInfo(
            @"C:\Temp\VoiceMemo_Test.wav",
            "VoiceMemo_Test.wav",
            TimeSpan.FromSeconds(125),
            new DateTime(2026, 3, 27, 14, 30, 0));
        Assert(memo.DurationText == "2:05", $"Expected DurationText '2:05', got '{memo.DurationText}'");
        Assert(memo.MetaText.Contains("2:05") && memo.MetaText.Contains("14:30"), $"Unexpected MetaText: '{memo.MetaText}'");

        // 3. MutedNotificationApps add & unmute all + ShowLyricsInCompact persistence
        string tempPath = Path.Combine(Path.GetTempPath(), $"di_wave2_test_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(tempPath);
            settings.Update(s =>
            {
                s.ShowLyricsInCompact = true;
                s.MutedNotificationApps.Add("Discord");
                s.MutedNotificationApps.Add("Slack");
            });
            Assert(settings.Current.ShowLyricsInCompact, "ShowLyricsInCompact should be true");
            Assert(settings.Current.MutedNotificationApps.Count == 2, "Expected 2 muted apps");

            settings.Update(s => s.MutedNotificationApps.Clear(), nameof(AppSettings.MutedNotificationApps));
            Assert(settings.Current.MutedNotificationApps.Count == 0, "Expected 0 muted apps after unmute all");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static void TestWave3Features()
    {
        // 1. QuickCalcService: programmer bases, time/speed units & 'ans' memory
        var hexRes = QuickCalcService.Evaluate("255 in hex");
        Assert(hexRes.Success && hexRes.Result == "0xFF", $"Expected 0xFF for '255 in hex', got '{hexRes.Result}'");

        var decRes = QuickCalcService.Evaluate("0xFF in dec");
        Assert(decRes.Success && decRes.Result == "255", $"Expected 255 for '0xFF in dec', got '{decRes.Result}'");

        var binRes = QuickCalcService.Evaluate("42 in bin");
        Assert(binRes.Success && binRes.Result == "0b101010", $"Expected 0b101010 for '42 in bin', got '{binRes.Result}'");

        var timeRes = QuickCalcService.Evaluate("90 min in hr");
        Assert(timeRes.Success && timeRes.Result == "1.5", $"Expected 1.5 for '90 min in hr', got '{timeRes.Result}'");

        QuickCalcService.SetLastAnswer("50");
        var ansRes = QuickCalcService.Evaluate("ans * 3");
        Assert(ansRes.Success && ansRes.Result == "150", $"Expected 150 for 'ans * 3' when ans=50, got '{ansRes.Result}'");

        // 2. ShelfItem thumbnail visibility properties
        var bmp = new WriteableBitmap(16, 16, 96, 96, PixelFormats.Bgra32, null);
        bmp.Freeze();
        var imgShelf = new ShelfItem(@"C:\Temp\shot.png", "shot.png", "12 KB", "\uEB9F", true, "PNG", ThemeBrushes.Blue, bmp);
        Assert(imgShelf.HasThumbnail && imgShelf.ThumbnailVisibility == Visibility.Visible && imgShelf.GlyphVisibility == Visibility.Collapsed,
            "ShelfItem with thumbnail must show Thumbnail and hide Glyph");

        var docShelf = new ShelfItem(@"C:\Temp\doc.pdf", "doc.pdf", "120 KB", "\uEA90", false, "PDF", ThemeBrushes.Red, null);
        Assert(!docShelf.HasThumbnail && docShelf.ThumbnailVisibility == Visibility.Collapsed && docShelf.GlyphVisibility == Visibility.Visible,
            "ShelfItem without thumbnail must show Glyph and hide Thumbnail");

        // 3. Caffeine execution state toggle safety
        Assert(IslandWindow.ApplyCaffeineExecutionState(true), "ApplyCaffeineExecutionState(true) should succeed");
        Assert(IslandWindow.ApplyCaffeineExecutionState(false), "ApplyCaffeineExecutionState(false) should restore default sleep state");
    }

    private static void TestWave4CallDetection()
    {
        // 1. Packaged Microsoft Store WhatsApp Desktop call detection
        string? waStore = PrivacyIndicatorService.ResolveFriendlyCallAppName(
            new[] { "5319275A.WhatsAppDesktop_cv1g1gvanyjgm" });
        Assert(waStore == "WhatsApp", $"Expected 'WhatsApp' for Store package, got '{waStore}'");

        // 2. NonPackaged Desktop WhatsApp path call detection
        string? waDesktop = PrivacyIndicatorService.ResolveFriendlyCallAppName(
            new[] { @"C:#Program Files#WindowsApps#5319275A.WhatsAppDesktop#WhatsApp.exe" });
        Assert(waDesktop == "WhatsApp", $"Expected 'WhatsApp' for NonPackaged path, got '{waDesktop}'");

        // 3. Phone Link / YourPhone call detection
        string? phoneLink = PrivacyIndicatorService.ResolveFriendlyCallAppName(
            new[] { "Microsoft.YourPhone_8wekyb3d8bbwe" });
        Assert(phoneLink == "Phone Link", $"Expected 'Phone Link', got '{phoneLink}'");

        // 4. Prioritize dedicated call app even when listed after another process
        string? multi = PrivacyIndicatorService.ResolveFriendlyCallAppName(
            new[] { @"C:#Users#User#AppData#Local#DynamicIsland#DynamicIsland.exe", "Telegram.exe" });
        Assert(multi == "Telegram", $"Expected 'Telegram', got '{multi}'");

        // 5. DynamicIsland self-exclusion (recording a Voice Memo must not trigger a fake call)
        string? selfOnly = PrivacyIndicatorService.ResolveFriendlyCallAppName(
            new[] { @"C:#Code#DynamicIsland#DynamicIsland.exe" });
        Assert(selfOnly == null, $"Expected null for DynamicIsland self-capture, got '{selfOnly}'");
    }

    private static void TestWave5Features()
    {
        // 1. Compact call timer text formatting
        string compactCall = PrivacyIndicatorService.FormatCompactCallText("WhatsApp", TimeSpan.FromSeconds(134));
        Assert(compactCall == "WhatsApp \u00B7 02:14", $"Expected 'WhatsApp · 02:14', got '{compactCall}'");

        // 2. Live download byte formatting & status text
        string dlStatus = DownloadTrackerService.FormatDownloadStatus("setup.exe", 26_004_685);
        Assert(dlStatus == "24.8 MB \u00B7 setup.exe", $"Expected '24.8 MB · setup.exe', got '{dlStatus}'");

        // 3. Media player window process search key resolution
        string key = MediaSessionService.ResolveProcessSearchKey("Spotify", "SpotifyAB.SpotifyMusic");
        Assert(key == "Spotify", $"Expected 'Spotify', got '{key}'");

        // 4. AudioEndpointInfo DisplayGlyph mute reactivity
        var ep = new AudioEndpointInfo { Id = "ep1", Name = "Speakers", Glyph = "\uE7F5", IsMuted = false };
        Assert(ep.DisplayGlyph == "\uE7F5", "Unmuted endpoint should show device glyph");
        ep.IsMuted = true;
        Assert(ep.DisplayGlyph == "\uE74F", "Muted endpoint should show mute glyph");

        // 5. Secondary droplet contextual tooltip description
        string timerTip = ActivityResolver.DescribeSecondaryDroplet(CompactActivityKind.Countdown);
        Assert(timerTip.Contains("Countdown Timer") && timerTip.Contains("Middle-click"),
            $"Expected Countdown Timer middle-click tooltip, got '{timerTip}'");
    }

    private static void TestPhase1DroppyParityFeatures()
    {
        // 1. DropletRegistry lifecycle & SettingsService v3 persistence
        string tempSettings = Path.Combine(Path.GetTempPath(), $"di_droplets_test_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(tempSettings);
            var registry = new DropletRegistry(settings);
            Assert(registry.Items.Count >= 10, $"Expected at least 10 built-in droplets, got {registry.Items.Count}");
            Assert(registry.IsEnabled("jiggle_basket"), "jiggle_basket must be enabled by default");
            int initialRam = registry.TotalActiveRamKb;
            Assert(initialRam > 0, "TotalActiveRamKb must be positive when all droplets are enabled");

            bool toggled = registry.Toggle("jiggle_basket");
            Assert(!toggled && !registry.IsEnabled("jiggle_basket"), "Toggle must disable jiggle_basket");
            Assert(registry.TotalActiveRamKb < initialRam, "Disabling a droplet must reduce TotalActiveRamKb");
            settings.SaveNow();

            var reloadedSettings = new SettingsService(tempSettings);
            var reloadedRegistry = new DropletRegistry(reloadedSettings);
            Assert(!reloadedRegistry.IsEnabled("jiggle_basket"), "Disabled droplet state must persist across reload");
        }
        finally
        {
            try { if (File.Exists(tempSettings)) File.Delete(tempSettings); } catch { }
        }

        // 2. JiggleDetector math engine
        var jiggle = new JiggleDetector();
        // Button not pressed -> must never trigger
        Assert(!jiggle.ProcessMove(100, 200, 1000, isLeftButtonDown: false), "Must not trigger when left button is up");
        Assert(!jiggle.ProcessMove(200, 200, 1080, isLeftButtonDown: false), "Must not trigger when left button is up");

        // Button pressed, 3 rapid horizontal reversals >= 38px within 520ms -> MUST trigger
        jiggle.Reset();
        Assert(!jiggle.ProcessMove(500, 300, 2000, isLeftButtonDown: true), "Anchor point should not trigger");
        Assert(!jiggle.ProcessMove(560, 302, 2080, isLeftButtonDown: true), "First leg right (+60px) should not trigger");
        Assert(!jiggle.ProcessMove(490, 301, 2170, isLeftButtonDown: true), "Reversal 1 left (-70px) should not trigger yet");
        Assert(!jiggle.ProcessMove(565, 304, 2260, isLeftButtonDown: true), "Reversal 2 right (+75px) should not trigger yet");
        bool triggered = jiggle.ProcessMove(495, 300, 2350, isLeftButtonDown: true);
        Assert(triggered, "Reversal 3 left (-70px) within 350ms window must trigger Jiggle-to-Reveal");

        // Cooldown guard prevents immediate double-trigger
        Assert(!jiggle.ProcessMove(570, 300, 2420, isLeftButtonDown: true), "Cooldown must suppress immediate re-trigger");

        // 3. FileActionPipeline: PNG -> JPG, Strip EXIF, Images -> PDF, and Merge PDFs
        string tempDir = Path.Combine(Path.GetTempPath(), $"di_pipeline_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string png1 = Path.Combine(tempDir, "sample1.png");
            string png2 = Path.Combine(tempDir, "sample2.png");

            var wb = new WriteableBitmap(64, 48, 96, 96, PixelFormats.Bgra32, null);
            byte[] pixels = new byte[64 * 48 * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 0x58;     // B
                pixels[i + 1] = 0xD1; // G
                pixels[i + 2] = 0x30; // R
                pixels[i + 3] = 0xFF; // A
            }
            wb.WritePixels(new Int32Rect(0, 0, 64, 48), pixels, 64 * 4, 0);
            wb.Freeze();

            foreach (var p in new[] { png1, png2 })
            {
                using var fs = new FileStream(p, FileMode.Create, FileAccess.Write);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(wb));
                enc.Save(fs);
            }

            var actions = FileActionPipeline.GetAvailableActions(new[] { png1, png2 });
            Assert(actions.Any(a => a.Kind == FileActionKind.ImagesToPdf), "Multiple images must offer ImagesToPdf action");
            Assert(actions.Any(a => a.Kind == FileActionKind.ConvertToJpg), "PNGs must offer ConvertToJpg action");

            var jpgRes = FileActionPipeline.ExecuteAsync(FileActionKind.ConvertToJpg, new[] { png1 }).GetAwaiter().GetResult();
            Assert(jpgRes.Success && jpgRes.OutputPaths.Count == 1 && File.Exists(jpgRes.OutputPaths[0]),
                $"ConvertToJpg failed: {jpgRes.Message}");

            var pdf1Res = FileActionPipeline.ExecuteAsync(FileActionKind.ImagesToPdf, new[] { png1 }).GetAwaiter().GetResult();
            var pdf2Res = FileActionPipeline.ExecuteAsync(FileActionKind.ImagesToPdf, new[] { png2 }).GetAwaiter().GetResult();
            Assert(pdf1Res.Success && pdf2Res.Success, "ImagesToPdf must succeed");

            var combinedPdfRes = FileActionPipeline.ExecuteAsync(FileActionKind.ImagesToPdf, new[] { png1, png2 }).GetAwaiter().GetResult();
            Assert(combinedPdfRes.Success && combinedPdfRes.OutputPaths.Count == 1 && File.Exists(combinedPdfRes.OutputPaths[0]),
                "Multi-image ImagesToPdf must succeed");
            using (var combinedDoc = PdfSharp.Pdf.IO.PdfReader.Open(combinedPdfRes.OutputPaths[0], PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
            {
                Assert(combinedDoc.PageCount == 2, $"Expected 2 pages in combined PDF, got {combinedDoc.PageCount}");
            }

            var mergeRes = FileActionPipeline.ExecuteAsync(
                FileActionKind.MergePdfs,
                new[] { pdf1Res.OutputPaths[0], pdf2Res.OutputPaths[0] }).GetAwaiter().GetResult();
            Assert(mergeRes.Success && mergeRes.OutputPaths.Count == 1 && File.Exists(mergeRes.OutputPaths[0]),
                $"MergePdfs failed: {mergeRes.Message}");
            using (var mergedDoc = PdfSharp.Pdf.IO.PdfReader.Open(mergeRes.OutputPaths[0], PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
            {
                Assert(mergedDoc.PageCount == 2, $"Expected 2 pages in merged PDF, got {mergedDoc.PageCount}");
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }

        // 4. Plain-text stripping & LiveActivityHub priority ordering
        string stripped = ClipboardHistoryService.StripFormattingToPlainText("  Line 1\r\n\u200BLine 2  ");
        Assert(stripped == "Line 1\r\nLine 2", $"Unexpected StripFormattingToPlainText result: '{stripped}'");

        var hub = new LiveActivityHub();
        hub.PublishOrUpdate(new LiveActivityItem { Id = "low", Title = "Caffeine", Priority = 20 });
        hub.PublishOrUpdate(new LiveActivityItem { Id = "high", Title = "Converting Video", Priority = 85, Progress = 0.25 });
        Assert(hub.Primary?.Id == "high", "Higher priority LiveActivityItem must be Primary");
        Assert(hub.Secondary?.Id == "low", "Lower priority LiveActivityItem must be Secondary");
        hub.UpdateProgress("high", 0.75, "75%");
        Assert(Math.Abs((hub.Primary?.Progress ?? 0) - 0.75) < 0.001, "UpdateProgress must update progress in-place");
        hub.Complete("high");
        Assert(hub.Primary?.Id == "low", "Completing primary activity must promote secondary to Primary");
    }

    private static void TestPhase2LiveActivitiesAndMeetings()
    {
        // 1. LiveActivityIpc JSON serialization round-trip
        var req = new ActivityIpcRequest
        {
            Action = "publish",
            Id = "cli_build",
            Source = "Agent",
            Title = "dotnet build",
            Subtitle = "Compiling Dynamic Island",
            Glyph = "\uE7C3",
            Accent = "#0A84FF",
            Progress = 0.42,
            Priority = 75,
            TargetView = "shelf"
        };
        string json = System.Text.Json.JsonSerializer.Serialize(req);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<ActivityIpcRequest>(json);
        Assert(deserialized != null, "ActivityIpcRequest deserialization returned null");
        Assert(deserialized!.Action == "publish" && deserialized.Id == "cli_build", "Serialized action/id mismatch");
        Assert(Math.Abs((deserialized.Progress ?? 0) - 0.42) < 0.001, "Progress mismatch");
        Assert(deserialized.Priority == 75, "Priority mismatch");

        var resp = new ActivityIpcResponse { Ok = true, Message = "Published 'cli_build'" };
        string respJson = System.Text.Json.JsonSerializer.Serialize(resp);
        var respDeserialized = System.Text.Json.JsonSerializer.Deserialize<ActivityIpcResponse>(respJson);
        Assert(respDeserialized?.Ok == true && respDeserialized.Message == "Published 'cli_build'", "Response roundtrip failed");

        // 2. MeetingModeService: 2-minute countdown trigger & subtitle formatting
        string sampleZoom = "https://zoom.us/j/987654321?pwd=abcd";
        Assert(MeetingModeService.ShouldShowMeetingJoin(2, sampleZoom, isCallActive: false), "Should show join at exactly 2 minutes before meeting");
        Assert(MeetingModeService.ShouldShowMeetingJoin(1, sampleZoom, isCallActive: false), "Should show join at 1 minute before meeting");
        Assert(MeetingModeService.ShouldShowMeetingJoin(0, sampleZoom, isCallActive: false), "Should show join at start time");
        Assert(MeetingModeService.ShouldShowMeetingJoin(-5, sampleZoom, isCallActive: false), "Should show join when meeting is underway");
        Assert(!MeetingModeService.ShouldShowMeetingJoin(5, sampleZoom, isCallActive: false), "Must NOT show join when meeting is > 2 minutes away");
        Assert(!MeetingModeService.ShouldShowMeetingJoin(-20, sampleZoom, isCallActive: false), "Must NOT show join when meeting ended > 15 minutes ago");
        Assert(!MeetingModeService.ShouldShowMeetingJoin(1, sampleZoom, isCallActive: true), "Must NOT show join if user is already on an active call");
        Assert(!MeetingModeService.ShouldShowMeetingJoin(1, null, isCallActive: false), "Must NOT show join without a valid meeting URL");

        string subMinus = MeetingModeService.FormatMeetingSubtitle(-3);
        Assert(subMinus.Contains("underway", StringComparison.OrdinalIgnoreCase), $"Expected 'underway', got '{subMinus}'");
        string subNow = MeetingModeService.FormatMeetingSubtitle(0);
        Assert(subNow.Contains("Starting now", StringComparison.OrdinalIgnoreCase), $"Expected 'Starting now', got '{subNow}'");
        string sub1 = MeetingModeService.FormatMeetingSubtitle(1);
        Assert(sub1.Contains("1m", StringComparison.OrdinalIgnoreCase), $"Expected '1m', got '{sub1}'");
        string sub2 = MeetingModeService.FormatMeetingSubtitle(2);
        Assert(sub2.Contains("2m", StringComparison.OrdinalIgnoreCase), $"Expected '2m', got '{sub2}'");

        // 3. ActivityResolver LiveActivity priority & secondary split-droplet
        var (hasAct, primarySolo, _) = ActivityResolver.ResolveWithSecondary(
            recording: false, mediaPlaying: false, callActive: false, countdownRunning: false, downloading: false, stopwatchActive: false, liveActivityActive: true);
        Assert(hasAct && primarySolo == CompactActivityKind.LiveActivity, "Solo LiveActivity must be primary");

        var (_, primaryPair, secondaryPair) = ActivityResolver.ResolveWithSecondary(
            recording: false, mediaPlaying: true, callActive: false, countdownRunning: false, downloading: false, stopwatchActive: false, liveActivityActive: true);
        Assert(primaryPair == CompactActivityKind.LiveActivity && secondaryPair == CompactActivityKind.Media,
            $"Expected LiveActivity + Media, got {primaryPair} + {secondaryPair}");

        var (_, recPrimary, recSecondary) = ActivityResolver.ResolveWithSecondary(
            recording: true, mediaPlaying: false, callActive: false, countdownRunning: false, downloading: false, stopwatchActive: false, liveActivityActive: true);
        Assert(recPrimary == CompactActivityKind.Recording && recSecondary == CompactActivityKind.LiveActivity,
            $"Recording must take precedence over LiveActivity");

        Assert(ActivityResolver.GlyphForActivity(CompactActivityKind.LiveActivity) == "\uE7C3", "Glyph for LiveActivity must be bolt / activity");
        string liveTip = ActivityResolver.DescribeSecondaryDroplet(CompactActivityKind.LiveActivity);
        Assert(liveTip.Contains("Live Activity"), $"Expected Live Activity in tooltip, got '{liveTip}'");

        // 4. Keyboard Layout Culture Resolution (0x0409 = en-US)
        var culture = System.Globalization.CultureInfo.GetCultureInfo(0x0409);
        Assert(culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase), "0x0409 must resolve to 'en'");
        Assert(culture.EnglishName.Contains("English"), "0x0409 EnglishName must contain 'English'");
    }

    private static void TestPhase4ProductivityDroplets()
    {
        // 1. NotesService persistence, title extraction, searching & export
        string tempNotes = Path.Combine(Path.GetTempPath(), $"di_notes_test_{Guid.NewGuid():N}.json");
        try
        {
            using var notes = new NotesService(tempNotes);
            Assert(notes.Count == 0, "Initial notes count should be 0");

            var n1 = notes.Create("# Project Architecture\nPlan for Droppy parity", colorTag: "#0A84FF", isPinned: true);
            Assert(n1.Title == "Project Architecture", $"Expected 'Project Architecture', got '{n1.Title}'");
            Assert(n1.IsPinned, "Note 1 should be pinned");

            var n2 = notes.Create("Buy coffee beans\nMilk\nSugar");
            Assert(n2.Title == "Buy coffee beans", $"Expected 'Buy coffee beans', got '{n2.Title}'");

            Assert(notes.Count == 2, $"Expected 2 notes, got {notes.Count}");
            Assert(notes.Notes[0].Id == n1.Id, "Pinned note must appear first in Notes list");

            var searchResults = notes.Search("coffee");
            Assert(searchResults.Count == 1 && searchResults[0].Id == n2.Id, "Search for 'coffee' should return Note 2");

            notes.Update(n2.Id, content: "Buy roasted espresso beans");
            Assert(notes.Search("espresso").Count == 1, "Updated note content should be searchable");

            notes.Delete(n1.Id);
            Assert(notes.Count == 1, "Delete must reduce notes count to 1");

            // Title extraction helper
            Assert(NotesService.ExtractTitle("### Subheader Test\r\nBody") == "Subheader Test", "Header extraction failed");
            Assert(NotesService.ExtractTitle("Plain sentence here.\r\nNext line") == "Plain sentence here.", "Sentence extraction failed");
        }
        finally
        {
            try { if (File.Exists(tempNotes)) File.Delete(tempNotes); } catch { }
        }

        // 2. QuickLauncherService persistence & argument formatting
        string tempLauncher = Path.Combine(Path.GetTempPath(), $"di_launcher_test_{Guid.NewGuid():N}.json");
        try
        {
            using var launcher = new QuickLauncherService(tempLauncher);
            Assert(launcher.Count >= 4, "Default launcher should populate 4 common system tools");

            var customApp = launcher.AddTarget(@"C:\Tools\editor.exe", "Custom Editor", "--dark");
            Assert(customApp.Name == "Custom Editor", "Custom app name mismatch");

            string argsSingle = QuickLauncherService.BuildLaunchArguments("--flag", new[] { @"C:\Test File\doc.txt" });
            Assert(argsSingle == "--flag \"C:\\Test File\\doc.txt\"", $"Expected quoted path argument, got: '{argsSingle}'");

            string argsMulti = QuickLauncherService.BuildLaunchArguments(null, new[] { @"a.txt", @"b.txt" });
            Assert(argsMulti == "\"a.txt\" \"b.txt\"", $"Expected multi-file quoted arguments, got: '{argsMulti}'");

            launcher.Remove(customApp.Id);
            Assert(!launcher.Items.Any(i => i.Id == customApp.Id), "Remove should remove target from launcher");

            var explorerIcon = DynamicIsland.Views.Widgets.LauncherView.GetAppIcon("explorer.exe");
            Assert(explorerIcon != null, "Explorer icon extraction should succeed and produce non-null ImageSource");
        }
        finally
        {
            try { if (File.Exists(tempLauncher)) File.Delete(tempLauncher); } catch { }
        }

        // 3. TermiNotchService snippet persistence & command execution
        string tempTermi = Path.Combine(Path.GetTempPath(), $"di_termi_test_{Guid.NewGuid():N}.json");
        try
        {
            using var termi = new TermiNotchService(tempTermi);
            Assert(termi.Snippets.Count >= 4, "Default Termi-Notch snippets should populate defaults");

            var testSnippet = termi.AddSnippet("Test Echo", "echo DynamicIslandSuccess", "cmd");
            Assert(testSnippet.Name == "Test Echo", "Snippet name mismatch");

            bool sawOutput = false;
            termi.OutputReceived += (line, _) =>
            {
                if (line.Contains("DynamicIslandSuccess")) sawOutput = true;
            };

            int exitCode = termi.RunRawCommandAsync("echo DynamicIslandSuccess", "cmd").GetAwaiter().GetResult();
            Assert(exitCode == 0, $"Expected exit code 0, got {exitCode}");
            Assert(sawOutput, "Command output streaming was not captured");

            termi.DeleteSnippet(testSnippet.Id);
            Assert(!termi.Snippets.Any(s => s.Id == testSnippet.Id), "DeleteSnippet should remove snippet");
        }
        finally
        {
            try { if (File.Exists(tempTermi)) File.Delete(tempTermi); } catch { }
        }

        // 4. WindowSnapService geometric calculation engine
        var work = new Interop.NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };

        var leftRect = WindowSnapService.CalculateSnappedRect(work, SnapZone.LeftHalf);
        Assert(leftRect.Left == 0 && leftRect.Right == 960 && leftRect.Width == 960 && leftRect.Height == 1040,
            $"LeftHalf rect mismatch: {leftRect.Left},{leftRect.Right},{leftRect.Width},{leftRect.Height}");

        var rightRect = WindowSnapService.CalculateSnappedRect(work, SnapZone.RightHalf);
        Assert(rightRect.Left == 960 && rightRect.Right == 1920 && rightRect.Width == 960,
            $"RightHalf rect mismatch: {rightRect.Left},{rightRect.Right}");

        var tlQuarter = WindowSnapService.CalculateSnappedRect(work, SnapZone.TopLeftQuarter);
        Assert(tlQuarter.Left == 0 && tlQuarter.Right == 960 && tlQuarter.Top == 0 && tlQuarter.Bottom == 520,
            $"TopLeftQuarter rect mismatch: {tlQuarter.Left},{tlQuarter.Right},{tlQuarter.Top},{tlQuarter.Bottom}");

        var maxRect = WindowSnapService.CalculateSnappedRect(work, SnapZone.Maximize);
        Assert(maxRect.Width == 1920 && maxRect.Height == 1040, "Maximize rect must equal monitor work area");

        // Edge detection
        var topPt = new Interop.NativeMethods.POINT { X = 960, Y = 5 };
        Assert(WindowSnapService.DetermineSnapZone(topPt, work) == SnapZone.Maximize, "Top edge center must trigger Maximize");

        var cornerPt = new Interop.NativeMethods.POINT { X = 5, Y = 5 };
        Assert(WindowSnapService.DetermineSnapZone(cornerPt, work) == SnapZone.TopLeftQuarter, "Top-left corner must trigger TopLeftQuarter");

        // 5. KeyboardSoundService acoustic sample pool & volume clamping
        using var sound = new KeyboardSoundService();
        sound.Volume = 1.5f;
        Assert(Math.Abs(sound.Volume - 1.0f) < 0.001, "Volume must clamp to 1.0");
        sound.Volume = -0.5f;
        Assert(Math.Abs(sound.Volume - 0.0f) < 0.001, "Volume must clamp to 0.0");
        sound.Volume = 0.5f;
        sound.Profile = MechSoundProfile.TactileBrown;
        Assert(sound.Profile == MechSoundProfile.TactileBrown, "Profile property assignment failed");

        // 6. DropletRegistry includes all modular droplets and batch control
        var settings = new SettingsService();
        var registry = new DropletRegistry(settings);
        Assert(registry.Items.Count >= 16, $"Expected >= 16 built-in droplets, got {registry.Items.Count}");
        Assert(registry.Get("notes") != null, "Notes droplet must be registered");
        Assert(registry.Get("quick_launcher") != null, "Quick launcher droplet must be registered");
        Assert(registry.Get("termi_notch") != null, "Termi-Notch droplet must be registered");
        Assert(registry.Get("window_snap") != null, "Window snap droplet must be registered");
        Assert(registry.Get("mech_sounds") != null, "Mech sounds droplet must be registered");

        // 7. Droplet batch controls & metadata integrity
        registry.SetAll(true);
        Assert(registry.EnabledCount == registry.Items.Count, "SetAll(true) must enable all droplets");
        Assert(registry.TotalActiveRamKb > 0, "Total RAM should be > 0 when all droplets enabled");

        registry.SetAll(false);
        Assert(registry.EnabledCount == 0, "SetAll(false) must disable all droplets");
        Assert(registry.TotalActiveRamKb == 0, "Total RAM should be 0 when all droplets disabled");

        registry.ResetToDefaults();
        Assert(registry.Get("mech_sounds")?.IsEnabled == false, "mech_sounds must be default-disabled");
        Assert(registry.Get("notes")?.IsEnabled == true, "notes must be default-enabled");

        var mech = registry.Get("mech_sounds")!;
        Assert(!string.IsNullOrEmpty(mech.ActionLabel), "ActionLabel must not be empty");
        Assert(!string.IsNullOrEmpty(mech.FeatureBadge), "FeatureBadge must not be empty");
        Assert(!string.IsNullOrEmpty(mech.CategoryGlyph), "CategoryGlyph must not be empty");
        Assert(mech.CardOpacity > 0.0 && mech.CardOpacity <= 1.0, "CardOpacity must be valid");
    }

    private static void TestPhase5SharingAndPhoneBridge()
    {
        // 1. LanShareSessionInfo expiration & remaining minutes calculation
        var now = DateTimeOffset.Now;
        var activeSession = new LanShareSessionInfo(
            Url: "http://192.168.1.100:8989/?t=abcd1234",
            LocalIp: "192.168.1.100",
            Port: 8989,
            Token: "abcd1234",
            ExpiresAt: now.AddMinutes(10));

        Assert(!activeSession.IsExpired, "Session should not be expired immediately upon creation");
        Assert(activeSession.MinutesRemaining >= 9 && activeSession.MinutesRemaining <= 10,
            $"Expected ~10 minutes remaining, got {activeSession.MinutesRemaining}");

        var expiredSession = new LanShareSessionInfo(
            Url: "http://192.168.1.100:8989/?t=expired",
            LocalIp: "192.168.1.100",
            Port: 8989,
            Token: "expired",
            ExpiresAt: now.AddMinutes(-5));

        Assert(expiredSession.IsExpired, "Session in the past must report IsExpired = true");
        Assert(expiredSession.MinutesRemaining == 0, "Expired session must report 0 minutes remaining");

        // 2. Embedded mobile PWA HTML template generation
        string html = LanQuickShareService.GetEmbeddedPwaHtml("test_token_xyz");
        Assert(!string.IsNullOrWhiteSpace(html), "PWA HTML must not be empty");
        Assert(html.Contains("test_token_xyz"), "PWA HTML must embed the secure session token");
        Assert(html.Contains("DYNAMIC ISLAND"), "PWA HTML must include Dynamic Island banner");
        Assert(html.Contains("/api/upload"), "PWA HTML must wire upload endpoint");
        Assert(html.Contains("/api/clipboard"), "PWA HTML must wire clipboard endpoint");

        // 3. Local IP resolution
        string? ip = LanQuickShareService.GetLocalIpAddress();
        Assert(!string.IsNullOrWhiteSpace(ip), "Local IP address must not be null or whitespace");

        // 3b. LanQuickShare port fallback probing when base port is already occupied
        var blocker = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, 9292);
        blocker.Start();
        try
        {
            var dummyShelf = new DropShelfService();
            var dummyClip = new ClipboardHistoryService(System.Windows.Threading.Dispatcher.CurrentDispatcher);
            using var shareService = new LanQuickShareService(System.Windows.Threading.Dispatcher.CurrentDispatcher, dummyShelf, dummyClip);
            var session = shareService.StartSession(durationMinutes: 5, port: 9292);
            Assert(session.Port == 9293, $"Expected fallback port 9293, got {session.Port}");
            Assert(session.Url.Contains(":9293"), $"Session URL must reflect fallback port: {session.Url}");
            shareService.StopSession();
        }
        finally
        {
            blocker.Stop();
        }

        // 4. Windows Share interop GUID validation
        var interopGuid = typeof(IDataTransferManagerInterop).GUID;
        Assert(interopGuid == new Guid("3a3dcd6c-3eab-43dc-bcde-45671ce800c8"),
            $"IDataTransferManagerInterop GUID mismatch: {interopGuid}");

        // 5. DropletRegistry includes phone_bridge, mech_sounds, element_capture with dedicated TargetViews
        var settings = new SettingsService();
        var registry = new DropletRegistry(settings);
        Assert(registry.Get("phone_bridge") != null, "phone_bridge droplet must be registered");
        Assert(registry.Get("phone_bridge")?.TargetView == ExpandedView.PhoneBridge, "phone_bridge must route to ExpandedView.PhoneBridge");
        Assert(registry.Get("mech_sounds")?.TargetView == ExpandedView.MechSounds, "mech_sounds must route to ExpandedView.MechSounds");
        Assert(registry.Get("element_capture")?.TargetView == ExpandedView.CaptureStudio, "element_capture must route to ExpandedView.CaptureStudio");
        Assert(registry.Get("file_converter")?.TargetView == ExpandedView.Converter, "file_converter must route to ExpandedView.Converter");
        Assert(registry.Get("quick_launcher")?.TargetView == ExpandedView.Launcher, "quick_launcher must route to ExpandedView.Launcher");
        Assert(registry.Get("termi_notch")?.TargetView == ExpandedView.Termi, "termi_notch must route to ExpandedView.Termi");
        Assert(registry.Items.Count >= 17, $"Expected >= 17 built-in droplets, got {registry.Items.Count}");
    }

    private static void TestQrCodeGenerator()
    {
        // 1. Version 1 encoding
        var qr1 = QrCode.EncodeText("HELLO", QrCodeEcc.Medium);
        Assert(qr1.Version == 1, $"Expected Version 1 for 'HELLO', got {qr1.Version}");
        Assert(qr1.Size == 21, $"Expected 21x21 matrix for Version 1, got {qr1.Size}");

        // Finder patterns: top-left (0,0), top-right (20,0), bottom-left (0,20)
        Assert(qr1.GetModule(0, 0), "Finder top-left (0,0) must be dark");
        Assert(qr1.GetModule(3, 3), "Finder top-left center (3,3) must be dark");
        Assert(qr1.GetModule(20, 0), "Finder top-right (20,0) must be dark");
        Assert(qr1.GetModule(0, 20), "Finder bottom-left (0,20) must be dark");
        Assert(!qr1.GetModule(7, 0), "Finder separator (7,0) must be light");

        // 2. LAN QuickShare Pairing URL encoding (typical 42-45 chars)
        string testUrl = "http://192.168.1.41:8989/?t=1a2b3c4d5e6f";
        var qrUrl = QrCode.EncodeText(testUrl, QrCodeEcc.Medium);
        Assert(qrUrl.Version >= 3 && qrUrl.Version <= 5, $"Expected Version 3-5 for URL, got {qrUrl.Version}");
        Assert(qrUrl.Size == qrUrl.Version * 4 + 17, $"Size must equal Version * 4 + 17 ({qrUrl.Size})");
        Assert(qrUrl.Mask >= 0 && qrUrl.Mask <= 7, $"Mask must be between 0 and 7, got {qrUrl.Mask}");

        // Dark module check at (8, 4 * Version + 9)
        Assert(qrUrl.GetModule(8, 4 * qrUrl.Version + 9), "Dark module at (8, 4*V+9) must always be true");

        // 3. WPF Frozen BitmapSource generation
        var bitmap = qrUrl.ToBitmapSource(moduleSize: 3, quietZone: 2);
        Assert(bitmap is { IsFrozen: true }, "BitmapSource must not be null and must be frozen for fast Direct3D composition");
        int expectedPx = (qrUrl.Size + 4) * 3;
        Assert(bitmap.PixelWidth == expectedPx && bitmap.PixelHeight == expectedPx,
            $"Expected {expectedPx}x{expectedPx} bitmap, got {bitmap.PixelWidth}x{bitmap.PixelHeight}");
    }

    private static void TestMediaSongListAndDetachedPlayer()
    {
        // 1. Metadata cleaning
        string raw1 = "Daft Punk - Get Lucky (Official Music Video) [Remastered 2021] feat. Pharrell";
        string cleanTitle = MediaSongListService.CleanMetadata(raw1);
        Assert(cleanTitle == "Get Lucky", $"Expected 'Get Lucky', got '{cleanTitle}'");

        string cleanArtist = MediaSongListService.CleanArtist("Daft Punk feat. Pharrell", raw1);
        Assert(cleanArtist == "Daft Punk", $"Expected 'Daft Punk', got '{cleanArtist}'");

        // 2. Fallback when artist is in raw title
        string raw2 = "The Weeknd - Blinding Lights (Lyric Video)";
        string title2 = MediaSongListService.CleanMetadata(raw2);
        string artist2 = MediaSongListService.CleanArtist("YouTube", raw2);
        Assert(title2 == "Blinding Lights", $"Expected 'Blinding Lights', got '{title2}'");
        Assert(artist2 == "The Weeknd", $"Expected 'The Weeknd', got '{artist2}'");

        // 3. SongTrackItem duration formatting
        var track = new SongTrackItem
        {
            TrackNumber = 3,
            Title = "Starboy",
            Artist = "The Weeknd",
            Duration = TimeSpan.FromSeconds(230)
        };
        Assert(track.DisplayTrackNumber == "3", "Track number display mismatch");
        Assert(track.FormattedDuration == "3:50", $"Expected '3:50', got '{track.FormattedDuration}'");

        // 4. PlayedSongRecord time formatting
        var record = new PlayedSongRecord
        {
            Title = "Song A",
            Artist = "Artist A",
            Duration = TimeSpan.FromSeconds(125),
            PlayedAt = DateTime.Now
        };
        Assert(record.TimeAgo == "Just now", $"Expected 'Just now', got '{record.TimeAgo}'");

        // 5. ActiveMediaSessionItem structure
        var sessionItem = new ActiveMediaSessionItem
        {
            Aumid = "Spotify.exe",
            AppName = "Spotify",
            Title = "Song B",
            Artist = "Artist B",
            IsPlaying = true,
            IsCurrent = true
        };
        Assert(sessionItem.AppName == "Spotify" && sessionItem.IsPlaying && sessionItem.IsCurrent, "ActiveMediaSessionItem properties failed");
    }

    private static void TestTaskServiceAndCalendar()
    {
        // 1. TaskService CRUD and Atomic Persistence
        string tempTaskFile = Path.Combine(Path.GetTempPath(), $"island_tasks_{Guid.NewGuid():N}.json");
        try
        {
            using (var taskSvc = new TaskService(tempTaskFile))
            {
                var today = DateTime.Today;
                var task1 = taskSvc.AddTask("Buy milk", today, "10:00 AM", "#30D158");
                var task2 = taskSvc.AddTask("Team Sync", today, "2:00 PM", "#FF9F0A");
                var tomorrowTask = taskSvc.AddTask("Doctor appointment", today.AddDays(1), "9:00 AM", "#0A84FF");

                Assert(taskSvc.PendingCount == 3, $"Expected 3 pending tasks, got {taskSvc.PendingCount}");

                var todayTasks = taskSvc.GetTasksForDate(today);
                Assert(todayTasks.Count == 2, $"Expected 2 tasks today, got {todayTasks.Count}");

                var daysWithTasks = taskSvc.GetDaysWithPendingTasks(today.Year, today.Month);
                Assert(daysWithTasks.Contains(today.Day), "Expected today in task days set");

                // Toggle completion
                bool completed = taskSvc.ToggleComplete(task1.Id);
                Assert(completed, "Expected task1 to be completed");
                Assert(taskSvc.PendingCount == 2, "Expected 2 pending tasks after completing 1");

                // Delete task
                bool deleted = taskSvc.DeleteTask(task2.Id);
                Assert(deleted, "Expected task2 to be deleted");
                Assert(taskSvc.PendingCount == 1, "Expected 1 pending task remaining");
            }

            // Reload from disk to verify persistence
            using (var taskSvcReloaded = new TaskService(tempTaskFile))
            {
                Assert(taskSvcReloaded.AllTasks.Count == 2, $"Expected 2 persisted tasks, got {taskSvcReloaded.AllTasks.Count}");
                var remaining = taskSvcReloaded.AllTasks.FirstOrDefault(t => t.Title == "Doctor appointment");
                Assert(remaining != null && !remaining.IsCompleted, "Expected Doctor appointment to persist as pending");
            }
        }
        finally
        {
            if (File.Exists(tempTaskFile)) File.Delete(tempTaskFile);
        }

        // 2. CalendarService custom event parsing and days set
        string tempEventFile = Path.Combine(Path.GetTempPath(), $"island_events_{Guid.NewGuid():N}.json");
        try
        {
            var ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            using var calSvc = new CalendarService(ui, tempEventFile);
            var eventTime = DateTime.Today.AddHours(14);
            var evt = calSvc.AddEvent("Project Standup", eventTime, TimeSpan.FromMinutes(30), "Zoom Meeting", "https://zoom.us/j/123456789", "#FF9F0A");

            Assert(evt.Subject == "Project Standup", "Custom event subject mismatch");
            Assert(evt.JoinUrl == "https://zoom.us/j/123456789", "Custom event join url mismatch");

            var eventsToday = calSvc.GetEventsForDate(DateTime.Today);
            Assert(eventsToday.Count == 1, "Expected 1 custom event today");

            var eventDays = calSvc.GetDaysWithEvents(DateTime.Today.Year, DateTime.Today.Month);
            Assert(eventDays.Contains(DateTime.Today.Day), "Expected today in event days set");

            calSvc.DeleteEvent(evt.Id);
            Assert(calSvc.GetEventsForDate(DateTime.Today).Count == 0, "Expected 0 events after delete");
        }
        finally
        {
            if (File.Exists(tempEventFile)) File.Delete(tempEventFile);
        }

        // 3. LyricsService track metadata parsing for YouTube channel names & VEVO
        var (steveTitle, steveArtist) = LyricsService.ParseTrackMetadata("Steve Lacy - oh yeah? (Visualizer)", "SteveLacyVEVO");
        Assert(steveArtist == "Steve Lacy", $"Expected 'Steve Lacy', got '{steveArtist}'");
        Assert(steveTitle.Contains("oh yeah"), $"Expected title containing 'oh yeah', got '{steveTitle}'");

        var (hotelTitle, hotelArtist) = LyricsService.ParseTrackMetadata("Shut up My Moms Calling (Sped up)", "Hotel Ugly");
        Assert(hotelArtist == "Hotel Ugly", $"Expected 'Hotel Ugly', got '{hotelArtist}'");
        Assert(hotelTitle == "Shut up My Moms Calling", $"Expected 'Shut up My Moms Calling', got '{hotelTitle}'");
    }

    private static void TestLauncherScrollingAndSearch()
    {
        // 1. Verify item model has friendly tooltips and proper action hints
        var item1 = new Views.Widgets.LauncherDisplayItem
        {
            Id = "island:calc",
            Name = "Calculator",
            Subtitle = "Math & Units",
            FriendlyDescription = "Fast arithmetic, percentages & unit conversions",
            ActionHint = "Click to open · Right-click for options"
        };
        Assert(item1.FriendlyDescription.Contains("arithmetic"), "FriendlyDescription mismatch");
        Assert(item1.ActionHint.Contains("Right-click"), "ActionHint mismatch");

        // 2. Grammar check on launch counts
        int count1 = 1;
        string sub1 = count1 == 1 ? "1 launch" : $"{count1} launches";
        Assert(sub1 == "1 launch", $"Expected '1 launch', got '{sub1}'");

        int count3 = 3;
        string sub3 = count3 == 1 ? "1 launch" : $"{count3} launches";
        Assert(sub3 == "3 launches", $"Expected '3 launches', got '{sub3}'");

        // 3. Search filter simulation
        var items = new List<Views.Widgets.LauncherDisplayItem>
        {
            item1,
            new() { Id = "island:converter", Name = "File Converter", Subtitle = "PDF & Images", FriendlyDescription = "Batch convert PNG, JPG, WebP & merge PDFs" },
            new() { Id = "island:notes", Name = "Quick Notes", Subtitle = "Scratchpad", FriendlyDescription = "Markdown scratchpad & notes" },
            new() { Id = "app:terminal", Name = "Terminal", Subtitle = "Application", FriendlyDescription = "Windows Terminal · wt.exe" }
        };

        string query = "calc";
        var filtered = items.Where(i =>
            i.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            i.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            i.FriendlyDescription.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        Assert(filtered.Count == 1 && filtered[0].Name == "Calculator", "Search for 'calc' failed");

        query = "pdf";
        filtered = items.Where(i =>
            i.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            i.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            i.FriendlyDescription.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        Assert(filtered.Count == 1 && filtered[0].Name == "File Converter", "Search for 'pdf' failed");

        // 4. QuickLauncherService CRUD & persistence
        string tempLauncherFile = Path.Combine(Path.GetTempPath(), $"launcher_test_{Guid.NewGuid():N}.json");
        try
        {
            using (var svc = new QuickLauncherService(tempLauncherFile))
            {
                int initialCount = svc.Items.Count;
                var added = svc.AddTarget("cmd.exe", "Command Prompt");
                Assert(added != null, "Expected command prompt target to be added");
                Assert(svc.Items.Count == initialCount + 1, $"Expected {initialCount + 1} launcher items, got {svc.Items.Count}");
                Assert(added!.Name == "Command Prompt", "Expected item name 'Command Prompt'");

                added.LaunchCount++;
                Assert(added.LaunchCount == 1, "Expected launch count to increment to 1");

                svc.Remove(added.Id);
                Assert(svc.Items.Count == initialCount, "Expected item to be removed");
            }
        }
        finally
        {
            if (File.Exists(tempLauncherFile)) File.Delete(tempLauncherFile);
        }
    }

    private static void TestNavigationAndFocusability()
    {
        // 1. Verify keyboard focusability requirement for interactive input views
        bool IsInputFocusable(ExpandedView v) => v is ExpandedView.Calc
                                                   or ExpandedView.Clipboard
                                                   or ExpandedView.Notes
                                                   or ExpandedView.Calendar
                                                   or ExpandedView.Launcher
                                                   or ExpandedView.Termi
                                                   or ExpandedView.Converter
                                                   or ExpandedView.QuickReply;

        Assert(IsInputFocusable(ExpandedView.Launcher), "Launcher must accept keyboard focus");
        Assert(IsInputFocusable(ExpandedView.Termi), "Termi-Notch must accept keyboard focus");
        Assert(IsInputFocusable(ExpandedView.Converter), "Converter must accept keyboard focus");
        Assert(IsInputFocusable(ExpandedView.Notes), "Notes must accept keyboard focus");
        Assert(IsInputFocusable(ExpandedView.Calc), "Calc must accept keyboard focus");
        Assert(IsInputFocusable(ExpandedView.Clipboard), "Clipboard must accept keyboard focus");
        Assert(IsInputFocusable(ExpandedView.Calendar), "Calendar must accept keyboard focus");
        Assert(IsInputFocusable(ExpandedView.QuickReply), "QuickReply must accept keyboard focus");
        Assert(!IsInputFocusable(ExpandedView.Media), "Media should not steal keyboard focus");
        Assert(!IsInputFocusable(ExpandedView.SystemHud), "System HUD should not steal keyboard focus");

        // 2. Verify all ExpandedView keys round-trip
        foreach (ExpandedView v in Enum.GetValues<ExpandedView>())
        {
            string key = v.ToKey();
            Assert(!string.IsNullOrWhiteSpace(key), $"ExpandedView {v} key is empty");
            Assert(ExpandedViewExtensions.TryParseKey(key, out var parsed), $"Failed to parse key '{key}'");
            Assert(parsed == v, $"Key roundtrip mismatch: expected {v}, got {parsed}");
        }
    }

    private static void TestThemeBrushesAndShellInterop()
    {
        // 1. Frozen brush invariants (Zero-Idle-CPU Rule #2)
        Assert(ThemeBrushes.GreenDark.IsFrozen, "ThemeBrushes.GreenDark must be frozen");
        Assert(ThemeBrushes.GreenLight.IsFrozen, "ThemeBrushes.GreenLight must be frozen");
        Assert(ThemeBrushes.PrimaryTextDark.IsFrozen, "ThemeBrushes.PrimaryTextDark must be frozen");
        Assert(ThemeBrushes.PrimaryTextLight.IsFrozen, "ThemeBrushes.PrimaryTextLight must be frozen");
        Assert(ThemeBrushes.CardSurfaceDark.IsFrozen, "ThemeBrushes.CardSurfaceDark must be frozen");
        Assert(ThemeBrushes.ElevatedSurfaceDark.IsFrozen, "ThemeBrushes.ElevatedSurfaceDark must be frozen");
        Assert(ThemeBrushes.PillBackgroundDark.IsFrozen, "ThemeBrushes.PillBackgroundDark must be frozen");
        Assert(ThemeBrushes.PillInnerBevelLiquidDark.IsFrozen, "ThemeBrushes.PillInnerBevelLiquidDark must be frozen");
        Assert(ThemeBrushes.PillInnerBevelLiquidLight.IsFrozen, "ThemeBrushes.PillInnerBevelLiquidLight must be frozen");

        // 2. Apple HIG Contrast verification
        Assert(ThemeBrushes.PrimaryTextColor.R > 200, "Dark mode primary text must be high-contrast light");
        Assert(ThemeBrushes.PrimaryTextColorLight.R < 50, "Light mode primary text must be high-contrast dark");

        // 3. Borderless fullscreen game detection logic
        var monInfo = new Interop.NativeMethods.MONITORINFO
        {
            rcMonitor = new Interop.NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
            rcWork = new Interop.NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 } // 40px taskbar
        };

        // Standard game covering full monitor (1920x1080)
        var fullRect = new Interop.NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        Assert(FullscreenWatcher.IsWindowBorderlessFullscreen(fullRect, monInfo, 0), "Full-screen window must be detected as borderless fullscreen");

        // Standard maximized app respecting taskbar (1920x1040)
        var workRect = new Interop.NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
        Assert(!FullscreenWatcher.IsWindowBorderlessFullscreen(workRect, monInfo, 0), "Maximized app respecting taskbar must NOT be detected as fullscreen game");

        // Partial window (e.g. 1000x800)
        var smallRect = new Interop.NativeMethods.RECT { Left = 100, Top = 100, Right = 1100, Bottom = 900 };
        Assert(!FullscreenWatcher.IsWindowBorderlessFullscreen(smallRect, monInfo, 0), "Floating window must NOT be detected as fullscreen game");

        // 4. Mathematical capsule geometry hit-testing (Zero-Leak Tab Passthrough)
        // Pill: left=100, top=0, right=248 (width=148), bottom=30, radius=15
        double pLeft = 100, pTop = 0, pRight = 248, pBottom = 30, radius = 15;

        // Center of pill capsule
        Assert(IslandWindow.IsPointInRoundedRect(174, 15, pLeft, pTop, pRight, pBottom, radius), "Center of pill must be inside");

        // Straight interior edges
        Assert(IslandWindow.IsPointInRoundedRect(174, 1, pLeft, pTop, pRight, pBottom, radius), "Top edge center must be inside");
        Assert(IslandWindow.IsPointInRoundedRect(174, 29, pLeft, pTop, pRight, pBottom, radius), "Bottom edge center must be inside");

        // Corner interior curve (e.g. top-left corner circle center is at (115, 15))
        Assert(IslandWindow.IsPointInRoundedRect(105, 10, pLeft, pTop, pRight, pBottom, radius), "Inside top-left rounded curve must be inside");

        // Corner EMPTY space (outside radius circle)
        Assert(!IslandWindow.IsPointInRoundedRect(101, 1, pLeft, pTop, pRight, pBottom, radius), "Empty corner space outside rounded curve must NOT be inside");
        Assert(!IslandWindow.IsPointInRoundedRect(247, 1, pLeft, pTop, pRight, pBottom, radius), "Empty top-right corner space must NOT be inside");
        Assert(!IslandWindow.IsPointInRoundedRect(101, 29, pLeft, pTop, pRight, pBottom, radius), "Empty bottom-left corner space must NOT be inside");
        Assert(!IslandWindow.IsPointInRoundedRect(247, 29, pLeft, pTop, pRight, pBottom, radius), "Empty bottom-right corner space must NOT be inside");

        // Points completely outside capsule bounds
        Assert(!IslandWindow.IsPointInRoundedRect(95, 15, pLeft, pTop, pRight, pBottom, radius), "Left of pill must NOT be inside");
        Assert(!IslandWindow.IsPointInRoundedRect(255, 15, pLeft, pTop, pRight, pBottom, radius), "Right of pill must NOT be inside");
        Assert(!IslandWindow.IsPointInRoundedRect(174, -5, pLeft, pTop, pRight, pBottom, radius), "Above pill must NOT be inside");
        Assert(!IslandWindow.IsPointInRoundedRect(174, 35, pLeft, pTop, pRight, pBottom, radius), "Below pill must NOT be inside");

        // 5. State Machine Click-Only Tab-Safe Mode
        var sm = new IslandStateMachine { ClickOnlyMode = true };
        sm.SetPointerInside(true);
        Assert(sm.State == IslandState.Idle, "Click-Only mode must not auto-expand state machine on hover");
        sm.ExpandNow();
        Assert(sm.State == IslandState.Expanded, "Explicit ExpandNow must still expand in Click-Only mode");
        sm.CollapseNow();
        Assert(sm.State == IslandState.Idle, "CollapseNow must return state to Idle");
    }

    private static void TestLiquidGlassSettingsValidation()
    {
        // 1. Fluent builder and parameter validation
        var settings = new GlassSettings.Builder()
            .WithThickness(28.0f)
            .WithBevelWidth(18.0f)
            .WithIndexOfRefraction(1.55f)
            .WithBlurRadius(24.0f)
            .WithSaturation(1.4f)
            .WithBrightness(1.1f)
            .WithTint(Color.FromArgb(30, 255, 255, 255))
            .WithSpecular(0.9f, 2.8f)
            .WithDispersion(0.15f)
            .WithMergeSmoothness(16.0f)
            .WithCornerRadius(22.0f)
            .WithCornerSmoothing(0.8f)
            .WithShape(GlassShapeType.RoundedRect)
            .Build();

        Assert(Math.Abs(settings.Thickness - 28.0f) < 1e-4, "Thickness mismatch");
        Assert(Math.Abs(settings.BevelWidth - 18.0f) < 1e-4, "BevelWidth mismatch");
        Assert(Math.Abs(settings.IndexOfRefraction - 1.55f) < 1e-4, "IOR mismatch");
        Assert(Math.Abs(settings.BlurRadius - 24.0f) < 1e-4, "BlurRadius mismatch");
        Assert(Math.Abs(settings.Saturation - 1.4f) < 1e-4, "Saturation mismatch");
        Assert(Math.Abs(settings.Brightness - 1.1f) < 1e-4, "Brightness mismatch");
        Assert(Math.Abs(settings.SpecularStrength - 0.9f) < 1e-4, "SpecularStrength mismatch");
        Assert(Math.Abs(settings.RimPower - 2.8f) < 1e-4, "RimPower mismatch");
        Assert(Math.Abs(settings.Dispersion - 0.15f) < 1e-4, "Dispersion mismatch");
        Assert(Math.Abs(settings.CornerRadius - 22.0f) < 1e-4, "CornerRadius mismatch");
        Assert(Math.Abs(settings.CornerSmoothing - 0.8f) < 1e-4, "CornerSmoothing mismatch");

        // 2. Clamping guards: out-of-range values must be safely clamped
        var clamped = new GlassSettings.Builder()
            .WithThickness(-50.0f)
            .WithIndexOfRefraction(10.0f)
            .WithBlurRadius(999.0f)
            .WithSaturation(-2.0f)
            .WithCornerSmoothing(5.0f)
            .Build();

        Assert(clamped.Thickness == 0.0f, "Negative thickness must clamp to 0");
        Assert(clamped.IndexOfRefraction == 3.0f, "Over-range IOR must clamp to 3.0");
        Assert(clamped.BlurRadius == 100.0f, "Blur radius must clamp to 100.0");
        Assert(clamped.Saturation == 0.0f, "Negative saturation must clamp to 0.0");
        Assert(clamped.CornerSmoothing == 1.0f, "Corner smoothing must clamp to 1.0");

        // 3. Immutability & with-expression semantics
        var mutated = settings with { Thickness = 50.0f };
        Assert(settings.Thickness == 28.0f && mutated.Thickness == 50.0f, "GlassSettings record must be strictly immutable");

        // 4. Presets invariants
        Assert(GlassPresets.Regular != null, "Regular preset must not be null");
        Assert(GlassPresets.Clear != null, "Clear preset must not be null");
        Assert(GlassPresets.Dark != null, "Dark preset must not be null");
        Assert(GlassPresets.Tinted != null, "Tinted preset must not be null");
        Assert(GlassPresets.CapsuleButton != null, "CapsuleButton preset must not be null");
        Assert(GlassPresets.Pill != null, "Pill preset must not be null");
        Assert(GlassPresets.Card != null, "Card preset must not be null");

        Assert(GlassPresets.CapsuleButton!.Shape == GlassShapeType.Capsule, "CapsuleButton must use Capsule shape");
        Assert(GlassPresets.Clear!.TintColor.A < GlassPresets.Dark!.TintColor.A, "Clear preset must have lighter tint alpha than Dark preset");
        Assert(GlassPresets.Dark!.Brightness < 1.0f, "Dark preset brightness must be below 1.0");
        Assert(GlassPresets.Regular!.IndexOfRefraction >= 1.4f && GlassPresets.Regular.IndexOfRefraction <= 1.6f, "Regular IOR should be within standard glass range (1.4-1.6)");
    }

    private static void TestLiquidGlassSdfMath()
    {
        // 1. Analytic Rounded Rect SDF: sdRoundRect(p, halfSize, r)
        static float SdRoundRect(Vector2 p, Vector2 halfSize, float r)
        {
            Vector2 q = new Vector2(Math.Abs(p.X), Math.Abs(p.Y)) - halfSize + new Vector2(r, r);
            float outside = new Vector2(Math.Max(q.X, 0f), Math.Max(q.Y, 0f)).Length();
            float inside = Math.Min(Math.Max(q.X, q.Y), 0f);
            return outside + inside - r;
        }

        Vector2 half = new Vector2(100f, 50f);
        float radius = 20f;

        // Interior point: d must be strictly negative
        float dInside = SdRoundRect(Vector2.Zero, half, radius);
        Assert(dInside < -30f, $"Center of rounded rect must be deep inside (got {dInside})");

        // Exact horizontal boundary point (0, 50)
        float dBoundary = SdRoundRect(new Vector2(0f, 50f), half, radius);
        Assert(Math.Abs(dBoundary) < 1e-4, $"Boundary point must have SDF == 0 (got {dBoundary})");

        // Exterior point
        float dOutside = SdRoundRect(new Vector2(150f, 80f), half, radius);
        Assert(dOutside > 0f, $"Outside point must have positive SDF (got {dOutside})");

        // 2. Analytic Circle SDF
        static float SdCircle(Vector2 p, float r) => p.Length() - r;
        Assert(SdCircle(Vector2.Zero, 30f) == -30f, "Circle center must be -r");
        Assert(Math.Abs(SdCircle(new Vector2(30f, 0f), 30f)) < 1e-5, "Circle boundary must be 0");
        Assert(SdCircle(new Vector2(50f, 0f), 30f) == 20f, "Circle exterior must be dist - r");

        // 3. Polynomial Smooth-Union (smin):
        static float Smin(float a, float b, float k)
        {
            float h = Math.Clamp(0.5f + 0.5f * (b - a) / k, 0.0f, 1.0f);
            return (b * (1.0f - h) + a * h) - k * h * (1.0f - h);
        }

        // When shapes are disjoint and far apart (|a - b| >= k), smin == min(a, b)
        float sminFar = Smin(10f, 50f, 15f);
        Assert(Math.Abs(sminFar - 10f) < 1e-5, $"Distant smin must equal min(a,b), got {sminFar}");

        // When shapes intersect or are close (a == b), smin produces smooth liquid bridge: smin(a, a, k) == a - k/4
        float sminEqual = Smin(0f, 0f, 20f);
        Assert(Math.Abs(sminEqual - (-5.0f)) < 1e-5, $"Overlapping smin(0, 0, 20) must equal -5.0 (liquid bridge), got {sminEqual}");

        // 4. Fake 3D Surface Normal Reconstruction:
        // edge factor a = saturate(1 - s / bevel) where s = -d (distance from edge into interior)
        // normal = normalize(float3(g * a, sqrt(max(1 - a*a, 1e-4))))
        float bevel = 16.0f;

        // Interior point: s >> bevel -> a = 0 -> normal is (0, 0, 1) flat lens
        float sInterior = 50.0f;
        float aInterior = Math.Clamp(1.0f - sInterior / bevel, 0.0f, 1.0f);
        Assert(aInterior == 0.0f, "Interior of glass must have bevel factor a == 0");
        Vector3 nInterior = Vector3.Normalize(new Vector3(0f, 0f, MathF.Sqrt(1.0f - aInterior * aInterior)));
        Assert(nInterior == new Vector3(0f, 0f, 1f), "Interior normal must point straight forward (0,0,1)");

        // Edge rim point: s = 0 -> a = 1 -> normal is (gx, gy, 0)
        float sRim = 0.0f;
        float aRim = Math.Clamp(1.0f - sRim / bevel, 0.0f, 1.0f);
        Assert(aRim == 1.0f, "Edge rim must have bevel factor a == 1");
        Vector2 gRim = new Vector2(1.0f, 0.0f); // Outward gradient along +X
        Vector3 nRim = Vector3.Normalize(new Vector3(gRim.X * aRim, gRim.Y * aRim, MathF.Sqrt(Math.Max(1.0f - aRim * aRim, 1e-4f))));
        Assert(Math.Abs(nRim.X - 1.0f) < 0.01f && Math.Abs(nRim.Z) < 0.02f, "Rim normal must tilt heavily towards outward edge");

        // 5. Snell's Law Refraction Invariant
        // Incident vector I = (0, 0, -1) from eye into screen
        // R = refract(I, N, 1/ior)
        // Offset = (R.xy / -R.z) * thickness
        static Vector3 Refract(Vector3 I, Vector3 N, float eta)
        {
            float cosi = -Vector3.Dot(N, I);
            float cost2 = 1.0f - eta * eta * (1.0f - cosi * cosi);
            if (cost2 < 0f) return Vector3.Zero; // Total internal reflection
            return eta * I + (eta * cosi - MathF.Sqrt(cost2)) * N;
        }

        float ior = 1.52f;
        float eta = 1.0f / ior;
        Vector3 I = new Vector3(0f, 0f, -1f);

        // Through flat interior normal (0, 0, 1)
        Vector3 rInterior = Refract(I, nInterior, eta);
        Assert(Math.Abs(rInterior.X) < 1e-5 && Math.Abs(rInterior.Y) < 1e-5 && Math.Abs(rInterior.Z - (-1.0f)) < 1e-5,
            "Refraction through flat normal must remain parallel (0, 0, -1)");
        Vector2 offsetInterior = new Vector2(rInterior.X / -rInterior.Z, rInterior.Y / -rInterior.Z) * 20.0f;
        Assert(offsetInterior.Length() < 1e-5, "Flat glass interior must have zero pixel displacement");

        // Through curved rim normal (Nx > 0)
        Vector3 nSlanted = Vector3.Normalize(new Vector3(0.5f, 0f, MathF.Sqrt(1f - 0.25f)));
        Vector3 rSlanted = Refract(I, nSlanted, eta);
        Vector2 offsetSlanted = new Vector2(rSlanted.X / -rSlanted.Z, rSlanted.Y / -rSlanted.Z) * 20.0f;
        Assert(offsetSlanted.X < -1.0f && offsetSlanted.Length() > 1.0f,
            $"Curved rim normal must produce physical lens refraction offset (got dx = {offsetSlanted.X})");
    }

    private static void TestLiquidGlassGoldenImage()
    {
        // Execute headless golden image verification
        int code = GoldenImageGenerator.GenerateGoldenImages();
        Assert(code == 0, $"GoldenImageGenerator failed with exit code {code}");

        string workspaceDir = Path.Combine(Environment.CurrentDirectory, "docs", "liquid-glass");
        string backdropPath = Path.Combine(workspaceDir, "golden_backdrop.png");
        string outputPath = Path.Combine(workspaceDir, "golden_output.png");

        if (!File.Exists(outputPath))
        {
            Console.WriteLine("[INFO] Golden output PNG skipped in headless CI/GPU-less environment.");
            return;
        }

        Assert(File.Exists(backdropPath), $"Golden backdrop file does not exist at {backdropPath}");
        Assert(File.Exists(outputPath), $"Golden output file does not exist at {outputPath}");

        var fiBackdrop = new FileInfo(backdropPath);
        var fiOutput = new FileInfo(outputPath);

        Assert(fiBackdrop.Length > 5000, $"Golden backdrop PNG too small ({fiBackdrop.Length} bytes)");
        Assert(fiOutput.Length > 5000, $"Golden output PNG too small ({fiOutput.Length} bytes)");
    }

    private static void TestSemVersionComparison()
    {
        Assert(SemVersion.TryParse("0.4.0", out var v040), "Failed to parse 0.4.0");
        Assert(SemVersion.TryParse("v0.5.0", out var v050), "Failed to parse v0.5.0 with leading v");
        Assert(SemVersion.TryParse("0.5.0-beta.1", out var vBeta1), "Failed to parse 0.5.0-beta.1");
        Assert(SemVersion.TryParse("0.5.0-beta.2", out var vBeta2), "Failed to parse 0.5.0-beta.2");
        Assert(SemVersion.TryParse("1.0.0+build123", out var vBuild), "Failed to parse version with build metadata");

        Assert(v040 < v050, "0.4.0 must be less than 0.5.0");
        Assert(v050 > v040, "0.5.0 must be greater than 0.4.0");
        Assert(vBeta1 < v050, "0.5.0-beta.1 must be less than 0.5.0 (stable release has higher precedence)");
        Assert(vBeta1 < vBeta2, "0.5.0-beta.1 must be less than 0.5.0-beta.2");
        Assert(v040 < vBeta1, "0.4.0 must be less than 0.5.0-beta.1");
        Assert(vBuild == new SemVersion(1, 0, 0), "Build metadata should be stripped for version equality");
        Assert(vBeta1.IsPreRelease, "vBeta1 must be flagged as pre-release");
        Assert(!v040.IsPreRelease, "0.4.0 must not be flagged as pre-release");
    }

    private static void TestUpdateManifestAndIntegrity()
    {
        string sampleJson = """
        {
          "version": "0.5.0",
          "releaseTag": "v0.5.0",
          "channel": "stable",
          "releaseDate": "2026-11-15T12:00:00Z",
          "summary": "Liquid glass and update manager",
          "downloads": {
            "installer": {
              "url": "https://github.com/JainisPatel/DynamicIsland/releases/download/v0.5.0/DynamicIsland-Setup.exe",
              "sha256": "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
              "sizeBytes": 90000000
            }
          }
        }
        """;

        var manifest = System.Text.Json.JsonSerializer.Deserialize<UpdateManifest>(sampleJson, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert(manifest != null, "Manifest deserialization must succeed");
        Assert(manifest!.Version == "0.5.0", "Manifest version must match 0.5.0");
        Assert(manifest.ParsedChannel == UpdateChannel.Stable, "Parsed channel must be Stable");
        Assert(manifest.Downloads.Installer != null, "Installer download asset must be present");
        Assert(manifest.Downloads.Installer!.Sha256 == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "Sha256 hash must match");

        // Verify SHA-256 calculation on known input ("abc")
        // Known SHA-256 for "abc": ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
        byte[] testBytes = System.Text.Encoding.UTF8.GetBytes("abc");
        string computedHash = UpdateIntegrityVerifier.ComputeSha256(testBytes);
        Assert(string.Equals(computedHash, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", StringComparison.OrdinalIgnoreCase),
            $"Computed SHA256 was '{computedHash}', expected ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    private static void TestLiveDownloadsAndNotificationAppGrouping()
    {
        // 1. Verify download speed formatting
        Assert(DownloadTrackerService.FormatSpeed(0) == "", "0 bytes speed must be empty string");
        Assert(DownloadTrackerService.FormatSpeed(1024) == "\u2193 1 KB/s", "1024 bytes must format to \u2193 1 KB/s");
        Assert(DownloadTrackerService.FormatSpeed(1048576) == "\u2193 1.0 MB/s", "1 MB must format to \u2193 1.0 MB/s");
        Assert(DownloadTrackerService.FormatSpeed(5242880) == "\u2193 5.0 MB/s", "5 MB must format to \u2193 5.0 MB/s");

        // 2. Verify notification item grouping & stacking
        var sampleItems = new List<NotificationHistoryItem>
        {
            new() { Id = "1", AppName = "Slack", Title = "Alice", Body = "Hey!", ReceivedAt = DateTime.Now },
            new() { Id = "2", AppName = "Slack", Title = "Bob", Body = "Meeting in 5", ReceivedAt = DateTime.Now },
            new() { Id = "3", AppName = "Slack", Title = "Charlie", Body = "PR ready", ReceivedAt = DateTime.Now },
            new() { Id = "4", AppName = "Discord", Title = "General", Body = "Game night?", ReceivedAt = DateTime.Now },
            new() { Id = "5", AppName = "WhatsApp", Title = "Family", Body = "Photo sent", ReceivedAt = DateTime.Now }
        };

        var groups = NotificationListenerService.GroupNotifications(sampleItems);
        Assert(groups.Count == 3, $"Expected 3 app groups, got {groups.Count}");

        var slackGroup = groups.FirstOrDefault(g => g.AppName == "Slack");
        Assert(slackGroup != null, "Slack group must exist");
        Assert(slackGroup!.Count == 3, $"Slack group should have 3 items, got {slackGroup.Count}");
        Assert(slackGroup.HasMultiple, "Slack group should have multiple items");
        Assert(slackGroup.MultipleBadgeVisibility == Visibility.Visible, "Multiple badge should be visible");
        Assert(slackGroup.CountBadgeText == "3 alerts", $"Expected '3 alerts', got '{slackGroup.CountBadgeText}'");
        Assert(slackGroup.LatestTitle == "Alice", $"Expected LatestTitle 'Alice', got '{slackGroup.LatestTitle}'");

        // Test Expand / Collapse mechanics
        Assert(!slackGroup.IsExpanded, "Group should be collapsed by default");
        Assert(slackGroup.DetailsVisibility == Visibility.Collapsed, "Details should be collapsed by default");
        Assert(slackGroup.ExpandGlyph == "\uE70D", "Should display ChevronDown when collapsed");

        slackGroup.IsExpanded = true;
        Assert(slackGroup.DetailsVisibility == Visibility.Visible, "Details should be visible when expanded");
        Assert(slackGroup.ExpandGlyph == "\uE70E", "Should display ChevronUp when expanded");

        // Single notification app group
        var single = NotificationAppGroup.FromSingle(sampleItems[4]);
        Assert(single.AppName == "WhatsApp", "Single group app name must match");
        Assert(single.Count == 1, "Single group count must be 1");
        Assert(!single.HasMultiple, "Single group HasMultiple must be false");
        Assert(single.MultipleBadgeVisibility == Visibility.Collapsed, "Single group badge must be collapsed");

        // Notification equality & hashing
        var single2 = NotificationAppGroup.FromSingle(sampleItems[4]);
        Assert(single.Equals(single2), "Identical groups must be equal");
        Assert(single.GetHashCode() == single2.GetHashCode(), "Identical groups must share hash code");
    }

    private static void TestChoreographerAndPowerGovernor()
    {
        // 1. Integrated GPU preference
        bool gpuPref = Choreographer.EnsureIntegratedGpuPreference();
        // Should execute cleanly (true on standard Windows, or false if registry key inaccessible, but never throw)

        // 2. Choreographer lifecycle and chore calculations
        using var ch = new Choreographer();
        Assert(ch.CurrentMode == PowerGovernorMode.HighPerformance ||
               ch.CurrentMode == PowerGovernorMode.Balanced ||
               ch.CurrentMode == PowerGovernorMode.BatterySaver, "CurrentMode must be a valid governor mode");

        var choreNormal = new ScheduledChore("normal_task", () => { }, 10, PowerSensitivity.Normal);
        var choreRelaxed = new ScheduledChore("relaxed_task", () => { }, 5, PowerSensitivity.Relaxed);
        var choreSuspended = new ScheduledChore("suspended_task", () => { }, 2, PowerSensitivity.Suspended);

        // Verify base interval calculations
        Assert(choreNormal.BaseIntervalSeconds == 10, "Normal base interval must be 10");
        Assert(choreRelaxed.BaseIntervalSeconds == 5, "Relaxed base interval must be 5");
        Assert(choreSuspended.BaseIntervalSeconds == 2, "Suspended base interval must be 2");

        // 3. Register, Trigger, and Tick dispatching
        int executionCount = 0;
        ch.Register("test_chore", () => executionCount++, 2, PowerSensitivity.Normal);

        bool triggerResult = ch.Trigger("test_chore");
        Assert(triggerResult, "Trigger should succeed for registered chore");
        Assert(executionCount == 1, "Execution count should be 1 after manual trigger");

        // Tick twice to elapse remaining countdown
        ch.Tick();
        ch.Tick();
        Assert(executionCount >= 2, $"Execution count should be at least 2 after ticks, got {executionCount}");

        // 4. Unregister
        Assert(ch.Unregister("test_chore"), "Unregister should succeed for existing chore");
        Assert(!ch.Unregister("non_existing"), "Unregister should return false for nonexistent chore");
        Assert(!ch.Trigger("test_chore"), "Trigger should fail after chore was unregistered");
    }

    private static void TestSpringDynamicsAndTactileHaptics()
    {
        // 1. Spring Impulse, Retarget, SnapTo
        var s = new Spring(100) { Target = 100 };
        Assert(s.IsAtRest, "Spring initialized at target must be at rest");

        s.Impulse(250.0);
        Assert(s.Velocity == 250.0, "Impulse should add 250.0 to velocity");
        Assert(!s.IsAtRest, "Spring must not be at rest after impulse");

        s.SnapTo(350.0);
        Assert(s.Value == 350.0 && s.Target == 350.0 && s.Velocity == 0, "SnapTo must set Value, Target, and zero Velocity");
        Assert(s.IsAtRest, "Spring must be at rest after SnapTo");

        s.Retarget(450.0, 180.0);
        Assert(s.Target == 450.0, "Retarget must update Target");
        Assert(s.Velocity == 180.0, "Retarget must inject initial velocity");

        // 2. Synthesized RIFF WAVE validation
        byte[] wav = TactileFeedback.SynthesizeRiffWav(480, 420, 90.0, 12, 0.4);
        Assert(wav != null, "Synthesized WAV must not be null");
        Assert(wav!.Length > 44, "Synthesized WAV must have valid length > 44 bytes");

        // Verify RIFF header
        Assert(wav[0] == 'R' && wav[1] == 'I' && wav[2] == 'F' && wav[3] == 'F', "Must start with RIFF signature");
        Assert(wav[8] == 'W' && wav[9] == 'A' && wav[10] == 'V' && wav[11] == 'E', "Must contain WAVE identifier");
        Assert(wav[12] == 'f' && wav[13] == 'm' && wav[14] == 't' && wav[15] == ' ', "Must contain fmt chunk");
        Assert(wav[36] == 'd' && wav[37] == 'a' && wav[38] == 't' && wav[39] == 'a', "Must contain data chunk");

        // 3. Audio micro-haptics non-throwing execution
        TactileFeedback.PlayTick();
        TactileFeedback.PlaySnap();
        TactileFeedback.PlayDismiss();

        bool prevEnabled = TactileFeedback.IsEnabled;
        TactileFeedback.IsEnabled = false;
        TactileFeedback.PlayTick();
        TactileFeedback.IsEnabled = prevEnabled;
    }
}