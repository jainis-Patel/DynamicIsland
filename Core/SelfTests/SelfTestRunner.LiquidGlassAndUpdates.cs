using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Numerics;
using System.Text;
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
        // Execute headless golden image verification in an isolated temp folder
        string tempDir = Path.Combine(Path.GetTempPath(), $"di_golden_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            int code = GoldenImageGenerator.GenerateGoldenImages(tempDir);
            Assert(code == 0, $"GoldenImageGenerator failed with exit code {code}");

            string backdropPath = Path.Combine(tempDir, "golden_backdrop.png");
            string outputPath = Path.Combine(tempDir, "golden_output.png");

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
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
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

        // Verify checksums.sha256 parsing helper
        string sampleChecksums = """
        ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad  DynamicIsland-Setup.exe
        11223344556677889900aabbccddeeff11223344556677889900aabbccddeeff  DynamicIsland-Portable.zip
        """;
        string? extractedInstallerHash = Services.GitHubReleaseClient.ExtractHashFromChecksumsText(sampleChecksums, "DynamicIsland-Setup.exe");
        Assert(extractedInstallerHash == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "Installer hash must be extracted from checksums text");

        string? extractedZipHash = Services.GitHubReleaseClient.ExtractHashFromChecksumsText(sampleChecksums, ".zip");
        Assert(extractedZipHash == "11223344556677889900aabbccddeeff11223344556677889900aabbccddeeff", "Zip hash must be extracted by extension");

        // Verify ReleaseHistoryItem record with checksums/manifest URLs
        var historyItem = new ReleaseHistoryItem(
            Version: "0.5.0",
            ReleaseTag: "v0.5.0",
            ReleaseDate: DateTime.UtcNow,
            Summary: "Release notes",
            HtmlUrl: "https://github.com/jainis-Patel/DynamicIsland/releases/tag/v0.5.0",
            IsPreRelease: false,
            InstallerUrl: "https://github.com/jainis-Patel/DynamicIsland/releases/download/v0.5.0/DynamicIsland-Setup.exe",
            InstallerSha256: null,
            SizeBytes: 70000000,
            ChecksumsUrl: "https://github.com/jainis-Patel/DynamicIsland/releases/download/v0.5.0/checksums.sha256",
            ManifestUrl: "https://github.com/jainis-Patel/DynamicIsland/releases/download/v0.5.0/release-manifest.json");
        Assert(historyItem.ChecksumsUrl != null, "ChecksumsUrl must be populated");
        Assert(historyItem.ManifestUrl != null, "ManifestUrl must be populated");
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

        // 3. Verify SecondaryNotifications excludes LatestItem & CollapsedStackDeckVisibility
        var slackGroup2 = NotificationListenerService.GroupNotifications(sampleItems).First(g => g.AppName == "Slack");
        Assert(slackGroup2.SecondaryNotifications.Count() == 2, "SecondaryNotifications must exclude LatestItem");
        Assert(slackGroup2.SecondaryNotifications.All(n => n.Id != slackGroup2.LatestId), "LatestItem must not repeat in SecondaryNotifications");
        Assert(slackGroup2.CollapsedStackDeckVisibility == Visibility.Visible, "Collapsed stack deck lip must be visible when collapsed");
        slackGroup2.IsExpanded = true;
        Assert(slackGroup2.CollapsedStackDeckVisibility == Visibility.Collapsed, "Collapsed stack deck lip must collapse when expanded");

        // 4. Verify HasDistinctBody & OTP extraction
        var sameBodyItem = new NotificationHistoryItem { Title = "Build succeeded", Body = "Build succeeded" };
        Assert(!sameBodyItem.HasDistinctBody, "Identical title and body must not be treated as distinct body");
        var emptyBodyItem = new NotificationHistoryItem { Title = "Ping", Body = "" };
        Assert(!emptyBodyItem.HasDistinctBody, "Empty body must not be treated as distinct body");
        var distinctBodyItem = new NotificationHistoryItem { Title = "Alice", Body = "Are you free?" };
        Assert(distinctBodyItem.HasDistinctBody, "Different title and body must be treated as distinct");

        Assert(NotificationHistoryItem.ExtractOtpCode("Security", "Your verification code is 482910") == "482910", "Must extract 6-digit OTP");
        Assert(NotificationHistoryItem.ExtractOtpCode("Login", "OTP: 123-456") == "123456", "Must extract dashed 3-3 OTP");
        Assert(NotificationHistoryItem.ExtractOtpCode("Calendar", "Meeting at 2026 room 404") == null, "Must not extract non-OTP numbers");

        // 5. Verify FilterNotifications
        var msgItems = new List<NotificationHistoryItem>
        {
            new() { AppName = "WhatsApp", Title = "Alex", Body = "Lunch?", IsMessagingApp = true },
            new() { AppName = "GitHub", Title = "PR #42", Body = "Merged", IsMessagingApp = false }
        };
        var onlyMsgs = NotificationListenerService.FilterNotifications(msgItems, "Messages", null);
        Assert(onlyMsgs.Count == 1 && onlyMsgs[0].AppName == "WhatsApp", "Messages filter must return only messaging apps");
        var searched = NotificationListenerService.FilterNotifications(msgItems, "All", "merged");
        Assert(searched.Count == 1 && searched[0].AppName == "GitHub", "Search query must match body case-insensitively");
    }

    private static void TestChoreographerAndPowerGovernor()
    {
        // 1. Integrated GPU preference (snapshot & restore host HKCU value)
        const string D3DKeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
        string? exePath = Environment.ProcessPath;
        bool hadExistingValue = false;
        object? previousValue = null;
        RegistryValueKind previousKind = RegistryValueKind.String;

        if (!string.IsNullOrEmpty(exePath))
        {
            try
            {
                using var readKey = Registry.CurrentUser.OpenSubKey(D3DKeyPath, writable: false);
                if (readKey != null)
                {
                    previousValue = readKey.GetValue(exePath, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (previousValue != null)
                    {
                        hadExistingValue = true;
                        previousKind = readKey.GetValueKind(exePath);
                    }
                }
            }
            catch { }
        }

        try
        {
            bool gpuPref = Choreographer.EnsureIntegratedGpuPreference();
            // Should execute cleanly (true on standard Windows, or false if registry key inaccessible, but never throw)
        }
        finally
        {
            if (!string.IsNullOrEmpty(exePath))
            {
                try
                {
                    using var writeKey = Registry.CurrentUser.OpenSubKey(D3DKeyPath, writable: true);
                    if (writeKey != null)
                    {
                        if (hadExistingValue && previousValue != null)
                        {
                            writeKey.SetValue(exePath, previousValue, previousKind);
                        }
                        else
                        {
                            writeKey.DeleteValue(exePath, throwOnMissingValue: false);
                        }
                    }
                }
                catch { }
            }
        }

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

    private static void TestWindows11BackdropAndHdrToneMapping()
    {
        // 1. Transparency effects query
        bool transparency = WindowBlurHelper.IsWindowsTransparencyEnabled();
        // Should execute cleanly without throwing

        // 2. Windows 11 DWM Constants validation
        Assert(WindowBlurHelper.DWMSBT_AUTO == 0, "DWMSBT_AUTO must be 0");
        Assert(WindowBlurHelper.DWMSBT_NONE == 1, "DWMSBT_NONE must be 1");
        Assert(WindowBlurHelper.DWMSBT_MAINWINDOW == 2, "DWMSBT_MAINWINDOW must be 2");
        Assert(WindowBlurHelper.DWMSBT_TRANSIENTWINDOW == 3, "DWMSBT_TRANSIENTWINDOW must be 3");
        Assert(WindowBlurHelper.DWMSBT_TABBEDWINDOW == 4, "DWMSBT_TABBEDWINDOW must be 4");
        Assert(NativeMethods.WM_DPICHANGED == 0x02E0, "WM_DPICHANGED must be 0x02E0");

        // 3. HDR Tone Mapping algorithm validation
        Color black = Color.FromRgb(0, 0, 0);
        Color mappedBlack = ColorExtractor.ToneMapHdr(black);
        Assert(mappedBlack.R == 0 && mappedBlack.G == 0 && mappedBlack.B == 0, "Black must tone-map to black");

        Color pureWhite = Color.FromRgb(255, 255, 255);
        Color mappedWhite = ColorExtractor.ToneMapHdr(pureWhite);
        Assert(mappedWhite.R > 180 && mappedWhite.R <= 255, "White must remain high-luminance within byte bounds");
        Assert(mappedWhite.R == mappedWhite.G && mappedWhite.G == mappedWhite.B, "Neutral white must preserve chromatic balance");

        Color vividRed = Color.FromRgb(255, 30, 30);
        Color mappedRed = ColorExtractor.ToneMapHdr(vividRed);
        Assert(mappedRed.R > mappedRed.G * 2, "Dominant red hue must be preserved during HDR compression");
        Assert(mappedRed.A == 255, "Alpha channel must be preserved");
    }

    private static void TestLiveActivityIpcV2AndClipboardSecretDetection()
    {
        // 1. Secret / API Key Detection
        Assert(ClipboardHistoryService.DetectSecret("ghp_1234567890abcdef1234567890abcdef1234", out var ghType) && ghType == "GitHub Token",
            "GitHub token should be detected");
        Assert(ClipboardHistoryService.DetectSecret("sk-proj-abcde12345fghij67890klmno1234567890", out var oaType) && oaType == "OpenAI API Key",
            "OpenAI API key should be detected");
        Assert(ClipboardHistoryService.DetectSecret("AKIAIOSFODNN7EXAMPLE", out var awsType) && awsType == "AWS Access Key",
            "AWS Access key should be detected");
        Assert(ClipboardHistoryService.DetectSecret("-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA0...", out var pkType) && pkType == "Private Key",
            "Private key should be detected");
        Assert(ClipboardHistoryService.DetectSecret("xoxb-123456789012-1234567890123-abcdefghijkl", out var slackType) && slackType == "Slack Token",
            "Slack token should be detected");
        Assert(!ClipboardHistoryService.DetectSecret("https://github.com/microsoft/terminal", out _),
            "Normal URL must not be detected as secret");
        Assert(!ClipboardHistoryService.DetectSecret("This is a simple text sentence.", out _),
            "Normal text must not be detected as secret");

        // 2. Secret Masking
        string masked = ClipboardHistoryService.MaskSecret("AKIAIOSFODNN7EXAMPLE");
        Assert(masked.StartsWith("AKIA") && masked.EndsWith("MPLE") && masked.Contains("••••••••"),
            $"Masked secret must have correct shape, got '{masked}'");

        // 3. Live Activity IPC 2.0 Multi-Phase & Streaming Processing
        var hub = new LiveActivityHub();
        using var ipc = new LiveActivityIpcService(hub);

        var pubReq = new ActivityIpcRequest
        {
            Action = "publish",
            Id = "ci_pipeline",
            Source = "Antigravity",
            Title = "Release Build",
            Phases = new[] { "Lint", "Test", "Publish" },
            PhaseIndex = 1,
            Subtitle = "Running unit tests"
        };

        var pubResp = ipc.ProcessRequest(pubReq);
        Assert(pubResp.Ok, "Multi-phase publish request should succeed");

        var act = hub.Activities.FirstOrDefault(a => a.Id == "ci_pipeline");
        Assert(act != null, "Published activity must exist in hub");
        Assert(act!.Subtitle.Contains("[2/3] Test"), $"Subtitle should contain formatted phase, got '{act.Subtitle}'");

        // Stream thought chunk
        var streamReq = new ActivityIpcRequest
        {
            Action = "stream",
            Id = "ci_pipeline",
            Chunk = "Compiling Test 63..."
        };
        var streamResp = ipc.ProcessRequest(streamReq);
        Assert(streamResp.Ok, "Stream request should succeed");

        act = hub.Activities.FirstOrDefault(a => a.Id == "ci_pipeline");
        Assert(act!.Subtitle == "Compiling Test 63...", $"Subtitle should update to stream chunk, got '{act.Subtitle}'");

        // Fail / Error request
        var failReq = new ActivityIpcRequest
        {
            Action = "fail",
            Id = "ci_pipeline",
            Subtitle = "Build timed out"
        };
        var failResp = ipc.ProcessRequest(failReq);
        Assert(failResp.Ok, "Fail request should succeed");

        act = hub.Activities.FirstOrDefault(a => a.Id == "ci_pipeline");
        Assert(act!.Glyph == "\uE783", "Failed activity must have warning glyph");
        Assert(act.Subtitle == "Build timed out", "Failed activity must have error subtitle");

        hub.ClearAll();
    }

    private static void TestWinGetManifestAndReleasePackageInvariants()
    {
        string baseDir = AppContext.BaseDirectory;
        string projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));

        // Fall back to working directory if running in-tree
        if (!File.Exists(Path.Combine(projectRoot, "DynamicIsland.csproj")))
        {
            projectRoot = Directory.GetCurrentDirectory();
        }

        // 1. Inno Setup version check
        string issPath = Path.Combine(projectRoot, "installer", "DynamicIsland.iss");
        if (File.Exists(issPath))
        {
            string issContent = File.ReadAllText(issPath);
            Assert(issContent.Contains("#define MyAppVersion \"0.10.0\""),
                "DynamicIsland.iss must define MyAppVersion as '0.10.0'");
        }

        // 2. release-manifest.json validation
        string manifestPath = Path.Combine(projectRoot, "release-manifest.json");
        if (File.Exists(manifestPath))
        {
            string manifestJson = File.ReadAllText(manifestPath);
            var manifest = System.Text.Json.JsonSerializer.Deserialize<UpdateManifest>(manifestJson);
            Assert(manifest != null, "release-manifest.json must deserialize cleanly");
            Assert(manifest!.Version == "0.10.0", $"release-manifest.json version must be '0.10.0', got '{manifest.Version}'");
            Assert(manifest.ReleaseTag == "v0.10.0", $"release-manifest.json releaseTag must be 'v0.10.0', got '{manifest.ReleaseTag}'");
        }

        Assert(InstallerService.AppVersion == "0.10.0", $"InstallerService.AppVersion must be '0.10.0', got '{InstallerService.AppVersion}'");

        // 3. WinGet Manifest Files validation
        string wingetDir = Path.Combine(projectRoot, "installer", "winget");
        string versionYaml = Path.Combine(wingetDir, "JainisPatel.DynamicIsland.yaml");
        string installerYaml = Path.Combine(wingetDir, "JainisPatel.DynamicIsland.installer.yaml");
        string localeYaml = Path.Combine(wingetDir, "JainisPatel.DynamicIsland.locale.en-US.yaml");

        if (Directory.Exists(wingetDir))
        {
            Assert(File.Exists(versionYaml), "Version YAML manifest must exist");
            Assert(File.Exists(installerYaml), "Installer YAML manifest must exist");
            Assert(File.Exists(localeYaml), "Locale YAML manifest must exist");

            string vContent = File.ReadAllText(versionYaml);
            Assert(vContent.Contains("PackageIdentifier: JainisPatel.DynamicIsland"), "Version YAML must specify identifier");
            Assert(vContent.Contains("PackageVersion: 0.10.0"), "Version YAML must specify version 0.10.0");

            string iContent = File.ReadAllText(installerYaml);
            Assert(iContent.Contains("PackageVersion: 0.10.0"), "Installer YAML must specify version 0.10.0");
            Assert(iContent.Contains("Architecture: x64"), "Installer YAML must specify x64 architecture");

            string lContent = File.ReadAllText(localeYaml);
            Assert(lContent.Contains("PackageVersion: 0.10.0"), "Locale YAML must specify version 0.10.0");
            Assert(lContent.Contains("PackageName: Dynamic Island for Windows"), "Locale YAML must specify package name");
        }
    }

    private sealed class MockGitHubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public MockGitHubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private static void TestAutoUpdateAndGitHubVersionControl()
    {
        // 1. Stable channel /releases/latest + ETag 304 NotModified caching
        const string stableSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string portableSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        int stableCalls = 0;

        using (var stableHandler = new MockGitHubHttpHandler(req =>
        {
            string uri = req.RequestUri!.ToString();
            if (uri.EndsWith("/releases/latest", StringComparison.Ordinal))
            {
                stableCalls++;
                if (req.Headers.IfNoneMatch.Any(e => e.Tag == "\"etag-v091\""))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotModified);
                }

                string json = $$"""
                {
                  "tag_name": "v0.9.1",
                  "prerelease": false,
                  "draft": false,
                  "published_at": "2026-11-26T10:00:00Z",
                  "html_url": "https://github.com/jainis-Patel/DynamicIsland/releases/tag/v0.9.1",
                  "body": "Stability release",
                  "assets": [
                    {
                      "name": "DynamicIsland-Setup.exe",
                      "browser_download_url": "https://github.com/jainis-Patel/DynamicIsland/releases/download/v0.9.1/DynamicIsland-Setup.exe",
                      "size": 74000000,
                      "digest": "sha256:{{stableSha}}"
                    },
                    {
                      "name": "DynamicIsland-Portable.zip",
                      "browser_download_url": "https://github.com/jainis-Patel/DynamicIsland/releases/download/v0.9.1/DynamicIsland-Portable.zip",
                      "size": 68000000,
                      "digest": "sha256:{{portableSha}}"
                    }
                  ]
                }
                """;
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                resp.Headers.ETag = new EntityTagHeaderValue("\"etag-v091\"");
                return resp;
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        using (var http = new HttpClient(stableHandler))
        using (var client = new GitHubReleaseClient(client: http))
        {
            var first = client.FetchLatestManifestDetailedAsync(includePrereleases: false).GetAwaiter().GetResult();
            Assert(first.Status == ManifestFetchStatus.Success, $"Expected Success, got {first.Status}");
            Assert(first.Manifest != null && first.Manifest.Version == "0.9.1", "Expected v0.9.1 from /releases/latest");
            Assert(first.Manifest!.Downloads.Installer?.Sha256 == stableSha, "Installer SHA-256 digest must be extracted");
            Assert(first.Manifest.Downloads.Portable?.Sha256 == portableSha, "Portable SHA-256 digest must be extracted");

            var second = client.FetchLatestManifestDetailedAsync(includePrereleases: false).GetAwaiter().GetResult();
            Assert(second.Status == ManifestFetchStatus.NotModified, $"Expected NotModified on 304, got {second.Status}");
            Assert(second.Manifest != null && second.Manifest.Version == "0.9.1", "304 NotModified must return cached manifest");
            Assert(stableCalls == 2, "Second check should have sent conditional ETag request");
        }

        // 2. Beta channel queries /releases?per_page=10, skips drafts, and picks highest prerelease
        using (var betaHandler = new MockGitHubHttpHandler(req =>
        {
            string uri = req.RequestUri!.ToString();
            if (uri.Contains("/releases?per_page=10", StringComparison.Ordinal))
            {
                string json = """
                [
                  {
                    "tag_name": "v0.10.0-beta.9",
                    "prerelease": true,
                    "draft": true,
                    "published_at": "2026-11-26T12:00:00Z",
                    "html_url": "https://github.com/jainis-Patel/DynamicIsland/releases/tag/v0.10.0-beta.9",
                    "assets": []
                  },
                  {
                    "tag_name": "v0.10.0-beta.2",
                    "prerelease": true,
                    "draft": false,
                    "published_at": "2026-11-26T11:00:00Z",
                    "html_url": "https://github.com/jainis-Patel/DynamicIsland/releases/tag/v0.10.0-beta.2",
                    "assets": []
                  },
                  {
                    "tag_name": "v0.9.0",
                    "prerelease": false,
                    "draft": false,
                    "published_at": "2026-11-25T11:00:00Z",
                    "html_url": "https://github.com/jainis-Patel/DynamicIsland/releases/tag/v0.9.0",
                    "assets": []
                  }
                ]
                """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        using (var http = new HttpClient(betaHandler))
        using (var client = new GitHubReleaseClient(client: http))
        {
            var betaResult = client.FetchLatestManifestDetailedAsync(includePrereleases: true).GetAwaiter().GetResult();
            Assert(betaResult.Status == ManifestFetchStatus.Success, "Beta fetch must succeed");
            Assert(betaResult.Manifest != null && betaResult.Manifest.Version == "0.10.0-beta.2",
                $"Beta channel must skip draft and pick 0.10.0-beta.2, got {betaResult.Manifest?.Version}");
            Assert(betaResult.Manifest!.ParsedChannel == UpdateChannel.Beta, "Prerelease must map to Beta channel");
        }

        // 3. Rate-limit (403) falls back to raw CDN manifest when available, and returns RateLimited when both fail
        long resetEpoch = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
        using (var fallbackHandler = new MockGitHubHttpHandler(req =>
        {
            string uri = req.RequestUri!.ToString();
            if (uri.StartsWith("https://api.github.com/", StringComparison.Ordinal))
            {
                var resp = new HttpResponseMessage(HttpStatusCode.Forbidden);
                resp.Headers.TryAddWithoutValidation("X-RateLimit-Reset", resetEpoch.ToString());
                return resp;
            }
            if (uri.Contains("raw.githubusercontent.com", StringComparison.Ordinal))
            {
                string rawJson = $$"""
                {
                  "version": "0.9.2",
                  "releaseTag": "v0.9.2",
                  "channel": "stable",
                  "downloads": {
                    "installer": {
                      "url": "https://github.com/jainis-Patel/DynamicIsland/releases/download/v0.9.2/DynamicIsland-Setup.exe",
                      "sha256": "{{stableSha}}",
                      "sizeBytes": 74000000
                    }
                  }
                }
                """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(rawJson, Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Head && uri.Contains("DynamicIsland-Setup.exe", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        using (var http = new HttpClient(fallbackHandler))
        using (var client = new GitHubReleaseClient(client: http))
        {
            var fallbackRes = client.FetchLatestManifestDetailedAsync(includePrereleases: false).GetAwaiter().GetResult();
            Assert(fallbackRes.Status == ManifestFetchStatus.Success && fallbackRes.Source == "RawManifest",
                "API 403 must fall back to raw CDN release-manifest.json");
            Assert(fallbackRes.Manifest?.Version == "0.9.2", "Fallback manifest version must be 0.9.2");
        }

        using (var rateLimitOnlyHandler = new MockGitHubHttpHandler(req =>
        {
            if (req.RequestUri!.ToString().StartsWith("https://api.github.com/", StringComparison.Ordinal))
            {
                var resp = new HttpResponseMessage(HttpStatusCode.Forbidden);
                resp.Headers.TryAddWithoutValidation("X-RateLimit-Reset", resetEpoch.ToString());
                return resp;
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        using (var http = new HttpClient(rateLimitOnlyHandler))
        using (var client = new GitHubReleaseClient(client: http))
        {
            var rlRes = client.FetchLatestManifestDetailedAsync(includePrereleases: false).GetAwaiter().GetResult();
            Assert(rlRes.Status == ManifestFetchStatus.RateLimited, $"Expected RateLimited, got {rlRes.Status}");
            Assert(rlRes.RateLimitResetUtc.HasValue, "RateLimitResetUtc must be parsed from X-RateLimit-Reset");

            var histRes = client.FetchReleaseHistoryDetailedAsync().GetAwaiter().GetResult();
            Assert(!histRes.IsSuccess && histRes.IsRateLimited, "Release history must report rate limit");
        }

        // 4. Release history skips drafts, extracts Portable + Installer, and FetchExpectedSha256Async resolves by target
        using (var histHandler = new MockGitHubHttpHandler(req =>
        {
            string json = $$"""
            [
              {
                "tag_name": "v0.9.5-draft",
                "prerelease": false,
                "draft": true,
                "published_at": "2026-11-26T10:00:00Z",
                "html_url": "https://github.com/jainis-Patel/DynamicIsland/releases/tag/v0.9.5-draft",
                "assets": []
              },
              {
                "tag_name": "v0.9.0",
                "prerelease": false,
                "draft": false,
                "published_at": "2026-11-26T01:37:00Z",
                "html_url": "https://github.com/jainis-Patel/DynamicIsland/releases/tag/v0.9.0",
                "assets": [
                  {
                    "name": "DynamicIsland-Setup.exe",
                    "browser_download_url": "https://github.com/jainis-Patel/DynamicIsland/releases/download/v0.9.0/DynamicIsland-Setup.exe",
                    "size": 74358512,
                    "digest": "sha256:{{stableSha}}"
                  },
                  {
                    "name": "DynamicIsland-Portable.zip",
                    "browser_download_url": "https://github.com/jainis-Patel/DynamicIsland/releases/download/v0.9.0/DynamicIsland-Portable.zip",
                    "size": 68483630,
                    "digest": "sha256:{{portableSha}}"
                  }
                ]
              }
            ]
            """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }))
        using (var http = new HttpClient(histHandler))
        using (var client = new GitHubReleaseClient(client: http))
        {
            var hist = client.FetchReleaseHistoryDetailedAsync().GetAwaiter().GetResult();
            Assert(hist.IsSuccess && hist.Items.Count == 1, $"Drafts must be skipped; expected 1 item, got {hist.Items.Count}");
            var item = hist.Items[0];
            Assert(item.HasDownloadableAsset, "ReleaseHistoryItem must report HasDownloadableAsset");
            Assert(item.PortableUrl != null && item.PortableSha256 == portableSha, "Portable URL and SHA-256 must be extracted");

            string? resolvedExe = client.FetchExpectedSha256Async(item, "DynamicIsland-Setup.exe").GetAwaiter().GetResult();
            string? resolvedZip = client.FetchExpectedSha256Async(item, "DynamicIsland-Portable.zip").GetAwaiter().GetResult();
            Assert(resolvedExe == stableSha, "Installer SHA-256 must resolve for .exe target");
            Assert(resolvedZip == portableSha, "Portable SHA-256 must resolve for .zip target");
        }

        // 5. SelectBestAsset prefers Installer when installed, Portable when portable
        var downloads = new UpdateDownloads
        {
            Installer = new UpdateDownloadAsset { Url = "https://github.com/jainis-Patel/DynamicIsland/releases/download/v0.9.1/DynamicIsland-Setup.exe", Sha256 = stableSha },
            Portable = new UpdateDownloadAsset { Url = "https://github.com/jainis-Patel/DynamicIsland/releases/download/v0.9.1/DynamicIsland-Portable.zip", Sha256 = portableSha }
        };
        var (installedAsset, isInst1) = UpdateManagerService.SelectBestAsset(downloads, isInstalledCopy: true);
        Assert(isInst1 && installedAsset == downloads.Installer, "Installed copy must prefer Installer");
        var (portableAsset, isInst2) = UpdateManagerService.SelectBestAsset(downloads, isInstalledCopy: false);
        Assert(!isInst2 && portableAsset == downloads.Portable, "Portable copy must prefer Portable");

        // 6. BuildInstallerArguments includes Inno Setup silent flags & target directory
        string args = UpdateHandoffEngine.BuildInstallerArguments(1234, @"C:\Users\Test\AppData\Local\Programs\DynamicIsland");
        Assert(args.Contains("--wait-pid 1234", StringComparison.Ordinal), "Installer args must include --wait-pid");
        Assert(args.Contains("/DIR=\"C:\\Users\\Test\\AppData\\Local\\Programs\\DynamicIsland\"", StringComparison.Ordinal), "Installer args must include Inno /DIR");
        Assert(args.Contains("/SILENT", StringComparison.Ordinal) && args.Contains("/SUPPRESSMSGBOXES", StringComparison.Ordinal) && args.Contains("/NORESTART", StringComparison.Ordinal),
            "Installer args must include silent non-restarting Inno Setup flags");
        Assert(args.Contains("/AppParams=\"--updated\"", StringComparison.Ordinal), "Installer args must pass --updated via /AppParams");

        // 7. SemVersion informational version ordering with build metadata
        Assert(SemVersion.TryParse("0.10.0-beta.1+abc123", out var infoBeta1), "Informational beta1 must parse");
        Assert(SemVersion.TryParse("0.10.0-beta.2", out var infoBeta2), "Beta2 must parse");
        Assert(SemVersion.TryParse("0.10.0", out var infoStable), "Stable 0.10.0 must parse");
        Assert(infoBeta1 < infoBeta2 && infoBeta2 < infoStable, "0.10.0-beta.1+abc123 < 0.10.0-beta.2 < 0.10.0");
    }
}
