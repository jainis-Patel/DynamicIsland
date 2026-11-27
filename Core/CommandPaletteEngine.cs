using DynamicIsland.Services;

namespace DynamicIsland.Core;

public enum CommandResultKind
{
    Math,
    SystemAction,
    Profile,
    Setting,
    App,
    Clipboard,
    AudioEndpoint,
    Window
}

public sealed record CommandPaletteItem(
    string Id,
    string Title,
    string Subtitle,
    string Glyph,
    CommandResultKind Kind,
    string ActionKey,
    string? Payload = null,
    int Score = 0);

/// <summary>
/// Unified, deterministic fuzzy-ranked Command Palette 2.0 engine aggregating inline math/unit
/// conversions, built-in Island actions, Context Profiles, Settings deep-links, pinned apps,
/// non-sensitive clipboard items, audio endpoints, and open desktop windows.
/// Supports prefix-scoped queries (<c>clip:</c>, <c>audio:</c>, <c>win:</c>, <c>set:</c>, <c>app:</c>).
/// </summary>
internal static class CommandPaletteEngine
{
    public static readonly IReadOnlyList<(string Key, string Title, string Subtitle, string Glyph, string Keywords)> BuiltInActions = new[]
    {
        ("toggle_dnd", "Toggle Do Not Disturb", "Silence or allow incoming banner animations", "\uE708", "dnd silence notifications quiet focus"),
        ("start_pomodoro", "Start 25m Pomodoro", "Start or toggle 25-minute focus timer", "\uE916", "timer focus countdown pomodoro clock"),
        ("toggle_night_light", "Toggle Night Light", "Warm display color temperature filter", "\uE708", "night light warmth kelvin blue filter"),
        ("toggle_mic_mute", "Toggle Microphone Mute", "Mute or unmute hardware microphone", "\uE720", "mic mute unmute privacy call"),
        ("toggle_low_memory", "Toggle Low Memory Mode", "Reduce visual allocations and effects", "\uE945", "low memory ram performance mode"),
        ("open_settings", "Open Settings", "Configure appearance, droplets, and hotkeys", "\uE713", "settings preferences config"),
        ("purge_ram", "Purge Idle RAM", "Trim working set and compact managed heap", "\uE9D9", "purge ram memory trim buster cleanup"),
        ("export_hooks", "Export AI Agent Hooks", "Generate Claude Code & CLI pipe scripts", "\uE943", "ai agent hooks claude pipe ipc export"),
        ("export_diagnostics", "Export Diagnostics Bundle", "Save sanitized diagnostics JSON to Desktop", "\uE9D9", "diagnostics debug log bundle export support"),
        ("replay_onboarding", "Show Quick-Start Guide", "Replay onboarding tips and shortcuts", "\uE734", "onboarding guide help tour tips"),
    };

    public static IReadOnlyList<CommandPaletteItem> Query(
        string? input,
        IReadOnlyList<LauncherItem>? apps = null,
        IReadOnlyList<ClipboardItem>? clips = null,
        IReadOnlyList<IslandProfile>? profiles = null,
        int maxResults = 12,
        IReadOnlyList<AudioEndpointInfo>? audioEndpoints = null,
        IReadOnlyList<OpenWindowEntry>? openWindows = null)
    {
        string trimmed = input?.Trim() ?? "";
        if (string.IsNullOrEmpty(trimmed))
        {
            return Array.Empty<CommandPaletteItem>();
        }

        // Prefix-scoped providers (clip:, audio:, win:, set:, app:)
        if (TryExtractPrefix(trimmed, out string prefix, out string subQuery))
        {
            return QueryPrefixed(prefix, subQuery, apps, clips, audioEndpoints, openWindows, maxResults);
        }

        var results = new List<CommandPaletteItem>();

        // 1. Inline Math & Unit Conversion (highest priority when valid expression)
        var (calcOk, calcResult, calcExplanation) = QuickCalcService.Evaluate(trimmed);
        if (calcOk && !string.IsNullOrWhiteSpace(calcResult))
        {
            results.Add(new CommandPaletteItem(
                Id: "math_eval",
                Title: $"= {calcResult}",
                Subtitle: string.IsNullOrWhiteSpace(calcExplanation) ? "Press Enter to copy result" : $"{calcExplanation} · Enter to copy",
                Glyph: "\uE8EF",
                Kind: CommandResultKind.Math,
                ActionKey: "copy_calc",
                Payload: calcResult,
                Score: 1000));
        }

        // 2. Built-in System & Island Actions
        foreach (var act in BuiltInActions)
        {
            int score = ScoreMatch(trimmed, act.Title, act.Subtitle, act.Keywords);
            if (score > 0)
            {
                results.Add(new CommandPaletteItem(
                    Id: $"action:{act.Key}",
                    Title: act.Title,
                    Subtitle: act.Subtitle,
                    Glyph: act.Glyph,
                    Kind: CommandResultKind.SystemAction,
                    ActionKey: act.Key,
                    Score: score + 40));
            }
        }

        // 3. Context Profiles
        var profileList = profiles ?? ContextProfileService.BuiltInProfiles;
        foreach (var prof in profileList)
        {
            int score = ScoreMatch(trimmed, $"Switch Profile: {prof.Name}", prof.Description, $"profile mode {prof.Id} {prof.Name}");
            if (score > 0)
            {
                results.Add(new CommandPaletteItem(
                    Id: $"profile:{prof.Id}",
                    Title: $"Profile: {prof.Name}",
                    Subtitle: prof.Description,
                    Glyph: prof.Glyph,
                    Kind: CommandResultKind.Profile,
                    ActionKey: "switch_profile",
                    Payload: prof.Id,
                    Score: score + 35));
            }
        }

        // 4. Settings Deep-Links
        AddSettingMatches(results, trimmed);

        // 5. Pinned Apps & Folders
        AddAppMatches(results, trimmed, apps);

        // 6. Audio Endpoints
        AddAudioMatches(results, trimmed, audioEndpoints);

        // 7. Open Windows
        AddWindowMatches(results, trimmed, openWindows);

        // 8. Recent Non-Sensitive Clipboard Items (never expose secrets)
        AddClipMatches(results, trimmed, clips);

        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }

