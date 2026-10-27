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
    Stopwatch
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
        bool stopwatchActive)
    {
        Span<CompactActivityKind> active = stackalloc CompactActivityKind[6];
        int count = 0;

        if (recording) active[count++] = CompactActivityKind.Recording;
        if (mediaPlaying) active[count++] = CompactActivityKind.Media;
        if (callActive) active[count++] = CompactActivityKind.Call;
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
        _ => "\uE7E7"
    };

    /// <summary>
    /// Determines which <see cref="ExpandedView"/> should open when the user clicks the Compact pill.
    /// </summary>
    public static ExpandedView ResolveCompactTargetView(CompactActivityKind kind) => kind switch
    {
        CompactActivityKind.Recording => ExpandedView.VoiceMemo,
        CompactActivityKind.Download => ExpandedView.Shelf,
        _ => ExpandedView.Media
    };
}
