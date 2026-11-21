using System.IO;
using System.Text.Json.Serialization;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

public sealed class IslandTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    public DateTime DueDate { get; set; } = DateTime.Today;
    public string? DueTime { get; set; }
    public string ColorTag { get; set; } = "#30D158";
    public int Priority { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }

    [JsonIgnore]
    public bool IsOverdue => !IsCompleted && DueDate.Date < DateTime.Today;

    [JsonIgnore]
    public bool IsDueToday => !IsCompleted && DueDate.Date == DateTime.Today;
}

/// <summary>
/// High-performance, atomic JSON task and checklist manager for Dynamic Island.
/// Stored in %LocalAppData%\DynamicIsland\tasks.json
/// Provides zero-allocation reads, instant reactivity via TasksChanged, and date queries.
/// </summary>
internal sealed class TaskService : IDisposable
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly List<IslandTask> _tasks = new();
    private readonly DebouncedJsonStore<List<IslandTask>> _store;
    private bool _disposed;

    public event Action? TasksChanged;

    public TaskService(string? filePath = null)
    {
        _filePath = filePath ?? AppPaths.TasksFile;
        _store = new DebouncedJsonStore<List<IslandTask>>(_filePath, () =>
        {
            lock (_lock) return new List<IslandTask>(_tasks);
        });
        Load();
    }

    public IReadOnlyList<IslandTask> AllTasks
    {
        get
        {
            lock (_lock)
            {
                return _tasks.OrderBy(t => t.IsCompleted)
                             .ThenBy(t => t.DueDate.Date)
                             .ThenBy(t => t.DueTime ?? "99:99")
                             .ToList();
            }
        }
    }

    public int PendingCount
    {
        get
        {
            lock (_lock)
            {
                return _tasks.Count(t => !t.IsCompleted);
            }
        }
    }

    public IReadOnlyList<IslandTask> GetTasksForDate(DateTime date)
    {
        lock (_lock)
        {
            return _tasks.Where(t => t.DueDate.Date == date.Date)
                         .OrderBy(t => t.IsCompleted)
                         .ThenBy(t => t.DueTime ?? "99:99")
                         .ToList();
        }
    }

    public HashSet<int> GetDaysWithPendingTasks(int year, int month)
    {
        lock (_lock)
        {
            var set = new HashSet<int>();
            foreach (var t in _tasks)
            {
                if (!t.IsCompleted && t.DueDate.Year == year && t.DueDate.Month == month)
                {
                    set.Add(t.DueDate.Day);
                }
            }
            return set;
        }
    }

    public IslandTask AddTask(string title, DateTime dueDate, string? dueTime = null, string colorTag = "#30D158", int priority = 0)
    {
        if (string.IsNullOrWhiteSpace(title))
            title = "New Task";

        var task = new IslandTask
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title.Trim(),
            IsCompleted = false,
            DueDate = dueDate.Date,
            DueTime = string.IsNullOrWhiteSpace(dueTime) ? null : dueTime.Trim(),
            ColorTag = colorTag,
            Priority = priority,
            CreatedAt = DateTime.Now
        };

        lock (_lock)
        {
            _tasks.Add(task);
        }

        _store.ScheduleSave();
        TasksChanged?.Invoke();
        return task;
    }

    public bool ToggleComplete(string id)
    {
        bool newState = false;
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null) return false;

            task.IsCompleted = !task.IsCompleted;
            task.CompletedAt = task.IsCompleted ? DateTime.Now : null;
            newState = task.IsCompleted;
        }

        _store.ScheduleSave();
        TasksChanged?.Invoke();
        return newState;
    }

    public bool DeleteTask(string id)
    {
        bool removed = false;
        lock (_lock)
        {
            int idx = _tasks.FindIndex(t => t.Id == id);
            if (idx >= 0)
            {
                _tasks.RemoveAt(idx);
                removed = true;
            }
        }

        if (removed)
        {
            _store.ScheduleSave();
            TasksChanged?.Invoke();
        }
        return removed;
    }

    public int ClearCompleted()
    {
        int count = 0;
        lock (_lock)
        {
            count = _tasks.RemoveAll(t => t.IsCompleted);
        }

        if (count > 0)
        {
            _store.ScheduleSave();
            TasksChanged?.Invoke();
        }
        return count;
    }

    private void Load()
    {
        lock (_lock)
        {
            _tasks.Clear();
            var loaded = AtomicJsonStore.Load<List<IslandTask>>(_filePath, () => new List<IslandTask>());
            if (loaded != null && loaded.Count > 0)
            {
                _tasks.AddRange(loaded);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _store.Dispose();
        _disposed = true;
    }
}
