using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

public partial class IslandWindow
{
    // =================================================================
    //  Drop Shelf ("Drop Notch") Drag & Drop
    // =================================================================

    private void Window_PreviewDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DropOverlay.Visibility = Visibility.Visible;
            if (ThemeManager.IsLiquidGlass && DynamicIsland.LiquidGlass.GlassManager.Instance.IsEnabled)
            {
                var accent = Core.ThemeBrushes.AccentColor;
                DynamicIsland.LiquidGlass.GlassHost.Attach(DropOverlay, DynamicIsland.LiquidGlass.GlassPresets.Tinted(accent, 0.40f));
            }
            if (_machine.State == IslandState.Idle)
            {
                _width.Target = 240;
                _height.Target = 52;
                EnsureRendering();
            }
        }
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Window_PreviewDragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (ThemeManager.IsLiquidGlass)
        {
            DynamicIsland.LiquidGlass.GlassHost.Detach(DropOverlay);
        }
        var shape = GetTargetShape(_machine.State);
        _width.Target = shape.Width;
        _height.Target = shape.Height;
        EnsureRendering();
    }

    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (ThemeManager.IsLiquidGlass)
        {
            DynamicIsland.LiquidGlass.GlassHost.Detach(DropOverlay);
        }
        var shape = GetTargetShape(_machine.State);
        _width.Target = shape.Width;
        _height.Target = shape.Height;
        EnsureRendering();

        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            _shelfService.AddPaths(files);
            _machine.PushToast(new ToastContent(
                "\uE8B7",
                "Drop Shelf",
                $"{files.Length} item{(files.Length == 1 ? "" : "s")} added",
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.8),
                TargetView: "shelf",
                Priority: ToastPriority.System));
        }
    }

    private void OnShelfChanged()
    {
        ShelfBadgeText.Text = _shelfService.Count.ToString();
        ShelfBadgeText.Visibility = _shelfService.HasItems ? Visibility.Visible : Visibility.Collapsed;
        if (DockShelfBadgeHost != null && DockShelfBadgeText != null)
        {
            DockShelfBadgeText.Text = _shelfService.Count.ToString();
            DockShelfBadgeHost.Visibility = _shelfService.HasItems ? Visibility.Visible : Visibility.Collapsed;
        }
        ShelfCountText.Text = _shelfService.HasItems
            ? $"{_shelfService.Count} item{(_shelfService.Count == 1 ? "" : "s")} \u00B7 {_shelfService.TotalSizeFormatted}"
            : "0 items";
        ShelfEmptyPlaceholder.Visibility = _shelfService.HasItems ? Visibility.Collapsed : Visibility.Visible;
        if (ShelfItemActionsGroup != null)
        {
            ShelfItemActionsGroup.Visibility = _shelfService.HasItems ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ShelfActionsButton != null)
        {
            ShelfActionsButton.Visibility = _shelfService.HasItems ? Visibility.Visible : Visibility.Collapsed;
        }
        if (CopyAllShelfButton != null)
        {
            CopyAllShelfButton.Visibility = _shelfService.HasItems ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ClearShelfButton != null)
        {
            ClearShelfButton.Visibility = _shelfService.HasItems ? Visibility.Visible : Visibility.Collapsed;
        }
        if (DragAllShelfBtn != null)
        {
            DragAllShelfBtn.Visibility = _shelfService.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        }
        SyncObservableCollection(_shelfItems, _shelfService.Items);
        UpdateShelfScrollButtons();
    }

    internal void CopyShelfFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            if (DropShelfService.CopyFileToClipboard(path))
            {
                _machine.PushToast(new ToastContent(
                    "\uE8C8",
                    "File Copied",
                    $"{Path.GetFileName(path)} \u00B7 Ready to paste (Ctrl+V)",
                    ThemeBrushes.GreenColor,
                    TimeSpan.FromSeconds(2.2),
                    TargetView: "shelf",
                    Priority: ToastPriority.Feedback,
                    Category: "shelfcopy"));
            }
        }
    }

    internal void CopyAllShelfButton_Click(object sender, RoutedEventArgs e)
    {
        if (_shelfService.CopyAllFilesToClipboard())
        {
            _machine.PushToast(new ToastContent(
                "\uE8C8",
                "All Files Copied",
                $"{_shelfService.Count} files placed on clipboard (Ctrl+V pasteable)",
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.8),
                TargetView: "shelf",
                Priority: ToastPriority.Feedback,
                Category: "shelfcopy"));
        }
    }

    internal void ClearShelfButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_shelfService.HasItems) return;

        bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (!_confirmGuard.TryConfirm("clear_shelf", sender, "Confirm?", bypass: shiftHeld, dispatcher: Dispatcher))
        {
            TriggerLiquidImpulse(24, -8, 3);
            return;
        }

        _shelfService.Clear();
        TriggerLiquidImpulse(180, -90, 25);
    }

    internal void PasteToShelf_Click(object sender, RoutedEventArgs e)
    {
        if (_shelfService.PasteFromClipboard())
        {
            TriggerLiquidImpulse(180, -90, 25);
            _machine.PushToast(new ToastContent(
                "\uE77F",
                "Pasted to Drop Shelf",
                $"{_shelfService.Count} items on shelf",
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.2),
                TargetView: "shelf",
                Priority: ToastPriority.Feedback,
                Category: "shelfpaste"));
        }
        else
        {
            _machine.PushToast(new ToastContent(
                "\uE7BA",
                "Nothing to Paste",
                "Clipboard has no files or images",
                ThemeBrushes.RedColor,
                TimeSpan.FromSeconds(2),
                TargetView: "shelf",
                Priority: ToastPriority.Feedback,
                Category: "shelferror"));
        }
    }

    internal void BrowseToShelf_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _isModalDialogOpen = true;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Title = "Select files to hold in Drop Shelf"
            };
            if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
            {
                _shelfService.AddPaths(dlg.FileNames);
                TriggerLiquidImpulse(180, -90, 25);
            }
        }
        catch (Exception ex)
        {
            App.Log($"[DropShelf] Browse dialog failed: {ex.Message}");
        }
        finally
        {
            _isModalDialogOpen = false;
            _machine.ExpandNow();
        }
    }

    internal void DragAllShelf_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
    }

    internal void DragAllShelf_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement element)
        {
            var diff = _dragStartPoint - e.GetPosition(this);
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var allPaths = _shelfService.Items.Select(i => i.FullPath).Where(File.Exists).ToArray();
                if (allPaths.Length > 0)
                {
                    var data = new DataObject(DataFormats.FileDrop, allPaths);
                    DragDrop.DoDragDrop(element, data, DragDropEffects.Copy);
                }
            }
        }
    }

    internal void ZipShelfButton_Click(object sender, RoutedEventArgs e)
    {
        string? zipPath = _shelfService.CreateZipBundle();
        if (!string.IsNullOrEmpty(zipPath))
        {
            DropShelfService.CopyPath(zipPath);
            _machine.PushToast(new ToastContent(
                "\uF012",
                "ZIP Archive Created",
                $"{Path.GetFileName(zipPath)} \u00B7 Path copied",
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.8),
                TargetView: "shelf",
                Priority: ToastPriority.Feedback,
                Category: "shelfzip"));
        }
    }

    internal void WindowsShareButton_Click(object sender, RoutedEventArgs e)
    {
        var paths = _shelfService.Items.Select(i => i.FullPath).Where(File.Exists).ToList();
        if (paths.Count == 0) return;
        _windowsShareService.ShareFiles(paths);
    }

    internal void ItemWindowsShare_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path } && File.Exists(path))
        {
            _windowsShareService.ShareFiles(new[] { path }, Path.GetFileName(path));
        }
    }

    internal void PhoneShareButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int duration = _settingsService.Current.PhoneBridgeDurationMinutes;
            string? password = _settingsService.Current.PhoneBridgePassword;
            var session = _lanQuickShareService.StartSession(duration, password: password);
            try { SafeClipboard.TrySetText(session.Url); } catch { }
            string lockTag = session.RequiresPassword ? " · Password protected" : string.Empty;
            _machine.PushToast(new ToastContent(
                "\uE8EA",
                "Phone Bridge Ready",
                $"{session.DisplayAddress} (Link copied · {duration}m{lockTag})",
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(5.5),
                TargetView: "phonebridge",
                Priority: ToastPriority.Feedback,
                Category: "phonebridge"));
        }
        catch (Exception ex)
        {
            App.Log($"[PhoneShare] Failed to start: {ex.Message}");
        }
    }

    internal void SummonNotesPopupWindow()
    {
        try
        {
            var popup = new Views.NotesPopupWindow(_notesService, _shelfService);
            popup.ShowAtCursor();
        }
        catch (Exception ex)
        {
            App.Log($"[NotesPopup] Failed to open: {ex.Message}");
        }
    }

    internal void ShelfItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement { DataContext: ShelfItem item })
        {
            try
            {
                if (File.Exists(item.FullPath) || Directory.Exists(item.FullPath))
                {
                    using var proc = Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
                    _machine.PushToast(new ToastContent(
                        "\uE8E5",
                        "Opening Item",
                        item.FileName,
                        ThemeBrushes.BlueColor,
                        TimeSpan.FromSeconds(1.8),
                        TargetView: "shelf",
                        Priority: ToastPriority.Feedback,
                        Category: "shelfopen"));
                }
            }
            catch (Exception ex)
            {
                App.Log($"[DropShelf] Open failed: {ex.Message}");
            }
            e.Handled = true;
            return;
        }

        _dragStartPoint = e.GetPosition(this);
    }

    internal void ShelfItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement { DataContext: ShelfItem item } element)
        {
            var diff = _dragStartPoint - e.GetPosition(this);
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var data = new DataObject(DataFormats.FileDrop, new[] { item.FullPath });
                DragDrop.DoDragDrop(element, data, DragDropEffects.Copy);
            }
        }
    }

    internal void RemoveShelfItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            _shelfService.Remove(path);
        }
    }

    internal void CopyShelfPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            DropShelfService.CopyPath(path);
            _machine.PushToast(new ToastContent(
                "\uE8C8",
                "Path Copied",
                Path.GetFileName(path),
                ThemeBrushes.BlueColor,
                TimeSpan.FromSeconds(2),
                TargetView: null,
                Priority: ToastPriority.Feedback,
                Category: "clipboard"));
        }
    }

    internal async void ItemOcr_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path } && File.Exists(path))
        {
            e.Handled = true;
            try
            {
                string? text = await ScreenToolsService.ExtractTextFromFileAsync(path);
                if (_isClosing) return;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    try { SafeClipboard.TrySetText(text); } catch { }
                    string preview = text.Replace("\r", " ").Replace("\n", " ").Trim();
                    if (preview.Length > 36) preview = preview[..33] + "...";
                    NotifyCaptureAction("OCR Copied to Clipboard", preview);
                }
                else
                {
                    NotifyCaptureAction("OCR Notice", "No readable text found in image");
                }
            }
            catch (Exception ex)
            {
                App.Log($"[DropShelf] OCR failed: {ex.Message}");
            }
        }
    }

    internal void RevealShelfItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            DropShelfService.Reveal(path);
        }
    }

    internal void PinShelfItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: string path })
        {
            bool pinned = _shelfService.TogglePin(path);
            _machine.PushToast(new ToastContent(
                pinned ? "\uE718" : "\uE77A",
                pinned ? "Pinned to Shelf" : "Unpinned from Shelf",
                Path.GetFileName(path),
                pinned ? ThemeBrushes.AmberColor : ThemeBrushes.MutedGreyColor,
                TimeSpan.FromSeconds(1.8),
                TargetView: "shelf",
                Priority: ToastPriority.Feedback,
                Category: "shelfpin"));
        }
    }

    internal void SummonBasketButton_Click(object sender, RoutedEventArgs e)
    {
        var basket = _jiggleBasketService?.SummonBasketAtCursor(force: true);
        if (basket != null && _shelfService.HasItems)
        {
            basket.AddFilesToBasket(_shelfService.Items.Select(i => i.FullPath));
        }
    }

    internal void ShelfActionsButton_Click(object sender, RoutedEventArgs e)
    {
        var paths = _shelfService.Items.Select(i => i.FullPath).ToList();
        if (paths.Count == 0) return;
        OpenFileActionsContextMenu(paths, ShelfActionsButton);
    }

    internal void ShelfItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ShelfItem item } el)
        {
            e.Handled = true;
            OpenFileActionsContextMenu(new[] { item.FullPath }, el, item);
        }
    }

    private void OpenFileActionsContextMenu(IReadOnlyList<string> paths, UIElement? placementTarget, ShelfItem? singleItem = null)
    {
        var menu = new ContextMenu();
        var actions = FileActionPipeline.GetAvailableActions(paths);

        foreach (var act in actions)
        {
            var mi = new MenuItem { Header = $"{act.Label} — {act.Subtitle}" };
            var capturedKind = act.Kind;
            mi.Click += async (_, _) =>
            {
                try
                {
                    await RunShelfFileActionAsync(capturedKind, paths, act.Label);
                }
                catch (Exception ex)
                {
                    App.Log($"[DropShelf] File action failed: {ex.Message}");
                }
            };
            menu.Items.Add(mi);
        }

        if (menu.Items.Count > 0) menu.Items.Add(new Separator());

        var zipItem = new MenuItem { Header = "📦 Bundle into ZIP Archive" };
        zipItem.Click += (_, _) => ZipShelfButton_Click(zipItem, new RoutedEventArgs());
        menu.Items.Add(zipItem);

        var shareWinItem = new MenuItem { Header = "🔗 Share via Windows Sheet..." };
        shareWinItem.Click += (_, _) => _windowsShareService.ShareFiles(paths);
        menu.Items.Add(shareWinItem);

        var phoneShareItem = new MenuItem { Header = "📱 Phone Drop / LAN Quickshare..." };
        phoneShareItem.Click += (_, _) => PhoneShareButton_Click(phoneShareItem, new RoutedEventArgs());
        menu.Items.Add(phoneShareItem);

        var copyPathsItem = new MenuItem
        {
            Header = paths.Count > 1 ? $"📋 Copy All File Paths ({paths.Count})" : "📋 Copy File Path"
        };
        copyPathsItem.Click += (_, _) =>
        {
            if (SafeClipboard.TrySetText(string.Join(Environment.NewLine, paths)))
            {
                NotifyCaptureAction("Paths Copied", paths.Count > 1 ? $"{paths.Count} paths copied to clipboard" : System.IO.Path.GetFileName(paths[0]));
            }
        };
        menu.Items.Add(copyPathsItem);

        if (singleItem == null && paths.Count > 0)
        {
            var revealFirstItem = new MenuItem { Header = "📂 Show First in Explorer" };
            revealFirstItem.Click += (_, _) => DropShelfService.Reveal(paths[0]);
            menu.Items.Add(revealFirstItem);
        }

        if (singleItem != null)
        {
            if (singleItem.IsImage)
            {
                var ocrItem = new MenuItem { Header = "🔍 Extract Text (Offline OCR)" };
                ocrItem.Click += async (_, _) =>
                {
                    try
                    {
                        string? text = await ScreenToolsService.ExtractTextFromFileAsync(singleItem.FullPath);
                        if (_isClosing) return;
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            try { SafeClipboard.TrySetText(text); } catch { }
                            string preview = text.Replace("\r", " ").Replace("\n", " ").Trim();
                            if (preview.Length > 36) preview = preview[..33] + "...";
                            NotifyCaptureAction("OCR Copied to Clipboard", preview);
                        }
                        else
                        {
                            NotifyCaptureAction("OCR Notice", "No readable text found in image");
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Log($"[DropShelf] Menu OCR failed: {ex.Message}");
                    }
                };
                menu.Items.Add(ocrItem);
            }

            var pinItem = new MenuItem { Header = singleItem.IsPinned ? "📌 Unpin from Shelf" : "📌 Pin to Shelf" };
            pinItem.Click += (_, _) => _shelfService.TogglePin(singleItem.FullPath);
            menu.Items.Add(pinItem);

            var revealItem = new MenuItem { Header = "📂 Show in Explorer" };
            revealItem.Click += (_, _) => DropShelfService.Reveal(singleItem.FullPath);
            menu.Items.Add(revealItem);
        }

        if (menu.Items.Count == 0) return;
        menu.PlacementTarget = placementTarget;
        menu.IsOpen = true;
    }

    private async Task RunShelfFileActionAsync(FileActionKind kind, IReadOnlyList<string> paths, string label)
    {
        string actId = $"convert_{Guid.NewGuid():N}";
        _liveActivityHub?.PublishOrUpdate(new LiveActivityItem
        {
            Id = actId,
            Category = "converter",
            Glyph = "\uE70F",
            Title = label,
            Subtitle = $"Processing {paths.Count} file(s)...",
            AccentColor = ThemeBrushes.AmberColor,
            Progress = 0.05,
            Priority = 85,
            TargetView = "shelf"
        });

        var progress = new Progress<double>(p => _liveActivityHub?.UpdateProgress(actId, p, $"{(int)(p * 100)}%"));
        var res = await FileActionPipeline.ExecuteAsync(kind, paths, progress);
        _liveActivityHub?.Complete(actId);

        if (res.Success)
        {
            if (!string.IsNullOrEmpty(res.CopiedClipboardText))
            {
                try { SafeClipboard.TrySetText(res.CopiedClipboardText); } catch { }
            }
            if (res.OutputPaths.Count > 0)
            {
                _shelfService.AddPaths(res.OutputPaths);
            }
        }
        NotifyFileActionCompleted(res);
    }

    internal void NotifyFileActionCompleted(FileActionResult res)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _machine.PushToast(new ToastContent(
                res.Success ? "\uE73E" : "\uE7BA",
                res.Success ? "File Converter" : "Conversion Failed",
                res.Message,
                res.Success ? ThemeBrushes.GreenColor : ThemeBrushes.RedColor,
                TimeSpan.FromSeconds(2.8),
                TargetView: "shelf",
                Priority: ToastPriority.Feedback,
                Category: "converter"));
        });
    }

    internal void NotifyCaptureAction(string title, string subtitle)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _machine.PushToast(new ToastContent(
                "\uE722",
                title,
                subtitle,
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.6),
                TargetView: "clipboard",
                Priority: ToastPriority.Feedback,
                Category: "capture"));
        });
    }

}
