using System.Windows.Media;

namespace DynamicIsland.Core;

public sealed record LiveActivityAction(
    string Label,
    string Glyph,
    Action Callback);

public sealed record LiveActivityItem(
    string Id = "",
    string Source = "",
    string Title = "",
    string Subtitle = "",
    string Glyph = "",
    Color AccentColor = default,
    double? Progress = null,
    int Priority = 0,
    string? TargetViewKey = null,
    IReadOnlyList<LiveActivityAction>? Actions = null,
    string? Diff = null,
    DateTimeOffset? ExpiresAt = null)
{
    public string Category { get => Source; init => Source = value; }
    public string? TargetView { get => TargetViewKey; init => TargetViewKey = value; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool IsIndeterminate => !Progress.HasValue;
    public double ClampedProgress => Progress.HasValue ? Math.Clamp(Progress.Value, 0.0, 1.0) : 0.0;
    public string ProgressPercentText => Progress.HasValue ? $"{(int)Math.Round(ClampedProgress * 100)}%" : "";
    public bool IsInteractivePrompt => Actions != null && Actions.Count > 0;
}

/// <summary>
/// Priority-ordered hub for Live Activities (file conversions, caffeine countdowns, downloads, timers, and external tools).
/// Zero-idle-CPU: purely event-driven state store with no background polling loops.
/// </summary>
public sealed class LiveActivityHub
{
    private readonly List<LiveActivityItem> _activities = new();
    private readonly object _lock = new();
    private int _selectedIndex;

    public event Action? Changed;

    private void PruneExpiredLocked()
    {
        var now = DateTimeOffset.UtcNow;
        _activities.RemoveAll(a => a.ExpiresAt.HasValue && a.ExpiresAt.Value <= now);
        if (_activities.Count == 0)
        {
            _selectedIndex = 0;
        }
        else if (_selectedIndex >= _activities.Count)
        {
            _selectedIndex = _activities.Count - 1;
        }
    }

    public IReadOnlyList<LiveActivityItem> Activities
    {
        get
        {
            lock (_lock)
            {
                PruneExpiredLocked();
                return _activities.ToList();
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                PruneExpiredLocked();
                return _activities.Count;
            }
        }
    }

    public bool HasActivities
    {
        get
        {
            lock (_lock)
            {
                PruneExpiredLocked();
                return _activities.Count > 0;
            }
        }
    }

    public LiveActivityItem? Primary
    {
        get
        {
            lock (_lock)
            {
                PruneExpiredLocked();
                if (_activities.Count == 0) return null;
                int idx = Math.Clamp(_selectedIndex, 0, _activities.Count - 1);
                return _activities[idx];
            }
        }
    }

    public LiveActivityItem? Secondary
    {
        get
        {
            lock (_lock)
            {
                PruneExpiredLocked();
                if (_activities.Count <= 1) return null;
                int secondIdx = (_selectedIndex == 0) ? 1 : 0;
                return _activities[secondIdx];
            }
        }
    }

    public void PublishOrUpdate(LiveActivityItem item) => Publish(item);

    public bool UpdateProgress(string id, double? progress, string? subtitle = null)
    {
        lock (_lock)
        {
            int idx = _activities.FindIndex(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return false;
            var cur = _activities[idx];
            _activities[idx] = cur with
            {
                Progress = progress,
                Subtitle = subtitle ?? cur.Subtitle,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
        Changed?.Invoke();
        return true;
    }

    public void Publish(LiveActivityItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_lock)
        {
            PruneExpiredLocked();
            int existingIdx = _activities.FindIndex(a => string.Equals(a.Id, item.Id, StringComparison.OrdinalIgnoreCase));
            if (existingIdx >= 0)
            {
                _activities[existingIdx] = item;
            }
            else
            {
                _activities.Add(item);
            }

            _activities.Sort((a, b) =>
            {
                int cmp = b.Priority.CompareTo(a.Priority);
                return cmp != 0 ? cmp : b.UpdatedAt.CompareTo(a.UpdatedAt);
            });

            if (_selectedIndex >= _activities.Count)
            {
                _selectedIndex = 0;
            }
        }
        Changed?.Invoke();
    }

    public bool Complete(string id)
    {
        bool removed;
        lock (_lock)
        {
            removed = _activities.RemoveAll(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (_selectedIndex >= _activities.Count)
            {
                _selectedIndex = Math.Max(0, _activities.Count - 1);
            }
        }
        if (removed)
        {
            Changed?.Invoke();
        }
        return removed;
    }

    public void CycleNext()
    {
        lock (_lock)
        {
            if (_activities.Count <= 1) return;
            _selectedIndex = (_selectedIndex + 1) % _activities.Count;
        }
        Changed?.Invoke();
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            if (_activities.Count == 0) return;
            _activities.Clear();
            _selectedIndex = 0;
        }
        Changed?.Invoke();
    }
}
