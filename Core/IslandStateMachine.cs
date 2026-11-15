using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Services;

namespace DynamicIsland.Core;

public enum IslandState { Idle, Compact, Toast, Expanded, Hidden }

public enum ToastPriority
{
    Low = 0,
    Feedback = 1,
    System = 2,
    Notification = 3,
    Critical = 4
}

public sealed record ToastContent(
    string Glyph,
    string Title,
    string Subtitle,
    Color Accent,
    TimeSpan Duration,
    string? TargetView = null,
    ToastPriority Priority = ToastPriority.System,
    string? Category = null,
    bool CanReply = false,
    NotificationHistoryItem? HistoryItem = null);

/// <summary>
/// Resolves the visible state from independent inputs, by priority:
///   Hidden (fullscreen app) &gt; Expanded (hover/click) &gt; Toast &gt; Compact (activity) &gt; Idle.
/// Hover is debounced (150 ms enter / 280 ms leave) so the cursor brushing past the top edge
/// doesn't make the island flicker.
/// Includes a priority-aware toast queue with category coalescing so low-priority feedback toasts
/// (e.g. volume steps) never clobber critical alerts or incoming notifications.
/// </summary>
public sealed class IslandStateMachine
{
    public static readonly TimeSpan HoverEnterDelay = TimeSpan.FromMilliseconds(110);
    public static readonly TimeSpan HoverLeaveDelay = TimeSpan.FromMilliseconds(260);
    public const int MaxQueuedToasts = 5;

    private readonly DispatcherTimer _enterTimer;
    private readonly DispatcherTimer _leaveTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly List<ToastContent> _toastQueue = new();

    private bool _pointerInside;
    private bool _hovered;
    private bool _pinned;
    private bool _activity;
    private bool _suppressed;
    private ToastContent? _toast;

    public bool ClickOnlyMode { get; set; }
    public TimeSpan CustomHoverEnterDelay { get; set; } = HoverEnterDelay;

    public IslandState State { get; private set; } = IslandState.Idle;
    public bool IsPinned => _pinned;
    public ToastContent? ActiveToast => _toast;
    public int QueuedToastCount => _toastQueue.Count;

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
            if (!_pointerInside && !_pinned) { _hovered = false; Evaluate(); }
        };

        _toastTimer = new DispatcherTimer();
        _toastTimer.Tick += (_, _) => AdvanceToast();
    }

    public void SetPointerInside(bool inside)
    {
        if (inside == _pointerInside || _suppressed) return;
        _pointerInside = inside;

        if (inside)
        {
            _leaveTimer.Stop();
            if (!_hovered && !ClickOnlyMode)
            {
                if (_enterTimer.Interval != CustomHoverEnterDelay)
                {
                    _enterTimer.Interval = CustomHoverEnterDelay;
                }
                _enterTimer.Start();
            }
        }
        else
        {
            _enterTimer.Stop();
            if (_hovered && !_pinned) _leaveTimer.Start();
        }
    }

    /// <summary>Locks or unlocks the expanded state so mouse-leave does not auto-collapse the island.</summary>
    public void SetPinned(bool pinned)
    {
        if (_pinned == pinned) return;
        _pinned = pinned;
        if (_pinned)
        {
            _leaveTimer.Stop();
            if (!_suppressed) _hovered = true;
        }
        else if (!_pointerInside && _hovered)
        {
            _leaveTimer.Start();
        }
        Evaluate();
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
        _pinned = false;
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
        if (_toast is null)
        {
            DisplayToastImmediate(toast, replacing: false);
            return;
        }

        // 1. Same category coalescing (e.g., rapid volume scroll steps update the active volume toast in-place)
        if (!string.IsNullOrEmpty(toast.Category) &&
            string.Equals(toast.Category, _toast.Category, StringComparison.OrdinalIgnoreCase))
        {
            DisplayToastImmediate(toast, replacing: State == IslandState.Toast);
            return;
        }

        // 2. Incoming toast has equal or higher priority than the active toast
        if (toast.Priority >= _toast.Priority)
        {
            // If a Critical alert preempts an important Notification, re-queue the notification so it isn't lost
            if (toast.Priority > _toast.Priority && _toast.Priority >= ToastPriority.Notification)
            {
                EnqueueToast(_toast);
            }

            DisplayToastImmediate(toast, replacing: State == IslandState.Toast);
            return;
        }

        // 3. Incoming toast has lower priority than the currently visible toast:
        //    Queue it if it is System priority or higher; coalesce in queue if same category.
        if (toast.Priority >= ToastPriority.System)
        {
            EnqueueToast(toast);
        }
    }

    /// <summary>
    /// Advances to the next queued toast when the current toast timer expires, or clears the active toast.
    /// </summary>
    public void AdvanceToast()
    {
        _toastTimer.Stop();
        if (_toastQueue.Count > 0)
        {
            var next = _toastQueue[0];
            _toastQueue.RemoveAt(0);
            DisplayToastImmediate(next, replacing: State == IslandState.Toast);
        }
        else
        {
            _toast = null;
            Evaluate();
        }
    }

    private void EnqueueToast(ToastContent toast)
    {
        if (!string.IsNullOrEmpty(toast.Category))
        {
            int existingIdx = _toastQueue.FindIndex(t =>
                string.Equals(t.Category, toast.Category, StringComparison.OrdinalIgnoreCase));
            if (existingIdx >= 0)
            {
                _toastQueue[existingIdx] = toast;
                return;
            }
        }

        // Insert ordered by Priority descending (preserving FIFO within the same priority)
        int insertIndex = _toastQueue.FindIndex(t => t.Priority < toast.Priority);
        if (insertIndex >= 0)
            _toastQueue.Insert(insertIndex, toast);
        else
            _toastQueue.Add(toast);

        while (_toastQueue.Count > MaxQueuedToasts)
        {
            _toastQueue.RemoveAt(_toastQueue.Count - 1);
        }
    }

    private void DisplayToastImmediate(ToastContent toast, bool replacing)
    {
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
