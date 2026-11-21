using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

internal sealed record ShelfItem(
    string FullPath,
    string FileName,
    string FileSizeText,
    string Glyph,
    bool IsImage,
    string ExtensionTag,
    Brush BadgeColorBrush,
    ImageSource? Thumbnail = null,
    bool IsPinned = false)
{
    public bool HasThumbnail => Thumbnail != null;
    public Visibility ThumbnailVisibility => Thumbnail != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GlyphVisibility => Thumbnail == null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OcrVisibility => IsImage ? Visibility.Visible : Visibility.Collapsed;
    public string PinGlyph => IsPinned ? "\uE840" : "\uE718";
    public Brush PinForeground => IsPinned ? ThemeBrushes.Amber : ThemeBrushes.SecondaryText;

    public string DirectoryName
    {
        get
        {
            try
            {
                string? trimmed = FullPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string? parent = Path.GetDirectoryName(trimmed);
                string? name = Path.GetFileName(parent);
                return !string.IsNullOrWhiteSpace(name) ? name : (Path.GetPathRoot(FullPath) ?? "Local");
            }
            catch
            {
                return "Local";
            }
        }
    }
}

/// <summary>
/// Manages the Drop Shelf ("Drop Notch") temporary holding tray for dragged files,
/// with crash-safe atomic JSON persistence across app restarts.
/// </summary>
internal sealed class DropShelfService : IDisposable
{
    private readonly string _shelfFile;
    private readonly object _itemsLock = new();
    private readonly DebouncedJsonStore<string[]> _store;
    private bool _disposed;

