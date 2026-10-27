using System.Diagnostics;
using System.IO;
using System.Windows;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

internal sealed record ShelfItem(string FullPath, string FileName, string FileSizeText, string Glyph);

/// <summary>
/// Manages the Drop Shelf ("Drop Notch") temporary holding tray for dragged files,
/// with crash-safe atomic JSON persistence across app restarts.
/// </summary>
internal sealed class DropShelfService
{
    private static readonly string ShelfFile = AppPaths.ShelfFile;
    private static readonly object SaveLock = new();

    private readonly List<ShelfItem> _items = new();

    public IReadOnlyList<ShelfItem> Items => _items;
    public int Count => _items.Count;
    public bool HasItems => _items.Count > 0;

    public event Action? Changed;

    public DropShelfService()
    {
        LoadPersistedItems();
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        bool modified = false;
        foreach (var path in paths)
        {
            if (_items.Any(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (TryCreateItem(path, out var item) && item != null)
            {
                _items.Insert(0, item);
                modified = true;
            }
        }

        if (modified)
        {
            SaveAsync();
            Changed?.Invoke();
        }
    }

    public void Remove(string path)
    {
        if (_items.RemoveAll(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            SaveAsync();
            Changed?.Invoke();
        }
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        SaveAsync();
        Changed?.Invoke();
    }

    private void LoadPersistedItems()
    {
        var paths = AtomicJsonStore.Load<string[]?>(ShelfFile, () => null);
        if (paths == null) return;

        foreach (var path in paths)
        {
            if (TryCreateItem(path, out var item) && item != null)
            {
                _items.Add(item);
            }
        }
    }

    private void SaveAsync()
    {
        string[] paths = _items.Select(i => i.FullPath).ToArray();
        _ = Task.Run(() => AtomicJsonStore.Save(ShelfFile, paths, SaveLock));
    }

    private static bool TryCreateItem(string path, out ShelfItem? item)
    {
        item = null;
        try
        {
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                string size = FormatFileSize(fi.Length);
                string glyph = GlyphForExtension(fi.Extension);
                item = new ShelfItem(path, fi.Name, size, glyph);
                return true;
            }
            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                item = new ShelfItem(path, di.Name, "Folder", "\uE8B7");
                return true;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[DropShelf] Failed to inspect '{path}': {ex.Message}");
        }
        return false;
    }

    public static void Reveal(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path))
                Process.Start("explorer.exe", $"\"{path}\"");
        }
        catch (Exception ex)
        {
            App.Log($"[DropShelf] Reveal error: {ex.Message}");
        }
    }

    public static void CopyPath(string path)
    {
        try
        {
            Clipboard.SetText(path);
        }
        catch (Exception ex)
        {
            App.Log($"[DropShelf] CopyPath error: {ex.Message}");
        }
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    private static string GlyphForExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" or ".ico" => "\uEB9F",
        ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" => "\uE714",
        ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a" => "\uE8D6",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "\uF012",
        ".pdf" => "\uEA90",
        ".cs" or ".js" or ".ts" or ".html" or ".css" or ".json" or ".py" or ".cpp" => "\uE943",
        ".txt" or ".md" or ".log" => "\uE8C4",
        _ => "\uE7C3"
    };
}
