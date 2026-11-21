using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private bool _isClosing;

    public int ItemCount => _items.Count;

    private void CloseSafely()
    {
        if (_isClosing) return;
        _isClosing = true;
        Close();
    }

    internal BasketWindow(DropShelfService shelfService)
    {
        _shelfService = shelfService;
        InitializeComponent();
        BasketItemsList.ItemsSource = _items;

        // Auto-hide empty basket after 12 seconds if user doesn't drop anything
        _autoCloseTimer = new DispatcherTimer(TimeSpan.FromSeconds(12), DispatcherPriority.Background, (sender, _) =>
        {
            if (_items.Count == 0)
            {
                CloseSafely();
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
            PlayEntranceAnimation();
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
                CloseSafely();
                e.Handled = true;
            }
        };
    }

    private void PlayEntranceAnimation()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var animX = new DoubleAnimation(0.92, 1.0, new Duration(TimeSpan.FromMilliseconds(160))) { EasingFunction = ease };
        var animY = new DoubleAnimation(0.92, 1.0, new Duration(TimeSpan.FromMilliseconds(160))) { EasingFunction = ease };
        var animFade = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromMilliseconds(120)));

        BasketScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
        BasketScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
        OuterCard.BeginAnimation(UIElement.OpacityProperty, animFade);
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
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
        PositionWindowNearPhysicalPoint(this, physicalX, physicalY, yOffsetDip: 42.0);
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

    // P2c perf: Frozen brush for drag border (avoids unfrozen SolidColorBrush on every drag event)
    private static readonly SolidColorBrush DragIdleBorderBrush = Core.ThemeBrushes.CreateFrozen(
        Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        OuterCard.BorderBrush = DragIdleBorderBrush;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        OuterCard.BorderBrush = DragIdleBorderBrush;
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

            if (DropShelfService.TryCreateItem(path, out var shelfItem) && shelfItem != null)
            {
                _items.Insert(0, shelfItem);
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
            if (_items.Count == 0) CloseSafely();
        }
    }

    private void SendToShelfBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count > 0)
        {
            int count = _items.Count;
            _shelfService.AddPaths(_items.Select(i => i.FullPath).ToList());
            IslandWindow.Instance?.PushToast(new ToastContent(
                "\uE8B7",
                "Drop Shelf",
                $"{count} item{(count == 1 ? "" : "s")} moved to Shelf",
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.5),
                TargetView: "shelf",
                Priority: ToastPriority.System));
            CloseSafely();
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => CloseSafely();

    private void Item_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            if (sender is FrameworkElement { DataContext: ShelfItem item } && (File.Exists(item.FullPath) || Directory.Exists(item.FullPath)))
            {
                try
                {
                    using var proc = Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    App.Log($"[BasketWindow] Open failed for '{item.FullPath}': {ex.Message}");
                }
            }
            e.Handled = true;
            return;
        }

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

    private void CopyItemBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path } && File.Exists(path))
        {
            DropShelfService.CopyFileToClipboard(path);
            IslandWindow.Instance?.PushToast(new ToastContent(
                "\uE8C8",
                "File Copied",
                $"{Path.GetFileName(path)} \u00B7 Ready to paste (Ctrl+V)",
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "shelf",
                Priority: ToastPriority.Feedback,
                Category: "basketcopy"));
        }
    }

    private void CopyAllBtn_Click(object sender, RoutedEventArgs e)
    {
        var files = _items.Select(i => i.FullPath).Where(f => File.Exists(f) || Directory.Exists(f)).ToArray();
        if (files.Length == 0) return;

        var data = new DataObject(DataFormats.FileDrop, files);
        Clipboard.SetDataObject(data, true);

        IslandWindow.Instance?.PushToast(new ToastContent(
            "\uE8C8",
            "All Files Copied",
            $"{files.Length} file{(files.Length == 1 ? "" : "s")} placed on clipboard (Ctrl+V)",
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(2.5),
            TargetView: "shelf",
            Priority: ToastPriority.Feedback,
            Category: "basketcopy"));
    }

    private void ClearAllBtn_Click(object sender, RoutedEventArgs e)
    {
        _items.Clear();
        RefreshUI();
        CloseSafely();
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
                CloseSafely();
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
            IslandWindow.Instance?.PushToast(new ToastContent(
                "\uF012",
                "ZIP Archive Created",
                $"{Path.GetFileName(zipPath)} placed in Basket",
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.8),
                TargetView: "shelf",
                Priority: ToastPriority.Feedback,
                Category: "basketzip"));
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
                try
                {
                    var res = await FileActionPipeline.ExecuteAsync(captured, paths);
                    if (_isClosing) return;
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
                }
                catch (Exception ex)
                {
                    App.Log($"[BasketWindow] Convert action failed: {ex.Message}");
                }
            };
            menu.Items.Add(mi);
        }
        menu.PlacementTarget = ConvertMenuBtn;
        menu.IsOpen = true;
    }
}
