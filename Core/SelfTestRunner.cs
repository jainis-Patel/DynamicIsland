using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        Run("AudioSpectrum: Lock-free band read & idle zeroing", TestAudioSpectrumLockFree);

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
        sm.CollapseNow();
        Assert(sm.State == IslandState.Toast, "CollapseNow should fall back to active Toast");
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
            }, nameof(AppSettings.NotchYOffset));

            Assert(lastChangedProp == nameof(AppSettings.NotchYOffset), $"Expected PropertyChanged 'NotchYOffset', got '{lastChangedProp}'");
            svc.SaveNow();
            Assert(File.Exists(tempFile), "AtomicJsonStore did not write settings file");

            var reloaded = new SettingsService(tempFile);
            Assert(reloaded.Current.SchemaVersion == 2, "Expected SchemaVersion 2");
            Assert(Math.Abs(reloaded.Current.NotchYOffset - 14) < 0.001, $"Expected NotchYOffset 14, got {reloaded.Current.NotchYOffset}");
            Assert(reloaded.Current.DoNotDisturb, "Expected DoNotDisturb=true after reload");
            Assert(reloaded.Current.MonitorDeviceName == @"\\.\DISPLAY2", "Expected MonitorDeviceName persisted");
            Assert(Math.Abs(reloaded.Current.LiquidIntensity - 0.85) < 0.001, "Expected LiquidIntensity 0.85 persisted");
            Assert(Math.Abs(reloaded.Current.AnimationSpeed - 1.25) < 0.001, "Expected AnimationSpeed 1.25 persisted");
            Assert(Math.Abs(reloaded.Current.IslandScale - 1.15) < 0.001, "Expected IslandScale 1.15 persisted");
            Assert(Math.Abs(reloaded.Current.BackdropOpacity - 0.80) < 0.001, "Expected BackdropOpacity 0.80 persisted");
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
        Assert(bounded.PixelHeight == 360,
            $"Expected bounded height 360 (16:9), got {bounded.PixelHeight}");
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
        _ = Application.Current ?? new Application();
        var dict = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/DynamicIsland;component/Resources/IslandStyles.xaml", UriKind.Absolute)
        };
        Assert(dict.Count > 0, "IslandStyles.xaml resource dictionary must load with entries");
    }

    private static void TestThemeBrushesFrozen()
    {
        Assert(ThemeBrushes.Green.IsFrozen, "ThemeBrushes.Green must be frozen");
        Assert(ThemeBrushes.Amber.IsFrozen, "ThemeBrushes.Amber must be frozen");
        Assert(ThemeBrushes.Red.IsFrozen, "ThemeBrushes.Red must be frozen");
        Assert(ThemeBrushes.Blue.IsFrozen, "ThemeBrushes.Blue must be frozen");
        Assert(ThemeBrushes.DockActiveBg.IsFrozen, "ThemeBrushes.DockActiveBg must be frozen");
    }

    private static void TestAudioSpectrumLockFree()
    {
        using var spectrum = new AudioSpectrum();
        float[] bands = { 0.8f, 0.7f, 0.6f, 0.5f };
        spectrum.ReadBands(bands);
        Assert(bands.All(b => b == 0f), "Inactive AudioSpectrum.ReadBands must zero destination buffer without locking");
    }
}
