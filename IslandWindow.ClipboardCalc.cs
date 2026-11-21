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
    //  Clipboard History, Downloads & Quick Calculator
    // =================================================================

    private ClipboardCategoryFilter _activeClipFilter = ClipboardCategoryFilter.All;
    private string? _clipSearchQuery;

    private void OnClipboardChanged() => RefreshClipboardView();

    private void RefreshClipboardView()
    {
        var filtered = _clipboardService.GetFilteredItems(_clipSearchQuery, _activeClipFilter);
        int totalCount = _clipboardService.Items.Count;
        ClipboardBadgeText.Text = totalCount.ToString();
        ClipboardBadgeText.Visibility = totalCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(_clipSearchQuery) || _activeClipFilter != ClipboardCategoryFilter.All)
        {
            ClipboardCountText.Text = $"{filtered.Count} / {totalCount} items";
        }
        else
        {
            ClipboardCountText.Text = $"{totalCount} item{(totalCount == 1 ? "" : "s")}";
        }

        ClipboardEmptyPlaceholder.Visibility = filtered.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        if (ClipboardQueueBtnText != null)
        {
            ClipboardQueueBtnText.Text = _clipboardService.PasteQueueCount > 0
                ? $"Queue ({_clipboardService.PasteQueueCount})"
                : "Queue";
        }
        SyncObservableCollection(_clipboardItems, filtered);
        UpdateClipboardScrollButtons();
    }

    internal void ClipFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        _activeClipFilter = tag switch
        {
            "Pinned" => ClipboardCategoryFilter.Pinned,
            "Links" => ClipboardCategoryFilter.Links,
            "Images" => ClipboardCategoryFilter.Images,
            "Colors" => ClipboardCategoryFilter.Colors,
            "Code" => ClipboardCategoryFilter.Code,
            _ => ClipboardCategoryFilter.All
        };

        UpdateClipFilterButtonsUI();
        RefreshClipboardView();
        if (ClipboardScrollViewer != null)
        {
            SmoothScrollTo(ClipboardScrollViewer, 0);
        }
    }

    private void UpdateClipFilterButtonsUI()
    {
        var buttons = new (Button? Btn, ClipboardCategoryFilter Filter)[]
        {
            (ClipFilterAllBtn, ClipboardCategoryFilter.All),
            (ClipFilterPinnedBtn, ClipboardCategoryFilter.Pinned),
            (ClipFilterLinksBtn, ClipboardCategoryFilter.Links),
            (ClipFilterImagesBtn, ClipboardCategoryFilter.Images),
            (ClipFilterColorsBtn, ClipboardCategoryFilter.Colors),
            (ClipFilterCodeBtn, ClipboardCategoryFilter.Code)
        };

        foreach (var (btn, filter) in buttons)
        {
            if (btn == null) continue;
            bool isActive = _activeClipFilter == filter;
            btn.Background = isActive
                ? ThemeBrushes.FilterPillActiveBg
                : ThemeBrushes.FilterPillInactiveBg;
            btn.BorderBrush = isActive
                ? (Brush)FindResource("AccentBrush")
                : ThemeBrushes.FilterPillInactiveBorder;
        }
    }

    internal void ClipboardSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _clipSearchQuery = ClipboardSearchBox?.Text?.Trim();
        if (ClearClipboardSearchBtn != null)
        {
            ClearClipboardSearchBtn.Visibility = string.IsNullOrEmpty(_clipSearchQuery)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
        RefreshClipboardView();
    }

    internal void ClipboardSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (ClipboardSearchBox != null)
            {
                ClipboardSearchBox.Text = string.Empty;
            }
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (_clipboardItems.Count > 0)
            {
                CopyClipboardItem(_clipboardItems[0]);
            }
            e.Handled = true;
        }
    }

    internal void ClearClipboardSearch_Click(object sender, RoutedEventArgs e)
    {
        if (ClipboardSearchBox != null)
        {
            ClipboardSearchBox.Text = string.Empty;
        }
    }

    internal void ClearClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        int initialCount = _clipboardService.Items.Count;
        if (initialCount == 0) return;

        _clipboardService.Clear();
        TriggerLiquidImpulse(140, -60, 20);

        int remainingPinned = _clipboardService.Items.Count;
        int cleared = initialCount - remainingPinned;

        _machine.PushToast(new ToastContent(
            "\uE74D",
            "History Cleared",
            cleared > 0 ? $"{cleared} items cleared \u00B7 {remainingPinned} pinned kept" : "Pinned items preserved",
            ThemeBrushes.RedColor,
            TimeSpan.FromSeconds(2.4),
            TargetView: "clipboard",
            Priority: ToastPriority.Feedback,
            Category: "clipboardclear"));
    }

    internal void OpenUrlClipboardItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string rawUrl } && !string.IsNullOrWhiteSpace(rawUrl))
        {
            string target = rawUrl.Trim();
            if (!target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                target = "https://" + target;
            }
            if (SafeProcessLauncher.TryOpenUrl(target))
            {
                _machine.PushToast(new ToastContent(
                    "\uE8A7",
                    "Opening Link",
                    target,
                    ThemeBrushes.BlueColor,
                    TimeSpan.FromSeconds(2.0),
                    TargetView: "clipboard",
                    Priority: ToastPriority.Feedback,
                    Category: "browser"));
            }
            e.Handled = true;
        }
    }

    internal void CopyColorAsRgb_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string rgb } && !string.IsNullOrEmpty(rgb))
        {
            if (SafeClipboard.TrySetText(rgb))
            {
                _machine.PushToast(new ToastContent(
                    "\uE8C8",
                    "RGB Copied",
                    rgb,
                    ThemeBrushes.AmberColor,
                    TimeSpan.FromSeconds(1.8),
                    TargetView: "clipboard",
                    Priority: ToastPriority.Feedback,
                    Category: "clipboard"));
            }
            e.Handled = true;
        }
    }

    internal void CopyPlainClipboardItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: string id })
        {
            var item = _clipboardService.Items.FirstOrDefault(i => i.Id == id);
            if (item != null)
            {
                CopyClipboardItem(item, plainTextOnly: true);
            }
        }
    }

    internal async void OcrCardItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            e.Handled = true;
            try
            {
                var item = _clipboardService.Items.FirstOrDefault(i => i.Id == id);
                if (item?.ImagePreview is BitmapSource bmp)
                {
                    _machine.PushToast(new ToastContent(
                        "\uE8B7",
                        "Extracting Text",
                        "Running OCR on image...",
                        ThemeBrushes.BlueColor,
                        TimeSpan.FromSeconds(1.5),
                        TargetView: "clipboard",
                        Priority: ToastPriority.Feedback,
                        Category: "ocr"));

                    string? text = await ScreenToolsService.ExtractTextFromBitmapAsync(bmp);
                    if (_isClosing) return;

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        try { Clipboard.SetText(text); } catch { }
                        string preview = text.Replace("\r", " ").Replace("\n", " ").Trim();
                        if (preview.Length > 36) preview = preview[..33] + "...";

                        _machine.PushToast(new ToastContent(
                            "\uE73E",
                            "OCR Text Copied",
                            preview,
                            ThemeBrushes.GreenColor,
                            TimeSpan.FromSeconds(3.0),
                            TargetView: "clipboard",
                            Priority: ToastPriority.Feedback,
                            Category: "ocr"));
                    }
                    else
                    {
                        _machine.PushToast(new ToastContent(
                            "\uE7BA",
                            "No Text Detected",
                            "Windows OCR found no readable text in image",
                            ThemeBrushes.AmberColor,
                            TimeSpan.FromSeconds(2.2),
                            TargetView: "clipboard",
                            Priority: ToastPriority.Feedback,
                            Category: "ocr"));
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log($"[Clipboard] OCR card failed: {ex.Message}");
            }
        }
    }

    internal void CopyCardButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            var item = _clipboardService.Items.FirstOrDefault(i => i.Id == id);
            if (item != null)
            {
                CopyClipboardItem(item);
            }
            e.Handled = true;
        }
    }

    private Point _clipDragStart;
    private double _clipDragStartOffset;
    private bool _clipIsDragging;

    private void ClipboardItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _clipDragStart = e.GetPosition(this);
        _clipDragStartOffset = ClipboardScrollViewer?.HorizontalOffset ?? 0;
        _clipIsDragging = false;
        if (sender is UIElement el)
        {
            el.CaptureMouse();
        }
    }

    private void ClipboardItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is UIElement el && el.IsMouseCaptured)
        {
            Point current = e.GetPosition(this);
            double deltaX = current.X - _clipDragStart.X;
            if (!_clipIsDragging && Math.Abs(deltaX) > SystemParameters.MinimumHorizontalDragDistance)
            {
                _clipIsDragging = true;
            }

            if (_clipIsDragging && ClipboardScrollViewer != null)
            {
                ClipboardScrollViewer.ScrollToHorizontalOffset(_clipDragStartOffset - deltaX);
            }
        }
    }

    private void ClipboardItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement el && el.IsMouseCaptured)
        {
            el.ReleaseMouseCapture();
        }

        if (!_clipIsDragging && sender is FrameworkElement { DataContext: ClipboardItem item })
        {
            CopyClipboardItem(item);
        }
        _clipIsDragging = false;
    }

    internal void ClipboardItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClipboardItem item })
        {
            bool plainTextOnly = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            CopyClipboardItem(item, plainTextOnly);
        }
    }

    private void CopyClipboardItem(ClipboardItem item, bool plainTextOnly = false)
    {
        _clipboardService.CopyItem(item, plainTextOnly);
        string preview = item.Title;
        if (preview.Length > 24) preview = preview[..21] + "...";
        _machine.PushToast(new ToastContent(
            "\uE8C8",
            plainTextOnly ? "Copied as Plain Text" : "Copied to Clipboard",
            preview,
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(1.8),
            TargetView: "clipboard",
            Priority: ToastPriority.Feedback,
            Category: "clipboard"));
    }

    internal void EnqueueClipboardItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: string id })
        {
            var item = _clipboardService.Items.FirstOrDefault(i => i.Id == id);
            if (item != null)
            {
                _clipboardService.EnqueueForSequentialPaste(item);
                RefreshClipboardView();
                _machine.PushToast(new ToastContent(
                    "\uE762",
                    $"Added to Paste Queue (#{_clipboardService.PasteQueueCount})",
                    item.Title,
                    ThemeBrushes.BlueColor,
                    TimeSpan.FromSeconds(1.8),
                    TargetView: "clipboard",
                    Priority: ToastPriority.Feedback,
                    Category: "clipboardqueue"));
            }
        }
    }

    internal void ClipboardQuickPickerButton_Click(object sender, RoutedEventArgs e)
    {
        SummonClipboardPopupWindow();
    }

    internal void SummonClipboardPopupWindow()
    {
        try
        {
            var popup = new Views.ClipboardPopupWindow(_clipboardService);
            popup.ShowAtCursor();
        }
        catch (Exception ex)
        {
            App.Log($"[ClipboardPopup] Failed to open: {ex.Message}");
        }
    }

    internal void CaptureRegionButton_Click(object sender, RoutedEventArgs e)
    {
        LaunchElementCaptureOverlay();
    }

    internal void LaunchElementCaptureOverlay()
    {
        try
        {
            _machine.CollapseNow();
            Dispatcher.InvokeAsync(() =>
            {
                var overlay = new Views.CaptureOverlayWindow(_shelfService);
                overlay.Show();
                overlay.Activate();
            }, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            App.Log($"[ElementCapture] Failed to launch overlay: {ex.Message}");
        }
    }

    internal void RemoveClipboardItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            _clipboardService.Remove(id);
            e.Handled = true;
        }
    }

    internal void PinClipboardItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            bool pinned = _clipboardService.TogglePin(id);
            _machine.PushToast(new ToastContent(
                pinned ? "\uE840" : "\uE718",
                pinned ? "Clip Pinned" : "Clip Unpinned",
                pinned ? "Protected from history clear & eviction" : "Standard history retention",
                pinned ? ThemeBrushes.AmberColor : ThemeBrushes.MutedGreyColor,
                TimeSpan.FromSeconds(1.6),
                TargetView: "clipboard",
                Priority: ToastPriority.Feedback,
                Category: "clipboardpin"));
            e.Handled = true;
        }
    }

    internal void SendClipboardItemToShelf_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            var item = _clipboardService.Items.FirstOrDefault(i => i.Id == id);
            if (item != null)
            {
                string? savedPath = null;
                if (item.ImagePreview is BitmapSource bmp)
                {
                    savedPath = _shelfService.SaveBitmapToShelf(bmp);
                }
                else if (!string.IsNullOrWhiteSpace(item.RawText))
                {
                    savedPath = _shelfService.SaveTextSnippetToShelf(item.RawText);
                }

                if (!string.IsNullOrEmpty(savedPath))
                {
                    TriggerLiquidImpulse(140, -60, 18);
                    _machine.PushToast(new ToastContent(
                        "\uE8B7",
                        "Pinned to Drop Shelf",
                        Path.GetFileName(savedPath),
                        ThemeBrushes.GreenColor,
                        TimeSpan.FromSeconds(2.2),
                        TargetView: "shelf",
                        Priority: ToastPriority.Feedback,
                        Category: "shelfpaste"));
                }
            }
            e.Handled = true;
        }
    }

    internal async void EyedropperButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _machine.PushToast(new ToastContent(
                "\uEF3C",
                "Eyedropper Armed",
                "Sampling pixel under cursor in 1.2s...",
                ThemeBrushes.AmberColor,
                TimeSpan.FromSeconds(1.2),
                TargetView: "clipboard",
                Priority: ToastPriority.Feedback,
                Category: "eyedropper"));

            await Task.Delay(1200);
            if (_isClosing) return;

            var sample = ScreenToolsService.SampleCursorPixelColor();
            try { Clipboard.SetText(sample.Hex); } catch { }

            _machine.PushToast(new ToastContent(
                "\uEF3C",
                $"Sampled {sample.Hex}",
                $"{sample.RgbText} \u00B7 Copied to clipboard",
                sample.Color,
                TimeSpan.FromSeconds(3.0),
                TargetView: "clipboard",
                Priority: ToastPriority.Feedback,
                Category: "eyedropper"));
        }
        catch (Exception ex)
        {
            App.Log($"[Clipboard] Eyedropper failed: {ex.Message}");
        }
    }

    internal async void OcrClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Media.Imaging.BitmapSource? bmp = null;
            try
            {
                if (Clipboard.ContainsImage())
                {
                    bmp = Clipboard.GetImage();
                }
            }
            catch { }

            bmp ??= _clipboardService.Items.FirstOrDefault(i => i.ImagePreview != null)?.ImagePreview as System.Windows.Media.Imaging.BitmapSource;
            if (bmp == null)
            {
                _machine.PushToast(new ToastContent(
                    "\uE7BA",
                    "No Image in Clipboard",
                    "Copy a screenshot first (Win+Shift+S) to extract text",
                    ThemeBrushes.AmberColor,
                    TimeSpan.FromSeconds(2.5),
                    TargetView: "clipboard",
                    Priority: ToastPriority.Feedback,
                    Category: "ocr"));
                return;
            }

            string? text = await ScreenToolsService.ExtractTextFromBitmapAsync(bmp);
            if (_isClosing) return;

            if (string.IsNullOrWhiteSpace(text))
            {
                _machine.PushToast(new ToastContent(
                    "\uE7BA",
                    "No Text Detected",
                    "Windows OCR found no readable text in image",
                    ThemeBrushes.AmberColor,
                    TimeSpan.FromSeconds(2.2),
                    TargetView: "clipboard",
                    Priority: ToastPriority.Feedback,
                    Category: "ocr"));
                return;
            }

            try { Clipboard.SetText(text); } catch { }
            string preview = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (preview.Length > 36) preview = preview[..33] + "...";

            _machine.PushToast(new ToastContent(
                "\uE73E",
                "OCR Text Copied",
                preview,
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(3.0),
                TargetView: "clipboard",
                Priority: ToastPriority.Feedback,
                Category: "ocr"));
        }
        catch (Exception ex)
        {
            App.Log($"[Clipboard] OCR clipboard button failed: {ex.Message}");
        }
    }

    private void OnDownloadsChanged()
    {
        bool hasActive = _downloadService.HasActiveDownloads;
        if (hasActive)
        {
            CompactDownloadText.Text = _downloadService.LatestDownloadStatusText
                ?? _downloadService.LatestDownloadName
                ?? "DOWNLOADING";
            AnimateDownloadRing(true);
        }
        else
        {
            AnimateDownloadRing(false);
        }

        EvaluateActivity();
        EnsureRendering();
    }

    private void AnimateDownloadRing(bool active)
    {
        if (DownloadRingRotate == null) return;
        if (active)
        {
            var anim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(1.2),
                RepeatBehavior = RepeatBehavior.Forever
            };
            DownloadRingRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
        }
        else
        {
            DownloadRingRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }

    internal void CompactDownloadHost_Click(object sender, MouseButtonEventArgs e)
    {
        DownloadTrackerService.OpenDownloadsFolder();
        e.Handled = true;
    }

    private void OnDownloadCompleted(string fileName, string fullPath)
    {
        _shelfService.AddPaths(new[] { fullPath });
        _machine.PushToast(new ToastContent(
            "\uE896",
            "Download Complete",
            fileName,
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(3.5),
            TargetView: "shelf",
            Priority: ToastPriority.Notification));
    }

    internal void CalcInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = CalcInputBox.Text.Trim();
        CalcPlaceholder.Visibility = string.IsNullOrWhiteSpace(query) ? Visibility.Visible : Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(query))
        {
            CalcResultHost.Visibility = Visibility.Collapsed;
            return;
        }

        var (success, result, explanation) = QuickCalcService.Evaluate(query);
        if (success)
        {
            CalcExplanationText.Text = explanation;
            CalcResultText.Text = result;
            CalcResultHost.Visibility = Visibility.Visible;
        }
        else
        {
            CalcResultHost.Visibility = Visibility.Collapsed;
        }
    }

    internal void CalcInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && CalcResultHost.Visibility == Visibility.Visible)
        {
            CommitCalcResult();
            e.Handled = true;
        }
    }

    internal void CalcResult_Click(object sender, MouseButtonEventArgs e)
    {
        CommitCalcResult();
        e.Handled = true;
    }

    private readonly System.Collections.ObjectModel.ObservableCollection<CalcHistoryEntry> _calcHistory = new();

    private void CommitCalcResult()
    {
        string res = CalcResultText.Text;
        string expr = CalcInputBox.Text.Trim();
        if (!string.IsNullOrEmpty(res))
        {
            QuickCalcService.SetLastAnswer(res);
            if (!string.IsNullOrEmpty(expr))
            {
                if (CalcHistoryItemsControl != null && CalcHistoryItemsControl.ItemsSource == null)
                {
                    CalcHistoryItemsControl.ItemsSource = _calcHistory;
                }

                var existing = _calcHistory.FirstOrDefault(h => string.Equals(h.Expression, expr, StringComparison.OrdinalIgnoreCase));
                if (existing != null) _calcHistory.Remove(existing);
                _calcHistory.Insert(0, new CalcHistoryEntry(expr, res));
                while (_calcHistory.Count > 6)
                {
                    _calcHistory.RemoveAt(_calcHistory.Count - 1);
                }
            }

            try { Clipboard.SetText(res); } catch { }
            _machine.PushToast(new ToastContent(
                "\uE73E",
                "Copied to Clipboard",
                $"{res} (saved to 'ans')",
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "calc",
                Priority: ToastPriority.Feedback,
                Category: "clipboard"));
        }
    }

    internal void CalcHistoryChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CalcHistoryEntry entry }) return;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            try { Clipboard.SetText(entry.Result); } catch { }
            _machine.PushToast(new ToastContent(
                "\uE73E",
                "Result Copied",
                entry.Result,
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(1.8),
                TargetView: "calc",
                Priority: ToastPriority.Feedback,
                Category: "clipboard"));
        }
        else if (CalcInputBox != null)
        {
            CalcInputBox.Text = entry.Expression;
            CalcInputBox.CaretIndex = CalcInputBox.Text.Length;
            CalcInputBox.Focus();
        }
    }

}
