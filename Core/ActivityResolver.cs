namespace DynamicIsland.Core;

/// <summary>
/// Identifies which Compact-mode live activity host should be displayed when the island is in <see cref="IslandState.Compact"/>.
/// </summary>
public enum CompactActivityKind
{
    None,
    Recording,
    Media,
    Call,
    Countdown,
    Download,
    Stopwatch,
    LiveActivity
}

/// <summary>
/// Pure, deterministic resolver for Compact live-activity priority:
/// Recording &gt; Media &gt; Active Call &gt; Countdown Timer &gt; Active Download &gt; Stopwatch.
/// </summary>
public static class ActivityResolver
{
    public static (bool HasActivity, CompactActivityKind ActiveHost) Resolve(
        bool recording,
        bool mediaPlaying,
        bool callActive,
        bool countdownRunning,
        bool downloading,
        bool stopwatchActive)
    {
        var (hasActivity, primary, _) = ResolveWithSecondary(
            recording, mediaPlaying, callActive, countdownRunning, downloading, stopwatchActive);
        return (hasActivity, primary);
    }

    /// <summary>
    /// Resolves both the primary Compact capsule activity and an optional secondary activity
    /// for the iOS-style detached split-droplet bubble when two live activities run concurrently.
    /// </summary>
    public static (bool HasActivity, CompactActivityKind PrimaryHost, CompactActivityKind SecondaryHost) ResolveWithSecondary(
        bool recording,
        bool mediaPlaying,
        bool callActive,
        bool countdownRunning,
        bool downloading,
        bool stopwatchActive) =>
        ResolveWithSecondary(recording, mediaPlaying, callActive, countdownRunning, downloading, stopwatchActive, false);

    /// <summary>
    /// Resolves both the primary Compact capsule activity and an optional secondary activity
    /// for the iOS-style detached split-droplet bubble when two live activities run concurrently.
    /// Priority: Recording &gt; LiveActivity &gt; Call &gt; Media &gt; Countdown &gt; Download &gt; Stopwatch.
    /// </summary>
    public static (bool HasActivity, CompactActivityKind PrimaryHost, CompactActivityKind SecondaryHost) ResolveWithSecondary(
        bool recording,
        bool mediaPlaying,
        bool callActive,
        bool countdownRunning,
        bool downloading,
        bool stopwatchActive,
        bool liveActivityActive)
    {
        Span<CompactActivityKind> active = stackalloc CompactActivityKind[7];
        int count = 0;

        if (recording) active[count++] = CompactActivityKind.Recording;
        if (liveActivityActive) active[count++] = CompactActivityKind.LiveActivity;
        if (callActive) active[count++] = CompactActivityKind.Call;
        if (mediaPlaying) active[count++] = CompactActivityKind.Media;
        if (countdownRunning) active[count++] = CompactActivityKind.Countdown;
        if (downloading) active[count++] = CompactActivityKind.Download;
        if (!countdownRunning && stopwatchActive) active[count++] = CompactActivityKind.Stopwatch;

        if (count == 0)
            return (false, CompactActivityKind.None, CompactActivityKind.None);

        var primary = active[0];
        var secondary = count > 1 ? active[1] : CompactActivityKind.None;
        return (true, primary, secondary);
    }

    /// <summary>
    /// Returns the Segoe Fluent icon glyph representing a <see cref="CompactActivityKind"/> in the secondary droplet bubble.
    /// </summary>
    public static string GlyphForActivity(CompactActivityKind kind) => kind switch
    {
        CompactActivityKind.Recording => "\uE7C8",
        CompactActivityKind.Media => "\uE8D6",
        CompactActivityKind.Call => "\uE717",
        CompactActivityKind.Countdown => "\uE916",
        CompactActivityKind.Download => "\uE896",
        CompactActivityKind.Stopwatch => "\uE916",
        CompactActivityKind.LiveActivity => "\uE7C3",
        _ => "\uE7E7"
    };

    /// <summary>
    /// Determines which <see cref="ExpandedView"/> should open when the user clicks the Compact pill.
    /// </summary>
    public static ExpandedView ResolveCompactTargetView(CompactActivityKind kind) => kind switch
    {
        CompactActivityKind.Recording => ExpandedView.VoiceMemo,
        CompactActivityKind.Download => ExpandedView.Shelf,
        CompactActivityKind.LiveActivity => ExpandedView.Shelf,
        _ => ExpandedView.Media
    };

    /// <summary>
    /// Returns a contextual tooltip describing the secondary droplet's live activity and click/middle-click actions.
    /// </summary>
    public static string DescribeSecondaryDroplet(CompactActivityKind kind) => kind switch
    {
        CompactActivityKind.Recording => "Voice Memo Recording \u00B7 Click to expand \u00B7 Middle-click to stop",
        CompactActivityKind.Media => "Now Playing \u00B7 Click to expand \u00B7 Middle-click to play/pause",
        CompactActivityKind.Call => "Active Call \u00B7 Click to expand \u00B7 Middle-click to mute/unmute mic",
        CompactActivityKind.Countdown => "Countdown Timer \u00B7 Click to expand \u00B7 Middle-click to pause/resume",
        CompactActivityKind.Stopwatch => "Stopwatch \u00B7 Click to expand \u00B7 Middle-click to pause/resume",
        CompactActivityKind.Download => "Active Download \u00B7 Click to open Drop Shelf",
        CompactActivityKind.LiveActivity => "Live Activity \u00B7 Click to swap \u00B7 Middle-click to inspect",
        _ => "Secondary Live Activity \u00B7 Click to expand"
    };

    /// <summary>
    /// Resolves high-load CPU/RAM warning state for the detached SecondaryDroplet bubble.
    /// RAM >= 85% takes priority over CPU unless CPU is strictly higher; CPU >= 90% or any RAM alert is Critical (red).
    /// </summary>
    public static (bool IsActive, bool IsRamPriority, int Percent, bool IsCritical, string Label) ResolveHighLoadAlert(
        bool isHighCpu,
        int cpuPercent,
        bool isHighRam,
        int ramPercent)
    {
        if (!isHighCpu && !isHighRam)
            return (false, false, 0, false, string.Empty);

        bool isRamPriority = isHighRam && (!isHighCpu || ramPercent >= cpuPercent);
        int percent = isRamPriority ? ramPercent : cpuPercent;
        bool isCritical = isRamPriority || cpuPercent >= 90;
        string label = isRamPriority ? "RAM" : "CPU";
        return (true, isRamPriority, percent, isCritical, label);
    }
}
