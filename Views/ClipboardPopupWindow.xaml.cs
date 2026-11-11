using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Views;

public partial class ClipboardPopupWindow : Window
{
    private readonly ClipboardHistoryService _clipboardService;

    internal ClipboardPopupWindow(ClipboardHistoryService clipboardService)
    {
        _clipboardService = clipboardService;
        InitializeComponent();
        RefreshList();

        Loaded += (_, _) =>
        {
            if (DynamicIsland.Core.ThemeManager.IsLiquidGlass)
            {
                DynamicIsland.LiquidGlass.GlassHost.Attach(ClipboardCard, DynamicIsland.LiquidGlass.GlassPresets.Card);
            }
        };

        LocationChanged += (_, _) =>
        {
            if (DynamicIsland.Core.ThemeManager.IsLiquidGlass)
            {
                DynamicIsland.LiquidGlass.GlassHost.ForceRender(ClipboardCard);
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
        SearchBox.Focus();
    }

    private void RefreshList()
    {
        string? q = SearchBox?.Text?.Trim();
        var items = _clipboardService.GetFilteredItems(q, ClipboardCategoryFilter.All);
        ClipsItemsControl.ItemsSource = items;

        int qCount = _clipboardService.PasteQueueCount;
        if (PopQueueBtn != null && PopQueueBtnText != null)
        {
            PopQueueBtn.Visibility = qCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            PopQueueBtnText.Text = $"Next Queue ({qCount})";
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

    private void ClipRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClipboardItem item })
        {
            bool plain = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            _clipboardService.CopyItem(item, plainTextOnly: plain);
            Close();
        }
    }

    private void PlainCopyBtn_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: string id })
        {
            var item = _clipboardService.Items.FirstOrDefault(i => i.Id == id);
            if (item != null)
            {
                _clipboardService.CopyItem(item, plainTextOnly: true);
                Close();
            }
        }
    }

    private void QueueAddBtn_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: string id })
        {
            var item = _clipboardService.Items.FirstOrDefault(i => i.Id == id);
            if (item != null)
            {
                _clipboardService.EnqueueForSequentialPaste(item);
                RefreshList();
            }
        }
    }

    private void PopQueueBtn_Click(object sender, RoutedEventArgs e)
    {
        _clipboardService.DequeueNextPasteItem(copyToClipboard: true);
        Close();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        try { Close(); } catch { }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (ClipsItemsControl.ItemsSource is IList<ClipboardItem> { Count: > 0 } list)
            {
                bool plain = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                _clipboardService.CopyItem(list[0], plainTextOnly: plain);
                Close();
            }
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        DynamicIsland.LiquidGlass.GlassHost.Detach(ClipboardCard);
        base.OnClosed(e);
    }
}
