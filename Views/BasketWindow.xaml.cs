using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Views;

public partial class BasketWindow : Window
{
    private readonly DropShelfService _shelfService;
    private readonly ObservableCollection<ShelfItem> _items = new();
    private readonly DispatcherTimer _autoCloseTimer;
    private Point _dragStart;

    public int ItemCount => _items.Count;

    internal BasketWindow(DropShelfService shelfService)
    {
        _shelfService = shelfService;
        InitializeComponent();
        BasketItemsList.ItemsSource = _items;

        // Auto-hide empty basket after 9 seconds if user doesn't drop anything
        _autoCloseTimer = new DispatcherTimer(TimeSpan.FromSeconds(9), DispatcherPriority.Background, (sender, _) =>
        {
            if (_items.Count == 0)
            {
                Close();
            }
            else
            {
                (sender as DispatcherTimer)?.Stop();
            }
        }, Dispatcher);
        _autoCloseTimer.Start();

        Loaded += (_, _) =>
        {
            if (ThemeManager.IsLiquidGlass)
            {
                DynamicIsland.LiquidGlass.GlassHost.Attach(OuterCard, DynamicIsland.LiquidGlass.GlassPresets.Regular);
            }
        };

        LocationChanged += (_, _) =>
        {
            if (ThemeManager.IsLiquidGlass)
            {
                DynamicIsland.LiquidGlass.GlassHost.ForceRender(OuterCard);
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

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        DynamicIsland.LiquidGlass.GlassHost.Detach(OuterCard);
        _autoCloseTimer.Stop();
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        long ex = GetExStyle(hwnd);
        ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        ex &= ~WS_EX_APPWINDOW;
        SetExStyle(hwnd, ex);
    }

    public void ShowAtPhysicalPoint(int physicalX, int physicalY)
    {
        MoveNearPhysicalPoint(physicalX, physicalY);
        Show();
    }

    public void MoveNearPhysicalPoint(int physicalX, int physicalY)
    {
        var mon = MonitorFromPoint(new POINT { X = physicalX, Y = physicalY }, MONITOR_DEFAULTTONEAREST);
        double scale = 1.0;
        if (mon != IntPtr.Zero && GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0)
        {
            scale = dpiX / 96.0;
        }

        double dipX = (physicalX / scale) - (Width / 2);
        double dipY = (physicalY / scale) - 42;

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

    public void ActivatePulse()
    {
        _autoCloseTimer.Stop();
        _autoCloseTimer.Start();
        OuterCard.BorderBrush = ThemeBrushes.Amber;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text) || e.Data.GetDataPresent(DataFormats.Bitmap))
        {
            e.Effects = DragDropEffects.Copy;
            OuterCard.BorderBrush = ThemeBrushes.Green;
            _autoCloseTimer.Stop();
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        OuterCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        OuterCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
        _autoCloseTimer.Stop();

        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            AddFilesToBasket(files);
        }
        else if (e.Data.GetDataPresent(DataFormats.Text) && e.Data.GetData(DataFormats.Text) is string text && !string.IsNullOrWhiteSpace(text))
        {
            string? path = _shelfService.SaveTextSnippetToShelf(text);
            if (!string.IsNullOrEmpty(path))
            {
                AddFilesToBasket(new[] { path });
            }
        }
    }

    public void AddFilesToBasket(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (_items.Any(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                string ext = fi.Extension.ToLowerInvariant();
                _items.Insert(0, new ShelfItem(
                    path,
                    fi.Name,
                    DropShelfService.FormatFileSize(fi.Length),
                    "\uE7C3",
                    DropShelfService.IsImageExtension(ext),
                    DropShelfService.TagForExtension(ext),
                    DropShelfService.BrushForExtension(ext)));
            }
            else if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                _items.Insert(0, new ShelfItem(
                    path,
                    di.Name,
                    "Folder",
                    "\uE8B7",
                    false,
                    "DIR",
                    ThemeBrushes.Blue));
            }
        }
        RefreshUI();
    }

    private void RefreshUI()
    {
        CountBadgeText.Text = _items.Count.ToString();
        bool has = _items.Count > 0;
        EmptyHintBorder.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        ItemsScroller.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        ActionBar.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        SendToShelfBtn.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            var found = _items.FirstOrDefault(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase));
            if (found != null) _items.Remove(found);
            RefreshUI();
            if (_items.Count == 0) Close();
        }
    }

    private void SendToShelfBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count > 0)
        {
            _shelfService.AddPaths(_items.Select(i => i.FullPath).ToList());
            Close();
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void Item_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        e.Handled = true;
    }

    private void Item_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement { DataContext: ShelfItem item } el)
        {
            var diff = _dragStart - e.GetPosition(this);
            if (Math.Abs(diff.X) > 4 || Math.Abs(diff.Y) > 4)
            {
                var data = new DataObject(DataFormats.FileDrop, new[] { item.FullPath });
                DragDrop.DoDragDrop(el, data, DragDropEffects.Copy);
            }
        }
    }

    private void DragAll_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
    }

    private void DragAll_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement el && _items.Count > 0)
        {
            var diff = _dragStart - e.GetPosition(this);
            if (Math.Abs(diff.X) > 4 || Math.Abs(diff.Y) > 4)
            {
                string[] all = _items.Select(i => i.FullPath).ToArray();
                var data = new DataObject(DataFormats.FileDrop, all);
                DragDrop.DoDragDrop(el, data, DragDropEffects.Copy);
                Close();
            }
        }
    }

    private void ZipAllBtn_Click(object sender, RoutedEventArgs e)
    {
        var files = _items.Select(i => i.FullPath).Where(File.Exists).ToList();
        if (files.Count == 0) return;

        try
        {
            string dir = Path.GetDirectoryName(files[0]) ?? AppPaths.DataDirectory;
            string zipPath = Path.Combine(dir, $"Basket_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var f in files)
                {
                    archive.CreateEntryFromFile(f, Path.GetFileName(f), CompressionLevel.Fastest);
                }
            }
            _items.Clear();
            AddFilesToBasket(new[] { zipPath });
            _shelfService.AddPaths(new[] { zipPath });
        }
        catch (Exception ex)
        {
            App.Log($"[BasketWindow] Zip failed: {ex.Message}");
        }
    }

    private void ConvertMenuBtn_Click(object sender, RoutedEventArgs e)
    {
        var paths = _items.Select(i => i.FullPath).ToList();
        var actions = FileActionPipeline.GetAvailableActions(paths);
        if (actions.Count == 0) return;

        var menu = new ContextMenu();
        foreach (var act in actions)
        {
            var mi = new MenuItem { Header = $"{act.Label} — {act.Subtitle}" };
            var captured = act.Kind;
            mi.Click += async (_, _) =>
            {
                var res = await FileActionPipeline.ExecuteAsync(captured, paths);
                if (res.Success)
                {
                    if (!string.IsNullOrEmpty(res.CopiedClipboardText))
                    {
                        try { Clipboard.SetText(res.CopiedClipboardText); } catch { }
                    }
                    if (res.OutputPaths.Count > 0)
                    {
                        AddFilesToBasket(res.OutputPaths);
                        _shelfService.AddPaths(res.OutputPaths);
                    }
                    IslandWindow.Instance?.NotifyFileActionCompleted(res);
                }
            };
            menu.Items.Add(mi);
        }
        menu.PlacementTarget = ConvertMenuBtn;
        menu.IsOpen = true;
    }

}
