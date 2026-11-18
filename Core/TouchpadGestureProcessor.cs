using System;
using DynamicIsland.Interop;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Core;

/// <summary>
/// Categorizes high-level user actions deciphered from raw precision touchpad and multi-touch gestures.
/// </summary>
public enum GestureActionKind
{
    None,
    /// <summary>Two-finger smooth horizontal drag scrub of active media timeline.</summary>
    Scrub,
    /// <summary>Two-finger high-velocity horizontal flick to skip to the next track.</summary>
    NextTrack,
    /// <summary>Two-finger high-velocity horizontal flick to skip to the previous track.</summary>
    PreviousTrack,
    /// <summary>Two-finger vertical pull downward or pinch-out to fluidly expand the island.</summary>
    ExpandPull,
    /// <summary>Two-finger vertical push upward or pinch-in to fluidly collapse the island.</summary>
    CollapsePinch,
    /// <summary>Standard content scroll within expanded views or the bottom dock carousel.</summary>
    ScrollContent
}

/// <summary>
/// Zero-allocation result struct emitted by <see cref="TouchpadGestureProcessor"/>.
/// </summary>
public readonly struct GestureResult
{
    public static readonly GestureResult None = new(GestureActionKind.None);

    public GestureActionKind Kind { get; }
    public TimeSpan ScrubOffset { get; }
    public double Velocity { get; }
    public double Delta { get; }

    public GestureResult(GestureActionKind kind, TimeSpan scrubOffset = default, double velocity = 0, double delta = 0)
    {
        Kind = kind;
        ScrubOffset = scrubOffset;
        Velocity = velocity;
        Delta = delta;
    }
}

/// <summary>
/// Sub-millisecond, zero-allocation gesture decoder for Windows Precision Touchpads (PTP)
/// and multi-touch touchscreens (Surface, XPS, ThinkPad).
/// Intercepts high-resolution WM_MOUSEHWHEEL, WM_MOUSEWHEEL, and WM_GESTURE packets,
/// performing low-pass velocity filtering, flick trajectory detection, scrub quantization,
/// and pinch-to-morph gesture recognition.
/// </summary>
public sealed class TouchpadGestureProcessor
{
    // ── Tunable Thresholds (configurable for testing) ─────────────────
    public double FlickVelocityThreshold { get; set; } = 400.0;
    public double FlickAccumulatorThreshold { get; set; } = 70.0;
    public double ScrubDeltaThreshold { get; set; } = 35.0;
    public double VerticalPullThreshold { get; set; } = 90.0;
    public long FlickCooldownMs { get; set; } = 450;
    public long ExpandCooldownMs { get; set; } = 450;
    public double ZoomExpandRatio { get; set; } = 1.25;
    public double ZoomCollapseRatio { get; set; } = 0.78;

    // ── Horizontal Tracking State ─────────────────────────────────────
    private long _lastHorizontalTicks;
    private double _horizontalAccumulator;
    private double _horizontalVelocity;
    private long _lastFlickTicks;

    // ── Vertical Tracking State ───────────────────────────────────────
    private long _lastVerticalTicks;
    private double _verticalAccumulator;
    private double _verticalVelocity;
    private long _lastExpandCooldownTicks;

    // ── Multi-Touch Win32 State ───────────────────────────────────────
    private ulong _initialZoomDistance;
    private int _panStartX;
    private int _panStartY;

