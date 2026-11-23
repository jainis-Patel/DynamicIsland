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
    private static void TestTouchpadGesturesAndVelocityInvariants()
    {
        var proc = new TouchpadGestureProcessor
        {
            FlickVelocityThreshold = 400.0,
            FlickAccumulatorThreshold = 70.0,
            ScrubDeltaThreshold = 35.0,
            VerticalPullThreshold = 90.0,
            FlickCooldownMs = 300,
            ExpandCooldownMs = 300
        };

        long t0 = 10000;

        // 1. High-velocity horizontal flick: NextTrack
        // Rapid 3 deltas within 30ms (high velocity ~1000 units/s)
        var res1 = proc.ProcessHorizontalScroll(30, t0, isMediaActive: true, canSeek: true, isExpanded: false);
        var res2 = proc.ProcessHorizontalScroll(30, t0 + 10, isMediaActive: true, canSeek: true, isExpanded: false);
        var res3 = proc.ProcessHorizontalScroll(30, t0 + 20, isMediaActive: true, canSeek: true, isExpanded: false);

        Assert(res1.Kind == GestureActionKind.None, "First micro-delta should not prematurely trigger flick");
        Assert(res3.Kind == GestureActionKind.NextTrack, $"Third rapid delta must trigger NextTrack flick, got {res3.Kind}");
        Assert(res3.Velocity >= 400.0, $"Flick velocity must exceed threshold, got {res3.Velocity}");

        // 2. Debounce: immediate subsequent delta within cooldown must be suppressed
        var resDebounced = proc.ProcessHorizontalScroll(30, t0 + 30, isMediaActive: true, canSeek: true, isExpanded: false);
        Assert(resDebounced.Kind == GestureActionKind.None, "Subsequent delta within cooldown must be debounced");

        // 3. Fast negative flick after cooldown: PreviousTrack
        long tFlick2 = t0 + 350;
        proc.ProcessHorizontalScroll(-30, tFlick2, isMediaActive: true, canSeek: true, isExpanded: false);
        proc.ProcessHorizontalScroll(-30, tFlick2 + 10, isMediaActive: true, canSeek: true, isExpanded: false);
        var resPrev = proc.ProcessHorizontalScroll(-30, tFlick2 + 20, isMediaActive: true, canSeek: true, isExpanded: false);
        Assert(resPrev.Kind == GestureActionKind.PreviousTrack, $"Negative flick must trigger PreviousTrack, got {resPrev.Kind}");

        // 4. Smooth Two-Finger Scrubbing (low velocity, below flick threshold)
        long tScrub = tFlick2 + 400;
        proc.Reset();
        // Gentle scrolls 15 units every 50ms (velocity = 300 < 400)
        var s1 = proc.ProcessHorizontalScroll(15, tScrub, isMediaActive: true, canSeek: true, isExpanded: false);
        Assert(s1.Kind == GestureActionKind.None, "Sub-threshold delta must accumulate without triggering");

        var s2 = proc.ProcessHorizontalScroll(25, tScrub + 60, isMediaActive: true, canSeek: true, isExpanded: false);
        // Total accumulated = 40 >= ScrubDeltaThreshold (35)
        Assert(s2.Kind == GestureActionKind.Scrub, $"Accumulated horizontal drag must emit Scrub action, got {s2.Kind}");
        Assert(s2.ScrubOffset > TimeSpan.Zero, $"Forward scrub offset must be positive, got {s2.ScrubOffset}");

        // 5. Vertical Pull-to-Expand in Compact State
        long tVert = tScrub + 500;
        proc.Reset();
        // Downward scroll is negative delta
        proc.ProcessVerticalScroll(-40, tVert, IslandState.Compact);
        var vExpand = proc.ProcessVerticalScroll(-60, tVert + 15, IslandState.Compact);
        Assert(vExpand.Kind == GestureActionKind.ExpandPull, $"Downward two-finger pull must emit ExpandPull, got {vExpand.Kind}");

        // 6. Vertical Push-to-Collapse in Expanded State
        long tVert2 = tVert + 400;
        proc.Reset();
        // Upward scroll is positive delta
        proc.ProcessVerticalScroll(40, tVert2, IslandState.Expanded);
        var vCollapse = proc.ProcessVerticalScroll(60, tVert2 + 15, IslandState.Expanded);
        Assert(vCollapse.Kind == GestureActionKind.CollapsePinch, $"Upward two-finger push must emit CollapsePinch, got {vCollapse.Kind}");

        // 7. Multi-Touch Win32 GID_ZOOM Pinch Testing
        var giBegin = new NativeMethods.GESTUREINFO
        {
            dwID = NativeMethods.GID_ZOOM,
            dwFlags = NativeMethods.GF_BEGIN,
            ullArguments = 1000
        };
        proc.ProcessWin32Gesture(ref giBegin, IslandState.Compact, isMediaActive: false, canSeek: false);

        // Zoom in / Pinch out (distance expands from 1000 to 1350)
        var giZoomIn = new NativeMethods.GESTUREINFO
        {
            dwID = NativeMethods.GID_ZOOM,
            dwFlags = 0,
            ullArguments = 1350
        };
        var zoomRes = proc.ProcessWin32Gesture(ref giZoomIn, IslandState.Compact, isMediaActive: false, canSeek: false);
        Assert(zoomRes.Kind == GestureActionKind.ExpandPull, $"Pinch-out zoom ratio 1.35 must trigger ExpandPull, got {zoomRes.Kind}");

        // Zoom out / Pinch in (distance collapses from 1000 to 700) in Expanded state
        var giZoomOut = new NativeMethods.GESTUREINFO
        {
            dwID = NativeMethods.GID_ZOOM,
            dwFlags = 0,
            ullArguments = 700
        };
        var pinchRes = proc.ProcessWin32Gesture(ref giZoomOut, IslandState.Expanded, isMediaActive: false, canSeek: false);
        Assert(pinchRes.Kind == GestureActionKind.CollapsePinch, $"Pinch-in zoom ratio 0.70 must trigger CollapsePinch, got {pinchRes.Kind}");

        // 8. Zero-Allocation Invariant: 1,000 gesture calls
        proc.Reset();
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            proc.ProcessHorizontalScroll((i % 20) - 10, t0 + i * 10, isMediaActive: true, canSeek: true, isExpanded: false);
        }
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();
        Assert(allocAfter - allocBefore == 0, $"Gesture loop must allocate 0 heap bytes, allocated {allocAfter - allocBefore} bytes");
    }

    private static void TestAiAgentInteractiveApprovalsAndDiffViewer()
    {
        // 1. Two-way Named Pipe IPC Prompt Flow & Hub Invariants
        var hub = new LiveActivityHub();
        using var ipc = new LiveActivityIpcService(hub);

        var promptReq = new ActivityIpcRequest
        {
            Action = "prompt",
            Id = "ai_agent_approval_001",
            Source = "Antigravity Agent",
            Title = "File Edit Authorization",
            Subtitle = "Modify LiveActivityIpcService.cs",
            Buttons = new[] { "Approve", "Reject", "Diff" },
            Diff = "--- a/Core/Service.cs\n+++ b/Core/Service.cs\n@@ -10,4 +10,6 @@\n public void Run()\n-    OldCall();\n+    NewCall();\n+    AuditLog();\n }\n",
            Priority = 150
        };

        var resp = ipc.ProcessRequest(promptReq);
        Assert(resp.Ok, "ProcessRequest with action 'prompt' must succeed");
        Assert(ipc.HasPendingPrompts, "LiveActivityIpcService must report HasPendingPrompts == true");
        Assert(ipc.GetLatestPendingPromptId() == "ai_agent_approval_001", "GetLatestPendingPromptId must match registered prompt id");

        var act = hub.Activities.FirstOrDefault(a => a.Id == "ai_agent_approval_001");
        Assert(act != null, "Published prompt must exist in LiveActivityHub");
        Assert(act!.IsInteractivePrompt, "LiveActivityItem.IsInteractivePrompt must be true for prompt action");
        Assert(act.Diff != null && act.Diff.Contains("NewCall()"), "LiveActivityItem must preserve diff string");
        Assert(act.Actions != null && act.Actions.Count == 3, "LiveActivityItem must have exactly 3 actions");
        Assert(act.Actions![0].Label == "Approve", "First action should be Approve");
        Assert(act.Actions[1].Label == "Reject", "Second action should be Reject");
        Assert(act.Actions[2].Label == "Diff", "Third action should be Diff");

        // 2. Resolve prompt decision via IPC
        bool resolved = ipc.ResolvePrompt("ai_agent_approval_001", "Approve");
        Assert(resolved, "ResolvePrompt must return true for registered prompt");
        Assert(!ipc.HasPendingPrompts, "HasPendingPrompts must be false after prompt resolution");
        Assert(!hub.Activities.Any(a => a.Id == "ai_agent_approval_001"), "Prompt activity must be completed and removed from hub upon resolution");

        // 3. Graceful fallback on non-existent prompt
        Assert(!ipc.ResolvePrompt("non_existent_prompt", "Approve"), "ResolvePrompt for invalid ID must return false");

        // 4. Unified Diff Syntax Classification & Metrics Extraction
        string testDiff = "diff --git a/src/Island.cs b/src/Island.cs\n" +
                          "--- a/src/Island.cs\n" +
                          "+++ b/src/Island.cs\n" +
                          "@@ -45,6 +45,8 @@\n" +
                          " public void Initialize()\n" +
                          " {\n" +
                          "-    SetupLegacyNotch();\n" +
                          "+    SetupModernIsland();\n" +
                          "+    InitializeSpringEngine();\n" +
                          "+    RegisterTouchpadGestures();\n" +
                          " }\n" +
                          "@@ -100,3 +102,2 @@\n" +
                          "-    DeprecatedCall();\n" +
                          " }";

        var (filename, added, removed, chunks) = TermiNotchView.ParseDiffMetrics(testDiff);
        Assert(filename == "src/Island.cs", $"Expected filename 'src/Island.cs', got '{filename}'");
        Assert(added == 3, $"Expected 3 added lines, got {added}");
        Assert(removed == 2, $"Expected 2 removed lines, got {removed}");
        Assert(chunks == 2, $"Expected 2 diff chunks, got {chunks}");

        var emptyMetrics = TermiNotchView.ParseDiffMetrics("");
        Assert(emptyMetrics.Filename == "code.diff" && emptyMetrics.Added == 0 && emptyMetrics.Removed == 0 && emptyMetrics.Chunks == 0,
            "Empty diff must yield default code.diff with zero metrics");

        // 5. ToastContent Interactive Prompt Invariant
        string? chosenDecision = null;
        var toast = new ToastContent(
            Glyph: "\uE756",
            Title: "AI Prompt",
            Subtitle: "Authorize changes?",
            Accent: ThemeBrushes.BlueColor,
            Duration: TimeSpan.FromSeconds(30),
            PromptActivityId: "agent_prompt_99",
            DiffText: "+ line added",
            OnPromptDecision: decision => chosenDecision = decision
        );

        Assert(toast.IsInteractivePrompt, "ToastContent.IsInteractivePrompt must be true when PromptActivityId is populated");
        Assert(toast.DiffText == "+ line added", "ToastContent must preserve DiffText");
        toast.OnPromptDecision?.Invoke("Reject");
        Assert(chosenDecision == "Reject", "Invoking OnPromptDecision must pass decision through");

        hub.ClearAll();
    }

    private static void TestPillar3DetachableFloatingIslandAndHardwareNotch()
    {
        // 1. Tear-Off Drag Threshold Invariant
        Assert(MagneticDockingEngine.TearOffThreshold == 35.0, "TearOffThreshold must be 35.0 DIP");
        Assert(MagneticDockingEngine.MagneticSnapRadius == 85.0, "MagneticSnapRadius must be 85.0 DIP");

        // 2. Magnetic Edge & Corner Snapping Calculations
        var monBounds = new Rect(0, 0, 1920, 1080);
        var workArea = new Rect(0, 0, 1920, 1040); // 40px taskbar at bottom
        double pillW = 180.0;
        double pillH = 34.0;

        // 2a. Top-Center (Bezel Re-Dock)
        var (targetTop, snappedTop) = MagneticDockingEngine.CalculateMagneticSnap(
            new Point(960, 32), monBounds, workArea, pillW, pillH, notchYOffset: 0);
        Assert(targetTop == MagneticDockTarget.TopCenter, $"Expected TopCenter target near top bezel, got {targetTop}");
        Assert(snappedTop.X == 960.0, $"Snapped X must be monitor center 960, got {snappedTop.X}");
        Assert(snappedTop.Y == 17.0, $"Snapped Y must align to half-pill height 17, got {snappedTop.Y}");

        // 2b. Taskbar Notification Tray (Bottom-Right)
        var (targetTray, snappedTray) = MagneticDockingEngine.CalculateMagneticSnap(
            new Point(1800, 1005), monBounds, workArea, pillW, pillH, notchYOffset: 0);
        Assert(targetTray == MagneticDockTarget.TaskbarTray, $"Expected TaskbarTray target near bottom right, got {targetTray}");
        Assert(snappedTray.X == 1806.0, $"Expected snapped tray X 1806, got {snappedTray.X}");
        Assert(snappedTray.Y == 1011.0, $"Expected snapped tray Y 1011, got {snappedTray.Y}");

        // 2c. Bottom-Left Corner
        var (targetBL, snappedBL) = MagneticDockingEngine.CalculateMagneticSnap(
            new Point(125, 1005), monBounds, workArea, pillW, pillH, notchYOffset: 0);
        Assert(targetBL == MagneticDockTarget.BottomLeft, $"Expected BottomLeft target, got {targetBL}");
        Assert(snappedBL.X == 114.0, $"Expected snapped BL X 114, got {snappedBL.X}");
        Assert(snappedBL.Y == 1011.0, $"Expected snapped BL Y 1011, got {snappedBL.Y}");

        // 2d. Top-Right Corner
        var (targetTR, _) = MagneticDockingEngine.CalculateMagneticSnap(
            new Point(1810, 45), monBounds, workArea, pillW, pillH, notchYOffset: 0);
        Assert(targetTR == MagneticDockTarget.TopRight, $"Expected TopRight target, got {targetTR}");

        // 2e. Top-Left Corner
        var (targetTL, _) = MagneticDockingEngine.CalculateMagneticSnap(
            new Point(110, 45), monBounds, workArea, pillW, pillH, notchYOffset: 0);
        Assert(targetTL == MagneticDockTarget.TopLeft, $"Expected TopLeft target, got {targetTL}");

        // 2f. Free Floating (No Magnetic Snap)
        var (targetFree, snappedFree) = MagneticDockingEngine.CalculateMagneticSnap(
            new Point(960, 500), monBounds, workArea, pillW, pillH, notchYOffset: 0);
        Assert(targetFree == MagneticDockTarget.None, $"Expected None (free floating) at screen center, got {targetFree}");
        Assert(snappedFree.X == 960.0 && snappedFree.Y == 500.0, "Free floating position should preserve raw coordinates");

        // 3. Corner Radii Invariants
        // Detached PiP: must produce symmetric capsule (top == bottom == effectiveR)
        var (pipTopR, pipBottomR) = MagneticDockingEngine.ComputeCornerRadii(
            effectiveRadius: 17.0, isDetached: true, hardwareNotch: false, notchYOffset: 0, liquidIntensity: 0.65, motionEnergy: 0);
        Assert(pipTopR == 17.0 && pipBottomR == 17.0, $"PiP detached mode must be symmetric capsule (17, 17), got ({pipTopR}, {pipBottomR})");

        // Docked Hardware Notch: top must be flat 0, bottom rounded
        var (notchTopR, notchBottomR) = MagneticDockingEngine.ComputeCornerRadii(
            effectiveRadius: 17.0, isDetached: false, hardwareNotch: true, notchYOffset: 0, liquidIntensity: 0.65, motionEnergy: 0);
        Assert(notchTopR == 0.0 && notchBottomR == 17.0, $"Hardware notch mode must have flat top 0 and rounded bottom 17, got ({notchTopR}, {notchBottomR})");

        // 4. Dynamic Concave Ear SVG Fillet Generation
        string leftEarSvg = MagneticDockingEngine.GenerateNotchEarSvgPath(12.0, isLeft: true);
        Assert(leftEarSvg == "M 0,0 L 12,0 L 12,12 A 12,12 0 0 0 0,0 Z", $"Unexpected left ear SVG: {leftEarSvg}");

        string rightEarSvg = MagneticDockingEngine.GenerateNotchEarSvgPath(12.0, isLeft: false);
        Assert(rightEarSvg == "M 0,12 L 0,0 L 12,0 A 12,12 0 0 0 0,12 Z", $"Unexpected right ear SVG: {rightEarSvg}");

        // 5. Hardware Notch Presets
        var mbp16 = SettingsService.GetNotchPresetDimensions("MacBookPro16");
        Assert(mbp16.Width == 188.0 && mbp16.Height == 35.0 && mbp16.Radius == 12.0, "MBP16 preset dimensions mismatch");

        var mbp14 = SettingsService.GetNotchPresetDimensions("MacBookPro14");
        Assert(mbp14.Width == 172.0 && mbp14.Height == 32.0 && mbp14.Radius == 11.0, "MBP14 preset dimensions mismatch");

        var mba = SettingsService.GetNotchPresetDimensions("MacBookAir");
        Assert(mba.Width == 160.0 && mba.Height == 30.0 && mba.Radius == 10.0, "MBA preset dimensions mismatch");

        var xps = SettingsService.GetNotchPresetDimensions("DellXps");
        Assert(xps.Width == 140.0 && xps.Height == 26.0 && xps.Radius == 8.0, "Dell XPS preset dimensions mismatch");

        // 6. AppSettings serialization roundtrip for floating PiP & notch properties
        var settings = new AppSettings
        {
            IsDetachedFloating = true,
            FloatingX = 1420.5,
            FloatingY = 820.0,
            FloatingDockTarget = "TaskbarTray",
            HardwareNotchMode = true,
            HardwareNotchWidth = 172.0,
            HardwareNotchHeight = 32.0,
            HardwareNotchRadius = 11.0,
            HardwareNotchPreset = "MacBookPro14"
        };
        string json = System.Text.Json.JsonSerializer.Serialize(settings);
        var roundtripped = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
        Assert(roundtripped != null, "Settings must deserialize cleanly");
        Assert(roundtripped!.IsDetachedFloating, "IsDetachedFloating must survive serialization");
        Assert(roundtripped.FloatingX == 1420.5 && roundtripped.FloatingY == 820.0, "Floating coordinates must roundtrip exactly");
        Assert(roundtripped.HardwareNotchPreset == "MacBookPro14", "HardwareNotchPreset must roundtrip");
    }

    private static void TestPillar4CommunityDropletsSdk()
    {
        // 1. Schema Validation - Valid Definition
        string validJson = @"
        {
            ""id"": ""github_pr_tracker"",
            ""name"": ""GitHub PR Tracker"",
            ""description"": ""Track pending PR review requests & CI checks"",
            ""version"": ""1.0.0"",
            ""author"": ""Community Dev"",
            ""glyph"": ""\uE7B5"",
            ""category"": ""Productivity"",
            ""refreshIntervalSec"": 30,
            ""endpoint"": ""https://api.github.com/repos/sample/repo/pulls"",
            ""formatTemplate"": ""{value} PRs Pending"",
            ""responsePath"": ""count"",
            ""actions"": [
                { ""label"": ""Review PR"", ""glyph"": ""\uE7B5"", ""command"": ""https://github.com/pulls"", ""type"": ""OpenUrl"" }
            ]
        }";

        var def = CommunityDropletSchemaValidator.Deserialize(validJson);
        Assert(def != null, "Deserializing valid droplet JSON must succeed");
        bool valid = CommunityDropletSchemaValidator.Validate(def, out var errors);
        Assert(valid, $"Valid droplet must pass schema validation, got errors: {string.Join("; ", errors)}");
        Assert(def!.Id == "github_pr_tracker" && def.Name == "GitHub PR Tracker", "Droplet properties mismatch");
        Assert(def.RefreshIntervalSec == 30, "RefreshIntervalSec must be 30");

        // 2. Schema Validation - Negative Cases
        Assert(!CommunityDropletSchemaValidator.Validate(null, out _), "Null definition must fail validation");

        var invalidIdDef = def with { Id = "invalid name with spaces!" };
        Assert(!CommunityDropletSchemaValidator.Validate(invalidIdDef, out var idErrors) && idErrors.Count > 0,
            "Alphanumeric regex check must fail for invalid IDs");

        var invalidIntervalDef = def with { RefreshIntervalSec = 2 };
        Assert(!CommunityDropletSchemaValidator.Validate(invalidIntervalDef, out var intErrors) && intErrors.Count > 0,
            "Interval below 5s must fail validation to prevent aggressive polling");

        var invalidUrlDef = def with { Endpoint = "ftp://invalid-scheme" };
        Assert(!CommunityDropletSchemaValidator.Validate(invalidUrlDef, out var urlErrors) && urlErrors.Count > 0,
            "Non-http/https endpoint must fail validation");

        // 3. Dot-Syntax JSON Value Extraction Engine
        string sampleJson = @"{ ""status"": ""ok"", ""data"": { ""price"": 94250.75, ""currency"": ""USD"" } }";
        string extractedStatus = CommunityDropletService.ExtractJsonValue(sampleJson, "status");
        Assert(extractedStatus == "ok", $"Expected 'ok', got '{extractedStatus}'");

        string extractedPrice = CommunityDropletService.ExtractJsonValue(sampleJson, "data.price");
        Assert(extractedPrice == "94250.75", $"Expected '94250.75', got '{extractedPrice}'");

        string missingFallback = CommunityDropletService.ExtractJsonValue(sampleJson, "nonexistent.key");
        Assert(missingFallback.Contains("status"), "Missing key should gracefully return fallback content");

        // 4. Dynamic Droplet Registration & Hot-Unload Lifecycle
        var registry = new DropletRegistry();
        var hub = new LiveActivityHub();
        using var service = new CommunityDropletService(registry, hub);

        bool loaded = service.LoadDropletFromJson(validJson, notifyUser: false);
        Assert(loaded, "LoadDropletFromJson must return true for valid definition");

        var regDescriptor = registry.Get("github_pr_tracker");
        Assert(regDescriptor != null, "Droplet must be registered in DropletRegistry");
        Assert(regDescriptor!.Name == "GitHub PR Tracker", "Registered name must match");
        Assert(service.LoadedDroplets.Count == 1, "LoadedDroplets count must be 1");

        // Verify LiveActivityHub integration
        var act = hub.Activities.FirstOrDefault(a => a.Id == "community_github_pr_tracker");
        Assert(act != null, "Droplet must publish LiveActivity to hub");
        Assert(act!.Actions != null && act.Actions.Count == 1, "Published activity must include declared actions");
        Assert(act.Actions![0].Label == "Review PR", "Action label must match");

        // Hot Unregister
        bool removed = service.UnregisterCommunityDroplet("github_pr_tracker", notifyUser: false);
        Assert(removed, "UnregisterCommunityDroplet must return true");
        Assert(registry.Get("github_pr_tracker") == null, "Droplet must be removed from DropletRegistry");
        Assert(!hub.Activities.Any(a => a.Id == "community_github_pr_tracker"), "Activity must be removed from hub");
        Assert(service.LoadedDroplets.Count == 0, "LoadedDroplets count must be 0 after unregister");

        // 5. AppPaths DropletsDirectory Invariant
        string dropletsDir = AppPaths.DropletsDirectory;
        Assert(Directory.Exists(dropletsDir), $"DropletsDirectory '{dropletsDir}' must exist");
    }

    private static void TestPillar5DeepGpuAndHardwareTelemetry()
    {
        // 1. Dual-Engine GPU Telemetry Provider (NVML + DXGI)
        using var gpuProvider = new GpuTelemetryProvider();
        var gpuSnap = gpuProvider.Sample();

        Assert(gpuSnap.IsAvailable, "GPU Telemetry provider must detect a valid GPU adapter");
        Assert(!string.IsNullOrWhiteSpace(gpuSnap.Name), "GPU model name must not be empty");
        Assert(gpuSnap.UtilizationPercent >= 0 && gpuSnap.UtilizationPercent <= 100, $"GPU utilization out of bounds: {gpuSnap.UtilizationPercent}%");
        Assert(gpuSnap.VramPercent >= 0 && gpuSnap.VramPercent <= 100, $"GPU VRAM percent out of bounds: {gpuSnap.VramPercent}%");
        Assert(gpuSnap.VramUsedGb >= 0.0 && gpuSnap.VramUsedGb <= gpuSnap.VramTotalGb + 0.1, "GPU VRAM used must not exceed total VRAM");
        Assert(gpuSnap.EngineType == "NVML" || gpuSnap.EngineType == "DXGI", $"Unexpected engine type: {gpuSnap.EngineType}");

        // 2. High-Performance NtQuerySystemInformation Per-Core CPU Sampler
        using var coreSampler = new PerCoreCpuSampler();
        var baseline = coreSampler.Sample();
        Thread.Sleep(30);
        var activeSample = coreSampler.Sample();

        Assert(activeSample.CoreLoads != null, "Core loads array must not be null");
        Assert(activeSample.CoreLoads!.Length == Environment.ProcessorCount, $"Expected {Environment.ProcessorCount} cores, got {activeSample.CoreLoads.Length}");
        for (int i = 0; i < activeSample.CoreLoads!.Length; i++)
        {
            int load = activeSample.CoreLoads[i];
            Assert(load >= 0 && load <= 100, $"Core #{i} load out of bounds: {load}%");
        }
        Assert(activeSample.AverageCorePercent >= 0 && activeSample.AverageCorePercent <= 100, $"Average core percent out of bounds: {activeSample.AverageCorePercent}%");
        Assert(activeSample.PeakCorePercent >= 0 && activeSample.PeakCorePercent <= 100, $"Peak core percent out of bounds: {activeSample.PeakCorePercent}%");

        // 3. Protected OS Process Invariants
        Assert(SystemPerformanceService.IsProtectedProcess("dwm.exe"), "dwm.exe must be protected from termination");
        Assert(SystemPerformanceService.IsProtectedProcess("explorer.exe"), "explorer.exe must be protected from termination");
        Assert(SystemPerformanceService.IsProtectedProcess("csrss.exe"), "csrss.exe must be protected from termination");
        Assert(SystemPerformanceService.IsProtectedProcess("svchost.exe"), "svchost.exe must be protected from termination");
        Assert(SystemPerformanceService.IsProtectedProcess("DynamicIsland.exe"), "DynamicIsland.exe must be protected from termination");
        Assert(!SystemPerformanceService.IsProtectedProcess("rogue_miner.exe"), "Non-system processes must not be marked protected");

        // 4. Runaway Process Model & Detection Verification
        var runawayDef = new RunawayProcessInfo(true, "rogue_task.exe", 12345, 48.6, 620.0);
        Assert(runawayDef.HasRunaway, "RunawayProcessInfo must reflect active runaway state");
        Assert(runawayDef.CpuPercent == 48.6, "Runaway CPU percentage must match");
        Assert(runawayDef.ExeName == "rogue_task.exe", "Runaway executable name must match");

        // 5. SystemPerformanceService Deep Telemetry Integration & Purge
        using var perfService = new SystemPerformanceService();
        var snap = perfService.SampleTelemetry(forceProcessScan: true, forceDispatch: true);

        Assert(snap.Gpu.IsAvailable, "SystemVitalsSnapshot must contain valid GPU vitals");
        Assert(snap.Cores.CoreLoads.Length == Environment.ProcessorCount, "SystemVitalsSnapshot must contain valid per-core heatmap");
        Assert(snap.Runaway.CpuPercent >= 0.0, "Runaway CPU percent must be non-negative");

        // Runaway purge execution test (high threshold so live host processes are never terminated)
        var purgeResult = perfService.PurgeRunawayProcesses(minCpuPercent: 100000.0);
        Assert(purgeResult.KilledProcesses >= 0, "PurgeRunawayProcesses must return non-negative killed count");
        Assert(purgeResult.FreedCpuPercent >= 0.0, "PurgeRunawayProcesses must return non-negative freed CPU percent");
    }

    private static void TestUpdateManagerV2Invariants()
    {
        // 1. AppSettings AutoDownloadUpdates default invariant
        var settings = new AppSettings();
        Assert(settings.AutoDownloadUpdates, "AutoDownloadUpdates must default to true");
        Assert(settings.AutoCheckUpdates, "AutoCheckUpdates must default to true");

        // 2. SkippedVersionDate 7-day expiration logic invariant
        settings.SkippedVersion = "0.7.0";
        settings.SkippedVersionDate = DateTime.UtcNow - TimeSpan.FromDays(8);
        bool isExpired = (DateTime.UtcNow - settings.SkippedVersionDate.Value) >= TimeSpan.FromDays(7);
        Assert(isExpired, "Skipped updates older than 7 days must be considered expired");

        settings.SkippedVersionDate = DateTime.UtcNow - TimeSpan.FromDays(2);
        bool isRecent = (DateTime.UtcNow - settings.SkippedVersionDate.Value) < TimeSpan.FromDays(7);
        Assert(isRecent, "Skipped updates younger than 7 days must remain suppressed");

        // 3. GitHubReleaseClient SetToken & Bearer Authorization Header
        using var client = new Services.GitHubReleaseClient(gitHubToken: "ghp_initialDummyToken");
        client.SetToken("ghp_updatedDummyToken");
        client.SetToken(null);

        // 3. UpdateManifest Critical Update & Changelog serialization
        string sampleCriticalJson = """
        {
          "version": "0.7.0",
          "releaseTag": "v0.7.0",
          "channel": "stable",
          "releaseDate": "2026-11-20T00:00:00Z",
          "changelogUrl": "https://github.com/jainis-Patel/DynamicIsland/releases/tag/v0.7.0",
          "summary": "Critical zero-day security fix for Win32 hook interop",
          "isCritical": true,
          "downloads": {
            "installer": {
              "url": "https://github.com/jainis-Patel/DynamicIsland/releases/download/v0.7.0/DynamicIsland-Setup.exe",
              "sha256": "abcdef11223344556677889900aabbccddeeff11223344556677889900aabbcc",
              "sizeBytes": 75000000
            }
          }
        }
        """;

        var manifest = System.Text.Json.JsonSerializer.Deserialize<UpdateManifest>(sampleCriticalJson, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert(manifest != null, "Critical update manifest must deserialize cleanly");
        Assert(manifest!.IsCritical, "Manifest isCritical must be true");
        Assert(manifest.ChangelogUrl == "https://github.com/jainis-Patel/DynamicIsland/releases/tag/v0.7.0", "ChangelogUrl must match");
        Assert(manifest.ParsedVersion > SemVersion.Parse("0.6.0"), "0.7.0 must be greater than 0.6.0");

        // 4. UpdateIntegrityVerifier TryCleanFile safety on non-existent path
        string nonExistentPath = Path.Combine(Path.GetTempPath(), "DynamicIsland", "TestNonExistentFile.tmp");
        bool cleaned = UpdateIntegrityVerifier.TryCleanFile(nonExistentPath);
        Assert(!cleaned, "TryCleanFile on non-existent file must safely return false without throwing");
    }

    private static void TestVirtualPetOverhaulAndPlaygroundInvariants()
    {
        // 1. ExpandedView.Pet strongly-typed enum & key aliases
        Assert(ExpandedView.Pet.ToKey() == "pet", "ExpandedView.Pet ToKey must be 'pet'");
        Assert(ExpandedViewExtensions.TryParseKey("pet", out var petView) && petView == ExpandedView.Pet,
            "Key 'pet' must parse to ExpandedView.Pet");
        Assert(ExpandedViewExtensions.TryParseKey("companion", out var companionView) && companionView == ExpandedView.Pet,
            "Key 'companion' must parse to ExpandedView.Pet");
        Assert(ExpandedViewExtensions.TryParseKey("sanctuary", out var sanctuaryView) && sanctuaryView == ExpandedView.Pet,
            "Key 'sanctuary' must parse to ExpandedView.Pet");

        // 2. DropletRegistry pet_sanctuary registration
        var registry = new DropletRegistry();
        var petDroplet = registry.Get("pet_sanctuary");
        Assert(petDroplet != null, "DropletRegistry must include 'pet_sanctuary'");
        Assert(petDroplet!.TargetView == ExpandedView.Pet, "pet_sanctuary TargetView must be ExpandedView.Pet");
        Assert(petDroplet.Category == DropletCategory.Productivity, "pet_sanctuary category must be Productivity");

        // 3. VirtualPetService species unique signature abilities & perks
        string tempSettings = Path.Combine(Path.GetTempPath(), $"di_pet_test_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(tempSettings);
            using var petService = new VirtualPetService(System.Windows.Threading.Dispatcher.CurrentDispatcher, settings);

            // Test Cat
            petService.SetSpecies("Cat");
            Assert(petService.GetSpeciesSpecialActionKind() == PetSpecialActionKind.LaserHunt, "Cat action must be LaserHunt");
            Assert(petService.GetSpecialActionName().Contains("Laser"), "Cat action name must contain Laser");
            Assert(!string.IsNullOrWhiteSpace(petService.GetSpeciesPerkDescription()), "Cat must have non-empty perk");

            // Test Dog
            petService.SetSpecies("Dog");
            Assert(petService.GetSpeciesSpecialActionKind() == PetSpecialActionKind.BallFetch, "Dog action must be BallFetch");
            Assert(petService.GetSpecialActionName().Contains("Ball"), "Dog action name must contain Ball");

            // Test Bunny
            petService.SetSpecies("Bunny");
            Assert(petService.GetSpeciesSpecialActionKind() == PetSpecialActionKind.BinkyFlip, "Bunny action must be BinkyFlip");
            Assert(petService.GetSpecialActionName().Contains("Binky"), "Bunny action name must contain Binky");

            // Test Fox
            petService.SetSpecies("Fox");
            Assert(petService.GetSpeciesSpecialActionKind() == PetSpecialActionKind.FoxfireDance, "Fox action must be FoxfireDance");
            Assert(petService.GetSpecialActionName().Contains("Foxfire"), "Fox action name must contain Foxfire");

            // Test Panda
            petService.SetSpecies("Panda");
            Assert(petService.GetSpeciesSpecialActionKind() == PetSpecialActionKind.KungFuRoll, "Panda action must be KungFuRoll");
            Assert(petService.GetSpecialActionName().Contains("Kung-Fu"), "Panda action name must contain Kung-Fu");

            // Test Dragon
            petService.SetSpecies("Dragon");
            Assert(petService.GetSpeciesSpecialActionKind() == PetSpecialActionKind.DragonBreath, "Dragon action must be DragonBreath");
            Assert(petService.GetSpecialActionName().Contains("Breath"), "Dragon action name must contain Breath");

            // 4. PlaySpecialAction execution, affection increment & event dispatching
            int scoreBefore = petService.AffectionScore;
            PetSpecialActionKind triggeredKind = PetSpecialActionKind.None;
            petService.SpecialActionTriggered += k => triggeredKind = k;

            string actionMsg = petService.PlaySpecialAction();
            Assert(!string.IsNullOrWhiteSpace(actionMsg), "PlaySpecialAction must return celebratory message");
            Assert(triggeredKind == PetSpecialActionKind.DragonBreath, "SpecialActionTriggered must fire with DragonBreath");
            Assert(petService.AffectionScore == scoreBefore + 6, "PlaySpecialAction must award +6 affection score");
            Assert(petService.CurrentMood == PetMood.Playing, "CurrentMood must transition to Playing");
        }
        finally
        {
            if (File.Exists(tempSettings))
            {
                try { File.Delete(tempSettings); } catch { }
            }
        }
    }

    private static void TestPhase5ArchitectureAndHygiene()
    {
        // 1. DebouncedJsonStore coalescing & deterministic Dispose flush
        string tempJsonPath = Path.Combine(Path.GetTempPath(), $"di_debounced_{Guid.NewGuid():N}.json");
        try
        {
            int snapshotReads = 0;
            int latestCount = 0;
            var store = new DebouncedJsonStore<Dictionary<string, int>>(
                tempJsonPath,
                () =>
                {
                    snapshotReads++;
                    return new Dictionary<string, int> { ["count"] = latestCount };
                },
                delayMs: 250);

            latestCount = 1;
            store.ScheduleSave();
            latestCount = 2;
            store.ScheduleSave();
            latestCount = 3;
            store.ScheduleSave();

            Assert(snapshotReads == 0, $"Rapid ScheduleSave calls must coalesce before timer fires (got {snapshotReads} reads)");

            store.Dispose();
            Assert(snapshotReads == 1, $"Dispose must flush pending dirty state exactly once (got {snapshotReads} reads)");
            Assert(File.Exists(tempJsonPath), "DebouncedJsonStore must write file cleanly on Dispose");

            var loaded = AtomicJsonStore.Load(tempJsonPath, () => new Dictionary<string, int>());
            Assert(loaded.TryGetValue("count", out int savedVal) && savedVal == 3,
                $"Expected coalesced final value 3, got {savedVal}");

            // Post-dispose saves must be a safe no-op
            latestCount = 99;
            store.ScheduleSave();
            store.Flush();
            Assert(snapshotReads == 1, "Disposed DebouncedJsonStore must ignore subsequent saves");
        }
        finally
        {
            if (File.Exists(tempJsonPath))
            {
                try { File.Delete(tempJsonPath); } catch { }
            }
        }

        // 2. SafeProcessLauncher URI scheme & CRLF injection protection
        Assert(!SafeProcessLauncher.TryOpenUrl(null), "Null URL must be rejected");
        Assert(!SafeProcessLauncher.TryOpenUrl(""), "Empty URL must be rejected");
        Assert(!SafeProcessLauncher.TryOpenUrl("   "), "Whitespace URL must be rejected");
        Assert(!SafeProcessLauncher.TryOpenUrl("file:///C:/Windows/System32/calc.exe"), "file:// URL scheme must be rejected");
        Assert(!SafeProcessLauncher.TryOpenUrl("javascript:alert(1)"), "javascript: URL scheme must be rejected");
        Assert(!SafeProcessLauncher.TryOpenUrl("https://example.com/q?a=1\r\nb"), "CRLF-injected URL must be rejected");
        Assert(!SafeProcessLauncher.TryOpenShellUri("ms-settings:bluetooth\ncmd"), "Newline-injected shell URI must be rejected");
        Assert(!SafeProcessLauncher.TryOpenShellUri("ftp://example.com/file.zip"), "Unlisted ftp:// shell scheme must be rejected");
        Assert(!SafeProcessLauncher.TryOpenFolder(Path.Combine(Path.GetTempPath(), $"missing_folder_{Guid.NewGuid():N}")),
            "Non-existent directory must be rejected");

        // 3. Shared DeviceClassifier & Core.AppIconLoader invariants
        Assert(DeviceClassifier.ClassifyPeripheral("Logitech MX Master 3S").Category == "Mouse", "Mouse classification mismatch");
        Assert(DeviceClassifier.ClassifyPeripheral("Keychron K2 Keyboard").Category == "Keyboard", "Keyboard classification mismatch");
        Assert(DeviceClassifier.ClassifyPeripheral("Xbox Wireless Controller").Category == "Controller", "Controller classification mismatch");
        Assert(DeviceClassifier.ClassifyPeripheral("Sony WH-1000XM5").Category == "Headset", "Headset classification mismatch");
        Assert(DeviceClassifier.ClassifyPeripheral("AirPods Pro").IconGlyph == "\uE7F6", "Headset glyph mismatch");
        Assert(DeviceClassifier.ClassifyPeripheral("").Category == "Peripheral", "Fallback category mismatch");
        Assert(DeviceClassifier.IsAudioDeviceName("AirPods Pro"), "Audio device hint must match AirPods Pro");
        Assert(!DeviceClassifier.IsAudioDeviceName("Logitech MX Master 3S"), "Mouse must not classify as audio device");
        Assert(DeviceClassifier.ClassifyAudioEndpoint("NVIDIA High Definition Audio").Type == "Display Audio", "HDMI/Display audio classification mismatch");
        Assert(AppIconLoader.GetAppIcon(null) == null, "Null executable path must return null icon");
        Assert(AppIconLoader.GetAppIcon(@"C:\__nonexistent__\missing_binary_999.exe") == null, "Missing executable path must return null icon");
        Assert(!string.IsNullOrEmpty(AppIconLoader.ResolveExePath("cmd.exe")), "System32 binary cmd.exe must resolve");

        // 4. LyricsService multi-timestamp LRC expansion & binary search boundaries
        string multiLrc = """
            [ti:Test Track]
            [00:05.00]Verse One
            [00:10.00][00:25.50]Shared Chorus
            [00:18.25]Verse Two
            """;
        var lrcLines = LyricsService.ParseLrc(multiLrc);
        Assert(lrcLines.Count == 4, $"Expected 4 expanded LRC lines (excluding metadata), got {lrcLines.Count}");
        Assert(lrcLines[0].Text == "Verse One" && lrcLines[0].Timestamp == TimeSpan.FromSeconds(5), "First LRC line mismatch");
        Assert(lrcLines[1].Text == "Shared Chorus" && lrcLines[1].Timestamp == TimeSpan.FromSeconds(10), "Second LRC line mismatch");
        Assert(lrcLines[2].Text == "Verse Two" && lrcLines[2].Timestamp == TimeSpan.FromSeconds(18.25), "Third LRC line mismatch");
        Assert(lrcLines[3].Text == "Shared Chorus" && lrcLines[3].Timestamp == TimeSpan.FromSeconds(25.5), "Fourth LRC line mismatch");
        Assert(LyricsService.FindCurrentIndex(lrcLines, TimeSpan.FromSeconds(4.5)) == -1, "Position well before first line must return -1");
        Assert(LyricsService.FindCurrentIndex(lrcLines, TimeSpan.FromSeconds(4.9)) == 0, "Position within 120ms lead-in of first line must return index 0");
        Assert(LyricsService.FindCurrentIndex(lrcLines, TimeSpan.FromSeconds(10.0)) == 1, "Exact match at 10s must return index 1");
        Assert(LyricsService.FindCurrentIndex(lrcLines, TimeSpan.FromSeconds(25.5)) == 3, "Repeated chorus timestamp at 25.5s must return index 3");

        // 5. Light Mode hover veil opacity fix & ThemeManager conditional event suppression
        Assert(ThemeBrushes.ButtonHoverVeilLight.Color.A == 0xFF,
            $"ButtonHoverVeilLight must have opaque black base for opacity animations, got alpha 0x{ThemeBrushes.ButtonHoverVeilLight.Color.A:X2}");
        Assert(ThemeBrushes.ButtonHoverVeilLight.IsFrozen, "ButtonHoverVeilLight must be frozen");

        if (!ThemeManager.Initialized)
        {
            ThemeManager.Initialize("Dark", "Default");
        }

        ThemeMode savedMode = ThemeManager.CurrentMode;
        ThemeStyle savedStyle = ThemeManager.CurrentStyle;
        int themeEvents = 0;
        int styleEvents = 0;
        void OnThemeChanged(AppTheme _) => themeEvents++;
        void OnStyleChanged(ThemeStyle _) => styleEvents++;

        try
        {
            ThemeManager.SetThemeMode(ThemeMode.Dark);
            ThemeManager.SetThemeStyle(ThemeStyle.Default);

            ThemeManager.ThemeChanged += OnThemeChanged;
            ThemeManager.ThemeStyleChanged += OnStyleChanged;

            // No-op re-apply must suppress redundant events
            ThemeManager.SetThemeMode(ThemeMode.Dark);
            ThemeManager.SetThemeStyle(ThemeStyle.Default);
            Assert(themeEvents == 0 && styleEvents == 0,
                $"No-op theme/style calls must fire 0 events (got theme={themeEvents}, style={styleEvents})");

            // Switching only style fires only ThemeStyleChanged
            ThemeManager.SetThemeStyle(ThemeStyle.LiquidGlass);
            Assert(styleEvents == 1 && themeEvents == 0,
                $"Style change must fire only ThemeStyleChanged (got theme={themeEvents}, style={styleEvents})");

            // Switching only mode fires only ThemeChanged
            ThemeManager.SetThemeMode(ThemeMode.Light);
            Assert(themeEvents == 1 && styleEvents == 1,
                $"Theme change must fire only ThemeChanged (got theme={themeEvents}, style={styleEvents})");
        }
        finally
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
            ThemeManager.ThemeStyleChanged -= OnStyleChanged;
            ThemeManager.SetThemeMode(savedMode);
            ThemeManager.SetThemeStyle(savedStyle);
        }

        // 6. InstallerTokens.xaml static brushes must all be frozen at parse time
        var installerTokens = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/DynamicIsland;component/Resources/InstallerTokens.xaml", UriKind.Absolute)
        };
        int tokenBrushCount = 0;
        foreach (object? val in installerTokens.Values)
        {
            if (val is Brush brush)
            {
                tokenBrushCount++;
                Assert(brush.IsFrozen, "Every brush in InstallerTokens.xaml must be frozen via po:Freeze=\"True\"");
            }
        }
        Assert(tokenBrushCount >= 14, $"Expected at least 14 frozen brushes in InstallerTokens.xaml, found {tokenBrushCount}");

        // 7. Expanded Drop Shelf & Clipboard Bento Card ViewModel and Control Integrity
        var shelfSample = new ShelfItem(
            @"C:\Users\Demo\Pictures\shot.png",
            "shot.png",
            "1.4 MB",
            "\uE8B9",
            true,
            "PNG",
            ThemeBrushes.Blue);
        Assert(shelfSample.DirectoryName == "Pictures", $"Expected parent folder 'Pictures', got '{shelfSample.DirectoryName}'");

        var codeClip = new ClipboardItem
        {
            Type = ClipboardItemType.Code,
            Title = "public void Run()",
            Subtitle = "3 lines · C#",
            RawText = "\tpublic void Run()\n\t{\n\t\tConsole.WriteLine(42);\n\t}"
        };
        Assert(codeClip.PreviewSnippet.Contains('\n') && codeClip.PreviewSnippet.Contains("Console.WriteLine"),
            $"ClipboardItem.PreviewSnippet must preserve multi-line code structure, got '{codeClip.PreviewSnippet}'");
        Assert(codeClip.CardFooterText == "3 lines · C#", $"Expected code CardFooterText, got '{codeClip.CardFooterText}'");
        Assert(codeClip.CanCopyPlain, "Code clip must allow plain-text copy");

        var urlClip = new ClipboardItem
        {
            Type = ClipboardItemType.Url,
            Title = "github.com",
            Subtitle = "https://github.com/jainis-Patel/DynamicIsland",
            RawText = "https://github.com/jainis-Patel/DynamicIsland"
        };
        Assert(urlClip.CardFooterText == "github.com", $"URL CardFooterText must be host 'github.com', got '{urlClip.CardFooterText}'");
        Assert(urlClip.PreviewSnippet == "https://github.com/jainis-Patel/DynamicIsland", "URL PreviewSnippet must show full URL");

        var shelfView = new ShelfView();
        Assert(shelfView.ShelfItemsControl != null && shelfView.DragAllShelfBtn != null && shelfView.ShelfScrollViewer != null,
            "ShelfView named forwarder controls must be present");

        var clipboardView = new ClipboardView();
        Assert(clipboardView.ClipboardItemsControl != null && clipboardView.ClipboardSearchBox != null && clipboardView.ClipFilterAllBtn != null,
            "ClipboardView named forwarder controls must be present");

        // 8. Installer CLI argument parsing & native GitHub asset digest extraction
        string[] silentArgs = { "--install", "--silent", "--relaunch", "--wait-pid", "4242", "--target-dir", @"C:\Apps\DynamicIsland" };
        Assert(InstallerService.IsInstallerMode(silentArgs, @"C:\Temp\DynamicIsland-0.8.0.exe"), "--install must trigger installer mode regardless of filename");
        Assert(InstallerService.IsSilent(silentArgs), "--silent must be recognized");
        Assert(InstallerService.ShouldRelaunchAfterSilentInstall(silentArgs), "--relaunch must trigger post-install restart");
        Assert(InstallerService.TryParseWaitPid(silentArgs, out int waitPid) && waitPid == 4242, "TryParseWaitPid must parse 4242");
        Assert(InstallerService.TryParseTargetDir(silentArgs, out string? parsedDir) && parsedDir == @"C:\Apps\DynamicIsland",
            $"TryParseTargetDir mismatch: '{parsedDir}'");

        using var digestDoc = System.Text.Json.JsonDocument.Parse("""
            {"name":"DynamicIsland-Setup.exe","digest":"sha256:ABCDEF11223344556677889900aabbccddeeff11223344556677889900aabbcc"}
            """);
        string? extractedDigest = GitHubReleaseClient.TryExtractAssetDigestSha256(digestDoc.RootElement);
        Assert(extractedDigest == "abcdef11223344556677889900aabbccddeeff11223344556677889900aabbcc",
            $"Expected lowercase 64-hex digest, got '{extractedDigest}'");
    }

    private static void TestInlineConfirmGuardAndBentoWidgets()
    {
        var guard = new InlineConfirmGuard(TimeSpan.FromSeconds(3.2));
        var t0 = new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);

        // 1. First click arms; second click within window confirms
        Assert(!guard.TryConfirmState("clear_shelf", t0), "First click must arm confirmation rather than executing");
        Assert(guard.TryConfirmState("clear_shelf", t0.AddSeconds(1.5)), "Second click within 3.2s window must confirm");
        Assert(!guard.TryConfirmState("clear_shelf", t0.AddSeconds(1.6)), "Confirmation must disarm immediately after execution");

        // 2. Expiry past ConfirmWindow requires re-arming
        Assert(!guard.TryConfirmState("reset_settings", t0), "Reset action must arm on first click");
        Assert(!guard.TryConfirmState("reset_settings", t0.AddSeconds(4.0)), "Expired confirmation window must re-arm rather than execute");

        // 3. Shift+Click bypass confirms immediately and DisarmAll clears armed state
        Assert(!guard.TryConfirmState("purge_runaways", t0), "Purge action must arm without bypass");
        Assert(guard.TryConfirmState("purge_runaways", t0.AddSeconds(0.1), bypass: true), "Shift+Click bypass must confirm immediately");

        Assert(!guard.TryConfirm("clear_clipboard"), "UI TryConfirm without sender must arm on first call");
        Assert(guard.IsArmed("clear_clipboard"), "IsArmed must report true while armed");
        guard.DisarmAll();
        Assert(!guard.IsArmed("clear_clipboard"), "DisarmAll must clear armed confirmation state");

        // 4. Verify Bento widgets & independent per-card ScaleTransforms instantiate cleanly
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var stylesUri = new Uri("pack://application:,,,/DynamicIsland;component/Resources/IslandStyles.xaml", UriKind.Absolute);
        if (!app.Resources.MergedDictionaries.Any(d => d.Source == stylesUri))
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = stylesUri });
        }

        var batteryView = new BatteryView();
        Assert(batteryView.PlanSaverCard != null && batteryView.PlanBalancedCard != null && batteryView.PlanPerfCard != null,
            "BatteryView plan cards must initialize cleanly");
        Assert(batteryView.PlanSaverCard!.RenderTransform is ScaleTransform && batteryView.PlanBalancedCard!.RenderTransform is ScaleTransform,
            "BentoSpringMotionBorder must attach a ScaleTransform to each card");
        Assert(!ReferenceEquals(batteryView.PlanSaverCard!.RenderTransform, batteryView.PlanBalancedCard!.RenderTransform),
            "Each Bento card must receive an independent ScaleTransform instance");

        var networkView = new NetworkView();
        Assert(networkView.WifiToggleCard != null && networkView.BtToggleCard != null &&
               networkView.WifiNetworksCard != null && networkView.HotspotCard != null,
            "NetworkView cards must initialize cleanly");
        Assert(!ReferenceEquals(networkView.WifiToggleCard!.RenderTransform, networkView.WifiNetworksCard!.RenderTransform),
            "NetworkView Bento cards must receive distinct ScaleTransforms");

        var peripheralsView = new PeripheralsView();
        Assert(peripheralsView.PeripheralsItemsControl != null && peripheralsView.PeripheralsEmptyPlaceholder != null,
            "PeripheralsView controls and Bento empty placeholder must initialize cleanly");
    }

    private static void TestWidgetsGalleryAndDesktopWidgets()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var stylesUri = new Uri("pack://application:,,,/DynamicIsland;component/Resources/IslandStyles.xaml", UriKind.Absolute);
        if (!app.Resources.MergedDictionaries.Any(d => d.Source == stylesUri))
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = stylesUri });
        }

        var gallery = new WidgetsGalleryView();
        gallery.RefreshAllWidgets();
        Assert(gallery.WeatherTempBigText != null && gallery.TimerBigReadoutText != null &&
               gallery.StorageUsedDetailText != null && gallery.AudioDeviceNameText != null &&
               gallery.PetNameAndSpeciesText != null,
            "All 5 new live Bento gallery card elements must initialize cleanly");
        Assert(!string.IsNullOrWhiteSpace(gallery.StorageUsedDetailText!.Text),
            "Storage gallery card must populate disk usage details");

        gallery.ApplyFilter("Live");
        Assert(gallery.LiveSectionHeader.Visibility == Visibility.Visible &&
               gallery.ToolsSectionHeader.Visibility == Visibility.Collapsed &&
               gallery.ToolCardsWrapPanel.Visibility == Visibility.Collapsed,
            "Live filter must show Live section and hide Tools section");

        gallery.ApplyFilter("Tools");
        Assert(gallery.LiveSectionHeader.Visibility == Visibility.Collapsed &&
               gallery.MediaHeroCard.Visibility == Visibility.Collapsed &&
               gallery.ToolsSectionHeader.Visibility == Visibility.Visible &&
               gallery.ToolCardsWrapPanel.Visibility == Visibility.Visible,
            "Tools filter must show Tools section and hide Live section");

        gallery.ApplyFilter("Pinned");
        Assert(gallery.PinnedEmptyStateCard.Visibility == Visibility.Visible &&
               gallery.LiveSectionHeader.Visibility == Visibility.Collapsed &&
               gallery.ToolsSectionHeader.Visibility == Visibility.Collapsed,
            "Pinned filter with 0 active widgets must display PinnedEmptyStateCard");

        gallery.ApplyFilter("All");
        Assert(gallery.LiveSectionHeader.Visibility == Visibility.Visible &&
               gallery.ToolsSectionHeader.Visibility == Visibility.Visible &&
               gallery.PinnedEmptyStateCard.Visibility == Visibility.Collapsed,
            "All filter must restore both Live and Tools sections");

        var widget = new DynamicIsland.Views.DesktopWidgetWindow(DynamicIsland.Views.DesktopWidgetKind.Storage, null);
        try
        {
            widget.RefreshData();
            Assert(widget.HeroValueText != null && widget.HeroValueText.Text.Contains("GB Free", StringComparison.OrdinalIgnoreCase),
                $"Desktop Storage widget must populate free space hero value, got '{widget.HeroValueText?.Text}'");
            Assert(widget.FooterUtilityBtn != null && widget.FooterUtilityBtn.Visibility == Visibility.Visible,
                "Desktop Storage widget must expose Explorer quick utility pill button");

            foreach (var kind in Enum.GetValues<DynamicIsland.Views.DesktopWidgetKind>())
            {
                var w = new DynamicIsland.Views.DesktopWidgetWindow(kind, null);
                try
                {
                    w.RefreshData();
                    Assert(!string.IsNullOrWhiteSpace(w.HeaderTitleText?.Text),
                        $"Desktop widget {kind} must populate header title");
                    Assert(!string.IsNullOrWhiteSpace(w.HeroValueText?.Text),
                        $"Desktop widget {kind} must populate hero value");

                    if (kind == DynamicIsland.Views.DesktopWidgetKind.Battery)
                        Assert(w.BatteryVisualCellHost.Visibility == Visibility.Visible &&
                               w.BatteryCellFillBar.Width > 0 &&
                               w.BatteryPlanStrip.Visibility == Visibility.Visible &&
                               w.FooterUtilityBtn.Visibility == Visibility.Visible,
                            "Battery widget must display sculpted visual battery cell, segmented power plan strip, and Keep Awake utility button");
                    else if (kind == DynamicIsland.Views.DesktopWidgetKind.Network)
                        Assert(w.NetworkDualCardsPanel.Visibility == Visibility.Visible, "Network widget must display dual download/upload throughput cards");
                    else if (kind == DynamicIsland.Views.DesktopWidgetKind.Timer)
                        Assert(w.TimerPresetStrip.Visibility == Visibility.Visible, "Timer widget must display 15m/25m/45m/Reset preset strip");
                    else if (kind == DynamicIsland.Views.DesktopWidgetKind.Media)
                        Assert(w.MediaArtworkHost.Visibility == Visibility.Visible && w.MediaPrevBtn.Visibility == Visibility.Visible && w.MediaNextBtn.Visibility == Visibility.Visible,
                            "Media widget must display album artwork squircle and Prev/Play/Next transport controls");
                    else if (kind == DynamicIsland.Views.DesktopWidgetKind.Calendar)
                        Assert(w.CalendarDateLeafHost.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(w.CalendarLeafDayNumText.Text),
                            "Calendar widget must display Apple-style two-tone date leaf");
                    else if (kind == DynamicIsland.Views.DesktopWidgetKind.Audio)
                        Assert(w.AudioControlStrip.Visibility == Visibility.Visible, "Audio widget must display Mute/-10%/+10%/Output control strip");
                }
                finally
                {
                    w.Close();
                }
            }
        }
        finally
        {
            widget.Close();
            DynamicIsland.Views.DesktopWidgetWindow.CloseAll();
        }
        Assert(DynamicIsland.Views.DesktopWidgetWindow.ActiveCount == 0,
            "DesktopWidgetWindow.CloseAll must leave zero active widgets");

        var homeMediaView = new MediaView();
        Assert(homeMediaView.MediaArtworkSquircle != null &&
               homeMediaView.MediaMiniClockCard != null &&
               homeMediaView.FocusStopwatchCard != null &&
               homeMediaView.AudioOutputQuickBtn != null &&
               homeMediaView.PrevButton != null &&
               homeMediaView.PlayPauseButton != null &&
               homeMediaView.NextButton != null,
            "Home Screen MediaView must initialize Apple Dynamic Island artwork squircle, transport triad, and StandBy clock pod cleanly");
        Assert(homeMediaView.MediaArtworkSquircle!.RenderTransform is ScaleTransform &&
               homeMediaView.PlayPauseButton!.RenderTransform is ScaleTransform &&
               !ReferenceEquals(homeMediaView.MediaArtworkSquircle.RenderTransform, homeMediaView.PlayPauseButton.RenderTransform),
            "Home Screen interactive controls must receive independent ScaleTransforms");
    }

    private static void TestSettingsOverhaulInvariants()
    {
        // 1. Verify SettingsSearchIndex matching across all key terms & category routes
        var waveMatches = SettingsSearchIndex.Search("waveform");
        Assert(waveMatches.Any(e => e.CategoryKey == "Clock"), "Search 'waveform' must route to Clock category");

        var notchMatches = SettingsSearchIndex.Search("notch");
        Assert(notchMatches.Any(e => e.CategoryKey == "Screen"), "Search 'notch' must route to Screen category");

        var updateMatches = SettingsSearchIndex.Search("update");
        Assert(updateMatches.Any(e => e.CategoryKey == "Updates"), "Search 'update' must route to Updates category");

        var mutedMatches = SettingsSearchIndex.Search("muted");
        Assert(mutedMatches.Any(e => e.CategoryKey == "Notifications"), "Search 'muted' must route to Notifications category");

        var weatherMatches = SettingsSearchIndex.Search("weather");
        Assert(weatherMatches.Any(e => e.CategoryKey == "Updates"), "Search 'weather' must route to Updates category");

        var glassMatches = SettingsSearchIndex.Search("glass");
        Assert(glassMatches.Any(e => e.CategoryKey == "Appearance"), "Search 'glass' must route to Appearance category");

        var startupMatches = SettingsSearchIndex.Search("startup");
        Assert(startupMatches.Any(e => e.CategoryKey == "Updates"), "Search 'startup' must route to Updates category");

        Assert(SettingsSearchIndex.Search("zzzzqwerty12345").Count == 0,
            "Nonsense search query must return zero matches");
        Assert(SettingsSearchIndex.Search("").Count == 0,
            "Empty search query must return zero matches");

        // 2. Verify SettingsService round-trip and complete ResetToDefaults()
        string tempFile = Path.Combine(Path.GetTempPath(), $"di_settings_overhaul_{Guid.NewGuid():N}.json");
        try
        {
            using (var svc = new SettingsService(tempFile))
            {
                svc.Update(s =>
                {
                    s.NatureDensity = 1.35;
                    s.NatureScale = 1.25;
                    s.NatureDropLength = 110.0;
                    s.NatureButterflyCount = 4;
                    s.NatureBloomDensity = 1.40;
                    s.NaturePollenEnabled = false;
                    s.NaturePollenCount = 3;
                    s.GlassBlurIntensity = 2.1;
                    s.GlassRefractionIntensity = 1.8;
                    s.ExpandTrigger = "HoverDwell";
                    s.GhostModifierKey = "Alt";
                    s.EnableFastSwipeGuard = false;
                    s.EnableQuickTuck = false;
                    s.EnableLockKeyHud = false;
                    s.EnableKeyboardLayoutHud = false;
                    s.AutoCheckUpdates = false;
                    s.AutoDownloadUpdates = false;
                    s.UpdateChannel = "Beta";
                    s.HardwareNotchPreset = "DellXps";
                    s.GroupNotificationsByApp = false;
                    s.WeatherCityOverride = "Tokyo";
                    s.LowMemoryMode = true;
                    s.ShowLottieAnimation = false;
                });
                svc.SaveNow();
            }

            // Reload from disk and verify persistence of newly exposed settings
            using (var reloaded = new SettingsService(tempFile))
            {
                var c = reloaded.Current;
                Assert(c.HardwareNotchPreset == "DellXps", $"Expected HardwareNotchPreset='DellXps', got '{c.HardwareNotchPreset}'");
                Assert(!c.GroupNotificationsByApp, "GroupNotificationsByApp must persist false");
                Assert(!c.EnableFastSwipeGuard, "EnableFastSwipeGuard must persist false");
                Assert(!c.EnableLockKeyHud, "EnableLockKeyHud must persist false");
                Assert(!c.EnableKeyboardLayoutHud, "EnableKeyboardLayoutHud must persist false");
                Assert(c.WeatherCityOverride == "Tokyo", $"Expected WeatherCityOverride='Tokyo', got '{c.WeatherCityOverride}'");
                Assert(c.LowMemoryMode, "LowMemoryMode must persist true");
                Assert(!c.ShowLottieAnimation, "ShowLottieAnimation must persist false");
                Assert(c.ExpandTrigger == "HoverDwell", $"Expected ExpandTrigger='HoverDwell', got '{c.ExpandTrigger}'");

                // Now invoke ResetToDefaults and assert all fields return to factory values
                reloaded.ResetToDefaults();
                Assert(Math.Abs(c.NatureDensity - 0.70) < 0.001, "ResetToDefaults must reset NatureDensity");
                Assert(Math.Abs(c.NatureScale - 0.85) < 0.001, "ResetToDefaults must reset NatureScale");
                Assert(Math.Abs(c.NatureDropLength - 60.0) < 0.01, "ResetToDefaults must reset NatureDropLength");
                Assert(c.NatureButterflyCount == 2, "ResetToDefaults must reset NatureButterflyCount");
                Assert(Math.Abs(c.NatureBloomDensity - 0.75) < 0.001, "ResetToDefaults must reset NatureBloomDensity");
                Assert(c.NaturePollenEnabled, "ResetToDefaults must reset NaturePollenEnabled");
                Assert(c.NaturePollenCount == 14, "ResetToDefaults must reset NaturePollenCount");
                Assert(Math.Abs(c.GlassBlurIntensity - 1.0) < 0.001, "ResetToDefaults must reset GlassBlurIntensity");
                Assert(Math.Abs(c.GlassRefractionIntensity - 1.0) < 0.001, "ResetToDefaults must reset GlassRefractionIntensity");
                Assert(c.ExpandTrigger == "HoverSnappy", "ResetToDefaults must reset ExpandTrigger");
                Assert(c.GhostModifierKey == "Ctrl", "ResetToDefaults must reset GhostModifierKey");
                Assert(c.EnableFastSwipeGuard, "ResetToDefaults must reset EnableFastSwipeGuard");
                Assert(c.EnableQuickTuck, "ResetToDefaults must reset EnableQuickTuck");
                Assert(c.EnableLockKeyHud, "ResetToDefaults must reset EnableLockKeyHud");
                Assert(c.EnableKeyboardLayoutHud, "ResetToDefaults must reset EnableKeyboardLayoutHud");
                Assert(c.AutoCheckUpdates && c.AutoDownloadUpdates && c.UpdateChannel == "Stable",
                    "ResetToDefaults must reset update automation and channel");
            }
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }

        // 3. Verify all 10 modular Settings panels instantiate cleanly inside SettingsView
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var stylesUri = new Uri("pack://application:,,,/DynamicIsland;component/Resources/IslandStyles.xaml", UriKind.Absolute);
        if (!app.Resources.MergedDictionaries.Any(d => d.Source == stylesUri))
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = stylesUri });
        }

        var sv = new SettingsView();
        Assert(sv.AppearancePanel != null &&
               sv.FloraPanel != null &&
               sv.ClockAudioPanel != null &&
               sv.DropletsPanel != null &&
               sv.DockPanel != null &&
               sv.DisplayPowerPanel != null &&
               sv.NotificationsPanel != null &&
               sv.PetPanel != null &&
               sv.ShortcutsPanel != null &&
               sv.SystemUpdatesPanel != null,
            "All 10 modular Settings panels must initialize cleanly");
        Assert(sv.DisplayPowerPanel!.ExpandTriggerDwellBtn != null &&
               sv.ClockAudioPanel!.VisualizerStyleWaveBtn != null &&
               sv.DockPanel!.DockPinWidgetsBtn != null &&
               sv.NotificationsPanel!.GroupNotifsToggleSwitch != null &&
               sv.ShortcutsPanel!.LockKeyHudToggleSwitch != null &&
               sv.SystemUpdatesPanel!.WeatherCityBox != null,
            "All newly exposed controls must exist in the modular Settings panels");
    }
}
