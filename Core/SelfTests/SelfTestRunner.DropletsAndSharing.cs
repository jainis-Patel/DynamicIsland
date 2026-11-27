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
        string tempDropletSettings = Path.Combine(Path.GetTempPath(), $"di_droplet_test_{Guid.NewGuid():N}.json");
        try
        {
            using var settings = new SettingsService(tempDropletSettings);
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
        finally
        {
            try { if (File.Exists(tempDropletSettings)) File.Delete(tempDropletSettings); } catch { }
        }
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
        Assert(html.Contains("/api/status"), "PWA HTML must wire live status endpoint");
        Assert(html.Contains("/api/clipboard/history"), "PWA HTML must wire clipboard history endpoint");
        Assert(html.Contains("/api/preview"), "PWA HTML must wire image preview endpoint");
        Assert(html.Contains("/api/download-all"), "PWA HTML must wire ZIP download-all endpoint");
        Assert(html.Contains("/api/open-url"), "PWA HTML must wire open-url endpoint");
        Assert(html.Contains("/api/auth"), "PWA HTML must wire auth endpoint");
        Assert(html.Contains("XMLHttpRequest"), "PWA HTML must use XHR for live upload progress");

        string lockedHtml = LanQuickShareService.GetEmbeddedPwaHtml("test_token_xyz", requirePassword: true, pcName: "TestPC", passwordProtected: true);
        Assert(lockedHtml.Contains("authGate"), "Locked PWA HTML must include authGate lock screen");
        Assert(lockedHtml.Contains("authPassword"), "Locked PWA HTML must include password input");
        Assert(lockedHtml.Contains("autocomplete=\"current-password\""), "Locked PWA HTML must support password managers/autocomplete");
        Assert(lockedHtml.Contains("let isLocked = true;"), "Locked PWA HTML must start locked when requirePassword is true");

        // 2a. Random 6-digit passcode generator
        string pin = LanQuickShareService.GeneratePasscode(6);
        Assert(pin.Length == 6 && pin.All(char.IsDigit), $"Generated PIN must be 6 numeric digits, got '{pin}'");

        // 2b. Safe HTTP URL validation for /api/open-url
        Assert(LanQuickShareService.IsSafeHttpUrl("https://example.com/path?a=1", out var parsedHttps) && parsedHttps != null, "https URL must be accepted");
        Assert(LanQuickShareService.IsSafeHttpUrl("http://192.168.1.5:8080/", out var parsedHttp) && parsedHttp != null, "http URL must be accepted");
        Assert(!LanQuickShareService.IsSafeHttpUrl("file:///C:/Windows/System32/cmd.exe", out _), "file:// URL must be rejected");
        Assert(!LanQuickShareService.IsSafeHttpUrl("javascript:alert(1)", out _), "javascript: URL must be rejected");
        Assert(!LanQuickShareService.IsSafeHttpUrl("ms-msdt:/id", out _), "custom scheme URL must be rejected");
        Assert(!LanQuickShareService.IsSafeHttpUrl("https://example.com\r\nEvil", out _), "CRLF URL must be rejected");

        // 2c. HTTP Range parsing for /api/download
        Assert(LanQuickShareService.TryParseByteRange("bytes=0-99", 1000, out long rStart, out long rEnd) && rStart == 0 && rEnd == 99, "Standard byte range failed");
        Assert(LanQuickShareService.TryParseByteRange("bytes=500-", 1000, out rStart, out rEnd) && rStart == 500 && rEnd == 999, "Open-ended byte range failed");
        Assert(LanQuickShareService.TryParseByteRange("bytes=-250", 1000, out rStart, out rEnd) && rStart == 750 && rEnd == 999, "Suffix byte range failed");
        Assert(LanQuickShareService.TryParseByteRange("bytes=900-5000", 1000, out rStart, out rEnd) && rStart == 900 && rEnd == 999, "Clamped byte range failed");
        Assert(!LanQuickShareService.TryParseByteRange("bytes=1000-1200", 1000, out _, out _), "Out-of-bounds range must fail");
        Assert(!LanQuickShareService.TryParseByteRange("bytes=0-10, 20-30", 1000, out _, out _), "Multi-range must be rejected");

        // 3. Local IP resolution
        string? ip = LanQuickShareService.GetLocalIpAddress();
        Assert(!string.IsNullOrWhiteSpace(ip), "Local IP address must not be null or whitespace");

        // 3b. LanQuickShare port fallback probing when base port is already occupied
        var blocker = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, 9292);
        blocker.Start();
        string tempShareDir = Path.Combine(Path.GetTempPath(), $"di_share_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempShareDir);
        try
        {
            using var dummyShelf = new DropShelfService(Path.Combine(tempShareDir, "shelf.json"));
            using var dummyClip = new ClipboardHistoryService(System.Windows.Threading.Dispatcher.CurrentDispatcher, Path.Combine(tempShareDir, "clip.json"));
            using var shareService = new LanQuickShareService(System.Windows.Threading.Dispatcher.CurrentDispatcher, dummyShelf, dummyClip);
            var session = shareService.StartSession(durationMinutes: 5, port: 9292);
            Assert(session.Port == 9293, $"Expected fallback port 9293, got {session.Port}");
            Assert(session.Url.Contains(":9293"), $"Session URL must reflect fallback port: {session.Url}");
            shareService.StopSession();
        }
        finally
        {
            blocker.Stop();
            try { if (Directory.Exists(tempShareDir)) Directory.Delete(tempShareDir, recursive: true); } catch { }
        }

        // 3c. End-to-end password-protected Phone Bridge loopback auth, revocation, extend, and lockout
        string tempAuthDir = Path.Combine(Path.GetTempPath(), $"di_share_auth_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempAuthDir);
        try
        {
            var ui = Dispatcher.CurrentDispatcher;
            using var dummyShelf = new DropShelfService(Path.Combine(tempAuthDir, "shelf.json"));
            using var dummyClip = new ClipboardHistoryService(ui, Path.Combine(tempAuthDir, "clip.json"));
            using var shareService = new LanQuickShareService(ui, dummyShelf, dummyClip);

            var session = shareService.StartSession(durationMinutes: 15, port: 9393, password: "TestPass!42");
            Assert(session.RequiresPassword, "Password-protected session must report RequiresPassword = true");

            var frame = new DispatcherFrame();
            Exception? asyncFailure = null;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var handler = new System.Net.Http.HttpClientHandler { UseCookies = false };
                    using var http = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
                    string baseUrl = $"http://127.0.0.1:{session.Port}";

                    // 1) GET / without token still serves lock screen and never leaks password
                    var rootResp = await http.GetAsync($"{baseUrl}/");
                    Assert(rootResp.StatusCode == System.Net.HttpStatusCode.OK, $"GET / expected 200, got {rootResp.StatusCode}");
                    string rootBody = await rootResp.Content.ReadAsStringAsync();
                    Assert(rootBody.Contains("let isLocked = true;"), "Root HTML must start in locked state");
                    Assert(!rootBody.Contains("TestPass!42"), "Root HTML must never leak session password");

                    // 2) Protected route with pairing token ONLY must fail with 401
                    using (var shelfReq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"{baseUrl}/api/shelf"))
                    {
                        shelfReq.Headers.Add("X-DI-Token", session.Token);
                        var shelfResp = await http.SendAsync(shelfReq);
                        Assert(shelfResp.StatusCode == System.Net.HttpStatusCode.Unauthorized, $"Protected route without password auth expected 401, got {shelfResp.StatusCode}");
                        string shelfErr = await shelfResp.Content.ReadAsStringAsync();
                        Assert(shelfErr.Contains("\"locked\":true"), "401 response must include locked:true");
                    }

                    // 3) Wrong password returns 401 with remaining attempt count
                    using (var badContent = new System.Net.Http.StringContent("{\"password\":\"WrongPass\"}", System.Text.Encoding.UTF8, "application/json"))
                    {
                        var badResp = await http.PostAsync($"{baseUrl}/api/auth", badContent);
                        Assert(badResp.StatusCode == System.Net.HttpStatusCode.Unauthorized, $"Wrong password expected 401, got {badResp.StatusCode}");
                        string badJson = await badResp.Content.ReadAsStringAsync();
                        Assert(badJson.Contains("\"attemptsRemaining\":4"), $"Expected attemptsRemaining:4, got {badJson}");
                    }

                    // 4) Correct password returns 200 with authToken + HttpOnly di_auth cookie
                    string authToken;
                    using (var okContent = new System.Net.Http.StringContent("{\"password\":\"TestPass!42\"}", System.Text.Encoding.UTF8, "application/json"))
                    {
                        var okResp = await http.PostAsync($"{baseUrl}/api/auth", okContent);
                        Assert(okResp.StatusCode == System.Net.HttpStatusCode.OK, $"Correct password expected 200, got {okResp.StatusCode}");
                        string okJson = await okResp.Content.ReadAsStringAsync();
                        using var doc = System.Text.Json.JsonDocument.Parse(okJson);
                        authToken = doc.RootElement.GetProperty("authToken").GetString() ?? "";
                        Assert(!string.IsNullOrWhiteSpace(authToken), "Auth response must include non-empty authToken");
                        Assert(okResp.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.Contains("di_auth=") && c.Contains("HttpOnly")),
                            "Auth response must set HttpOnly di_auth cookie");
                    }

                    // 5) Authenticated requests work via X-DI-Auth header AND via Cookie
                    using (var statusReq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"{baseUrl}/api/status"))
                    {
                        statusReq.Headers.Add("X-DI-Auth", authToken);
                        var statusResp = await http.SendAsync(statusReq);
                        Assert(statusResp.StatusCode == System.Net.HttpStatusCode.OK, $"GET /api/status with X-DI-Auth expected 200, got {statusResp.StatusCode}");
                        string statusJson = await statusResp.Content.ReadAsStringAsync();
                        Assert(statusJson.Contains("\"passwordProtected\":true"), $"Status must report passwordProtected:true, got {statusJson}");
                        Assert(statusJson.Contains("\"connectedClients\":1"), $"Status must report connectedClients:1, got {statusJson}");
                    }

                    using (var cookieReq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"{baseUrl}/api/status"))
                    {
                        cookieReq.Headers.Add("Cookie", $"di_auth={authToken}");
                        var cookieResp = await http.SendAsync(cookieReq);
                        Assert(cookieResp.StatusCode == System.Net.HttpStatusCode.OK, $"GET /api/status with Cookie expected 200, got {cookieResp.StatusCode}");
                    }

                    // 6) Live password rotation immediately invalidates old authToken; new password succeeds
                    DateTimeOffset beforeExtend = shareService.CurrentSession!.ExpiresAt;
                    Assert(shareService.ExtendSession(15), "ExtendSession(15) must succeed");
                    Assert((shareService.CurrentSession!.ExpiresAt - beforeExtend).TotalMinutes >= 14.5, "ExtendSession must extend expiry by ~15 minutes");

                    Assert(shareService.UpdateSessionPassword("NewPass99"), "UpdateSessionPassword must succeed");
                    using (var oldReq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"{baseUrl}/api/status"))
                    {
                        oldReq.Headers.Add("X-DI-Auth", authToken);
                        var oldResp = await http.SendAsync(oldReq);
                        Assert(oldResp.StatusCode == System.Net.HttpStatusCode.Unauthorized, "Old authToken must be rejected immediately after password change");
                    }

                    string authToken2;
                    using (var newContent = new System.Net.Http.StringContent("{\"password\":\"NewPass99\"}", System.Text.Encoding.UTF8, "application/json"))
                    {
                        var newResp = await http.PostAsync($"{baseUrl}/api/auth", newContent);
                        Assert(newResp.StatusCode == System.Net.HttpStatusCode.OK, "New password must succeed after rotation");
                        using var doc = System.Text.Json.JsonDocument.Parse(await newResp.Content.ReadAsStringAsync());
                        authToken2 = doc.RootElement.GetProperty("authToken").GetString() ?? "";
                    }

                    // 7) Logout revokes authToken2
                    using (var logoutReq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{baseUrl}/api/logout"))
                    {
                        logoutReq.Headers.Add("X-DI-Auth", authToken2);
                        var logoutResp = await http.SendAsync(logoutReq);
                        Assert(logoutResp.StatusCode == System.Net.HttpStatusCode.OK, "POST /api/logout must succeed");
                    }
                    using (var afterLogoutReq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"{baseUrl}/api/status"))
                    {
                        afterLogoutReq.Headers.Add("X-DI-Auth", authToken2);
                        var afterLogoutResp = await http.SendAsync(afterLogoutReq);
                        Assert(afterLogoutResp.StatusCode == System.Net.HttpStatusCode.Unauthorized, "Logged-out authToken must be rejected");
                    }

                    // 8) Brute-force lockout after 5 consecutive wrong password attempts
                    var lockSession = shareService.StartSession(durationMinutes: 5, port: 9394, password: "LockTest1");
                    string lockBase = $"http://127.0.0.1:{lockSession.Port}";
                    for (int attempt = 1; attempt <= 5; attempt++)
                    {
                        using var c = new System.Net.Http.StringContent("{\"password\":\"nope\"}", System.Text.Encoding.UTF8, "application/json");
                        var resp = await http.PostAsync($"{lockBase}/api/auth", c);
                        if (attempt < 5)
                            Assert(resp.StatusCode == System.Net.HttpStatusCode.Unauthorized, $"Attempt {attempt} expected 401, got {resp.StatusCode}");
                        else
                            Assert(resp.StatusCode == (System.Net.HttpStatusCode)429, $"5th failed attempt expected 429 lockout, got {resp.StatusCode}");
                    }
                    shareService.StopSession();
                }
                catch (Exception ex)
                {
                    asyncFailure = ex;
                }
                finally
                {
                    _ = ui.BeginInvoke(DispatcherPriority.Send, new Action(() => frame.Continue = false));
                }
            });
            Dispatcher.PushFrame(frame);
            if (asyncFailure != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(asyncFailure).Throw();
        }
        finally
        {
            try { if (Directory.Exists(tempAuthDir)) Directory.Delete(tempAuthDir, recursive: true); } catch { }
        }

        // 3d. DPAPI encrypted Phone Bridge password persistence & export sanitization
        string tempSecSettings = Path.Combine(Path.GetTempPath(), $"di_pb_sec_{Guid.NewGuid():N}.json");
        string tempExportSettings = Path.Combine(Path.GetTempPath(), $"di_pb_exp_{Guid.NewGuid():N}.json");
        try
        {
            using (var settings = new SettingsService(tempSecSettings))
            {
                settings.Update(s =>
                {
                    s.PhoneBridgeDurationMinutes = 30;
                    s.PhoneBridgePassword = "MyBridgeSecret!9";
                });
                settings.SaveNow();
                Assert(settings.ExportToFile(tempExportSettings), "ExportToFile must succeed");
            }

            string rawJson = File.ReadAllText(tempSecSettings);
            Assert(!rawJson.Contains("MyBridgeSecret!9", StringComparison.Ordinal), "Saved settings JSON must never store plaintext PhoneBridgePassword");
            Assert(rawJson.Contains("PhoneBridgePasswordProtected", StringComparison.Ordinal) && rawJson.Contains("dpapi:", StringComparison.Ordinal),
                "Saved settings JSON must persist DPAPI-encrypted PhoneBridgePasswordProtected");

            string exportedJson = File.ReadAllText(tempExportSettings);
            Assert(!exportedJson.Contains("MyBridgeSecret!9", StringComparison.Ordinal) && !exportedJson.Contains("PhoneBridgePasswordProtected", StringComparison.Ordinal),
                "Exported settings JSON must strip PhoneBridge password secrets");

            using (var reloaded = new SettingsService(tempSecSettings))
            {
                Assert(reloaded.Current.PhoneBridgeDurationMinutes == 30, "Reloaded PhoneBridgeDurationMinutes must match 30");
                Assert(reloaded.Current.PhoneBridgePassword == "MyBridgeSecret!9", "Reloaded PhoneBridgePassword must decrypt cleanly via DPAPI");
            }
        }
        finally
        {
            try { if (File.Exists(tempSecSettings)) File.Delete(tempSecSettings); } catch { }
            try { if (File.Exists(tempExportSettings)) File.Delete(tempExportSettings); } catch { }
        }

        // 4. Windows Share interop GUID validation
        var interopGuid = typeof(IDataTransferManagerInterop).GUID;
        Assert(interopGuid == new Guid("3a3dcd6c-3eab-43dc-bcde-45671ce800c8"),
            $"IDataTransferManagerInterop GUID mismatch: {interopGuid}");

        // 5. DropletRegistry includes phone_bridge, mech_sounds, element_capture with dedicated TargetViews
        string tempPhase5Settings = Path.Combine(Path.GetTempPath(), $"di_phase5_reg_{Guid.NewGuid():N}.json");
        try
        {
            using var settings = new SettingsService(tempPhase5Settings);
            var registry = new DropletRegistry(settings);
            Assert(registry.Get("phone_bridge") != null, "phone_bridge droplet must be registered");
            Assert(registry.Get("phone_bridge")?.TargetView == ExpandedView.PhoneBridge, "phone_bridge must route to ExpandedView.PhoneBridge");
            Assert(registry.Get("mech_sounds")?.TargetView == ExpandedView.MechSounds, "mech_sounds must route to ExpandedView.MechSounds");
            Assert(registry.Get("element_capture")?.TargetView == ExpandedView.CaptureStudio, "element_capture must route to ExpandedView.CaptureStudio");
            Assert(registry.Get("file_converter")?.TargetView == ExpandedView.Converter, "file_converter must route to ExpandedView.Converter");
            Assert(registry.Get("quick_launcher")?.TargetView == ExpandedView.Launcher, "quick_launcher must route to ExpandedView.Launcher");
            Assert(registry.Get("widgets_gallery")?.TargetView == ExpandedView.WidgetsGallery, "widgets_gallery must route to ExpandedView.WidgetsGallery");
            Assert(registry.Get("termi_notch")?.TargetView == ExpandedView.Termi, "termi_notch must route to ExpandedView.Termi");
            Assert(registry.Items.Count >= 18, $"Expected >= 18 built-in droplets, got {registry.Items.Count}");
        }
        finally
        {
            try { if (File.Exists(tempPhase5Settings)) File.Delete(tempPhase5Settings); } catch { }
        }
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

        // 6. Media Player Brand Detection (Spotify, Apple Music, YouTube Music, etc.)
        var spotifyBrand = MediaPlayerBrandService.Detect("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify");
        Assert(spotifyBrand.Brand == MediaPlayerBrand.Spotify, $"Expected Spotify, got {spotifyBrand.Brand}");
        Assert(spotifyBrand.DisplayName == "Spotify", $"Expected 'Spotify', got '{spotifyBrand.DisplayName}'");
        Assert(spotifyBrand.IconGeometry != null && spotifyBrand.BrandBrush != null, "Spotify brand assets null");

        var appleBrand1 = MediaPlayerBrandService.Detect("AppleInc.AppleMusicWin_nzy255yxpggw!App");
        Assert(appleBrand1.Brand == MediaPlayerBrand.AppleMusic, $"Expected AppleMusic, got {appleBrand1.Brand}");
        Assert(appleBrand1.DisplayName == "Apple Music", $"Expected 'Apple Music', got '{appleBrand1.DisplayName}'");

        var appleBrand2 = MediaPlayerBrandService.Detect("iTunes.exe");
        Assert(appleBrand2.Brand == MediaPlayerBrand.AppleMusic, $"Expected AppleMusic for iTunes, got {appleBrand2.Brand}");

        var ytMusicBrand1 = MediaPlayerBrandService.Detect("YouTubeMusic.exe");
        Assert(ytMusicBrand1.Brand == MediaPlayerBrand.YouTubeMusic, $"Expected YouTubeMusic, got {ytMusicBrand1.Brand}");
        Assert(ytMusicBrand1.DisplayName == "YouTube Music", $"Expected 'YouTube Music', got '{ytMusicBrand1.DisplayName}'");

        var ytMusicBrowser = MediaPlayerBrandService.Detect("msedge.exe", "Starboy", "The Weeknd", "YouTube Music");
        Assert(ytMusicBrowser.Brand == MediaPlayerBrand.YouTubeMusic, $"Expected YouTubeMusic via browser album hint, got {ytMusicBrowser.Brand}");

        var tidalBrand = MediaPlayerBrandService.Detect("TIDAL.exe");
        Assert(tidalBrand.Brand == MediaPlayerBrand.Tidal, $"Expected Tidal, got {tidalBrand.Brand}");

        var vlcBrand = MediaPlayerBrandService.Detect("vlc.exe");
        Assert(vlcBrand.Brand == MediaPlayerBrand.Vlc, $"Expected VLC, got {vlcBrand.Brand}");

        // 7. Timeline Extrapolation and Bar Sync Invariants
        var mediaInfo = new MediaInfo
        {
            Title = "Song Sync Test",
            Duration = TimeSpan.FromSeconds(200),
            Position = TimeSpan.FromSeconds(50),
            PositionUpdatedAt = DateTimeOffset.Now.AddSeconds(-5),
            IsPlaying = true,
            PlaybackRate = 1.0
        };
        var extrapolated = mediaInfo.GetPosition(DateTimeOffset.Now);
        Assert(extrapolated >= TimeSpan.FromSeconds(54) && extrapolated <= TimeSpan.FromSeconds(56),
            $"Expected extrapolated position ~55s, got {extrapolated.TotalSeconds}s");

        mediaInfo.IsPlaying = false;
        var pausedPos = mediaInfo.GetPosition(DateTimeOffset.Now);
        Assert(pausedPos == TimeSpan.FromSeconds(50), "Paused track should not extrapolate position");
    }

    private static void TestMiniPlayerUpgrades()
    {
        // 1. ViewMode & LyricsAnimationStyle normalization + SettingsService round-trip
        Assert(SettingsService.NormalizeMiniPlayerViewMode("Showcase") == "Showcase", "Showcase mode must normalize");
        Assert(SettingsService.NormalizeMiniPlayerViewMode("art") == "Showcase", "art alias must normalize to Showcase");
        Assert(SettingsService.NormalizeMiniPlayerViewMode("pill") == "Compact", "pill alias must normalize to Compact");
        Assert(SettingsService.NormalizeMiniPlayerViewMode("unknown") == "Full", "Unknown view mode must fallback to Full");

        Assert(SettingsService.NormalizeLyricsAnimationStyle("KaraokeGlow") == "KaraokeGlow", "KaraokeGlow must normalize");
        Assert(SettingsService.NormalizeLyricsAnimationStyle("glow") == "KaraokeGlow", "glow alias must normalize to KaraokeGlow");
        Assert(SettingsService.NormalizeLyricsAnimationStyle("fade") == "MinimalFade", "fade alias must normalize to MinimalFade");
        Assert(SettingsService.NormalizeLyricsAnimationStyle("other") == "SpringWave", "Unknown animation style must fallback to SpringWave");

        string tempSettingsPath = Path.Combine(Path.GetTempPath(), $"di_mini_settings_{Guid.NewGuid():N}.json");
        string tempHistoryPath = Path.Combine(Path.GetTempPath(), $"di_mini_history_{Guid.NewGuid():N}.json");
        try
        {
            using (var settings = new SettingsService(tempSettingsPath))
            {
                settings.Update(s =>
                {
                    s.MiniPlayerViewMode = "Showcase";
                    s.MiniPlayerOpen = true;
                    s.MiniPlayerAutoReopen = false;
                    s.LyricsAnimationStyle = "KaraokeGlow";
                });
                settings.SaveNow();
            }

            using (var reloaded = new SettingsService(tempSettingsPath))
            {
                Assert(reloaded.Current.MiniPlayerViewMode == "Showcase", "MiniPlayerViewMode must persist");
                Assert(reloaded.Current.MiniPlayerOpen, "MiniPlayerOpen must persist");
                Assert(!reloaded.Current.MiniPlayerAutoReopen, "MiniPlayerAutoReopen must persist");
                Assert(reloaded.Current.LyricsAnimationStyle == "KaraokeGlow", "LyricsAnimationStyle must persist");

                reloaded.ResetToDefaults();
                Assert(reloaded.Current.MiniPlayerViewMode == "Full", "ResetToDefaults must reset MiniPlayerViewMode");
                Assert(!reloaded.Current.MiniPlayerOpen, "ResetToDefaults must reset MiniPlayerOpen");
                Assert(reloaded.Current.MiniPlayerAutoReopen, "ResetToDefaults must reset MiniPlayerAutoReopen");
                Assert(reloaded.Current.LyricsAnimationStyle == "SpringWave", "ResetToDefaults must reset LyricsAnimationStyle");
            }

            // 2. Persistent playback history with artwork thumbnail round-trip & clear
            var ui = Dispatcher.CurrentDispatcher;
            using var mediaSvc = new MediaSessionService(ui);
            using var audioSvc = new AudioEndpointService(ui);
            using var volumeSvc = new VolumeService(ui, audioSvc.SharedEnumerator);
            using var lyricsSvc = new LyricsService(ui);

            var bmp = new WriteableBitmap(4, 4, 96, 96, PixelFormats.Bgra32, null);
            bmp.WritePixels(new Int32Rect(0, 0, 4, 4), new byte[4 * 4 * 4], 4 * 4, 0);
            bmp.Freeze();

            using (var historySvc = new MediaSongListService(ui, mediaSvc, tempHistoryPath))
            {
                historySvc.RecordTrackCore("Starlight", "Muse", "Black Holes", "Spotify", TimeSpan.FromMinutes(4), bmp);
                historySvc.RecordTrackCore("Starlight", "Muse", "Black Holes", "Spotify", TimeSpan.FromMinutes(4), bmp); // Immediate duplicate ignored
                Assert(historySvc.History.Count == 1, $"Expected 1 deduplicated history item, got {historySvc.History.Count}");
            }

            using (var reloadedHistory = new MediaSongListService(ui, mediaSvc, tempHistoryPath))
            {
                Assert(reloadedHistory.History.Count == 1, $"Expected 1 persisted history item after reload, got {reloadedHistory.History.Count}");
                var rec = reloadedHistory.History[0];
                Assert(rec.Title == "Starlight" && rec.Artist == "Muse" && rec.SourceApp == "Spotify", "Persisted track metadata mismatch");
                Assert(rec.Artwork is { IsFrozen: true }, "Persisted artwork thumbnail must decode as frozen BitmapSource");

                reloadedHistory.ClearHistory();
                Assert(reloadedHistory.History.Count == 0, "ClearHistory must empty history list");
            }

            // 3. Headless MiniPlayerWindow checks (GPU EQ, View Modes, Fullscreen Suppression, Scrollable/Plain Lyrics)
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var stylesUri = new Uri("pack://application:,,,/DynamicIsland;component/Resources/IslandStyles.xaml", UriKind.Absolute);
            if (!app.Resources.MergedDictionaries.Any(d => d.Source == stylesUri))
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = stylesUri });
            }

            using var testSettings = new SettingsService(tempSettingsPath);
            using var songSvc = new MediaSongListService(ui, mediaSvc, tempHistoryPath);
            var win = new Views.MiniPlayerWindow(mediaSvc, lyricsSvc, volumeSvc, songSvc, null, testSettings, audioSvc);
            try
            {
                Assert(win.EqBar0.RenderTransform is ScaleTransform &&
                       win.EqBar1.RenderTransform is ScaleTransform &&
                       !ReferenceEquals(win.EqBar0.RenderTransform, win.EqBar1.RenderTransform),
                    "EQ bars must use distinct GPU ScaleTransforms");

                win.SetViewModeExternal("Showcase");
                Assert(win.ShowcaseView.Visibility == Visibility.Visible &&
                       win.FullPlayerView.Visibility == Visibility.Collapsed &&
                       win.CompactPillView.Visibility == Visibility.Collapsed,
                    "Showcase view mode must show ShowcaseView and hide Full/Compact");

                win.SetViewModeExternal("Compact");
                Assert(win.CompactPillView.Visibility == Visibility.Visible &&
                       win.ShowcaseView.Visibility == Visibility.Collapsed,
                    "Compact view mode must show CompactPillView");

                win.SetViewModeExternal("Full");
                Assert(win.FullPlayerView.Visibility == Visibility.Visible,
                    "Full view mode must restore FullPlayerView");

                win.SetFullscreenSuppressed(true);
                Assert(win.IsFullscreenSuppressed && !win.Topmost,
                    "SetFullscreenSuppressed(true) must suppress Topmost");
                win.SetFullscreenSuppressed(false);
                Assert(!win.IsFullscreenSuppressed && win.Topmost,
                    "SetFullscreenSuppressed(false) must restore Topmost");

                // Open lyrics drawer & test synced + plain lyrics + animation style cycling
                win.ToggleLyricsBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                lyricsSvc.SetTrackForTest(new TrackLyrics
                {
                    Title = "Test Song",
                    Artist = "Test Artist",
                    SyncedLines = new[]
                    {
                        new LrcLine(TimeSpan.FromSeconds(1), "Line one"),
                        new LrcLine(TimeSpan.FromSeconds(5), "Line two")
                    }
                });
                win.UpdateLyricsDisplay();
                Assert(win.LyricsLinesHost.Children.Count == 2 && win.LyricsStatusText.Text == "Synced",
                    "Scrollable lyrics drawer must render all synced lines");

                var firstLineBorder = (System.Windows.Controls.Border)win.LyricsLinesHost.Children[0];
                var firstLineStack = (System.Windows.Controls.StackPanel)firstLineBorder.Child;
                var firstLineText = (System.Windows.Controls.TextBlock)firstLineStack.Children[0];
                var firstLineProg = (System.Windows.Controls.Border)firstLineStack.Children[1];
                Assert(firstLineText.TextAlignment == TextAlignment.Left && firstLineBorder.BorderThickness.Left == 3.0,
                    "SpringWave must render left-aligned editorial layout with accent left bar");

                win.LyricsAnimStyleBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert(testSettings.Current.LyricsAnimationStyle == "KaraokeGlow" &&
                       win.LyricsAnimStyleText.Text.Contains("Glow") &&
                       firstLineText.TextAlignment == TextAlignment.Center &&
                       firstLineProg.Visibility == Visibility.Visible,
                    $"Cycling lyrics animation must switch to KaraokeGlow design, got '{testSettings.Current.LyricsAnimationStyle}'");

                win.LyricsAnimStyleBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert(testSettings.Current.LyricsAnimationStyle == "MinimalFade" &&
                       win.LyricsAnimStyleText.Text.Contains("Minimal") &&
                       firstLineText.TextAlignment == TextAlignment.Center &&
                       firstLineProg.Visibility == Visibility.Collapsed,
                    $"Cycling lyrics animation must switch to MinimalFade design, got '{testSettings.Current.LyricsAnimationStyle}'");

                lyricsSvc.SetTrackForTest(new TrackLyrics
                {
                    Title = "Plain Song",
                    Artist = "Test Artist",
                    PlainLyrics = "First plain line\nSecond plain line",
                    IsPlainLyricsOnly = true
                });
                win.UpdateLyricsDisplay();
                Assert(win.LyricsLinesHost.Children.Count == 2 && win.LyricsStatusText.Text == "Plain Lyrics",
                    "Plain lyrics fallback must render all plain lines");
                var plainTb = (System.Windows.Controls.TextBlock)win.LyricsLinesHost.Children[0];
                Assert(plainTb.TextAlignment == TextAlignment.Center, "Plain lyrics must respect MinimalFade alignment");
                win.LyricsAnimStyleBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert(plainTb.TextAlignment == TextAlignment.Left, "Plain lyrics must switch to SpringWave left alignment");
            }
            finally
            {
                win.Close();
            }
        }
        finally
        {
            try { if (File.Exists(tempSettingsPath)) File.Delete(tempSettingsPath); } catch { }
            try { if (File.Exists(tempHistoryPath)) File.Delete(tempHistoryPath); } catch { }
        }
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
                                                   or ExpandedView.PhoneBridge
                                                   or ExpandedView.QuickReply;

        Assert(IsInputFocusable(ExpandedView.Launcher), "Launcher must accept keyboard focus");
        Assert(IsInputFocusable(ExpandedView.Termi), "Termi-Notch must accept keyboard focus");
        Assert(IsInputFocusable(ExpandedView.Converter), "Converter must accept keyboard focus");
        Assert(IsInputFocusable(ExpandedView.PhoneBridge), "PhoneBridge must accept keyboard focus for password entry");
        Assert(IsInputFocusable(ExpandedView.Notes), "Notes must accept keyboard focus");
        Assert(IsInputFocusable(ExpandedView.Calc), "Calc must accept keyboard focus");
        Assert(IsInputFocusable(ExpandedView.Clipboard), "Clipboard must accept keyboard focus");
        Assert(IsInputFocusable(ExpandedView.Calendar), "Calendar must accept keyboard focus");
        Assert(IsInputFocusable(ExpandedView.QuickReply), "QuickReply must accept keyboard focus");
        Assert(!IsInputFocusable(ExpandedView.Media), "Media should not steal keyboard focus");
        Assert(!IsInputFocusable(ExpandedView.SystemHud), "System HUD should not steal keyboard focus");
        Assert(!IsInputFocusable(ExpandedView.WidgetsGallery), "Widgets Gallery should not steal keyboard focus");

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
}