    /// <summary>
    /// Decodes high-resolution horizontal scroll / tilt messages (WM_MOUSEHWHEEL).
    /// Dispatches intentional flicks (Next/Prev track), smooth timeline scrubbing,
    /// or content carousel scrolling.
    /// </summary>
    public GestureResult ProcessHorizontalScroll(int delta, long nowTicks, bool isMediaActive, bool canSeek, bool isExpanded)
    {
        if (delta == 0) return GestureResult.None;

        if (isExpanded)
        {
            return new GestureResult(GestureActionKind.ScrollContent, delta: delta);
        }

        long dt = nowTicks - _lastHorizontalTicks;
        if (dt > 250)
        {
            // User paused or lifted fingers: start fresh gesture window
            _horizontalAccumulator = 0;
            _horizontalVelocity = 0;
        }
        _lastHorizontalTicks = nowTicks;

        double dtSeconds = Math.Max(0.008, dt / 1000.0);
        double instantVelocity = delta / dtSeconds;
        _horizontalVelocity = _horizontalVelocity * 0.6 + instantVelocity * 0.4;
        _horizontalAccumulator += delta;

        // 1. High-Velocity Horizontal Flick Skip (Debounced)
        if (nowTicks - _lastFlickTicks >= FlickCooldownMs)
        {
            if (Math.Abs(_horizontalVelocity) >= FlickVelocityThreshold &&
                Math.Abs(_horizontalAccumulator) >= FlickAccumulatorThreshold)
            {
                _lastFlickTicks = nowTicks;
                double flickDir = _horizontalVelocity > 0 ? 1 : -1;
                double v = Math.Abs(_horizontalVelocity);
                _horizontalAccumulator = 0;
                _horizontalVelocity = 0;

                return flickDir > 0
                    ? new GestureResult(GestureActionKind.NextTrack, velocity: v, delta: delta)
                    : new GestureResult(GestureActionKind.PreviousTrack, velocity: v, delta: delta);
            }
        }

        // 2. Smooth Two-Finger Media Scrubbing (only active below flick velocity threshold)
        if (isMediaActive && canSeek && Math.Abs(_horizontalVelocity) < FlickVelocityThreshold && nowTicks - _lastFlickTicks >= 200)
        {
            if (Math.Abs(_horizontalAccumulator) >= ScrubDeltaThreshold)
            {
                int steps = (int)(_horizontalAccumulator / ScrubDeltaThreshold);
                _horizontalAccumulator -= steps * ScrubDeltaThreshold;

                // Non-linear scrub curve: gentle pans step 2s; larger sweeps step progressively faster
                double scrubSeconds = steps * 2.0;
                return new GestureResult(
                    GestureActionKind.Scrub,
                    scrubOffset: TimeSpan.FromSeconds(scrubSeconds),
                    velocity: Math.Abs(_horizontalVelocity),
                    delta: delta);
            }
        }

        return GestureResult.None;
    }

    /// <summary>
    /// Decodes high-resolution vertical scroll messages (WM_MOUSEWHEEL).
    /// Dispatches fluid pull-to-expand or push-to-collapse morphing.
    /// </summary>
    public GestureResult ProcessVerticalScroll(int delta, long nowTicks, IslandState currentState)
    {
        if (delta == 0) return GestureResult.None;

        long dt = nowTicks - _lastVerticalTicks;
        if (dt > 250)
        {
            _verticalAccumulator = 0;
            _verticalVelocity = 0;
        }
        _lastVerticalTicks = nowTicks;

        double dtSeconds = Math.Max(0.008, dt / 1000.0);
        double instantVelocity = delta / dtSeconds;
        _verticalVelocity = _verticalVelocity * 0.6 + instantVelocity * 0.4;
        _verticalAccumulator += delta;

        // Downward pull in compact/idle state (WM_MOUSEWHEEL delta is negative for downward scroll)
        if (currentState != IslandState.Expanded)
        {
            if (_verticalAccumulator <= -VerticalPullThreshold || _verticalVelocity <= -350.0)
            {
                if (nowTicks - _lastExpandCooldownTicks >= ExpandCooldownMs)
                {
                    _lastExpandCooldownTicks = nowTicks;
                    double v = Math.Abs(_verticalVelocity);
                    _verticalAccumulator = 0;
                    _verticalVelocity = 0;
                    return new GestureResult(GestureActionKind.ExpandPull, velocity: v, delta: delta);
                }
            }
        }
        // Upward push in expanded state (WM_MOUSEWHEEL delta is positive for upward scroll)
        else
        {
            if (_verticalAccumulator >= VerticalPullThreshold || _verticalVelocity >= 350.0)
            {
                if (nowTicks - _lastExpandCooldownTicks >= ExpandCooldownMs)
                {
                    _lastExpandCooldownTicks = nowTicks;
                    double v = Math.Abs(_verticalVelocity);
                    _verticalAccumulator = 0;
                    _verticalVelocity = 0;
                    return new GestureResult(GestureActionKind.CollapsePinch, velocity: v, delta: delta);
                }
            }
        }

        return GestureResult.None;
    }

