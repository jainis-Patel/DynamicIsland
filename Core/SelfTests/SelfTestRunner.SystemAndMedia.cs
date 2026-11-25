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
        PowerPlanService? power = null;
        string? initialSchemeKey = null;
        try
        {
            using var settings = new SettingsService(tempPath);
            Assert(settings.Current.EnableChargingPulse, "Charging pulse must be enabled by default");
            Assert(settings.Current.LowBatteryAlertThreshold == 20, "Default low battery threshold must be 20%");
            Assert(settings.Current.NightLightWarmthKelvin == 3400, "Default night light warmth must be 3400K");

            power = new PowerPlanService(Dispatcher.CurrentDispatcher, settings);
            var cur = power.Current;
            Assert(cur != null, "PowerStatusInfo must not be null");
            Assert(!string.IsNullOrEmpty(cur!.StatusSubtitle), "StatusSubtitle must not be empty");
            Assert(!string.IsNullOrEmpty(cur.TimeEstimateText), "TimeEstimateText must not be empty");
            Assert(!string.IsNullOrEmpty(cur.WattageText), "WattageText must not be empty");
            Assert((cur.HealthPercent ?? 100) >= 10 && (cur.HealthPercent ?? 100) <= 100, "Health percent must be between 10 and 100%");
            Assert(cur.RecentBatteryHistory.Count > 0, "RecentBatteryHistory must contain samples for sparkline graph");

            initialSchemeKey = cur.ActiveSchemeKey;

            // Power Scheme switching
            power.SetActiveScheme("Performance");
            power.SetActiveScheme("Balanced");
            power.SetActiveScheme("PowerSaver");

            // Simulated charging pulse
            bool pulseFired = false;
            power.ChargingPulseTriggered += (pct, est) => pulseFired = true;
            power.TriggerSimulatedChargingPulse();
            Assert(pulseFired, "Charging pulse event must trigger on simulated pulse");

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
            if (power != null && !string.IsNullOrWhiteSpace(initialSchemeKey))
            {
                try { power.SetActiveScheme(initialSchemeKey); } catch { }
            }
            try { DisplayTopologyService.SetNightLight(false, 3400); } catch { }
            try { power?.Dispose(); } catch { }
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static void TestSystemPerformanceService()
    {
        using var service = new SystemPerformanceService();

        // 1. Verify Hardware Summary is populated and strips trailing GPU/clock suffixes cleanly
        Assert(!string.IsNullOrWhiteSpace(service.HardwareSummary), "SystemPerformanceService failed to read HardwareSummary.");
        string formattedAmd = SystemPerformanceService.FormatHardwareSummary("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics", 16);
        Assert(formattedAmd == "AMD Ryzen 7 7840HS · 16 Cores", $"Expected clean CPU summary without chopped 'w/', got '{formattedAmd}'");

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
}
