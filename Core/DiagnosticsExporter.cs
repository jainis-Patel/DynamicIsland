using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DynamicIsland.Services;

namespace DynamicIsland.Core;

/// <summary>
/// Exports a sanitized JSON diagnostics bundle (memory/GC stats, Choreographer state,
/// secret-stripped settings, and bounded in-memory log ring) for bug reports and support.
/// </summary>
internal static class DiagnosticsExporter
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    public static (bool Ok, string FilePath, string? Error) ExportBundle(
        string? outputPath,
        AppSettings settings,
        Choreographer? choreographer = null)
    {
        try
        {
            string targetPath = string.IsNullOrWhiteSpace(outputPath)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    $"DynamicIsland-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json")
                : outputPath;

            var proc = Process.GetCurrentProcess();
            var gc = GC.GetGCMemoryInfo();
            string appVer = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.9.0";

            JsonNode? sanitizedSettingsNode = null;
            try
            {
                string raw = JsonSerializer.Serialize(settings);
                if (JsonNode.Parse(raw) is JsonObject obj)
                {
                    obj.Remove(nameof(AppSettings.GitHubToken));
                    obj.Remove(nameof(AppSettings.PhoneBridgePassword));
                    obj.Remove(nameof(AppSettings.GitHubTokenProtected));
                    obj.Remove(nameof(AppSettings.PhoneBridgePasswordProtected));
                    sanitizedSettingsNode = obj;
                }
            }
            catch
            {
                sanitizedSettingsNode = new JsonObject();
            }

            var payload = new JsonObject
            {
                ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["appVersion"] = appVer,
                ["osVersion"] = Environment.OSVersion.VersionString,
                ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
                ["memory"] = new JsonObject
                {
                    ["workingSetMb"] = proc.WorkingSet64 / (1024 * 1024),
                    ["privateBytesMb"] = proc.PrivateMemorySize64 / (1024 * 1024),
                    ["managedHeapMb"] = GC.GetTotalMemory(false) / (1024 * 1024),
                    ["gcHeapSizeMb"] = gc.HeapSizeBytes / (1024 * 1024),
                    ["gcGen0Count"] = GC.CollectionCount(0),
                    ["gcGen1Count"] = GC.CollectionCount(1),
                    ["gcGen2Count"] = GC.CollectionCount(2),
                },
                ["choreographer"] = new JsonObject
                {
                    ["mode"] = choreographer?.CurrentMode.ToString() ?? "Unavailable",
                    ["isSuspended"] = choreographer?.IsSuspended ?? false,
                    ["registeredChores"] = new JsonArray(
                        (choreographer?.GetRegisteredChoreIds() ?? Array.Empty<string>())
                        .Select(id => JsonValue.Create(id))
                        .ToArray())
                },
                ["sanitizedSettings"] = sanitizedSettingsNode,
                ["recentLogs"] = new JsonArray(
                    App.GetRecentLogs()
                    .Select(line => JsonValue.Create(line))
                    .ToArray())
            };

            string? dir = Path.GetDirectoryName(Path.GetFullPath(targetPath));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = payload.ToJsonString(PrettyJson);
            string tmp = targetPath + ".tmp";
            File.WriteAllText(tmp, json, System.Text.Encoding.UTF8);
            File.Move(tmp, targetPath, overwrite: true);
            return (true, targetPath, null);
        }
        catch (Exception ex)
        {
            return (false, outputPath ?? "", ex.Message);
        }
    }
}