    /// <summary>
    /// Decodes native Windows Multi-Touch gestures (WM_GESTURE: GID_ZOOM, GID_PAN).
    /// </summary>
    internal GestureResult ProcessWin32Gesture(ref GESTUREINFO gi, IslandState currentState, bool isMediaActive, bool canSeek)
    {
        switch (gi.dwID)
        {
            case GID_ZOOM:
            {
                if ((gi.dwFlags & GF_BEGIN) != 0)
                {
                    _initialZoomDistance = gi.ullArguments;
                    return GestureResult.None;
                }

                if (_initialZoomDistance > 0)
                {
                    double ratio = (double)gi.ullArguments / _initialZoomDistance;

                    // Pinch-out / Zoom-in -> Expand
                    if (ratio >= ZoomExpandRatio && currentState != IslandState.Expanded)
                    {
                        _initialZoomDistance = gi.ullArguments;
                        return new GestureResult(GestureActionKind.ExpandPull, velocity: (ratio - 1.0) * 500.0);
                    }
                    // Pinch-in / Zoom-out -> Collapse
                    else if (ratio <= ZoomCollapseRatio && currentState == IslandState.Expanded)
                    {
                        _initialZoomDistance = gi.ullArguments;
                        return new GestureResult(GestureActionKind.CollapsePinch, velocity: (1.0 - ratio) * 500.0);
                    }
                }
                break;
            }

            case GID_PAN:
            {
                if ((gi.dwFlags & GF_BEGIN) != 0)
                {
                    _panStartX = gi.ptsLocationX;
                    _panStartY = gi.ptsLocationY;
                    return GestureResult.None;
                }

                int dx = gi.ptsLocationX - _panStartX;
                int dy = gi.ptsLocationY - _panStartY;

                // Vertical dominant drag
                if (Math.Abs(dy) >= 40 && Math.Abs(dy) > Math.Abs(dx) * 1.25)
                {
                    if (dy > 0 && currentState != IslandState.Expanded)
                    {
                        _panStartY = gi.ptsLocationY;
                        return new GestureResult(GestureActionKind.ExpandPull, velocity: dy * 4.0);
                    }
                    else if (dy < 0 && currentState == IslandState.Expanded)
                    {
                        _panStartY = gi.ptsLocationY;
                        return new GestureResult(GestureActionKind.CollapsePinch, velocity: Math.Abs(dy) * 4.0);
                    }
                }
                // Horizontal dominant drag across compact media pill
                else if (Math.Abs(dx) >= 30 && currentState != IslandState.Expanded && isMediaActive)
                {
                    _panStartX = gi.ptsLocationX;
                    if (canSeek)
                    {
                        double seconds = Math.Sign(dx) * Math.Max(2.0, Math.Round(Math.Abs(dx) / 15.0) * 2.0);
                        return new GestureResult(GestureActionKind.Scrub, scrubOffset: TimeSpan.FromSeconds(seconds));
                    }
                    else
                    {
                        return dx > 0
                            ? new GestureResult(GestureActionKind.NextTrack)
                            : new GestureResult(GestureActionKind.PreviousTrack);
                    }
                }
                break;
            }
        }

        return GestureResult.None;
    }

    /// <summary>
    /// Resets all internal accumulators and timing state.
    /// </summary>
    public void Reset()
    {
        _lastHorizontalTicks = 0;
        _horizontalAccumulator = 0;
        _horizontalVelocity = 0;
        _lastFlickTicks = 0;

        _lastVerticalTicks = 0;
        _verticalAccumulator = 0;
        _verticalVelocity = 0;
        _lastExpandCooldownTicks = 0;

        _initialZoomDistance = 0;
        _panStartX = 0;
        _panStartY = 0;
    }
}