    private static bool TryExtractPrefix(string input, out string prefix, out string subQuery)
    {
        int colon = input.IndexOf(':');
        if (colon > 0 && colon <= 8)
        {
            string candidate = input[..colon].Trim().ToLowerInvariant();
            if (candidate is "clip" or "audio" or "win" or "set" or "settings" or "app")
            {
                prefix = candidate;
                subQuery = input[(colon + 1)..].Trim();
                return true;
            }
        }

        prefix = "";
        subQuery = "";
        return false;
    }

    private static IReadOnlyList<CommandPaletteItem> QueryPrefixed(
        string prefix,
        string subQuery,
        IReadOnlyList<LauncherItem>? apps,
        IReadOnlyList<ClipboardItem>? clips,
        IReadOnlyList<AudioEndpointInfo>? audioEndpoints,
        IReadOnlyList<OpenWindowEntry>? openWindows,
        int maxResults)
    {
        var results = new List<CommandPaletteItem>();
        switch (prefix)
        {
            case "clip":
                AddClipMatches(results, subQuery, clips, includeAllWhenEmpty: true);
                break;
            case "audio":
                AddAudioMatches(results, subQuery, audioEndpoints, includeAllWhenEmpty: true);
                break;
            case "win":
                AddWindowMatches(results, subQuery, openWindows, includeAllWhenEmpty: true);
                break;
            case "set":
            case "settings":
                AddSettingMatches(results, subQuery, includeAllWhenEmpty: true);
                break;
            case "app":
                AddAppMatches(results, subQuery, apps, includeAllWhenEmpty: true);
                break;
        }

        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }

    private static void AddSettingMatches(List<CommandPaletteItem> results, string query, bool includeAllWhenEmpty = false)
    {
        foreach (var entry in SettingsSearchIndex.Entries)
        {
            int score = string.IsNullOrEmpty(query) && includeAllWhenEmpty
                ? 100
                : ScoreMatch(query, entry.Title, entry.Subtitle, entry.Keywords);
            if (score > 0)
            {
                results.Add(new CommandPaletteItem(
                    Id: $"setting:{entry.CategoryKey}:{entry.Title}",
                    Title: entry.Title,
                    Subtitle: $"Settings › {entry.CategoryLabel} · {entry.Subtitle}",
                    Glyph: entry.Glyph,
                    Kind: CommandResultKind.Setting,
                    ActionKey: "open_setting",
                    Payload: entry.CategoryKey,
                    Score: score + 15));
            }
        }
    }

    private static void AddAppMatches(List<CommandPaletteItem> results, string query, IReadOnlyList<LauncherItem>? apps, bool includeAllWhenEmpty = false)
    {
        if (apps == null) return;
        foreach (var app in apps)
        {
            int score = string.IsNullOrEmpty(query) && includeAllWhenEmpty
                ? 100
                : ScoreMatch(query, app.Name, app.TargetPath, app.IsFolder ? "folder" : "app");
            if (score > 0)
            {
                results.Add(new CommandPaletteItem(
                    Id: $"app:{app.Id}",
                    Title: app.Name,
                    Subtitle: app.IsFolder ? $"Open folder · {app.TargetPath}" : $"Launch app · {app.TargetPath}",
                    Glyph: app.Glyph,
                    Kind: CommandResultKind.App,
                    ActionKey: "launch_app",
                    Payload: app.TargetPath,
                    Score: score + 25));
            }
        }
    }

