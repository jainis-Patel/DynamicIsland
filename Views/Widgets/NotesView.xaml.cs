using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland.Views.Widgets;

public partial class NotesView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;
    private string? _currentNoteId;
    private string _currentColorTag = "#FF9F0A";
    private bool _isUpdatingUi;

    public void FocusEditor()
    {
        NoteEditorBox?.Focus();
    }

    private void BackToLauncher_Click(object sender, RoutedEventArgs e) =>
        Island?.NavigateToView(Core.ExpandedView.Launcher);

    public NotesView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RefreshList();
            LoadMostRecentOrNew();
        };
    }

    private NotesService? Notes => Island?.NotesService;
    private DropShelfService? Shelf => Island?.ShelfService;

    public void RefreshList()
    {
        if (Notes == null) return;
        string? q = NotesSearchBox?.Text?.Trim();
        NotesListControl.ItemsSource = Notes.Search(q);
    }

    private void LoadMostRecentOrNew()
    {
        if (Notes == null) return;
        var list = Notes.Notes;
        if (list.Count > 0)
        {
            LoadNote(list[0]);
        }
        else
        {
            NewNote();
        }
    }

    private void LoadNote(NoteItem note)
    {
        _isUpdatingUi = true;
        try
        {
            _currentNoteId = note.Id;
            _currentColorTag = note.ColorTag;
            ActiveNoteTitleText.Text = note.Title;
            NoteEditorBox.Text = note.Content;
            EditorPlaceholder.Visibility = string.IsNullOrEmpty(note.Content) ? Visibility.Visible : Visibility.Collapsed;
            UpdatePinVisual(note.IsPinned);
            UpdateWordStats(note.Content);
            NoteStatusText.Text = $"Updated {note.FormattedDate}";
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void NewNote()
    {
        _isUpdatingUi = true;
        try
        {
            _currentNoteId = null;
            ActiveNoteTitleText.Text = "Untitled Note";
            NoteEditorBox.Text = "";
            EditorPlaceholder.Visibility = Visibility.Visible;
            UpdatePinVisual(false);
            UpdateWordStats(string.Empty);
            NoteStatusText.Text = "New blank note";
            NoteEditorBox.Focus();
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void UpdatePinVisual(bool isPinned)
    {
        if (PinIconText != null)
        {
            PinIconText.Foreground = isPinned ? new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)) : new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93));
        }
    }

    private void NoteEditorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingUi || Notes == null) return;

        string text = NoteEditorBox.Text;
        EditorPlaceholder.Visibility = string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
        UpdateWordStats(text);

        if (string.IsNullOrWhiteSpace(text) && _currentNoteId == null) return;

        if (_currentNoteId == null)
        {
            var note = Notes.Create(text, colorTag: _currentColorTag);
            _currentNoteId = note.Id;
            ActiveNoteTitleText.Text = note.Title;
            NoteStatusText.Text = $"Created {DateTime.Now:HH:mm:ss}";
        }
        else
        {
            Notes.Update(_currentNoteId, content: text);
            string title = NotesService.ExtractTitle(text);
            ActiveNoteTitleText.Text = title;
            NoteStatusText.Text = $"Saved {DateTime.Now:HH:mm:ss}";
        }

        RefreshList();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

    private void NewNoteBtn_Click(object sender, RoutedEventArgs e) => NewNote();

    private void ExportShelfBtn_Click(object sender, RoutedEventArgs e)
    {
        if (Notes == null || Shelf == null) return;

        if (string.IsNullOrWhiteSpace(_currentNoteId))
        {
            if (!string.IsNullOrWhiteSpace(NoteEditorBox.Text))
            {
                var n = Notes.Create(NoteEditorBox.Text, colorTag: _currentColorTag);
                _currentNoteId = n.Id;
            }
            else return;
        }

        try
        {
            string path = Notes.ExportToShelf(_currentNoteId, Shelf);
            NoteStatusText.Text = $"Exported to Drop Shelf: {System.IO.Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            NoteStatusText.Text = $"Export error: {ex.Message}";
        }
    }

    private void CopyNoteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(NoteEditorBox.Text) && SafeClipboard.TrySetText(NoteEditorBox.Text))
        {
            NoteStatusText.Text = "Markdown copied to clipboard";
        }
    }

    private void UpdateWordStats(string text)
    {
        if (NoteStatsText == null) return;
        if (string.IsNullOrWhiteSpace(text))
        {
            NoteStatsText.Text = $"0w · {(text?.Length ?? 0)}c";
            return;
        }

        int words = 0;
        bool inWord = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                words++;
                inWord = true;
            }
        }
        NoteStatsText.Text = $"{words}w · {text.Length}c";
    }

    private void TransformNoteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (NoteEditorBox == null || string.IsNullOrWhiteSpace(NoteEditorBox.Text))
        {
            NoteStatusText.Text = "Type note text to transform";
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = TransformNoteBtn,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            VerticalOffset = 4
        };

        void AddItem(string header, ClipboardTransformKind kind)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => ApplyNoteTransform(kind);
            menu.Items.Add(mi);
        }

        AddItem("JSON Pretty", ClipboardTransformKind.JsonPretty);
        AddItem("JSON Minify", ClipboardTransformKind.JsonMinify);
        AddItem("Trim Empty Lines", ClipboardTransformKind.TrimLines);
        menu.Items.Add(new Separator());
        AddItem("UPPERCASE", ClipboardTransformKind.UpperCase);
        AddItem("lowercase", ClipboardTransformKind.LowerCase);
        AddItem("Title Case", ClipboardTransformKind.TitleCase);
        AddItem("camelCase", ClipboardTransformKind.CamelCase);
        AddItem("snake_case", ClipboardTransformKind.SnakeCase);
        menu.Items.Add(new Separator());
        AddItem("Base64 Encode", ClipboardTransformKind.Base64Encode);
        AddItem("Base64 Decode", ClipboardTransformKind.Base64Decode);
        AddItem("URL Encode", ClipboardTransformKind.UrlEncode);
        AddItem("URL Decode", ClipboardTransformKind.UrlDecode);
        menu.Items.Add(new Separator());
        AddItem("Extract URLs", ClipboardTransformKind.ExtractUrls);
        AddItem("Extract Emails", ClipboardTransformKind.ExtractEmails);

        menu.IsOpen = true;
    }

    private void ApplyNoteTransform(ClipboardTransformKind kind)
    {
        bool hasSelection = NoteEditorBox.SelectionLength > 0;
        string source = hasSelection ? NoteEditorBox.SelectedText : NoteEditorBox.Text;
        var (success, result, label) = ClipboardTransformer.TryTransform(source, kind);
        if (!success)
        {
            NoteStatusText.Text = $"Skipped: {label}";
            return;
        }

        if (hasSelection)
        {
            NoteEditorBox.SelectedText = result;
        }
        else
        {
            NoteEditorBox.Text = result;
            NoteEditorBox.CaretIndex = result.Length;
        }
        NoteStatusText.Text = $"Applied {label}";
    }

    private void TogglePinBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNoteId != null && Notes != null)
        {
            Notes.TogglePin(_currentNoteId);
            var note = Notes.Notes.FirstOrDefault(n => n.Id == _currentNoteId);
            if (note != null) UpdatePinVisual(note.IsPinned);
            RefreshList();
        }
    }

    private void DeleteNoteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNoteId != null && Notes != null)
        {
            Notes.Delete(_currentNoteId);
            LoadMostRecentOrNew();
            RefreshList();
        }
    }

    private void ColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex } && Notes != null)
        {
            _currentColorTag = hex;
            if (_currentNoteId != null)
            {
                Notes.Update(_currentNoteId, colorTag: hex);
                RefreshList();
            }
        }
    }

    private void NoteRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NoteItem note })
        {
            LoadNote(note);
        }
    }
}
