using System.Diagnostics;
using System.IO;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

public sealed class LauncherItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public string? Arguments { get; set; }
    public string Glyph { get; set; } = "\uE71D"; // App icon
    public bool IsFolder { get; set; }
    public int LaunchCount { get; set; }
}

/// <summary>
/// Quick App & Folder Launcher with file drop-to-open capabilities.
/// Users can launch pinned tools or drag files onto an icon to open them in that specific app.
/// Stored in %LocalAppData%\DynamicIsland\launcher.json
/// </summary>
internal sealed class QuickLauncherService : IDisposable
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly List<LauncherItem> _items = new();
    private bool _disposed;

    public event Action? Changed;

    public IReadOnlyList<LauncherItem> Items
    {
        get
        {
            lock (_lock) return _items.ToList();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock) return _items.Count;
        }
    }

    public QuickLauncherService(string? customFilePath = null)
    {
        _filePath = customFilePath ?? AppPaths.LauncherFile;
        Load();
    }

    private void Load()
    {
        lock (_lock)
        {
            try
            {
                var loaded = AtomicJsonStore.Load<List<LauncherItem>>(_filePath, () => new List<LauncherItem>());
                _items.Clear();
                if (loaded != null && loaded.Count > 0)
                {
                    _items.AddRange(loaded);
                }
                else
                {
                    PopulateDefaultItems();
                }
            }
            catch (Exception ex)
            {
                App.Log($"[QuickLauncher] Load error: {ex.Message}");
                PopulateDefaultItems();
            }
        }
    }

    private void PopulateDefaultItems()
    {
        _items.Add(new LauncherItem
        {
            Name = "File Explorer",
            TargetPath = "explorer.exe",
            Glyph = "\uEC50", // Folder
            IsFolder = true
        });

        _items.Add(new LauncherItem
        {
            Name = "Terminal",
            TargetPath = "wt.exe",
            Glyph = "\uE756", // Terminal prompt
            IsFolder = false
        });

        _items.Add(new LauncherItem
        {
            Name = "Notepad",
            TargetPath = "notepad.exe",
            Glyph = "\uE70B", // Notepad
            IsFolder = false
        });

        _items.Add(new LauncherItem
        {
            Name = "Task Manager",
            TargetPath = "taskmgr.exe",
            Glyph = "\uE9D9", // System vitals
            IsFolder = false
        });
    }

    public void SaveNow()
    {
        if (_disposed) return;
        List<LauncherItem> snapshot;
        lock (_lock)
        {
            snapshot = _items.ToList();
        }

        try
        {
            AtomicJsonStore.Save(_filePath, snapshot, _lock);
        }
        catch (Exception ex)
        {
            App.Log($"[QuickLauncher] SaveNow error: {ex.Message}");
        }
    }

    public LauncherItem AddTarget(string targetPath, string? name = null, string? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        bool isDir = Directory.Exists(targetPath);
        string resolvedName = !string.IsNullOrWhiteSpace(name)
            ? name.Trim()
            : (isDir ? Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                     : Path.GetFileNameWithoutExtension(targetPath));

        if (string.IsNullOrWhiteSpace(resolvedName))
        {
            resolvedName = targetPath;
        }

        string glyph = isDir ? "\uEC50" : (targetPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || targetPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || targetPath.Contains("wt") ? "\uE756" : "\uE71D");

        var item = new LauncherItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = resolvedName,
            TargetPath = targetPath,
            Arguments = arguments,
            Glyph = glyph,
            IsFolder = isDir
        };

        lock (_lock)
        {
            _items.Add(item);
        }

        SaveNow();
        Changed?.Invoke();
        return item;
    }

    public bool Remove(string id)
    {
        bool removed;
        lock (_lock)
        {
            removed = _items.RemoveAll(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        if (removed)
        {
            SaveNow();
            Changed?.Invoke();
        }
        return removed;
    }

    public bool Launch(string id, IEnumerable<string>? droppedFiles = null)
    {
        LauncherItem? item;
        lock (_lock)
        {
            item = _items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                item.LaunchCount++;
            }
        }

        if (item == null) return false;

        SaveNow();
        Changed?.Invoke();

        string fullArgs = BuildLaunchArguments(item.Arguments, droppedFiles);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = item.TargetPath,
                Arguments = fullArgs,
                UseShellExecute = true
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[QuickLauncher] Launch failed for '{item.Name}': {ex.Message}");
            return false;
        }
    }

    public static string BuildLaunchArguments(string? baseArgs, IEnumerable<string>? droppedFiles)
    {
        var fileList = droppedFiles?.Where(f => !string.IsNullOrWhiteSpace(f)).ToList() ?? new List<string>();
        if (fileList.Count == 0)
        {
            return baseArgs ?? "";
        }

        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(baseArgs))
        {
            sb.Append(baseArgs.Trim()).Append(' ');
        }

        for (int i = 0; i < fileList.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append('"').Append(fileList[i].Trim()).Append('"');
        }

        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SaveNow();
    }
}