    private static void AddClipMatches(List<CommandPaletteItem> results, string query, IReadOnlyList<ClipboardItem>? clips, bool includeAllWhenEmpty = false)
    {
        if (clips == null) return;
        int idx = 0;
        foreach (var clip in clips)
        {
            idx++;
            if (clip.IsSecret || string.IsNullOrWhiteSpace(clip.RawText)) continue;
            int score = string.IsNullOrEmpty(query) && includeAllWhenEmpty
                ? Math.Max(10, 200 - idx)
                : ScoreMatch(query, clip.Title, clip.Subtitle, $"clipboard copy {clip.TypeTag}");
            if (score > 0)
            {
                results.Add(new CommandPaletteItem(
                    Id: $"clip:{clip.Id}",
                    Title: clip.Title,
                    Subtitle: $"Clipboard ({clip.TypeTag}) · Enter to copy",
                    Glyph: "\uE8C8",
                    Kind: CommandResultKind.Clipboard,
                    ActionKey: "copy_clip",
                    Payload: clip.RawText,
                    Score: score + 10));
            }
        }
    }

    private static void AddAudioMatches(List<CommandPaletteItem> results, string query, IReadOnlyList<AudioEndpointInfo>? endpoints, bool includeAllWhenEmpty = false)
    {
        if (endpoints == null) return;
        foreach (var ep in endpoints)
        {
            int score = string.IsNullOrEmpty(query) && includeAllWhenEmpty
                ? (ep.IsDefault ? 180 : 120)
                : ScoreMatch(query, ep.Name, ep.DeviceType, $"audio sound speaker headphones output");
            if (score > 0)
            {
                results.Add(new CommandPaletteItem(
                    Id: $"audio:{ep.Id}",
                    Title: ep.IsDefault ? $"{ep.Name} (Active)" : ep.Name,
                    Subtitle: $"{ep.DeviceType} · {(int)Math.Round(ep.Volume * 100)}% · Enter to switch",
                    Glyph: ep.Glyph,
                    Kind: CommandResultKind.AudioEndpoint,
                    ActionKey: "switch_audio",
                    Payload: ep.Id,
                    Score: score + 20));
            }
        }
    }

    private static void AddWindowMatches(List<CommandPaletteItem> results, string query, IReadOnlyList<OpenWindowEntry>? windows, bool includeAllWhenEmpty = false)
    {
        if (windows == null) return;
        int idx = 0;
        foreach (var win in windows)
        {
            idx++;
            int score = string.IsNullOrEmpty(query) && includeAllWhenEmpty
                ? Math.Max(10, 180 - idx)
                : ScoreMatch(query, win.Title, win.ProcessName, $"window focus switch {win.ProcessName}");
            if (score > 0)
            {
                results.Add(new CommandPaletteItem(
                    Id: $"win:{win.Hwnd}",
                    Title: win.Title,
                    Subtitle: string.IsNullOrEmpty(win.ProcessName) ? "Window · Enter to focus" : $"{win.ProcessName} · Enter to focus",
                    Glyph: "\uE737",
                    Kind: CommandResultKind.Window,
                    ActionKey: "focus_window",
                    Payload: win.Hwnd.ToInt64().ToString(),
                    Score: score + 18));
            }
        }
    }

    public static int ScoreMatch(string query, string title, string? subtitle = null, string? keywords = null)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0;

        string q = query.Trim();
        if (title.Equals(q, StringComparison.OrdinalIgnoreCase)) return 300;
        if (title.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 240;

        int titleIdx = title.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (titleIdx > 0)
        {
            bool atWordBoundary = char.IsWhiteSpace(title[titleIdx - 1]) || title[titleIdx - 1] is ':' or '-' or '/';
            return atWordBoundary ? 210 : 175;
        }

        string[] tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return 0;

        int total = 0;
        foreach (string token in tokens)
        {
            if (title.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                total += 80;
            }
            else if (!string.IsNullOrEmpty(keywords) && keywords.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                total += 55;
            }
            else if (!string.IsNullOrEmpty(subtitle) && subtitle.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                total += 40;
            }
            else
            {
                return 0;
            }
        }

        return total;
    }
}
