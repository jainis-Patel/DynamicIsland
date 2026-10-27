using System.Diagnostics;

namespace DynamicIsland.Services;

/// <summary>
/// Provides a countdown timer and 25-minute Pomodoro preset alongside the existing stopwatch.
/// Supports deterministic time advancement for unit testing as well as wall-clock tracking.
/// </summary>
public sealed class CountdownTimerService : IDisposable
{
    public static readonly TimeSpan PomodoroWorkDuration = TimeSpan.FromMinutes(25);
    public static readonly TimeSpan ShortBreakDuration = TimeSpan.FromMinutes(5);

    private readonly Stopwatch _elapsed = new();
    private TimeSpan _targetDuration;
    private TimeSpan _accumulatedBeforePause;
    private bool _running;

    public bool IsRunning => _running;
    public bool IsActive => _running || (_targetDuration > TimeSpan.Zero && Remaining > TimeSpan.Zero && Remaining < _targetDuration);
    public TimeSpan TargetDuration => _targetDuration;
    public string Label { get; private set; } = "Timer";

    public TimeSpan Remaining
    {
        get
        {
            if (_targetDuration <= TimeSpan.Zero) return TimeSpan.Zero;
            var totalElapsed = _accumulatedBeforePause + (_running ? _elapsed.Elapsed : TimeSpan.Zero);
            var rem = _targetDuration - totalElapsed;
            return rem > TimeSpan.Zero ? rem : TimeSpan.Zero;
        }
    }

    public event Action? StateChanged;
    public event Action<string>? Completed;

    public void StartCountdown(TimeSpan duration, string label = "Timer")
    {
        if (duration <= TimeSpan.Zero) return;
        _targetDuration = duration;
        _accumulatedBeforePause = TimeSpan.Zero;
        Label = label;
        _elapsed.Restart();
        _running = true;
        StateChanged?.Invoke();
    }

    public void StartPomodoro() => StartCountdown(PomodoroWorkDuration, "Pomodoro 25m");

    public void TogglePause()
    {
        if (_targetDuration <= TimeSpan.Zero)
        {
            StartPomodoro();
            return;
        }

        if (_running)
        {
            _accumulatedBeforePause += _elapsed.Elapsed;
            _elapsed.Reset();
            _running = false;
        }
        else if (Remaining > TimeSpan.Zero)
        {
            _elapsed.Restart();
            _running = true;
        }
        else
        {
            // Restart same duration if already at zero
            _accumulatedBeforePause = TimeSpan.Zero;
            _elapsed.Restart();
            _running = true;
        }

        StateChanged?.Invoke();
    }

    public void Reset()
    {
        _elapsed.Reset();
        _accumulatedBeforePause = TimeSpan.Zero;
        _targetDuration = TimeSpan.Zero;
        _running = false;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Evaluates whether the countdown has reached zero and fires <see cref="Completed"/> once.
    /// Called on each UI clock tick.
    /// </summary>
    public bool PollCompletion()
    {
        if (!_running) return false;
        if (Remaining <= TimeSpan.Zero)
        {
            _running = false;
            _elapsed.Reset();
            _accumulatedBeforePause = _targetDuration;
            string finishedLabel = Label;
            StateChanged?.Invoke();
            Completed?.Invoke(finishedLabel);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Advances the timer deterministically by <paramref name="delta"/> (used by unit tests).
    /// </summary>
    internal void AdvanceSimulated(TimeSpan delta)
    {
        if (!_running || delta <= TimeSpan.Zero) return;
        _accumulatedBeforePause += delta;
        PollCompletion();
    }

    public string FormatRemaining()
    {
        var rem = Remaining;
        if (rem <= TimeSpan.Zero && _targetDuration <= TimeSpan.Zero) return "25:00";
        // Ceiling to whole seconds while running so 00:00 only shows on completion
        var display = _running && rem > TimeSpan.Zero
            ? TimeSpan.FromSeconds(Math.Ceiling(rem.TotalSeconds))
            : rem;
        return display.TotalHours >= 1
            ? $"{(int)display.TotalHours}:{display.Minutes:D2}:{display.Seconds:D2}"
            : $"{display.Minutes:D2}:{display.Seconds:D2}";
    }

    public void Dispose()
    {
        _elapsed.Stop();
        _running = false;
    }
}
