using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland.Core;

public enum IslandState { Idle, Compact, Toast, Expanded, Hidden }

public sealed record ToastContent(
    string Glyph,
    string Title,
    string Subtitle,
    Color Accent,
    TimeSpan Duration,
    string? TargetView = null);

/// <summary>
/// Resolves the visible state from independent inputs, by priority:
///   Hidden (fullscreen app) &gt; Expanded (hover/click) &gt; Toast &gt; Compact (activity) &gt; Idle.
/// Hover is debounced (150 ms enter / 280 ms leave) so the cursor brushing past the top edge
/// doesn't make the island flicker.
/// </summary>
public sealed class IslandStateMachine
{
    public static readonly TimeSpan HoverEnterDelay = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan HoverLeaveDelay = TimeSpan.FromMilliseconds(280);

    private readonly DispatcherTimer _enterTimer;
    private readonly DispatcherTimer _leaveTimer;
    private readonly DispatcherTimer _toastTimer;

    private bool _pointerInside;
    private bool _hovered;
    private bool _activity;
    private bool _suppressed;
    private ToastContent? _toast;

    public IslandState State { get; private set; } = IslandState.Idle;
    public ToastContent? ActiveToast => _toast;

    /// <summary>(previous, next)</summary>
    public event Action<IslandState, IslandState>? StateChanged;

    /// <summary>(content, replacedAVisibleToast)</summary>
    public event Action<ToastContent, bool>? ToastChanged;

    public IslandStateMachine()
    {
        _enterTimer = new DispatcherTimer { Interval = HoverEnterDelay };
        _enterTimer.Tick += (_, _) =>
        {
            _enterTimer.Stop();
            if (_pointerInside) { _hovered = true; Evaluate(); }
        };

        _leaveTimer = new DispatcherTimer { Interval = HoverLeaveDelay };
        _leaveTimer.Tick += (_, _) =>
        {
            _leaveTimer.Stop();
            if (!_pointerInside) { _hovered = false; Evaluate(); }
        };

        _toastTimer = new DispatcherTimer();
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            _toast = null;
            Evaluate();
        };
    }

    public void SetPointerInside(bool inside)
    {
        if (inside == _pointerInside || _suppressed) return;
        _pointerInside = inside;

        if (inside)
        {
            _leaveTimer.Stop();
            if (!_hovered) _enterTimer.Start();
        }
        else
        {
            _enterTimer.Stop();
            if (_hovered) _leaveTimer.Start();
        }
    }

    /// <summary>Skip the hover debounce (e.g. the user clicked the pill).</summary>
    public void ExpandNow()
    {
        if (_suppressed) return;
        _enterTimer.Stop();
        _leaveTimer.Stop();
        _hovered = true;
        Evaluate();
    }

    /// <summary>Immediately collapse expanded island back to idle or compact state.</summary>
    public void CollapseNow()
    {
        _enterTimer.Stop();
        _leaveTimer.Stop();
        _hovered = false;
        Evaluate();
    }

    public void SetActivity(bool active)
    {
        if (_activity == active) return;
        _activity = active;
        Evaluate();
    }

    /// <summary>True while a fullscreen game / video / presentation is in the foreground.</summary>
    public void SetSuppressed(bool suppressed)
    {
        if (_suppressed == suppressed) return;
        _suppressed = suppressed;
        if (suppressed)
        {
            _enterTimer.Stop();
            _leaveTimer.Stop();
            _pointerInside = false;
            _hovered = false;
        }
        Evaluate();
    }

    public void PushToast(ToastContent toast)
    {
        bool replacing = _toast is not null && State == IslandState.Toast;
        _toast = toast;
        _toastTimer.Stop();
        _toastTimer.Interval = toast.Duration;
        _toastTimer.Start();
        ToastChanged?.Invoke(toast, replacing);
        Evaluate();
    }

    private void Evaluate()
    {
        var next =
            _suppressed ? IslandState.Hidden :
            _hovered ? IslandState.Expanded :
            _toast is not null ? IslandState.Toast :
            _activity ? IslandState.Compact :
            IslandState.Idle;

        if (next == State) return;
        var previous = State;
        State = next;
        App.Log($"[StateMachine] Transition: {previous} -> {next} (supp={_suppressed}, hov={_hovered}, toast={_toast != null}, act={_activity})");
        StateChanged?.Invoke(previous, next);
    }
}