    private static readonly Brush ImgBadgeBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0xFF, 0xAF, 0x52, 0xDE));
    private static readonly Brush DocBadgeBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0xFF, 0xFF, 0x3B, 0x30));
    private static readonly Brush ZipBadgeBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0xFF, 0x30, 0xD1, 0x58));
    private static readonly Brush VideoBadgeBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0xFF, 0xBF, 0x5A, 0xF2));
    private static readonly Brush AudioBadgeBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0xFF, 0xFF, 0x9F, 0x0A));
    private static readonly Brush CodeBadgeBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0xFF, 0x00, 0xF0, 0xFF));
    private static readonly Brush FolderBadgeBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0xFF, 0x0A, 0x84, 0xFF));
    private static readonly Brush DefaultBadgeBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0xFF, 0x86, 0x86, 0x8B));

    private readonly List<ShelfItem> _items = new();
    private readonly HashSet<string> _pinnedPaths = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ShelfItem> Items
    {
        get { lock (_itemsLock) return _items.ToList(); }
    }

    public int Count
    {
        get { lock (_itemsLock) return _items.Count; }
    }

    public bool HasItems
    {
        get { lock (_itemsLock) return _items.Count > 0; }
    }

    public long TotalSizeBytes
    {
        get
        {
            ShelfItem[] snapshot;
            lock (_itemsLock) snapshot = _items.ToArray();
            long total = 0;
            foreach (var item in snapshot)
            {
                try
                {
                    if (File.Exists(item.FullPath))
                    {
                        total += new FileInfo(item.FullPath).Length;
                    }
                }
                catch { }
            }
            return total;
        }
    }

    public string TotalSizeFormatted => FormatFileSize(TotalSizeBytes);

    public event Action? Changed;

    public DropShelfService(string? customFilePath = null)
    {
        _shelfFile = string.IsNullOrWhiteSpace(customFilePath) ? AppPaths.ShelfFile : customFilePath;
        _store = new DebouncedJsonStore<string[]>(_shelfFile, BuildPathSnapshot);
        LoadPersistedItems();
    }

    private string[] BuildPathSnapshot()
    {
        lock (_itemsLock)
        {
            return _items.Select(i => i.IsPinned ? $"pin|{i.FullPath}" : i.FullPath).ToArray();
        }
    }

    public int AddRange(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        AddPaths(list);
        return list.Count;
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        bool modified = false;
        lock (_itemsLock)
        {
            foreach (var path in paths)
            {
                if (_items.Any(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (TryCreateItem(path, _pinnedPaths.Contains(path), out var item) && item != null)
                {
                    int insertIdx = item.IsPinned ? 0 : _items.TakeWhile(i => i.IsPinned).Count();
                    _items.Insert(insertIdx, item);
                    modified = true;
                }
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
        bool removed;
        lock (_itemsLock)
        {
            removed = _items.RemoveAll(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        if (removed)
        {
            SaveAsync();
            Changed?.Invoke();
        }
    }

    public void Clear()
    {
        lock (_itemsLock)
        {
            if (_items.Count == 0) return;
            int removed = _items.RemoveAll(i => !i.IsPinned);
            if (removed == 0 && _items.Count > 0)
            {
                // If only pinned items remain and user clicks Clear again, clear all
                _items.Clear();
                _pinnedPaths.Clear();
            }
        }
        SaveAsync();
        Changed?.Invoke();
    }

    public bool TogglePin(string path)
    {
        bool nextPinned;
        lock (_itemsLock)
        {
            int idx = _items.FindIndex(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return false;

            var cur = _items[idx];
            nextPinned = !cur.IsPinned;
            if (nextPinned) _pinnedPaths.Add(path);
            else _pinnedPaths.Remove(path);

            _items[idx] = cur with { IsPinned = nextPinned };
            var sorted = _items.OrderByDescending(i => i.IsPinned).ToList();
            _items.Clear();
            _items.AddRange(sorted);
        }

        SaveAsync();
        Changed?.Invoke();
        return nextPinned;
    }

    private void LoadPersistedItems()
    {
        var paths = AtomicJsonStore.Load<string[]?>(_shelfFile, () => null);
        if (paths == null) return;

        lock (_itemsLock)
        {
            foreach (var raw in paths)
            {
                bool pinned = raw.StartsWith("pin|", StringComparison.OrdinalIgnoreCase);
                string path = pinned ? raw[4..] : raw;
                if (pinned) _pinnedPaths.Add(path);

                if (TryCreateItem(path, pinned, out var item) && item != null)
                {
                    _items.Add(item);
                }
            }
        }
    }

    private void SaveAsync()
    {
        if (_disposed) return;
        _store.ScheduleSave();
    }

    internal static bool TryCreateItem(string path, out ShelfItem? item) => TryCreateItem(path, false, out item);

    internal static bool TryCreateItem(string path, bool isPinned, out ShelfItem? item)
    {
        item = null;
        try
        {
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                string size = FormatFileSize(fi.Length);
                string ext = fi.Extension.ToLowerInvariant();
                string glyph = GlyphForExtension(ext);
                bool isImage = IsImageExtension(ext);
                string tag = TagForExtension(ext);
                Brush badgeBrush = BrushForExtension(ext);
                ImageSource? thumbnail = isImage ? TryLoadThumbnail(path, 320) : null;

                item = new ShelfItem(path, fi.Name, size, glyph, isImage, tag, badgeBrush, thumbnail, isPinned);
                return true;
            }
            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                item = new ShelfItem(path, di.Name, "Folder", "\uE8B7", false, "FOLDER", FolderBadgeBrush, null, isPinned);
                return true;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[DropShelf] Failed to inspect '{path}': {ex.Message}");
        }
        return false;
    }

    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".webp", ".tif", ".tiff"
    };

    public static BitmapSource? TryLoadThumbnail(string path, int decodeWidth = 300)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string ext = Path.GetExtension(path);
            if (!SupportedImageExtensions.Contains(ext)) return null;

            var fi = new FileInfo(path);
            if (fi.Length > 50 * 1024 * 1024) return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = decodeWidth;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            App.Log($"[DropShelf] Thumbnail load failed for '{path}': {ex.Message}");
            return null;
        }
    }

    public static bool CopyFileToClipboard(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                var dropList = new StringCollection { path };
                Clipboard.SetFileDropList(dropList);
                return true;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[DropShelf] CopyFileToClipboard failed: {ex.Message}");
        }
        return false;
    }

    public bool CopyAllFilesToClipboard()
    {
        try
        {
            List<string> valid;
            lock (_itemsLock)
            {
                valid = _items
                    .Select(i => i.FullPath)
                    .Where(p => File.Exists(p) || Directory.Exists(p))
                    .ToList();
            }

            if (valid.Count == 0) return false;

            var dropList = new StringCollection();
            foreach (var path in valid)
            {
                dropList.Add(path);
            }
            Clipboard.SetFileDropList(dropList);
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[DropShelf] CopyAllFilesToClipboard failed: {ex.Message}");
            return false;
        }
    }

    public bool PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                if (files is { Count: > 0 })
                {
                    var paths = new List<string>(files.Count);
                    foreach (string? f in files)
                    {
                        if (!string.IsNullOrEmpty(f)) paths.Add(f);
                    }
                    if (paths.Count > 0)
                    {
                        AddPaths(paths);
                        return true;
                    }
                }
            }

            if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                if (image != null)
                {
                    string dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                        "Screenshots");
                    if (!Directory.Exists(dir))
                    {
                        dir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            "Downloads");
                    }
                    Directory.CreateDirectory(dir);

                    string filename = $"DropShelf_Clip_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    string filePath = Path.Combine(dir, filename);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(image));
                        encoder.Save(stream);
                    }

                    if (File.Exists(filePath))
                    {
                        AddPaths(new[] { filePath });
                        return true;
                    }
                }
            }

            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    string? saved = SaveTextSnippetToShelf(text);
                    return !string.IsNullOrEmpty(saved);
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[DropShelf] PasteFromClipboard error: {ex.Message}");
        }
        return false;
    }

    public string? SaveTextSnippetToShelf(string text)
    {
        try
        {
            string dir = Path.Combine(AppPaths.DataDirectory, "ShelfNotes");
            Directory.CreateDirectory(dir);
            string filename = $"Note_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string filePath = Path.Combine(dir, filename);
            File.WriteAllText(filePath, text);
            AddPaths(new[] { filePath });
            return filePath;
        }
        catch (Exception ex)
        {
            App.Log($"[DropShelf] SaveTextSnippetToShelf error: {ex.Message}");
            return null;
        }
    }

    public string? SaveBitmapToShelf(BitmapSource image)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Screenshots");
            if (!Directory.Exists(dir))
            {
                dir = Path.Combine(AppPaths.DataDirectory, "ShelfNotes");
            }
            Directory.CreateDirectory(dir);

            string filename = $"DropShelf_Clip_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string filePath = Path.Combine(dir, filename);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(stream);
            }

            if (File.Exists(filePath))
            {
                AddPaths(new[] { filePath });
                return filePath;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[DropShelf] SaveBitmapToShelf error: {ex.Message}");
        }
        return null;
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

    /// <summary>
    /// Bundles all existing files and folders currently on the Drop Shelf into a single <c>.zip</c> archive,
    /// adds the resulting archive to the top of the shelf, and returns its path.
    /// </summary>
    public string? CreateZipBundle(string? targetDirectory = null)
    {
        List<string> snapshot;
        lock (_itemsLock)
        {
            snapshot = _items
                .Select(i => i.FullPath)
                .Where(p => File.Exists(p) || Directory.Exists(p))
                .ToList();
        }

        if (snapshot.Count == 0) return null;

        try
        {
            string outDir = targetDirectory
                            ?? Path.GetDirectoryName(snapshot[0])
                            ?? AppPaths.DataDirectory;
            Directory.CreateDirectory(outDir);

            string zipName = $"DropShelf_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            string zipPath = Path.Combine(outDir, zipName);
            if (File.Exists(zipPath)) File.Delete(zipPath);

            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in snapshot)
                {
                    if (string.Equals(path, zipPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (File.Exists(path))
                    {
                        string entryName = Path.GetFileName(path);
                        int suffix = 1;
                        while (!usedEntryNames.Add(entryName))
                        {
                            entryName = $"{Path.GetFileNameWithoutExtension(path)}_{suffix++}{Path.GetExtension(path)}";
                        }
                        archive.CreateEntryFromFile(path, entryName, CompressionLevel.Fastest);
                    }
                    else if (Directory.Exists(path))
                    {
                        var dirInfo = new DirectoryInfo(path);
                        foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                        {
                            string rel = Path.Combine(dirInfo.Name, Path.GetRelativePath(path, file.FullName));
                            archive.CreateEntryFromFile(file.FullName, rel, CompressionLevel.Fastest);
                        }
                    }
                }
            }

            AddPaths(new[] { zipPath });
            return zipPath;
        }
        catch (Exception ex)
        {
            App.Log($"[DropShelf] CreateZipBundle failed: {ex.Message}");
            return null;
        }
    }

    public static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    public static bool IsImageExtension(string ext) => ext switch
    {
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" or ".ico" or ".svg" => true,
        _ => false
    };

    public static string TagForExtension(string ext) => ext switch
    {
        ".png" => "PNG",
        ".jpg" or ".jpeg" => "JPG",
        ".webp" => "WEBP",
        ".gif" => "GIF",
        ".svg" => "SVG",
        ".pdf" => "PDF",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "ZIP",
        ".mp4" or ".mov" or ".mkv" or ".avi" or ".webm" => "VIDEO",
        ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a" => "AUDIO",
        ".cs" or ".js" or ".ts" or ".html" or ".css" or ".json" or ".py" or ".cpp" => "CODE",
        ".txt" or ".md" or ".log" => "DOC",
        _ => ext.TrimStart('.').ToUpperInvariant()
    };

    public static Brush BrushForExtension(string ext) => ext switch
    {
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" or ".svg" => ImgBadgeBrush,
        ".pdf" => DocBadgeBrush,
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => ZipBadgeBrush,
        ".mp4" or ".mov" or ".mkv" or ".avi" => VideoBadgeBrush,
        ".mp3" or ".wav" or ".flac" or ".aac" => AudioBadgeBrush,
        ".cs" or ".js" or ".ts" or ".py" or ".html" or ".css" or ".json" or ".cpp" => CodeBadgeBrush,
        _ => DefaultBadgeBrush
    };

    private static string GlyphForExtension(string ext) => ext switch
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

    public void Dispose()
    {
        if (_disposed) return;
        _store.Dispose();
        _disposed = true;
    }
}
