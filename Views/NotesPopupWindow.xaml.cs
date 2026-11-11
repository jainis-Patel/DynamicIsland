using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DynamicIsland.Core;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Views;

public partial class NotesPopupWindow : Window
{
    private readonly NotesService _notesService;
    private readonly DropShelfService _shelfService;
    private string? _currentNoteId;
    private string _currentColorTag = "#FF9F0A";
    private bool _isUpdatingUi;

    internal NotesPopupWindow(NotesService notesService, DropShelfService shelfService)
    {
        _notesService = notesService;
        _shelfService = shelfService;
        InitializeComponent();
        RefreshList();
        LoadMostRecentOrNew();

        Loaded += (_, _) =>
        {
            if (ThemeManager.IsLiquidGlass)
            {
                DynamicIsland.LiquidGlass.GlassHost.Attach(NotesCard, DynamicIsland.LiquidGlass.GlassPresets.Card);
            }
        };

        LocationChanged += (_, _) =>
        {
            if (ThemeManager.IsLiquidGlass)
            {
                DynamicIsland.LiquidGlass.GlassHost.ForceRender(NotesCard);
            }
        };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowDisplayAffinity(hwnd, 0x00000011 /* WDA_EXCLUDEFROMCAPTURE */);
            }
        };
    }

    public void ShowAtCursor()
    {
        if (GetCursorPos(out var pt))
        {
            var mon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            double scale = 1.0;
            if (mon != IntPtr.Zero && GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0)
            {
                scale = dpiX / 96.0;
            }

            double dipX = (pt.X / scale) - (Width / 2);
            double dipY = (pt.Y / scale) - 24;

            if (mon != IntPtr.Zero)
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(mon, ref mi))
                {
                    double minX = mi.rcWork.Left / scale + 8;
                    double maxX = (mi.rcWork.Right / scale) - Width - 8;
                    double minY = mi.rcWork.Top / scale + 8;
                    double maxY = (mi.rcWork.Bottom / scale) - Height - 8;
                    dipX = Math.Clamp(dipX, minX, Math.Max(minX, maxX));
                    dipY = Math.Clamp(dipY, minY, Math.Max(minY, maxY));
                }
            }

            Left = dipX;
            Top = dipY;
        }

        RefreshList();
        Show();
        Activate();
        NoteEditorBox.Focus();
    }

    private void LoadMostRecentOrNew()
    {
        var notes = _notesService.Notes;
        if (notes.Count > 0)
        {
            LoadNote(notes[0]);
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
            NoteEditorBox.Text = note.Content;
            EditorPlaceholder.Visibility = string.IsNullOrEmpty(note.Content) ? Visibility.Visible : Visibility.Collapsed;
            UpdatePinVisual(note.IsPinned);
            UpdateStatus($"Editing '{note.Title}' · {note.FormattedDate}");
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
            NoteEditorBox.Text = "";
            EditorPlaceholder.Visibility = Visibility.Visible;
            UpdatePinVisual(false);
            UpdateStatus("New blank note");
            NoteEditorBox.Focus();
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void RefreshList()
    {
        string? q = SearchBox?.Text?.Trim();
        var items = _notesService.Search(q);
        NotesItemsControl.ItemsSource = items;

        if (NotesCountText != null)
        {
            NotesCountText.Text = $"{_notesService.Count} note{(_notesService.Count == 1 ? "" : "s")}";
        }
    }

    private void UpdatePinVisual(bool isPinned)
    {
        if (PinGlyphText != null)
        {
            PinGlyphText.Foreground = isPinned ? new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)) : new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93));
        }
    }

    private void UpdateStatus(string message)
    {
        if (StatusInfoText != null)
        {
            StatusInfoText.Text = message;
        }
    }

    private void NoteEditorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingUi) return;

        string text = NoteEditorBox.Text;
        EditorPlaceholder.Visibility = string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(text)) return;

        if (_currentNoteId == null)
        {
            var note = _notesService.Create(text, colorTag: _currentColorTag);
            _currentNoteId = note.Id;
            UpdateStatus($"Created '{note.Title}'");
        }
        else
        {
            _notesService.Update(_currentNoteId, content: text);
            string title = NotesService.ExtractTitle(text);
            UpdateStatus($"Saved '{title}' · {DateTime.Now:HH:mm:ss}");
        }

        RefreshList();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

    private void NewNoteBtn_Click(object sender, RoutedEventArgs e) => NewNote();

    private void ExportShelfBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentNoteId))
        {
            if (!string.IsNullOrWhiteSpace(NoteEditorBox.Text))
            {
                var n = _notesService.Create(NoteEditorBox.Text, colorTag: _currentColorTag);
                _currentNoteId = n.Id;
            }
            else
            {
                return;
            }
        }

        try
        {
            string path = _notesService.ExportToShelf(_currentNoteId, _shelfService);
            UpdateStatus($"Exported to Drop Shelf: {System.IO.Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Export failed: {ex.Message}");
        }
    }

    private void TogglePinBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNoteId != null)
        {
            _notesService.TogglePin(_currentNoteId);
            var note = _notesService.Notes.FirstOrDefault(n => n.Id == _currentNoteId);
            if (note != null) UpdatePinVisual(note.IsPinned);
            RefreshList();
        }
    }

    private void DeleteNoteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNoteId != null)
        {
            _notesService.Delete(_currentNoteId);
            LoadMostRecentOrNew();
            RefreshList();
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void ColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            _currentColorTag = hex;
            if (_currentNoteId != null)
            {
                _notesService.Update(_currentNoteId, colorTag: hex);
                RefreshList();
            }
        }
    }

    private void ToggleNotesListBtn_Click(object sender, RoutedEventArgs e)
    {
        bool isVis = NotesListBorder.Visibility == Visibility.Visible;
        NotesListBorder.Visibility = isVis ? Visibility.Collapsed : Visibility.Visible;
        ToggleNotesListText.Text = isVis ? "Show List" : "Hide List";
    }

    private void NoteRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NoteItem note })
        {
            LoadNote(note);
        }
    }

    private void RowExportBtn_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: string id })
        {
            try
            {
                string path = _notesService.ExportToShelf(id, _shelfService);
                UpdateStatus($"Exported: {System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Export error: {ex.Message}");
            }
        }
    }

    private void RowDeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: string id })
        {
            _notesService.Delete(id);
            if (_currentNoteId == id)
            {
                LoadMostRecentOrNew();
            }
            RefreshList();
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // Auto-close on focus lost
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            NewNote();
            e.Handled = true;
        }
        else if (e.Key == Key.E && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ExportShelfBtn_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        DynamicIsland.LiquidGlass.GlassHost.Detach(NotesCard);
        base.OnClosed(e);
    }
}
