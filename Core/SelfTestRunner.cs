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

/// <summary>
/// Built-in deterministic verification suite for core physics, state machine priority &amp; toast queueing,
/// fullscreen detection, clipboard privacy exclusion, atomic JSON persistence, activity resolution,
/// countdown/Pomodoro timer, calculator/unit conversion, and color extraction algorithms.
/// Run via: DynamicIsland.exe --self-test [--filter=<pattern>]
/// </summary>
internal static partial class SelfTestRunner
{
    public static int RunAll(string? filter = null)
    {
        int passed = 0;
        int failed = 0;
        int skipped = 0;

        void Run(string name, Action test)
        {
            if (!string.IsNullOrWhiteSpace(filter) &&
                !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                return;
            }
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
        Run("Phase3Visuals: Windows 11 Fluent 2 Mica/Acrylic, WM_DPICHANGED & HDR tone mapping", TestWindows11BackdropAndHdrToneMapping);
        Run("Phase4Ecosystem: Live Activity IPC 2.0 multi-phase pipelines & clipboard secret redaction", TestLiveActivityIpcV2AndClipboardSecretDetection);
        Run("Phase5Packaging: Inno Setup, WinGet manifest specification & v0.8.0 version invariants", TestWinGetManifestAndReleasePackageInvariants);
        Run("Pillar1PrecisionTouchpad: Fluid trackpad scrubbing, velocity flick skip & multi-touch pinch morphing", TestTouchpadGesturesAndVelocityInvariants);
        Run("Pillar2AiAgentApprovals: Two-way IPC prompt RPC, diff HUD parser & interactive buttons", TestAiAgentInteractiveApprovalsAndDiffViewer);
        Run("Pillar3FloatingPiPAndNotch: Tear-off drag gesture, magnetic corner snapping & hardware notch fillets", TestPillar3DetachableFloatingIslandAndHardwareNotch);
        Run("Pillar4CommunityDropletsSdk: Schema-validated JSON definitions, hot-reloading & live polling pipeline", TestPillar4CommunityDropletsSdk);
        Run("Pillar5DeepGpuAndHardwareTelemetry: NVML/DXGI GPU sensors, per-core CPU heatmap & runaway process purge", TestPillar5DeepGpuAndHardwareTelemetry);
        Run("UpdateManagerV2: Critical update handling, 7-day skipped reminder, auto-download setting & changelog invariants", TestUpdateManagerV2Invariants);
        Run("VirtualPetOverhaul: Large Pet Playground, species signature toys, distinct behaviors & desktop roam invariants", TestVirtualPetOverhaulAndPlaygroundInvariants);
        Run("Phase5ArchitectureAndHygiene: DebouncedJsonStore, SafeProcessLauncher, DeviceClassifier, multi-timestamp LRC, ThemeManager & frozen XAML tokens", TestPhase5ArchitectureAndHygiene);
        Run("InlineConfirmAndBentoWidgets: 2-step destructive confirmation guard & Bento card motion styling", TestInlineConfirmGuardAndBentoWidgets);
        Run("WidgetsGalleryAndDesktopWidgets: 5 new live Bento cards & detachable desktop widget lifecycle", TestWidgetsGalleryAndDesktopWidgets);
        Run("SettingsOverhaul: Left-rail panels, full ResetToDefaults, SettingsSearchIndex & exposed settings round-trip", TestSettingsOverhaulInvariants);

        Console.WriteLine(skipped > 0
            ? $"\nSelf-Test Summary: {passed} passed, {failed} failed, {skipped} skipped (filter: '{filter}')."
            : $"\nSelf-Test Summary: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
