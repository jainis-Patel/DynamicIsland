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
using DynamicIsland.Interop;
using DynamicIsland.Services;
using DynamicIsland.Views.Widgets;
using Microsoft.Win32;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Core;

internal static partial class SelfTestRunner
{
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
        Assert(ExpandedViewExtensions.TryParseKey("widgets", out var widgetsView) && widgetsView == ExpandedView.WidgetsGallery,
            "Expected 'widgets' alias to resolve to ExpandedView.WidgetsGallery");
        Assert(ExpandedViewExtensions.TryParseKey("bento", out var bentoView) && bentoView == ExpandedView.WidgetsGallery,
            "Expected 'bento' alias to resolve to ExpandedView.WidgetsGallery");
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
        var prevShutdownMode = app.ShutdownMode;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        IslandWindow? island = null;
        try
        {
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
            var settingsView = new DynamicIsland.Views.Widgets.SettingsView();
            Assert(settingsView.FloraPanel != null && settingsView.FloraPanel.PanelScrollViewer != null, "FloraPanel must not be null");
            Assert(settingsView.FloraPanel!.NatureDensitySlider != null, "NatureDensitySlider must not be null");
            Assert(settingsView.FloraPanel.NatureScaleSlider != null, "NatureScaleSlider must not be null");
            Assert(settingsView.FloraPanel.NatureDropSlider != null, "NatureDropSlider must not be null");
            Assert(settingsView.FloraPanel.NatureBloomSlider != null, "NatureBloomSlider must not be null");
            Assert(settingsView.FloraPanel.NatureButterfly0Btn != null, "NatureButterfly0Btn must not be null");
            Assert(settingsView.FloraPanel.NaturePollenSlider != null, "NaturePollenSlider must not be null");

            island = new IslandWindow();
            Assert(island.IslandThemeUnderlay != null, "IslandThemeUnderlay must not be null");
            Assert(island.IslandThemeOverlay != null, "IslandThemeOverlay must not be null");
        }
        catch (Exception ex)
        {
            var baseEx = ex.GetBaseException();
            Assert(false, $"Initialization crash: {ex.Message} -> Base: {baseEx.Message}\n{ex.StackTrace}");
        }
        finally
        {
            try { island?.Close(); } catch { }
            app.ShutdownMode = prevShutdownMode;
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

            string tempShelfPath = Path.Combine(tempDir, "shelf.json");
            using var shelf = new DropShelfService(tempShelfPath);
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
        // 1. Verify all 8 supported clock styles exist
        Assert(SettingsService.AvailableClockStyles.Count == 8, "Must define 8 clock styles");
        Assert(SettingsService.AvailableClockStyles.Contains("Minimal"), "Must include Minimal");
        Assert(SettingsService.AvailableClockStyles.Contains("Seconds"), "Must include Seconds");
        Assert(SettingsService.AvailableClockStyles.Contains("DateBadge"), "Must include DateBadge");
        Assert(SettingsService.AvailableClockStyles.Contains("Analog"), "Must include Analog");
        Assert(SettingsService.AvailableClockStyles.Contains("Neon"), "Must include Neon");
        Assert(SettingsService.AvailableClockStyles.Contains("Flip"), "Must include Flip");
        Assert(SettingsService.AvailableClockStyles.Contains("Matrix"), "Must include Matrix");
        Assert(SettingsService.AvailableClockStyles.Contains("JumpHour"), "Must include JumpHour");

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
        Assert(SettingsService.NormalizeClockStyle("Matrix") == "Matrix", "Must recognize Matrix");
        Assert(SettingsService.NormalizeClockStyle("FieldMatrix") == "Matrix", "Must recognize FieldMatrix alias as Matrix");
        Assert(SettingsService.NormalizeClockStyle("Telemetry") == "Matrix", "Must recognize Telemetry alias as Matrix");
        Assert(SettingsService.NormalizeClockStyle("JumpHour") == "JumpHour", "Must recognize JumpHour");
        Assert(SettingsService.NormalizeClockStyle("Guichet") == "JumpHour", "Must recognize Guichet alias as JumpHour");
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

    private static void TestAlbumArtStylesAndPlayerDisc()
    {
        // 1. Normalization & alias resolution
        Assert(SettingsService.AvailableAlbumArtStyles.Count == 3, "Must define 3 album art styles");
        Assert(SettingsService.NormalizeAlbumArtStyle("vinyl") == "VinylDisc", "Must normalize 'vinyl' to VinylDisc");
        Assert(SettingsService.NormalizeAlbumArtStyle("optical") == "OpticalDisc", "Must normalize 'optical' to OpticalDisc");
        Assert(SettingsService.NormalizeAlbumArtStyle("cd") == "OpticalDisc", "Must normalize 'cd' to OpticalDisc");
        Assert(SettingsService.NormalizeAlbumArtStyle("static") == "StaticSquircle", "Must normalize 'static' to StaticSquircle");
        Assert(SettingsService.NormalizeAlbumArtStyle("unknown") == "VinylDisc", "Must fallback unknown to VinylDisc");
        Assert(SettingsService.NormalizeAlbumArtStyle(null) == "VinylDisc", "Must fallback null to VinylDisc");

        // 2. Settings round-trip persistence
        string tempPath = Path.Combine(Path.GetTempPath(), $"di_disc_style_test_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(tempPath);
            Assert(settings.Current.AlbumArtStyle == "VinylDisc", "Default AlbumArtStyle must be VinylDisc");
            settings.Update(s => s.AlbumArtStyle = "OpticalDisc", nameof(AppSettings.AlbumArtStyle));
            settings.SaveNow();

            var reloaded = new SettingsService(tempPath);
            Assert(reloaded.Current.AlbumArtStyle == "OpticalDisc", "AlbumArtStyle must persist across instances");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }

        // 3. PlayerDiscControl inertial spin-up, coast-to-stop phase preservation, StaticSquircle & ReducedMotion
        var disc = new PlayerDiscControl { DiscSize = 52, ShowTonearm = true };
        disc.SetStyle("VinylDisc");
        bool settled = disc.StepPhysics(1.0 / 60.0, isPlaying: true, bassLevel: 0.4f, reducedMotion: false);
        Assert(!settled, "Playing VinylDisc must not report settled");
        for (int i = 0; i < 60; i++)
        {
            disc.StepPhysics(1.0 / 60.0, isPlaying: true, bassLevel: 0.3f, reducedMotion: false);
        }
        Assert(disc.AngularVelocity > 25.0, "VinylDisc must spin up smoothly toward target RPM");
        Assert(disc.Angle > 0.0, "VinylDisc angle must advance while playing");

        // Coast to stop while preserving angle
        double pausedAtAngle = disc.Angle;
        bool coastedToRest = false;
        for (int i = 0; i < 240; i++)
        {
            if (disc.StepPhysics(1.0 / 60.0, isPlaying: false, bassLevel: 0f, reducedMotion: false))
            {
                coastedToRest = true;
                break;
            }
        }
        Assert(coastedToRest, "Paused VinylDisc must coast to rest and allow render-loop detach");
        Assert(disc.AngularVelocity == 0.0, "Settled VinylDisc must have 0 angular velocity");
        Assert(disc.Angle != 0.0 && disc.Angle != pausedAtAngle, "Coast-to-stop must preserve rotational phase rather than snapping to 0");

        // Reduced motion & StaticSquircle must settle immediately without rotation
        double angleBeforeReduced = disc.Angle;
        bool reducedSettled = disc.StepPhysics(1.0 / 60.0, isPlaying: true, bassLevel: 0.8f, reducedMotion: true);
        Assert(reducedSettled, "ReducedMotion must settle immediately");
        Assert(disc.Angle == angleBeforeReduced, "ReducedMotion must not rotate disc");

        disc.SetStyle("StaticSquircle");
        Assert(disc.StepPhysics(1.0 / 60.0, isPlaying: true, bassLevel: 0.8f, reducedMotion: false), "StaticSquircle must settle immediately");
    }

    private static void TestPetUpgradesAndRoaming()
    {
        // 1. Custom Pet Name Sanitization
        Assert(SettingsService.SanitizePetName(null) == "Mochi", "Blank pet name must fallback to Mochi");
        Assert(SettingsService.SanitizePetName("   ") == "Mochi", "Whitespace pet name must fallback to Mochi");
        Assert(SettingsService.SanitizePetName("  Pixel  ") == "Pixel", "Pet name must trim whitespace");
        Assert(SettingsService.SanitizePetName("SuperLongCompanionName123").Length <= 18, "Pet name must clamp to 18 chars");

        // 2. VirtualPetService custom name, toggles, daily streak & context priority
        string tempPath = Path.Combine(Path.GetTempPath(), $"di_pet_upgrade_test_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(tempPath);
            using var pet = new VirtualPetService(Application.Current.Dispatcher, settings);

            Assert(pet.AutoRoamEnabled, "AutoRoam must default to true");
            Assert(pet.ContextReactionsEnabled, "ContextReactions must default to true");

            string savedName = pet.SetPetName("  KitsuneFox  ");
            Assert(savedName == "KitsuneFox" && pet.PetName == "KitsuneFox", "SetPetName must sanitize and persist custom name");

            pet.ToggleAutoRoam();
            Assert(!pet.AutoRoamEnabled, "ToggleAutoRoam must flip AutoRoamEnabled");
            pet.ToggleContextReactions();
            Assert(!pet.ContextReactionsEnabled, "ToggleContextReactions must flip ContextReactionsEnabled");
            pet.ToggleContextReactions(); // re-enable for context test

            // Daily care streak progression
            var day1 = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
            Assert(pet.RecordDailyCare(day1) == 1, "First care day must set streak to 1");
            Assert(pet.RecordDailyCare(day1.AddHours(4)) == 1, "Same-day care must keep streak at 1");
            Assert(pet.RecordDailyCare(day1.AddDays(1)) == 2, "Consecutive-day care must increment streak to 2");
            Assert(pet.RecordDailyCare(day1.AddDays(4)) == 1, "Missed-day gap must reset streak to 1");

            // Context reaction priority (Dancing > Focus > LowBattery > LateNight)
            // Reset happy timer influence by testing via UpdateActivity after clearing happy mood via reflection-free fresh instance
            using var ctxPet = new VirtualPetService(Application.Current.Dispatcher, settings);
            ctxPet.UpdateActivity(isMediaPlaying: true, audioEnergy: 0.25f, isUserNear: false,
                isFocusTimerActive: true, isLowBattery: true, isHighLoad: true, localHour: 2);
            Assert(ctxPet.CurrentMood == PetMood.Dancing, "Dancing must outrank focus/battery/late-night");

            ctxPet.UpdateActivity(isMediaPlaying: false, audioEnergy: 0f, isUserNear: false,
                isFocusTimerActive: true, isLowBattery: true, isHighLoad: false, localHour: 2);
            Assert(ctxPet.CurrentMood == PetMood.Awake && ctxPet.CurrentContextCaption.Contains("Focus"),
                "Focus timer must trigger Focus Buddy Awake pose");

            ctxPet.UpdateActivity(isMediaPlaying: false, audioEnergy: 0f, isUserNear: true,
                isFocusTimerActive: false, isLowBattery: true, isHighLoad: false, localHour: 14);
            Assert(ctxPet.CurrentMood == PetMood.Sleeping && ctxPet.CurrentContextCaption.Contains("battery", StringComparison.OrdinalIgnoreCase),
                "Low battery must trigger sleepy low-battery reaction");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }

        // 3. PetFloatWindow frameless background-free check
        string floatXamlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Views", "PetFloatWindow.xaml");
        if (File.Exists(floatXamlPath))
        {
            string xaml = File.ReadAllText(floatXamlPath);
            Assert(xaml.Contains("Background=\"Transparent\"", StringComparison.Ordinal) &&
                   !xaml.Contains("RootCard", StringComparison.Ordinal),
                "PetFloatWindow must be background-free without RootCard");
        }

        // 4. Realistic Pet Model Micro LOD & Hero LOD render smoke test across all 6 species
        foreach (string sp in SettingsService.AvailablePetSpecies)
        {
            var microPet = new VirtualPetControl { Width = 26, Height = 24, Species = sp, Mood = PetMood.Awake };
            microPet.Measure(new Size(26, 24));
            microPet.Arrange(new Rect(0, 0, 26, 24));
            microPet.UpdateTick(1.2, isNearCursor: false, gazeX: 0.2, gazeY: -0.1);

            var heroPet = new VirtualPetControl { Width = 135, Height = 105, Species = sp, Mood = PetMood.Happy };
            heroPet.Measure(new Size(135, 105));
            heroPet.Arrange(new Rect(0, 0, 135, 105));
            heroPet.UpdateTick(2.4, isNearCursor: true, gazeX: -0.3, gazeY: 0.2);

            var dg = new DrawingGroup();
            using (var dc = dg.Open())
            {
                // Verify DrawingContext render path for both Hero and Micro sizes without exceptions
                var mi = typeof(VirtualPetControl).GetMethod("OnRender",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert(mi != null, "VirtualPetControl.OnRender must exist");
                mi!.Invoke(microPet, new object[] { dc });
                mi.Invoke(heroPet, new object[] { dc });
            }
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

    private static void TestLazyServiceContainer()
    {
        int createdCount = 0;
        int disposedCount = 0;
        var container = new ServiceContainer();

        container.RegisterLazy(() =>
        {
            createdCount++;
            return new TrackingDisposable(() => disposedCount++);
        });

        Assert(container.Count == 1, "Lazy-registered service must count toward registered services");
        Assert(!container.IsMaterialized<TrackingDisposable>(), "Lazy service must not be materialized before Get");
        Assert(createdCount == 0, "Lazy factory must not run before Get");

        var resolved = container.Get<TrackingDisposable>();
        Assert(createdCount == 1, "Lazy factory must run exactly once on first Get");
        Assert(container.IsMaterialized<TrackingDisposable>(), "Service must be marked materialized after Get");
        Assert(ReferenceEquals(resolved, container.Get<TrackingDisposable>()), "Subsequent Get must return cached instance");
        Assert(createdCount == 1, "Cached Get must not re-invoke factory");

        container.Dispose();
        Assert(disposedCount == 1, "Materialized IDisposable lazy service must be disposed on container disposal");

        // Unmaterialized lazy service must not be created or disposed on Dispose()
        int unmaterializedCreated = 0;
        var container2 = new ServiceContainer();
        container2.RegisterLazy(() =>
        {
            unmaterializedCreated++;
            return new TrackingDisposable(() => { });
        });
        container2.Dispose();
        Assert(unmaterializedCreated == 0, "Unmaterialized lazy service must not be instantiated during Dispose");
    }

    private sealed class TrackingDisposable : IDisposable
    {
        private readonly Action _onDispose;
        public TrackingDisposable(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }

    private static void TestAsyncSafeFireAndForget()
    {
        // Faulted task must be observed cleanly without throwing
        Task.FromException(new InvalidOperationException("Simulated background failure"))
            .FireAndForget("SelfTest.Faulted");

        // Cancelled task must be ignored cleanly
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Task.FromCanceled(cts.Token).FireAndForget("SelfTest.Cancelled");

        // Completed task must be a no-op
        Task.CompletedTask.FireAndForget("SelfTest.Completed");
    }

    private static void TestWindowPlacementHelper()
    {
        var (cx, cy) = WindowPlacementHelper.ClampToWorkArea(
            x: -50, y: 2000, width: 200, height: 100,
            workLeft: 0, workTop: 0, workRight: 1920, workBottom: 1040, margin: 8);
        Assert(Math.Abs(cx - 8) < 1e-6, $"Clamped X expected 8, got {cx}");
        Assert(Math.Abs(cy - 932) < 1e-6, $"Clamped Y expected 932, got {cy}");

        var rect = WindowPlacementHelper.ComputeDockedWindowRectPx(
            monLeft: 0, monTop: 0, monWidth: 2560, workLeft: 0, workRight: 2560,
            canvasWidthDip: 680, canvasHeightDip: 580, refWidthDip: 484, dpiScale: 1.5,
            dockPosition: "Center", hardwareNotchMode: false, notchYOffsetDip: 4);
        Assert(rect.Width == 1020 && rect.Height == 870, $"Expected 1020x870 physical px at 1.5x, got {rect.Width}x{rect.Height}");
        Assert(rect.Left == (2560 - 1020) / 2, $"Expected centered X=770, got {rect.Left}");
        Assert(rect.Top == 6, $"Expected Top=6px at 1.5x, got {rect.Top}");

        // Ensure CanvasHeightDip never clips FloatingDockHost even at max Notifications height + spring overshoot + 125% IslandScale
        const double maxNotifPlusDockAndOvershoot = 396.0 + 20.0 + 4.0 + 40.0 + 16.0;
        Assert(maxNotifPlusDockAndOvershoot * 1.25 <= IslandWindow.CanvasHeightDip,
            $"CanvasHeightDip ({IslandWindow.CanvasHeightDip}) must exceed scaled max pill + dock footprint ({maxNotifPlusDockAndOvershoot * 1.25})");
    }

    private static void TestAutoHideSettingRoundTrip()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"di_autohide_test_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(tempPath);
            Assert(settings.Current.AutoHideIsland == false, "AutoHideIsland must default to false (Always Visible)");

            settings.Update(s => s.AutoHideIsland = true, nameof(AppSettings.AutoHideIsland));
            settings.SaveNow();

            var reloaded = new SettingsService(tempPath);
            Assert(reloaded.Current.AutoHideIsland == true, "AutoHideIsland=true must persist across reload");

            reloaded.ResetToDefaults();
            Assert(reloaded.Current.AutoHideIsland == false, "ResetToDefaults must restore AutoHideIsland=false (Always Visible)");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static void TestPhase1ChoreographerIntegration()
    {
        string tempEvents = Path.Combine(Path.GetTempPath(), $"di_cal_chore_test_{Guid.NewGuid():N}.json");
        try
        {
            using var choreographer = new Choreographer(Dispatcher.CurrentDispatcher);
            using var calendar = new CalendarService(Dispatcher.CurrentDispatcher, tempEvents);

            calendar.AttachChoreographer(choreographer);
            calendar.Start();
            Assert(choreographer.Trigger("calendar_refresh"), "CalendarService.Start with attached Choreographer must register 'calendar_refresh'");

            calendar.Dispose();
            Assert(!choreographer.Unregister("calendar_refresh"), "CalendarService.Dispose must unregister 'calendar_refresh'");
        }
        finally
        {
            if (File.Exists(tempEvents)) File.Delete(tempEvents);
        }
    }

    private static void TestHighLoadDropletResolution()
    {
        var none = ActivityResolver.ResolveHighLoadAlert(false, 40, false, 60);
        Assert(!none.IsActive, "Normal CPU/RAM should not activate high-load droplet");

        var cpuWarn = ActivityResolver.ResolveHighLoadAlert(true, 84, false, 50);
        Assert(cpuWarn.IsActive && !cpuWarn.IsRamPriority && !cpuWarn.IsCritical && cpuWarn.Label == "CPU" && cpuWarn.Percent == 84,
            "CPU 84% should resolve as non-critical CPU droplet alert");

        var cpuCrit = ActivityResolver.ResolveHighLoadAlert(true, 92, false, 50);
        Assert(cpuCrit.IsActive && cpuCrit.IsCritical && cpuCrit.Label == "CPU" && cpuCrit.Percent == 92,
            "CPU 92% should resolve as critical CPU droplet alert");

        var ramPrio = ActivityResolver.ResolveHighLoadAlert(true, 84, true, 88);
        Assert(ramPrio.IsActive && ramPrio.IsRamPriority && ramPrio.IsCritical && ramPrio.Label == "RAM" && ramPrio.Percent == 88,
            "RAM 88% >= CPU 84% should prioritize critical RAM droplet alert");

        var cpuBeatsRam = ActivityResolver.ResolveHighLoadAlert(true, 95, true, 88);
        Assert(cpuBeatsRam.IsActive && !cpuBeatsRam.IsRamPriority && cpuBeatsRam.IsCritical && cpuBeatsRam.Label == "CPU" && cpuBeatsRam.Percent == 95,
            "CPU 95% > RAM 88% should prioritize CPU droplet alert");
    }

    private static void TestWave3SettingsAndSafeLauncher()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"di_wave3_test_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(tempPath);
            Assert(settings.Current.EnableHighLoadAlerts, "EnableHighLoadAlerts must default to true");
            Assert(settings.Current.HighCpuAlertThreshold == 80, "HighCpuAlertThreshold must default to 80");
            Assert(settings.Current.HighRamAlertThreshold == 85, "HighRamAlertThreshold must default to 85");

            settings.Update(s =>
            {
                s.EnableHighLoadAlerts = false;
                s.HighCpuAlertThreshold = 90;
                s.HighRamAlertThreshold = 92;
            }, nameof(AppSettings.HighCpuAlertThreshold));
            settings.SaveNow();

            var reloaded = new SettingsService(tempPath);
            Assert(!reloaded.Current.EnableHighLoadAlerts && reloaded.Current.HighCpuAlertThreshold == 90 && reloaded.Current.HighRamAlertThreshold == 92,
                "High-load threshold settings must persist across reload");

            reloaded.ResetToDefaults();
            Assert(reloaded.Current.EnableHighLoadAlerts && reloaded.Current.HighCpuAlertThreshold == 80 && reloaded.Current.HighRamAlertThreshold == 85,
                "ResetToDefaults must restore high-load alert defaults");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }

        Assert(!SafeProcessLauncher.TryOpenUrl("javascript:alert(1)"), "TryOpenUrl must block javascript: scheme");
        Assert(!SafeProcessLauncher.TryOpenShellUri("file:///C:/Windows/notepad.exe"), "TryOpenShellUri must block file:// scheme");
    }

    private static void TestClipboardTransforms()
    {
        var jsonPretty = ClipboardTransformer.TryTransform("{\"a\":1,\"b\":[2,3]}", ClipboardTransformKind.JsonPretty);
        Assert(jsonPretty.Success && jsonPretty.Result.Contains('\n'), "JsonPretty should indent JSON");

        var jsonMin = ClipboardTransformer.TryTransform("{\n  \"a\": 1\n}", ClipboardTransformKind.JsonMinify);
        Assert(jsonMin.Success && jsonMin.Result == "{\"a\":1}", $"JsonMinify expected {{\"a\":1}}, got {jsonMin.Result}");

        var b64Enc = ClipboardTransformer.TryTransform("hello", ClipboardTransformKind.Base64Encode);
        Assert(b64Enc.Success && b64Enc.Result == "aGVsbG8=", "Base64Encode failed");

        var b64Dec = ClipboardTransformer.TryTransform("aGVsbG8=", ClipboardTransformKind.Base64Decode);
        Assert(b64Dec.Success && b64Dec.Result == "hello", "Base64Decode failed");

        var b64Bad = ClipboardTransformer.TryTransform("not_valid_base64!!!", ClipboardTransformKind.Base64Decode);
        Assert(!b64Bad.Success, "Invalid Base64 must return Success=false");

        var camel = ClipboardTransformer.TryTransform("hello_world_test", ClipboardTransformKind.CamelCase);
        Assert(camel.Success && camel.Result == "helloWorldTest", $"CamelCase expected helloWorldTest, got {camel.Result}");

        var snake = ClipboardTransformer.TryTransform("helloWorldTest", ClipboardTransformKind.SnakeCase);
        Assert(snake.Success && snake.Result == "hello_world_test", $"SnakeCase expected hello_world_test, got {snake.Result}");

        var urls = ClipboardTransformer.TryTransform("visit https://example.com and https://github.com", ClipboardTransformKind.ExtractUrls);
        Assert(urls.Success && urls.Result.Contains("https://example.com") && urls.Result.Contains("https://github.com"), "ExtractUrls failed");

        var emails = ClipboardTransformer.TryTransform("mail a@b.com or c@d.org", ClipboardTransformKind.ExtractEmails);
        Assert(emails.Success && emails.Result.Contains("a@b.com") && emails.Result.Contains("c@d.org"), "ExtractEmails failed");
    }

    private static void TestWebcamMirrorInvariants()
    {
        Assert(ExpandedView.Mirror.ToKey() == "mirror", "ExpandedView.Mirror.ToKey must be 'mirror'");
        Assert(ExpandedViewExtensions.TryParseKey("webcam", out var parsed) && parsed == ExpandedView.Mirror, "TryParseKey('webcam') must resolve to Mirror");

        using var mirror = new WebcamMirrorService(Dispatcher.CurrentDispatcher);
        Assert(!mirror.IsRunning, "WebcamMirrorService must stay idle (IsRunning=false) on construction");
    }

    private static void TestMeetingMirrorAction()
    {
        var ev = new CalendarEventInfo(
            Subject: "Design Sync",
            StartTime: DateTimeOffset.Now.AddMinutes(1),
            Duration: TimeSpan.FromMinutes(30),
            Location: "Zoom",
            JoinUrl: "https://zoom.us/j/123456789");

        var activity = MeetingModeService.BuildUpcomingMeetingActivity(ev, 1, null, Dispatcher.CurrentDispatcher);
        Assert(activity.Id == MeetingModeService.MeetingJoinActivityId, "Activity Id mismatch");
        Assert(activity.TargetViewKey == "calendar", "TargetViewKey must point to calendar");
        Assert(activity.Actions != null && activity.Actions.Count == 2, $"Expected 2 meeting actions (Join + Mirror), got {activity.Actions?.Count ?? 0}");
        Assert(activity.Actions![0].Label == "Join" && activity.Actions[0].Glyph == "\uE717", "First action must be Join");
        Assert(activity.Actions[1].Label == "Mirror" && activity.Actions[1].Glyph == "\uE714", "Second action must be Mirror");
    }

    private static void TestSettingsSearchHighLoadEntry()
    {
        var matches = SettingsSearchIndex.Search("high load");
        Assert(matches.Any(e => e.CategoryKey == "Screen" && e.Title.Contains("High-Load", StringComparison.OrdinalIgnoreCase)),
            "SettingsSearchIndex.Search('high load') must return the Display & Power High-Load Droplet Alerts entry");

        var ramMatches = SettingsSearchIndex.Search("ram threshold");
        Assert(ramMatches.Any(e => e.CategoryKey == "Screen"),
            "SettingsSearchIndex.Search('ram threshold') must return the Screen/Display category entry");
    }

    private static void TestCommandPalette()
    {
        var apps = new[]
        {
            new LauncherItem { Name = "Terminal", TargetPath = "wt.exe", Glyph = "\uE756" },
            new LauncherItem { Name = "Notepad", TargetPath = "notepad.exe", Glyph = "\uE70B" }
        };
        var clips = new[]
        {
            new ClipboardItem { Title = "API Key", RawText = "sk-proj-secret1234567890", IsSecret = true, Type = ClipboardItemType.Text },
            new ClipboardItem { Title = "Hello World Snippet", RawText = "console.log('hello')", IsSecret = false, Type = ClipboardItemType.Code }
        };

        // 1. Inline math must rank first
        var mathRes = CommandPaletteEngine.Query("12 * 85", apps, clips);
        Assert(mathRes.Count > 0 && mathRes[0].Kind == CommandResultKind.Math && mathRes[0].Payload == "1020",
            $"Expected inline math result 1020 at top, got {mathRes.FirstOrDefault()?.Payload}");

        // 2. System action fuzzy match
        var dndRes = CommandPaletteEngine.Query("dnd", apps, clips);
        Assert(dndRes.Any(r => r.Kind == CommandResultKind.SystemAction && r.ActionKey == "toggle_dnd"),
            "Query 'dnd' must surface Toggle Do Not Disturb action");

        // 3. Settings deep-link
        var glassRes = CommandPaletteEngine.Query("liquid glass", apps, clips);
        Assert(glassRes.Any(r => r.Kind == CommandResultKind.Setting),
            "Query 'liquid glass' must surface Settings deep-links");

        // 4. Secret clipboard items must never leak into Command Palette
        var secretRes = CommandPaletteEngine.Query("API Key", apps, clips);
        Assert(!secretRes.Any(r => r.Kind == CommandResultKind.Clipboard),
            "Secret clipboard items must be excluded from Command Palette results");

        // 5. Non-secret clipboard items must be searchable
        var clipRes = CommandPaletteEngine.Query("Hello World", apps, clips);
        Assert(clipRes.Any(r => r.Kind == CommandResultKind.Clipboard),
            "Non-secret clipboard items must be searchable in Command Palette");
    }

    private static void TestContextProfiles()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"di_profiles_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(tempPath);
            settings.Update(s =>
            {
                s.DoNotDisturb = false;
                s.EnableVirtualPet = true;
                s.AutoHideIsland = false;
            });

            using var profiles = new ContextProfileService(settings);
            Assert(profiles.GetAllProfiles().Count >= 4, "Must include at least 4 built-in profiles");

            // Apply Deep Focus -> DND true, Pet false, and snapshot baseline
            var focus = profiles.ApplyProfile("focus");
            Assert(focus != null && settings.Current.ActiveProfileId == "focus", "ActiveProfileId must be 'focus'");
            Assert(settings.Current.DoNotDisturb && !settings.Current.EnableVirtualPet, "Focus profile overrides must apply");

            // Restore Default -> baseline restored
            profiles.ApplyProfile("default");
            Assert(settings.Current.ActiveProfileId == "default", "ActiveProfileId must return to 'default'");
            Assert(!settings.Current.DoNotDisturb && settings.Current.EnableVirtualPet, "Baseline settings must be restored");

            // Process auto-matching
            string? matched = profiles.MatchProfileForContext(new[] { "explorer", "obs64" }, isOnBattery: false);
            Assert(string.Equals(matched, "presenting", StringComparison.OrdinalIgnoreCase),
                $"Expected 'presenting' profile for obs64, got '{matched}'");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static void TestIpcHookGenerator()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"di_hooks_{Guid.NewGuid():N}");
        try
        {
            var (ok, outDir, count, err) = IpcHookGenerator.ExportAll(tempDir);
            Assert(ok && count == 3 && string.IsNullOrEmpty(err), $"ExportAll failed: {err}");

            string ps1 = File.ReadAllText(Path.Combine(outDir, "claude-island-hook.ps1"));
            string json = File.ReadAllText(Path.Combine(outDir, "claude-hooks.json"));
            string cli = File.ReadAllText(Path.Combine(outDir, "di-run.ps1"));

            Assert(ps1.Contains("DynamicIsland.Activities", StringComparison.Ordinal), "PS1 hook must target DynamicIsland.Activities pipe");
            Assert(cli.Contains("DynamicIsland.Activities", StringComparison.Ordinal), "CLI wrapper must target DynamicIsland.Activities pipe");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            Assert(doc.RootElement.TryGetProperty("hooks", out _), "claude-hooks.json must contain root 'hooks' object");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    private static void TestAccessibilityAndHighContrast()
    {
        ThemeManager.SetHighContrastOverride(true);
        try
        {
            ThemeManager.Initialize("Dark", "Liquid Glass");
            Assert(ThemeManager.IsHighContrast, "ThemeManager.IsHighContrast must reflect override");
            Assert(!ThemeManager.IsLiquidGlass, "Liquid Glass translucency must be bypassed in High Contrast mode");
        }
        finally
        {
            ThemeManager.SetHighContrastOverride(null);
            ThemeManager.Initialize("Dark", "Default");
        }

        string localizedFallback = Localizer.Get("NonExistent_Test_Key", "FallbackValue");
        Assert(localizedFallback == "FallbackValue", "Localizer.Get must return fallback when key is absent");
    }

    private static void TestSettingsExportSanitization()
    {
        string srcPath = Path.Combine(Path.GetTempPath(), $"di_src_{Guid.NewGuid():N}.json");
        string expPath = Path.Combine(Path.GetTempPath(), $"di_exp_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(srcPath);
            settings.Update(s =>
            {
                s.ThemeName = "Cyberpunk Cyan";
                s.GitHubToken = "ghp_secret_token_12345";
                s.PhoneBridgePassword = "super-secret-password";
            });

            Assert(settings.ExportToFile(expPath), "ExportToFile must succeed");
            string exportedJson = File.ReadAllText(expPath);
            Assert(!exportedJson.Contains("ghp_secret_token_12345", StringComparison.Ordinal), "Exported JSON must strip GitHubToken");
            Assert(!exportedJson.Contains("super-secret-password", StringComparison.Ordinal), "Exported JSON must strip PhoneBridgePassword");
            Assert(!exportedJson.Contains("GitHubTokenProtected", StringComparison.Ordinal), "Exported JSON must strip GitHubTokenProtected");
            Assert(!exportedJson.Contains("PhoneBridgePasswordProtected", StringComparison.Ordinal), "Exported JSON must strip PhoneBridgePasswordProtected");

            var reimported = new SettingsService(Path.Combine(Path.GetTempPath(), $"di_imp_{Guid.NewGuid():N}.json"));
            Assert(reimported.ImportFromFile(expPath), "ImportFromFile must succeed");
            Assert(reimported.Current.ThemeName == "Cyberpunk Cyan", "Non-secret theme setting must round-trip");
            Assert(string.IsNullOrEmpty(reimported.Current.GitHubToken), "Imported GitHubToken must be empty");
        }
        finally
        {
            if (File.Exists(srcPath)) File.Delete(srcPath);
            if (File.Exists(expPath)) File.Delete(expPath);
        }
    }
}
