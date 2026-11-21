using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

public sealed class NoteItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled Note";
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public bool IsPinned { get; set; }
    public string ColorTag { get; set; } = "#FF9F0A"; // Amber default

    public string PreviewSnippet
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Content)) return "Empty note";
            string[] lines = Content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var l in lines)
            {
                string trimmed = l.Trim().TrimStart('#', '*', '-', '>', ' ', '\t');
                if (!string.IsNullOrWhiteSpace(trimmed)) return trimmed;
            }
            return "Empty note";
        }
    }

    public string FormattedDate => UpdatedAt.LocalDateTime.ToString("MMM d, HH:mm");
}

/// <summary>
/// Fast, offline markdown notes manager with atomic JSON persistence, pinning,
/// color tagging, search indexing, and 1-click Drop Shelf .md file export.
/// Stored in %LocalAppData%\DynamicIsland\notes.json
/// </summary>
internal sealed class NotesService : IDisposable
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly List<NoteItem> _notes = new();
    private readonly DebouncedJsonStore<List<NoteItem>> _store;
    private bool _disposed;

    public event Action? Changed;

    public IReadOnlyList<NoteItem> Notes
    {
        get
        {
            lock (_lock)
            {
                return _notes.OrderByDescending(n => n.IsPinned)
                             .ThenByDescending(n => n.UpdatedAt)
                             .ToList();
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_lock) return _notes.Count;
        }
    }

    public NotesService(string? customFilePath = null)
    {
        _filePath = customFilePath ?? AppPaths.NotesFile;
        _store = new DebouncedJsonStore<List<NoteItem>>(_filePath, () =>
        {
            lock (_lock) return _notes.ToList();
        });
        Load();
    }

    private void Load()
    {
        lock (_lock)
        {
            try
            {
                var loaded = AtomicJsonStore.Load<List<NoteItem>>(_filePath, () => new List<NoteItem>());
                _notes.Clear();
                if (loaded != null)
                {
                    _notes.AddRange(loaded);
                }
            }
            catch (Exception ex)
            {
                App.Log($"[NotesService] Load failed: {ex.Message}");
            }
        }
    }

    public void SaveNow()
    {
        if (_disposed) return;
        try
        {
            _store.FlushNow();
        }
        catch (Exception ex)
        {
            App.Log($"[NotesService] SaveNow failed: {ex.Message}");
        }
    }

    public NoteItem Create(string content, string? title = null, string? colorTag = null, bool isPinned = false)
    {
        string resolvedTitle = !string.IsNullOrWhiteSpace(title)
            ? title.Trim()
            : ExtractTitle(content);

        var note = new NoteItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = resolvedTitle,
            Content = content ?? "",
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
            ColorTag = colorTag ?? "#FF9F0A",
            IsPinned = isPinned
        };

        lock (_lock)
        {
            _notes.Add(note);
        }

        _store.ScheduleSave();
        Changed?.Invoke();
        return note;
    }

    public bool Update(string id, string? title = null, string? content = null, string? colorTag = null, bool? isPinned = null)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        NoteItem? note;
        lock (_lock)
        {
            note = _notes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase));
            if (note == null) return false;

            if (title != null) note.Title = title.Trim();
            if (content != null)
            {
                note.Content = content;
                if (title == null) note.Title = ExtractTitle(content);
            }
            if (colorTag != null) note.ColorTag = colorTag;
            if (isPinned.HasValue) note.IsPinned = isPinned.Value;
            note.UpdatedAt = DateTimeOffset.Now;
        }

        _store.ScheduleSave();
        Changed?.Invoke();
        return true;
    }

    public bool TogglePin(string id)
    {
        lock (_lock)
        {
            var note = _notes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase));
            if (note == null) return false;
            note.IsPinned = !note.IsPinned;
            note.UpdatedAt = DateTimeOffset.Now;
        }

        _store.ScheduleSave();
        Changed?.Invoke();
        return true;
    }

    public bool Delete(string id)
    {
        bool removed;
        lock (_lock)
        {
            removed = _notes.RemoveAll(n => string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        if (removed)
        {
            _store.ScheduleSave();
            Changed?.Invoke();
        }
        return removed;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _notes.Clear();
        }
        _store.ScheduleSave();
        Changed?.Invoke();
    }

    public IReadOnlyList<NoteItem> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Notes;

        string q = query.Trim();
        lock (_lock)
        {
            return _notes
                .Where(n => n.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            n.Content.Contains(q, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => n.IsPinned)
                .ThenByDescending(n => n.UpdatedAt)
                .ToList();
        }
    }

    public string ExportToShelf(string id, DropShelfService shelf)
    {
        NoteItem? note;
        lock (_lock)
        {
            note = _notes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        if (note == null) throw new ArgumentException($"Note '{id}' not found.");

        string safeTitle = Regex.Replace(note.Title, @"[^\w\s-]", "").Trim();
        if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = "Note";
        if (safeTitle.Length > 40) safeTitle = safeTitle.Substring(0, 40);

        string notesDir = Path.Combine(AppPaths.DataDirectory, "ExportedNotes");
        Directory.CreateDirectory(notesDir);
        string fileName = $"{safeTitle}_{DateTime.Now:yyyyMMdd_HHmmss}.md";
        string filePath = Path.Combine(notesDir, fileName);

        string markdown = ToMarkdownFileContent(note);
        File.WriteAllText(filePath, markdown, Encoding.UTF8);

        shelf.AddPaths(new[] { filePath });
        return filePath;
    }

    public static string ExtractTitle(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "Untitled Note";

        string[] lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith('#'))
            {
                string heading = line.TrimStart('#', ' ', '\t');
                if (!string.IsNullOrWhiteSpace(heading))
                {
                    return heading.Length > 60 ? heading.Substring(0, 60) + "..." : heading;
                }
            }
            else
            {
                string firstSentence = line.TrimStart('*', '-', '>', ' ', '\t');
                if (!string.IsNullOrWhiteSpace(firstSentence))
                {
                    return firstSentence.Length > 60 ? firstSentence.Substring(0, 60) + "..." : firstSentence;
                }
            }
        }

        return "Untitled Note";
    }

    public static string ToMarkdownFileContent(NoteItem note)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {note.Title}");
        sb.AppendLine();
        sb.AppendLine($"> Created: {note.CreatedAt:yyyy-MM-dd HH:mm:ss} | Updated: {note.UpdatedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine(note.Content);
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _store.Dispose();
        _disposed = true;
    }
}
