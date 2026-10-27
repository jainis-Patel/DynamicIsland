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
        if (recording)
            return (true, CompactActivityKind.Recording);
        if (mediaPlaying)
            return (true, CompactActivityKind.Media);
        if (callActive)
            return (true, CompactActivityKind.Call);
        if (countdownRunning)
            return (true, CompactActivityKind.Countdown);
        if (downloading)
            return (true, CompactActivityKind.Download);
        if (stopwatchActive)
            return (true, CompactActivityKind.Stopwatch);

        return (false, CompactActivityKind.None);
    }

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
