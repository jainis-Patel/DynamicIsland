using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

public partial class IslandWindow
{
    private Point _dragStartPoint;
    private string? _recordingHotkeyAction;
    private bool _isAudioAppMixerMode;

    // ═════════════════════════════════════════════════════════════════
    //  View Navigation & Dock Strip
    // ═════════════════════════════════════════════════════════════════

    private void SwitchExpandedView(ExpandedView view)
    {
        if (_machine.State != IslandState.Expanded)
        {
            _machine.ExpandNow();
        }

        var previousView = _currentExpandedView;
        if (_currentExpandedView == view && view != ExpandedView.Media)
        {
            view = ExpandedView.Media;
        }

        var (targetContainer, targetButton) = _viewRegistry.TryGetValue(view, out var entry)
            ? entry
            : _viewRegistry[ExpandedView.Media];

        SetDockActive(targetButton);
        AnimateContainerSwitch(targetContainer);
        _currentExpandedView = view;
        _recordingHotkeyAction = null;
        SetKeyboardFocusable(view == ExpandedView.Calc);

        // Morph the pill bigger (480x336) when selecting Settings, or back to 452x190 for other views,
        // using smooth spring target easing without jarring velocity spikes.
        SyncSpringMotionPreferences();
        var targetShape = GetTargetShape(_machine.State, view);
        ExpandedPanel.Width = targetShape.Width;
        ExpandedPanel.Height = targetShape.Height;
        _width.Target = targetShape.Width;
        _height.Target = targetShape.Height;
        _radius.Target = targetShape.Radius;

        if (view == ExpandedView.Settings && previousView != ExpandedView.Settings)
        {
            UpdateStartupUI();
            UpdateVolumeOsdUI();
            UpdateHotkeysUI();
            UpdateDockVisibilityUI();
        }
        else if (previousView != view)
        {
            TriggerLiquidImpulse(55, -20, 8);
        }
        EnsureRendering();

        if (view == ExpandedView.Calc)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                Keyboard.Focus(CalcInputBox);
                CalcInputBox.Focus();
                CalcInputBox.SelectAll();
            }));
        }
        else if (view == ExpandedView.Audio && _isAudioAppMixerMode)
        {
            _audioEndpointService.RefreshAppSessions();
        }
        else if (view == ExpandedView.Peripherals)
        {
            _ = _peripheralService.RefreshAsync();
        }
        else if (view == ExpandedView.Network)
        {
            _ = _networkService.RefreshRadioStatesAsync();
            _networkService.Sample();
        }
    }

    private void SecondaryDroplet_Click(object sender, MouseButtonEventArgs e)
    {
        var targetView = ActivityResolver.ResolveCompactTargetView(_secondaryCompactHost);
        _machine.ExpandNow();
        if (targetView != ExpandedView.Media)
        {
            SwitchExpandedView(targetView);
        }
        e.Handled = true;
    }

    private void HorizontalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - (e.Delta * 0.65));
            e.Handled = true;
        }
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e) => _machine.CollapseNow();

    private void IslandWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_recordingHotkeyAction != null)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            {
                return;
            }

            string action = _recordingHotkeyAction;
            _recordingHotkeyAction = null;
            SetKeyboardFocusable(false);

            if (key != Key.Escape)
            {
                int vk = KeyInterop.VirtualKeyFromKey(key);
                if (vk > 0)
                {
                    _settingsService.Update(s => s.CustomHotkeys[action] = (uint)vk, nameof(AppSettings.CustomHotkeys));
                    _machine.PushToast(new ToastContent(
                        "\uE765",
                        "Hotkey Bound",
                        $"{action} → Ctrl+Alt+{key}",
                        ThemeBrushes.GreenColor,
                        TimeSpan.FromSeconds(1.8),
                        TargetView: "settings",
                        Priority: ToastPriority.Feedback,
                        Category: "hotkey"));
                }
            }
            UpdateHotkeysUI();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _machine.State == IslandState.Expanded)
        {
            if (_currentExpandedView != ExpandedView.Media)
                SwitchExpandedView(ExpandedView.Media);
            else
                _machine.CollapseNow();

            e.Handled = true;
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  Media, Stopwatch, Pomodoro Countdown & Telemetry
    // ═════════════════════════════════════════════════════════════════

    private void CommitSeek()
    {
        var media = _mediaService.Current;
        if (media is null || media.Duration <= TimeSpan.Zero) return;
        double targetSec = (TimelineSlider.Value / 100.0) * media.Duration.TotalSeconds;
        _mediaService.Seek(TimeSpan.FromSeconds(targetSec));
    }

    private void MediaSourceBadge_Click(object sender, MouseButtonEventArgs e)
    {
        string? switchedTo = _mediaService.CycleNextSession();
        if (!string.IsNullOrEmpty(switchedTo))
        {
            _machine.PushToast(new ToastContent(
                "\uE8D6",
                "Media Source Switched",
                $"Active player: {switchedTo}",
                ThemeBrushes.BlueColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "media",
                Priority: ToastPriority.Feedback,
                Category: "mediasource"));
        }
        e.Handled = true;
    }

    private void OnTrackChanged()
    {
        var media = _mediaService.Current;
        if (media is null || string.IsNullOrWhiteSpace(media.Title)) return;

        if (_machine.State != IslandState.Expanded)
        {
            var accent = media.Accent ?? ThemeBrushes.AmberColor;
            _machine.PushToast(new ToastContent(
                "\uE8D6",
                media.Title,
                media.Artist,
                accent,
                TimeSpan.FromSeconds(2.8),
                TargetView: "media",
                Priority: ToastPriority.System,
                Category: "track"));
        }
    }

    private void OnMediaChanged()
    {
        var media = _mediaService.Current;
        bool hasMedia = media is not null && !string.IsNullOrWhiteSpace(media.Title);
        bool callActive = !string.IsNullOrWhiteSpace(_privacyService.ActiveCallApp);

        if (hasMedia && media is not null)
        {
            if (!callActive)
            {
                ExpandedMediaHeader.Visibility = Visibility.Visible;
                ExpandedClockHeader.Visibility = Visibility.Collapsed;
            }

            ExpandedTitle.Text = media.Title;
            ExpandedArtist.Text = string.IsNullOrWhiteSpace(media.Artist) ? "Unknown Artist" : media.Artist;
            string sourceLabel = string.IsNullOrWhiteSpace(media.Source) ? "Media" : media.Source;
            ExpandedSourceApp.Text = media.ActiveSessionCount > 1
                ? $"{sourceLabel} · 1/{media.ActiveSessionCount}"
                : sourceLabel;

            PlayPauseGlyph.Text = media.IsPlaying ? "\uE769" : "\uE768";
            PrevButton.IsEnabled = media.CanPrevious;
            NextButton.IsEnabled = media.CanNext;

            if (media.Artwork is not null)
            {
                CompactArtImage.Source = media.Artwork;
                ExpandedArtImage.Source = media.Artwork;
                CompactArtImage.Visibility = Visibility.Visible;
                ExpandedArtImage.Visibility = Visibility.Visible;
                CompactArtFallback.Visibility = Visibility.Collapsed;
                ExpandedArtFallback.Visibility = Visibility.Collapsed;
            }
            else
            {
                CompactArtImage.Visibility = Visibility.Collapsed;
                ExpandedArtImage.Visibility = Visibility.Collapsed;
                CompactArtFallback.Visibility = Visibility.Visible;
                ExpandedArtFallback.Visibility = Visibility.Visible;
            }

            if (media.Accent.HasValue)
            {
                Resources["AccentBrush"] = ThemeBrushes.CreateFrozen(media.Accent.Value);
                PillGlowColor.Color = media.Accent.Value;
                PillGlowSecondaryColor.Color = media.SecondaryAccent ?? media.Accent.Value;
            }

            CompactTrackText.Text = media.Title;
        }
        else
        {
            if (!callActive)
            {
                ExpandedMediaHeader.Visibility = Visibility.Collapsed;
                ExpandedClockHeader.Visibility = Visibility.Visible;
            }

            PlayPauseGlyph.Text = "\uE768";
        }

        EvaluateActivity();
        UpdateMediaTimelineUI();
        EnsureRendering();
    }

    private void ToggleStopwatch()
    {
        if (_countdownService.IsActive)
        {
            _countdownService.TogglePause();
            return;
        }

        if (_stopwatch.IsRunning) _stopwatch.Stop();
        else _stopwatch.Start();
        RefreshStopwatchUI();
    }

    private void StartPomodoroTimer()
    {
        _stopwatch.Reset();
        _lastTimerText = null;
        if (_countdownService.IsRunning)
        {
            _countdownService.TogglePause();
        }
        else
        {
            _countdownService.StartPomodoro();
            _machine.PushToast(new ToastContent(
                "\uE916",
                "Pomodoro Started",
                "25:00 focus countdown active",
                ThemeBrushes.AmberColor,
                TimeSpan.FromSeconds(2.2),
                TargetView: "media",
                Priority: ToastPriority.Feedback,
                Category: "timer"));
        }
    }

    private void ResetStopwatch()
    {
        _stopwatch.Reset();
        _countdownService.Reset();
        _lastTimerText = null;
        RefreshStopwatchUI();
    }

    private void OnCountdownCompleted(string label)
    {
        _lastTimerText = null;
        RefreshStopwatchUI();
        _machine.PushToast(new ToastContent(
            "\uE916",
            $"{label} Complete!",
            "Countdown finished · Time for a break",
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(4.5),
            TargetView: "media",
            Priority: ToastPriority.Critical));
    }

    private void RefreshStopwatchUI()
    {
        bool isCountdown = _countdownService.IsActive;
        bool running = isCountdown ? _countdownService.IsRunning : _stopwatch.IsRunning;
        bool hasProgress = isCountdown ? _countdownService.Remaining < _countdownService.TargetDuration : _stopwatch.Elapsed > TimeSpan.Zero;

        ExpandedTimerModeLabel.Text = isCountdown ? _countdownService.Label : "Stopwatch";
        StartPauseGlyph.Text = running ? "\uE769" : "\uE768";
        StartPauseLabel.Text = running ? "Pause" : hasProgress ? "Resume" : "Start";

        EvaluateActivity();
        UpdateClockAndTimeline();
        EnsureRendering();
    }

    private void ShowNextDemoToast()
    {
        _machine.PushToast(DemoToasts[_demoToastIndex]);
        _demoToastIndex = (_demoToastIndex + 1) % DemoToasts.Length;
    }

    private void Pill_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_machine.State == IslandState.Expanded) return;

        // Shift + Scroll skips tracks horizontally; Alt + Scroll adjusts display brightness; standard Scroll adjusts system master volume
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            if (e.Delta > 0) _mediaService.Next();
            else if (e.Delta < 0) _mediaService.Previous();
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            int pct = _brightnessService.StepBrightness(e.Delta > 0 ? 5 : -5);
            _machine.PushToast(new ToastContent(
                "\uE706",
                "Brightness",
                $"{pct}%",
                ThemeBrushes.AmberColor,
                TimeSpan.FromSeconds(1.8),
                TargetView: "battery",
                Priority: ToastPriority.Feedback,
                Category: "brightness"));
            e.Handled = true;
            return;
        }

        float delta = e.Delta > 0 ? 0.02f : -0.02f;
        _volumeService.StepVolume(delta);
        e.Handled = true;
    }

    private void OnVolumeChanged(float vol, bool isMuted)
    {
        if (_machine.State == IslandState.Expanded) return;

        string glyph = isMuted ? "\uE74F" : (vol > 0.66f ? "\uE767" : vol > 0.33f ? "\uE994" : "\uE993");
        string title = isMuted ? "Volume Muted" : "Volume";
        string subtitle = isMuted ? "0%" : $"{Math.Round(vol * 100)}%";
        Color accent = isMuted ? ThemeBrushes.RedColor : ThemeBrushes.BlueColor;

        _machine.PushToast(new ToastContent(
            glyph,
            title,
            subtitle,
            accent,
            TimeSpan.FromSeconds(1.8),
            TargetView: "audio",
            Priority: ToastPriority.Feedback,
            Category: "volume"));
    }

    private void OnTelemetryUpdated()
    {
        string cpu = $"CPU {_telemetryService.CpuPercent}%";
        string ram = $"RAM {_telemetryService.MemoryPercent}%";
        CpuText.Text = cpu;
        RamText.Text = ram;
        ClockCpuText.Text = cpu;
        ClockRamText.Text = ram;
    }

    // ═════════════════════════════════════════════════════════════════
    //  Drop Shelf ("Drop Notch") Drag & Drop
    // ═════════════════════════════════════════════════════════════════

    private void Window_PreviewDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DropOverlay.Visibility = Visibility.Visible;
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
        var shape = GetTargetShape(_machine.State);
        _width.Target = shape.Width;
        _height.Target = shape.Height;
        EnsureRendering();
    }

    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
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
        ShelfCountText.Text = $"{_shelfService.Count} item{(_shelfService.Count == 1 ? "" : "s")}";
        ShelfEmptyPlaceholder.Visibility = _shelfService.HasItems ? Visibility.Collapsed : Visibility.Visible;
        if (ZipShelfButton != null)
        {
            ZipShelfButton.Visibility = _shelfService.HasItems ? Visibility.Visible : Visibility.Collapsed;
        }
        SyncObservableCollection(_shelfItems, _shelfService.Items);
    }

    private void ZipShelfButton_Click(object sender, RoutedEventArgs e)
    {
        string? zipPath = _shelfService.CreateZipBundle();
        if (!string.IsNullOrEmpty(zipPath))
        {
            DropShelfService.CopyPath(zipPath);
            _machine.PushToast(new ToastContent(
                "\uF012",
                "ZIP Archive Created",
                $"{Path.GetFileName(zipPath)} · Path copied",
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.8),
                TargetView: "shelf",
                Priority: ToastPriority.Feedback,
                Category: "shelfzip"));
        }
    }

    private void ShelfItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
    }

    private void ShelfItem_MouseMove(object sender, MouseEventArgs e)
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

    private void RemoveShelfItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            _shelfService.Remove(path);
        }
    }

    private void CopyShelfPath_Click(object sender, RoutedEventArgs e)
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

    private void RevealShelfItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            DropShelfService.Reveal(path);
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  Privacy Dots, Call Indicators & Microphone Mute Controls
    // ═════════════════════════════════════════════════════════════════

    private void OnPrivacyStateChanged()
    {
        bool cam = _privacyService.IsCameraActive;
        bool mic = _privacyService.IsMicrophoneActive;
        string? callApp = _privacyService.ActiveCallApp;

        CamPrivacyDot.Visibility = cam ? Visibility.Visible : Visibility.Collapsed;
        MicPrivacyDot.Visibility = mic ? Visibility.Visible : Visibility.Collapsed;
        IdleSensorDot.Visibility = (!cam && !mic) ? Visibility.Visible : Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(callApp))
        {
            CompactCallText.Text = callApp;
            ExpandedCallHeader.Visibility = Visibility.Visible;
            ExpandedMediaHeader.Visibility = Visibility.Collapsed;
            ExpandedClockHeader.Visibility = Visibility.Collapsed;

            ExpandedCallTitle.Text = $"{callApp} Call";
            ExpandedCallMicBadge.Visibility = mic ? Visibility.Visible : Visibility.Collapsed;
            ExpandedCallCamBadge.Visibility = cam ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            ExpandedCallHeader.Visibility = Visibility.Collapsed;
            bool hasMedia = _mediaService.Current is not null && !string.IsNullOrWhiteSpace(_mediaService.Current.Title);
            ExpandedMediaHeader.Visibility = hasMedia ? Visibility.Visible : Visibility.Collapsed;
            ExpandedClockHeader.Visibility = hasMedia ? Visibility.Collapsed : Visibility.Visible;
        }

        UpdateMicMuteUI();
        EvaluateActivity();
        EnsureRendering();
    }

    private void UpdateMicMuteUI()
    {
        bool muted = _privacyService.IsMicrophoneMuted;
        MicMuteGlyph.Text = muted ? "\uE74F" : "\uE720";
        MicMuteButton.Background = muted ? ThemeBrushes.MicMutedBg : ThemeBrushes.ButtonSubtleBg;
        MicMuteGlyph.Foreground = muted ? ThemeBrushes.Red : ThemeBrushes.PrimaryText;
        MicMuteButton.ToolTip = muted ? "Unmute Microphone" : "Mute Microphone";
    }

    private void ToggleMicMute()
    {
        bool muted = _privacyService.ToggleMicrophoneMute();
        UpdateMicMuteUI();

        string glyph = muted ? "\uE74F" : "\uE720";
        string title = muted ? "Microphone Muted" : "Microphone Unmuted";
        string sub = muted ? "Hardware capture disabled" : "Hardware capture active";
        Color accent = muted ? ThemeBrushes.RedColor : ThemeBrushes.GreenColor;

        _machine.PushToast(new ToastContent(
            glyph,
            title,
            sub,
            accent,
            TimeSpan.FromSeconds(2.0),
            TargetView: null,
            Priority: ToastPriority.Feedback,
            Category: "mic"));
    }

    // ═════════════════════════════════════════════════════════════════
    //  Clipboard History, Downloads & Quick Calculator
    // ═════════════════════════════════════════════════════════════════

    private void OnClipboardChanged()
    {
        var items = _clipboardService.Items;
        ClipboardBadgeText.Text = items.Count.ToString();
        ClipboardBadgeText.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ClipboardCountText.Text = $"{items.Count} item{(items.Count == 1 ? "" : "s")}";
        ClipboardEmptyPlaceholder.Visibility = items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        SyncObservableCollection(_clipboardItems, items);
    }

    private void ClipboardItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClipboardItem item })
        {
            _clipboardService.CopyItem(item);
            string preview = item.Title;
            if (preview.Length > 24) preview = preview[..21] + "...";
            _machine.PushToast(new ToastContent(
                "\uE8C8",
                "Copied to Clipboard",
                preview,
                ThemeBrushes.BlueColor,
                TimeSpan.FromSeconds(1.8),
                TargetView: "clipboard",
                Priority: ToastPriority.Feedback,
                Category: "clipboard"));
        }
    }

    private void RemoveClipboardItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            _clipboardService.Remove(id);
            e.Handled = true;
        }
    }

    private void PinClipboardItem_Click(object sender, RoutedEventArgs e)
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

    private async void EyedropperButton_Click(object sender, RoutedEventArgs e)
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
        var sample = ScreenToolsService.SampleCursorPixelColor();
        try { Clipboard.SetText(sample.Hex); } catch { }

        _machine.PushToast(new ToastContent(
            "\uEF3C",
            $"Sampled {sample.Hex}",
            $"{sample.RgbText} · Copied to clipboard",
            sample.Color,
            TimeSpan.FromSeconds(3.0),
            TargetView: "clipboard",
            Priority: ToastPriority.Feedback,
            Category: "eyedropper"));
    }

    private async void OcrClipboardButton_Click(object sender, RoutedEventArgs e)
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
                "\uE8BCE",
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
        if (string.IsNullOrWhiteSpace(text))
        {
            _machine.PushToast(new ToastContent(
                "\uE8BCE",
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

    private void OnDownloadsChanged()
    {
        if (_downloadService.HasActiveDownloads)
        {
            CompactDownloadText.Text = _downloadService.LatestDownloadName ?? "DOWNLOADING";
        }

        EvaluateActivity();
        EnsureRendering();
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

    private void CalcInputBox_TextChanged(object sender, TextChangedEventArgs e)
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

    private void CalcInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && CalcResultHost.Visibility == Visibility.Visible)
        {
            CommitCalcResult();
            e.Handled = true;
        }
    }

    private void CalcResult_Click(object sender, MouseButtonEventArgs e)
    {
        CommitCalcResult();
        e.Handled = true;
    }

    private void CommitCalcResult()
    {
        string res = CalcResultText.Text;
        if (!string.IsNullOrEmpty(res))
        {
            Clipboard.SetText(res);
            _machine.PushToast(new ToastContent(
                "\uE73E",
                "Copied to Clipboard",
                res,
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "calc",
                Priority: ToastPriority.Feedback,
                Category: "clipboard"));
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  Customization Settings, Liquid Physics & Laptop Notch Offset
    // ═════════════════════════════════════════════════════════════════

    private void ApplySettings()
    {
        var cur = _settingsService.Current;
        NotchOffsetSlider.Value = cur.NotchYOffset;
        NotchOffsetText.Text = $"{cur.NotchYOffset} px";

        double liquidPct = Math.Round(cur.LiquidIntensity * 100);
        LiquidIntensitySlider.Value = liquidPct;
        LiquidIntensityText.Text = $"{liquidPct}%";

        double speedPct = Math.Round(cur.AnimationSpeed * 100);
        AnimationSpeedSlider.Value = speedPct;
        AnimationSpeedText.Text = $"{speedPct}%";

        double scalePct = Math.Round(cur.IslandScale * 100);
        IslandScaleSlider.Value = scalePct;
        IslandScaleText.Text = $"{scalePct}%";
        RootScaleTransform.ScaleX = cur.IslandScale;
        RootScaleTransform.ScaleY = cur.IslandScale;

        double opacityPct = Math.Round(cur.BackdropOpacity * 100);
        BackdropOpacitySlider.Value = opacityPct;
        BackdropOpacityText.Text = $"{opacityPct}%";
        PillBackground.Opacity = Math.Clamp(cur.BackdropOpacity, 0.65, 1.0);

        UpdateDockButtonsUI();
        UpdateHaloUI();
        UpdateDndUI();
        UpdateBannerModeUI();
        UpdateMotionModeUI();
        UpdateStartupUI();
        UpdateVolumeOsdUI();
        UpdateHotkeysUI();
        UpdateDockVisibilityUI();
        UpdateMonitorUI();
        ApplyTheme(cur.ThemeName);
        SyncSpringMotionPreferences();
        UpdateSpectrumActivity();
        PositionWindow();
    }

    private void OnSettingsPropertyChanged(string? propertyName)
    {
        var cur = _settingsService.Current;
        switch (propertyName)
        {
            case nameof(AppSettings.NotchYOffset):
                ApplyGeometry();
                PositionWindow();
                break;

            case nameof(AppSettings.LiquidIntensity):
            case nameof(AppSettings.AnimationSpeed):
                SyncSpringMotionPreferences();
                break;

            case nameof(AppSettings.IslandScale):
                RootScaleTransform.ScaleX = cur.IslandScale;
                RootScaleTransform.ScaleY = cur.IslandScale;
                break;

            case nameof(AppSettings.BackdropOpacity):
                PillBackground.Opacity = Math.Clamp(cur.BackdropOpacity, 0.65, 1.0);
                break;

            case nameof(AppSettings.DockPosition):
                UpdateDockButtonsUI();
                PositionWindow();
                break;

            case nameof(AppSettings.EnableAmbientGlow):
                UpdateHaloUI();
                UpdateSpectrumActivity();
                EnsureRendering();
                break;

            case nameof(AppSettings.ThemeName):
                ApplyTheme(cur.ThemeName);
                UpdateDockButtonsUI();
                break;

            case nameof(AppSettings.MonitorIndex):
            case nameof(AppSettings.MonitorDeviceName):
                _cachedTargetMonitor = IntPtr.Zero;
                UpdateMonitorUI();
                PositionWindow();
                break;

            case nameof(AppSettings.DoNotDisturb):
                UpdateDndUI();
                break;

            case nameof(AppSettings.ReplaceWindowsBanners):
                UpdateBannerModeUI();
                break;

            case nameof(AppSettings.EnableVolumeKeyOsd):
                UpdateVolumeOsdUI();
                UpdateVolumeKeyHook();
                break;

            case nameof(AppSettings.HiddenDockViews):
                UpdateDockVisibilityUI();
                break;

            case nameof(AppSettings.ReduceMotion):
                UpdateMotionModeUI();
                SyncSpringMotionPreferences();
                _appliedWidth = -1;
                ApplyGeometry();
                break;

            case nameof(AppSettings.CustomHotkeys):
            case nameof(AppSettings.EnableDeveloperHotkeys):
                RegisterConfiguredHotKeys();
                UpdateHotkeysUI();
                break;

            default:
                ApplySettings();
                break;
        }
    }

    private void ApplyTheme(string themeName)
    {
        var palette = SettingsService.ResolveTheme(themeName);

        SelectedThemeName.Text = palette.Name;
        Resources["AccentBrush"] = ThemeBrushes.CreateFrozen(palette.Accent);
        PillGlowColor.Color = palette.Glow;
        PillGlowSecondaryColor.Color = palette.Accent;

        EnsureRendering();
    }

    private void UpdateDockButtonsUI()
    {
        string dock = _settingsService.Current.DockPosition;
        var accent = (Brush)Resources["AccentBrush"];

        DockLeftBtn.Background = dock == "Left" ? accent : ThemeBrushes.ButtonSubtleBg;
        DockLeftBtn.Foreground = dock == "Left" ? ThemeBrushes.DarkInk : ThemeBrushes.PrimaryText;

        DockCenterBtn.Background = dock == "Center" ? accent : ThemeBrushes.ButtonSubtleBg;
        DockCenterBtn.Foreground = dock == "Center" ? ThemeBrushes.DarkInk : ThemeBrushes.PrimaryText;

        DockRightBtn.Background = dock == "Right" ? accent : ThemeBrushes.ButtonSubtleBg;
        DockRightBtn.Foreground = dock == "Right" ? ThemeBrushes.DarkInk : ThemeBrushes.PrimaryText;
    }

    private void UpdateHaloUI()
    {
        bool on = _settingsService.Current.EnableAmbientGlow;
        HaloToggleText.Text = on ? "Glow On" : "Glow Off";
        HaloToggleText.Foreground = on ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
    }

    private void UpdateDndUI()
    {
        if (DndToggleText == null) return;
        bool dnd = _settingsService.Current.DoNotDisturb;
        DndToggleText.Text = dnd ? "DND On" : "DND Off";
        DndToggleText.Foreground = dnd ? ThemeBrushes.Amber : ThemeBrushes.SecondaryText;
    }

    private void UpdateBannerModeUI()
    {
        if (BannerModeText == null) return;
        bool replace = _settingsService.Current.ReplaceWindowsBanners;
        BannerModeText.Text = replace ? "Island Only" : "Island + Win";
        BannerModeText.Foreground = replace ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
    }

    private void UpdateMotionModeUI()
    {
        if (MotionModeText == null) return;
        bool reduced = _settingsService.Current.ReduceMotion;
        MotionModeText.Text = reduced ? "Reduced Motion" : "Liquid Motion";
        MotionModeText.Foreground = reduced ? ThemeBrushes.Amber : ThemeBrushes.Green;
    }

    private void UpdateStartupUI()
    {
        if (StartupToggleText == null) return;
        bool on = StartupService.IsStartupEnabled();
        StartupToggleText.Text = on ? "Startup On" : "Startup Off";
        StartupToggleText.Foreground = on ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
    }

    private void UpdateMonitorUI()
    {
        if (MonitorSwitchText == null) return;
        var monitors = GetAvailableMonitors();
        int idx = Math.Clamp(_settingsService.Current.MonitorIndex, 0, Math.Max(0, monitors.Count - 1));
        MonitorSwitchText.Text = $"Screen {idx + 1}/{Math.Max(1, monitors.Count)}";
    }

    private void MonitorSwitchBtn_Click(object sender, RoutedEventArgs e)
    {
        var monitors = GetAvailableMonitors();
        int count = Math.Max(1, monitors.Count);
        int nextIdx = (_settingsService.Current.MonitorIndex + 1) % count;
        string? devName = (nextIdx >= 0 && nextIdx < monitors.Count)
            ? GetMonitorDeviceName(monitors[nextIdx])
            : null;

        _settingsService.Update(s =>
        {
            s.MonitorIndex = nextIdx;
            s.MonitorDeviceName = devName;
        }, nameof(AppSettings.MonitorIndex));

        _machine.PushToast(new ToastContent(
            "\uE7F4",
            "Display Switched",
            $"Moved to Screen {nextIdx + 1} of {count}",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(1.8),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "monitor"));
    }

    private void LiquidIntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settingsService == null || LiquidIntensityText == null) return;
        double pct = Math.Round(e.NewValue);
        double normalized = Math.Clamp(pct / 100.0, 0.0, 1.0);
        if (Math.Abs(_settingsService.Current.LiquidIntensity - normalized) < 0.005) return;

        LiquidIntensityText.Text = $"{pct}%";
        _settingsService.Update(s => s.LiquidIntensity = normalized, nameof(AppSettings.LiquidIntensity));

        // Give a live gooey ripple as the user drags so they immediately feel the liquid elasticity
        if (LiquidIntensitySlider.IsMouseCaptureWithin && Math.Abs(_width.Velocity) < 80)
        {
            TriggerLiquidImpulse(140, -55, 18);
        }
    }

    private void AnimationSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settingsService == null || AnimationSpeedText == null) return;
        double pct = Math.Round(e.NewValue);
        double normalized = Math.Clamp(pct / 100.0, 0.5, 1.6);
        if (Math.Abs(_settingsService.Current.AnimationSpeed - normalized) < 0.005) return;

        AnimationSpeedText.Text = $"{pct}%";
        _settingsService.Update(s => s.AnimationSpeed = normalized, nameof(AppSettings.AnimationSpeed));
    }

    private void IslandScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settingsService == null || IslandScaleText == null) return;
        double pct = Math.Round(e.NewValue);
        double normalized = Math.Clamp(pct / 100.0, 0.85, 1.25);
        if (Math.Abs(_settingsService.Current.IslandScale - normalized) < 0.005) return;

        IslandScaleText.Text = $"{pct}%";
        _settingsService.Update(s => s.IslandScale = normalized, nameof(AppSettings.IslandScale));
    }

    private void BackdropOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settingsService == null || BackdropOpacityText == null) return;
        double pct = Math.Round(e.NewValue);
        double normalized = Math.Clamp(pct / 100.0, 0.65, 1.0);
        if (Math.Abs(_settingsService.Current.BackdropOpacity - normalized) < 0.005) return;

        BackdropOpacityText.Text = $"{pct}%";
        _settingsService.Update(s => s.BackdropOpacity = normalized, nameof(AppSettings.BackdropOpacity));
    }

    private void NotchOffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settingsService == null || NotchOffsetText == null) return;
        double val = Math.Round(e.NewValue);
        if (Math.Abs(_settingsService.Current.NotchYOffset - val) < 0.01) return;
        NotchOffsetText.Text = $"{val} px";
        _settingsService.Update(s => s.NotchYOffset = val, nameof(AppSettings.NotchYOffset));
    }

    private void LiquidPulseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService.Current.ReduceMotion)
        {
            _settingsService.Update(s => s.ReduceMotion = false, nameof(AppSettings.ReduceMotion));
        }
        TriggerLiquidImpulse(480, -220, 55);
    }

    private void ResetSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Update(s =>
        {
            s.LiquidIntensity = 0.65;
            s.AnimationSpeed = 1.0;
            s.IslandScale = 1.0;
            s.NotchYOffset = 0;
            s.BackdropOpacity = 1.0;
            s.DockPosition = "Center";
            s.ThemeName = "Solar Amber";
            s.EnableAmbientGlow = true;
            s.ReduceMotion = false;
        });
        TriggerLiquidImpulse(360, 180, 40);
    }

    private void BannerModeBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.ReplaceWindowsBanners;
        _settingsService.Update(s => s.ReplaceWindowsBanners = next, nameof(AppSettings.ReplaceWindowsBanners));
        _machine.PushToast(new ToastContent(
            "\uE7E7",
            next ? "Island-Only Notifications" : "Island + Windows Banners",
            next ? "Native Windows popups dismissed after island toast" : "Native Windows popups remain in Action Center",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(1.8),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "banners"));
    }

    private void MotionModeBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.ReduceMotion;
        _settingsService.Update(s => s.ReduceMotion = next, nameof(AppSettings.ReduceMotion));
        if (!next)
        {
            TriggerLiquidImpulse(320, -140, 35);
        }
    }

    private void StartupToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !StartupService.IsStartupEnabled();
        StartupService.SetStartup(next);
        UpdateStartupUI();
    }

    private void UpdateVolumeOsdUI()
    {
        if (VolumeOsdText == null) return;
        bool on = _settingsService.Current.EnableVolumeKeyOsd;
        VolumeOsdText.Text = on ? "Vol Keys On" : "Vol Keys Off";
        VolumeOsdText.Foreground = on ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
    }

    private void VolumeOsdBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.EnableVolumeKeyOsd;
        _settingsService.Update(s => s.EnableVolumeKeyOsd = next, nameof(AppSettings.EnableVolumeKeyOsd));
    }

    private void ExportSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "DynamicIsland_Settings_Backup.json");

        if (_settingsService.ExportToFile(path))
        {
            _shelfService.AddPaths(new[] { path });
            _machine.PushToast(new ToastContent(
                "\uEDE1",
                "Settings Exported",
                "Saved to Downloads & added to Drop Shelf",
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.5),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "settingsio"));
        }
    }

    private void ImportSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        string? candidate = _shelfService.Items
            .FirstOrDefault(i => i.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))?.FullPath;
        candidate ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "DynamicIsland_Settings_Backup.json");

        if (File.Exists(candidate) && _settingsService.ImportFromFile(candidate))
        {
            TriggerLiquidImpulse(320, 160, 35);
            _machine.PushToast(new ToastContent(
                "\uE8B5",
                "Settings Imported",
                Path.GetFileName(candidate),
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.5),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "settingsio"));
        }
        else
        {
            _machine.PushToast(new ToastContent(
                "\uE8B5",
                "No Backup JSON Found",
                "Export first or drop a .json backup onto Drop Shelf",
                ThemeBrushes.AmberColor,
                TimeSpan.FromSeconds(2.5),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "settingsio"));
        }
    }

    private void HotkeyRecordBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string action })
        {
            _recordingHotkeyAction = action;
            SetKeyboardFocusable(true);
            UpdateHotkeysUI();
        }
    }

    private void UpdateHotkeysUI()
    {
        if (HotkeyBindClipboardText == null) return;
        var custom = _settingsService.Current.CustomHotkeys;

        string FormatVk(string action, string label, uint defaultVk)
        {
            if (_recordingHotkeyAction == action) return "Press key...";
            uint vk = (custom != null && custom.TryGetValue(action, out uint c)) ? c : defaultVk;
            if (vk == 0) return $"{label}: Off";
            Key k = KeyInterop.KeyFromVirtualKey((int)vk);
            return $"{label}: {k}";
        }

        HotkeyBindClipboardText.Text = FormatVk("Clipboard", "Clip", 0x56);
        HotkeyBindCalcText.Text = FormatVk("Calculator", "Calc", 0x43);
        HotkeyBindAudioText.Text = FormatVk("AudioDevices", "Audio", 0x4F);
        HotkeyBindWifiText.Text = FormatVk("Network", "Wi-Fi", 0x57);
        HotkeyBindTimerText.Text = FormatVk("Stopwatch", "Timer", 0x41);
    }

    private void DockPinToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string viewKey })
        {
            _settingsService.Update(s =>
            {
                if (!s.HiddenDockViews.Remove(viewKey))
                {
                    s.HiddenDockViews.Add(viewKey);
                }
            }, nameof(AppSettings.HiddenDockViews));
        }
    }

    private void UpdateDockVisibilityUI()
    {
        var hidden = _settingsService.Current.HiddenDockViews;
        void ApplyPin(string key, Button dockBtn, Button? pinBtn)
        {
            bool isHidden = hidden.Contains(key);
            dockBtn.Visibility = isHidden ? Visibility.Collapsed : Visibility.Visible;
            if (pinBtn != null)
            {
                pinBtn.Opacity = isHidden ? 0.42 : 1.0;
                pinBtn.BorderBrush = isHidden ? Brushes.Transparent : ThemeBrushes.Green;
            }
        }

        ApplyPin("shelf", ShelfButton, DockPinShelfBtn);
        ApplyPin("clipboard", ClipboardButton, DockPinClipBtn);
        ApplyPin("notifications", NotificationsButton, DockPinNotifBtn);
        ApplyPin("calc", CalcButton, DockPinCalcBtn);
        ApplyPin("voicememo", RecordButton, DockPinMemoBtn);
        ApplyPin("peripherals", PeripheralsButton, DockPinDevicesBtn);
        ApplyPin("network", NetworkButton, DockPinWifiBtn);
    }

    private void DockButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string dock })
        {
            _settingsService.Update(s => s.DockPosition = dock, nameof(AppSettings.DockPosition));
            _machine.PushToast(new ToastContent(
                "\uE713",
                "Dock Position",
                $"Docked {dock}",
                ThemeBrushes.BlueColor,
                TimeSpan.FromSeconds(1.5),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "dock"));
        }
    }

    private void HaloToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.EnableAmbientGlow;
        _settingsService.Update(s => s.EnableAmbientGlow = next, nameof(AppSettings.EnableAmbientGlow));
        _machine.PushToast(new ToastContent(
            "\uE713",
            "Ambient Backlight",
            next ? "Halo Glow Enabled" : "Halo Glow Disabled",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(1.5),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "halo"));
    }

    private void DndToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        SetDoNotDisturb(!_settingsService.Current.DoNotDisturb);
    }

    private void SetDoNotDisturb(bool enabled)
    {
        _settingsService.Update(s => s.DoNotDisturb = enabled, nameof(AppSettings.DoNotDisturb));
        _machine.PushToast(new ToastContent(
            "\uE708",
            enabled ? "Do Not Disturb On" : "Do Not Disturb Off",
            enabled ? "Incoming notification toasts silenced" : "Notification toasts enabled",
            enabled ? ThemeBrushes.AmberColor : ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(1.8),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "dnd"));
    }

    private void ThemeSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string themeName })
        {
            _settingsService.Update(s => s.ThemeName = themeName, nameof(AppSettings.ThemeName));
            _machine.PushToast(new ToastContent(
                "\uE790",
                "Theme Applied",
                themeName,
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(1.8),
                TargetView: "settings",
                Priority: ToastPriority.Feedback,
                Category: "theme"));
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  Audio Output & Bluetooth Quick-Switcher
    // ═════════════════════════════════════════════════════════════════

    private void OnAudioEndpointsChanged(IReadOnlyList<AudioEndpointInfo> endpoints)
    {
        SyncObservableCollection(_audioEndpoints, endpoints);
        var cur = _audioEndpointService.CurrentDefault;
        if (cur != null)
        {
            AudioButtonGlyph.Text = cur.Glyph;
        }
    }

    private void OnDefaultAudioEndpointChanged(AudioEndpointInfo newDef)
    {
        AudioButtonGlyph.Text = newDef.Glyph;
        _volumeService.ReattachDefaultEndpoint();
        _audioSpectrum.NotifyDefaultDeviceChanged();
        _machine.PushToast(new ToastContent(
            newDef.Glyph,
            "Audio Output Switched",
            newDef.Name,
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(2.8),
            TargetView: "audio",
            Priority: ToastPriority.System,
            Category: "audiodevice"));
    }

    private void AudioEndpointCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AudioEndpointInfo { IsDefault: false } endpoint })
        {
            _audioEndpointService.SetDefaultEndpoint(endpoint.Id);
            _machine.PushToast(new ToastContent(
                endpoint.Glyph,
                "Audio Output",
                $"Active: {endpoint.Name}",
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "audio",
                Priority: ToastPriority.Feedback,
                Category: "audiodevice"));
        }
    }

    private void EndpointSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider { Tag: string deviceId, IsMouseCaptureWithin: true })
        {
            _audioEndpointService.SetEndpointVolume(deviceId, (float)e.NewValue);
        }
    }

    private void AudioModeToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        _isAudioAppMixerMode = !_isAudioAppMixerMode;
        if (AudioSectionTitle != null)
        {
            AudioSectionTitle.Text = _isAudioAppMixerMode ? "Per-App Volume Mixer" : "Audio Output";
        }
        if (AudioModeToggleText != null)
        {
            AudioModeToggleText.Text = _isAudioAppMixerMode ? "Output Devices" : "App Mixer";
            AudioModeToggleText.Foreground = _isAudioAppMixerMode ? ThemeBrushes.Green : ThemeBrushes.Blue;
        }
        if (AudioEndpointsScrollViewer != null && AudioAppMixerScrollViewer != null)
        {
            AudioEndpointsScrollViewer.Visibility = _isAudioAppMixerMode ? Visibility.Collapsed : Visibility.Visible;
            AudioAppMixerScrollViewer.Visibility = _isAudioAppMixerMode ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_isAudioAppMixerMode)
        {
            _audioEndpointService.RefreshAppSessions();
        }
    }

    private void OnAudioAppSessionsChanged(IReadOnlyList<AppAudioSessionInfo> sessions)
    {
        SyncObservableCollection(_audioAppSessions, sessions);
    }

    private void AppSessionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider { Tag: uint pid, IsMouseCaptureWithin: true })
        {
            _audioEndpointService.SetAppSessionVolume(pid, (float)e.NewValue);
        }
    }

    private void AppSessionMuteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: uint pid })
        {
            _audioEndpointService.ToggleAppSessionMute(pid);
            _audioEndpointService.RefreshAppSessions();
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  Smart Battery & Power Management Widget
    // ═════════════════════════════════════════════════════════════════

    private void OnPowerStatusChanged(PowerStatusInfo status)
    {
        if (BatteryBadgeText == null) return;

        BatteryBadgeText.Text = status.HasBattery ? $"{status.BatteryPercent}%" : "AC";
        BatteryButtonGlyph.Text = status.BatteryGlyph;
        BatteryHeroPercentText.Text = $"{status.BatteryPercent}%";
        BatteryStatusSubtitle.Text = status.StatusSubtitle;
        BatteryTimeEstimate.Text = status.TimeEstimateText;
        BatteryChargingBolt.Visibility = status.IsCharging || !status.HasBattery ? Visibility.Visible : Visibility.Collapsed;

        BatteryHeaderBadgeText.Text = status.IsAcOnline
            ? (status.IsCharging ? "Charging" : (status.HasBattery ? "Connected to AC" : "Desktop AC Power"))
            : "On Battery";

        double pct = Math.Clamp(status.BatteryPercent / 100.0, 0.05, 1.0);
        BatteryLevelFill.Width = pct * 68;

        BatteryLevelFill.Background = status.IsCharging || status.BatteryPercent > 30
            ? ThemeBrushes.Green
            : status.BatteryPercent > 15
                ? ThemeBrushes.Amber
                : ThemeBrushes.Red;

        UpdatePowerPlanCardsUI(status.ActiveSchemeKey);
    }

    private void UpdatePowerPlanCardsUI(string activeKey)
    {
        bool isPerf = activeKey == "Performance";
        bool isBalanced = activeKey == "Balanced";
        bool isSaver = activeKey == "PowerSaver";
        var accent = (Brush)Resources["AccentBrush"];

        PlanPerfCard.BorderBrush = isPerf ? accent : Brushes.Transparent;
        PlanPerfCard.Background = isPerf ? ThemeBrushes.PlanPerfActiveBg : ThemeBrushes.ButtonSubtleBg;

        PlanBalancedCard.BorderBrush = isBalanced ? accent : Brushes.Transparent;
        PlanBalancedCard.Background = isBalanced ? ThemeBrushes.PlanBalancedActiveBg : ThemeBrushes.ButtonSubtleBg;

        PlanSaverCard.BorderBrush = isSaver ? accent : Brushes.Transparent;
        PlanSaverCard.Background = isSaver ? ThemeBrushes.PlanSaverActiveBg : ThemeBrushes.ButtonSubtleBg;
    }

    private void OnChargingPulseTriggered(int percent, string timeEstimate)
    {
        _machine.PushToast(new ToastContent(
            "\uE945",
            "Charging",
            $"{percent}% · {timeEstimate}",
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(3.5),
            TargetView: "battery",
            Priority: ToastPriority.System,
            Category: "power"));
        PillGlowColor.Color = ThemeBrushes.GreenColor;
        PillGlow.Opacity = 0.95;
        EnsureRendering();
    }

    private void OnPowerDisconnectedTriggered(int percent, string timeEstimate)
    {
        _machine.PushToast(new ToastContent(
            "\uEBB5",
            "On Battery",
            $"{percent}% · {timeEstimate}",
            ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(3.0),
            TargetView: "battery",
            Priority: ToastPriority.System,
            Category: "power"));
    }

    private void OnLowBatteryTriggered(int percent)
    {
        _machine.PushToast(new ToastContent(
            "\uEBA0",
            "Battery Low",
            $"{percent}% remaining · Plug in soon",
            ThemeBrushes.RedColor,
            TimeSpan.FromSeconds(4.0),
            TargetView: "battery",
            Priority: ToastPriority.Critical));
    }

    private void PlanPerfCard_Click(object sender, MouseButtonEventArgs e)
    {
        _powerPlanService.SetActiveScheme("Performance");
        _machine.PushToast(new ToastContent("\uE945", "Best Performance", "Windows Power Scheme Applied", ThemeBrushes.AmberColor, TimeSpan.FromSeconds(2.0), "battery", ToastPriority.Feedback, "powerplan"));
    }

    private void PlanBalancedCard_Click(object sender, MouseButtonEventArgs e)
    {
        _powerPlanService.SetActiveScheme("Balanced");
        _machine.PushToast(new ToastContent("\uE74C", "Balanced Mode", "Recommended Scheme Applied", ThemeBrushes.BlueColor, TimeSpan.FromSeconds(2.0), "battery", ToastPriority.Feedback, "powerplan"));
    }

    private void PlanSaverCard_Click(object sender, MouseButtonEventArgs e)
    {
        _powerPlanService.SetActiveScheme("PowerSaver");
        _machine.PushToast(new ToastContent("\uEC0A", "Power Saver", "Battery Saver Scheme Applied", ThemeBrushes.GreenColor, TimeSpan.FromSeconds(2.0), "battery", ToastPriority.Feedback, "powerplan"));
    }

    // ═════════════════════════════════════════════════════════════════
    //  Fluid Voice Notes & Audio Dictation
    // ═════════════════════════════════════════════════════════════════

    private void ToggleVoiceRecording()
    {
        if (_voiceMemoService.IsRecording)
        {
            _voiceMemoService.StopRecording();
            RecordButtonGlyph.Text = "\uE7C8";
            RecordBadgeText.Text = "Memo";
            RecordBadgeText.Foreground = ThemeBrushes.PrimaryText;
            HeroRecordGlyph.Text = "\uE7C8";
            HeroRecordStatus.Text = "Saving voice note to Drop Shelf...";
            RecordHalo.Opacity = 0;
            EvaluateActivity();
        }
        else if (_voiceMemoService.StartRecording())
        {
            RecordButtonGlyph.Text = "\uE71A";
            RecordBadgeText.Text = "Recording";
            RecordBadgeText.Foreground = ThemeBrushes.Red;
            HeroRecordGlyph.Text = "\uE71A";
            HeroRecordStatus.Text = "Recording audio... Click to save";
            RecordHalo.Opacity = 0.5;
            EvaluateActivity();
            EnsureRendering();
            _machine.PushToast(new ToastContent(
                "\uE7C8",
                "Recording Started",
                "Audio dictation memo active",
                ThemeBrushes.RedColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "voicememo",
                Priority: ToastPriority.System));
        }
    }

    private void HeroRecordBtn_Click(object sender, RoutedEventArgs e) => ToggleVoiceRecording();

    private void OnVoiceMemoTick(float peak, TimeSpan elapsed)
    {
        HeroRecordTimer.Text = elapsed.ToString(@"mm\:ss\.f");
        CompactRecordText.Text = elapsed.ToString(@"mm\:ss");

        double h = Math.Clamp(peak * 14.0, 3.0, 14.0);
        RecBar0.Height = Math.Max(3, h * 0.5);
        RecBar1.Height = Math.Max(3, h * 0.8);
        RecBar2.Height = Math.Max(3, h * 1.0);
        RecBar3.Height = Math.Max(3, h * 0.7);
        RecBar4.Height = Math.Max(3, h * 0.4);

        RecordHalo.Opacity = Math.Clamp(0.2 + peak * 0.8, 0.2, 0.95);
        EnsureRendering();
    }

    private void InitializeVoiceMemoUI()
    {
        if (_voiceMemoService.LastSavedMemo is { } memo)
        {
            LastMemoHost.Visibility = Visibility.Visible;
            NoMemoPlaceholder.Visibility = Visibility.Collapsed;
            LastMemoFileName.Text = memo.FileName;
            LastMemoDuration.Text = FormatTime(memo.Duration);
        }
        else
        {
            LastMemoHost.Visibility = Visibility.Collapsed;
            NoMemoPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private void OnVoiceMemoCompleted(VoiceMemoInfo info)
    {
        HeroRecordTimer.Text = "00:00";
        HeroRecordStatus.Text = "Ready";
        RecordBadgeText.Text = "Memo";
        RecordBadgeText.Foreground = ThemeBrushes.PrimaryText;
        RecordButtonGlyph.Text = "\uE7C8";
        HeroRecordGlyph.Text = "\uE7C8";
        RecordHalo.Opacity = 0;

        LastMemoHost.Visibility = Visibility.Visible;
        NoMemoPlaceholder.Visibility = Visibility.Collapsed;
        LastMemoFileName.Text = info.FileName;
        LastMemoDuration.Text = FormatTime(info.Duration);

        _shelfService.AddPaths(new[] { info.FilePath });
        _machine.PushToast(new ToastContent(
            "\uE7C8",
            "Voice Memo Saved",
            $"{info.FileName} · {FormatTime(info.Duration)}",
            ThemeBrushes.RedColor,
            TimeSpan.FromSeconds(3.5),
            TargetView: "voicememo",
            Priority: ToastPriority.Notification));
        EvaluateActivity();
    }

    private void MemoPlayPauseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_voiceMemoService.IsPlaying)
        {
            _voiceMemoService.StopPlayback();
        }
        else if (!string.IsNullOrEmpty(_voiceMemoService.LastSavedFilePath) && File.Exists(_voiceMemoService.LastSavedFilePath))
        {
            _voiceMemoService.PlayMemo(_voiceMemoService.LastSavedFilePath);
        }
    }

    private void OnMemoPlaybackStateChanged(bool isPlaying)
    {
        MemoPlayGlyph.Text = isPlaying ? "\uE769" : "\uE768";
        MemoPlayLabel.Text = isPlaying ? "Pause" : "Play";
    }

    private void SendMemoToShelf_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_voiceMemoService.LastSavedFilePath) && File.Exists(_voiceMemoService.LastSavedFilePath))
        {
            _shelfService.AddPaths(new[] { _voiceMemoService.LastSavedFilePath });
            _machine.PushToast(new ToastContent(
                "\uE8B7",
                "Added to Shelf",
                Path.GetFileName(_voiceMemoService.LastSavedFilePath),
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.5),
                TargetView: "shelf",
                Priority: ToastPriority.Feedback));
        }
    }

    private void OpenMemosFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "DynamicIsland_Memos");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Log($"[VoiceMemo] OpenMemosFolder failed: {ex.Message}");
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  Phone Link & Peripheral Wireless Battery Hub
    // ═════════════════════════════════════════════════════════════════

    private void OnPeripheralsChanged(IReadOnlyList<PeripheralDeviceInfo> devices)
    {
        SyncObservableCollection(_peripheralDevices, devices);
        PeripheralsEmptyPlaceholder.Visibility = devices.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

        PeripheralsButtonGlyph.Foreground = _peripheralService.HasLowBatteryDevice
            ? ThemeBrushes.Red
            : ThemeBrushes.PrimaryText;
    }

    private void OnPeripheralLowBatteryAlert(PeripheralDeviceInfo dev)
    {
        _machine.PushToast(new ToastContent(
            "\uE851",
            dev.Name,
            $"Battery low · {dev.BatteryPercent}% remaining",
            ThemeBrushes.RedColor,
            TimeSpan.FromSeconds(4.5),
            TargetView: "peripherals",
            Priority: ToastPriority.Critical));
    }

    private async void RefreshPeripheralsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _peripheralService.RefreshAsync();
            _machine.PushToast(new ToastContent(
                "\uE72C",
                "Peripherals Refreshed",
                $"{_peripheralService.ConnectedCount} devices found",
                ThemeBrushes.BlueColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "peripherals",
                Priority: ToastPriority.Feedback,
                Category: "peripherals"));
        }
        catch (Exception ex)
        {
            App.Log($"[Peripherals] Refresh failed: {ex.Message}");
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  Wi-Fi, Network & Connectivity Hub
    // ═════════════════════════════════════════════════════════════════

    private void OnNetworkUpdated(NetworkStatusSnapshot snap)
    {
        if (NetworkButtonGlyph == null) return;

        NetworkButtonGlyph.Text = snap.Glyph;
        NetworkButtonGlyph.Foreground = snap.IsConnected ? ThemeBrushes.PrimaryText : ThemeBrushes.Amber;

        // Hero Clock Wi-Fi / Live Throughput Badge
        ClockWifiGlyph.Text = snap.Glyph;
        ClockWifiGlyph.Foreground = snap.IsConnected ? ThemeBrushes.Green : ThemeBrushes.Amber;
        ClockWifiText.Text = snap.IsConnected
            ? (snap.DownloadBytesPerSec >= 1024 ? $"↓ {snap.DownloadSpeedText}" : snap.ConnectionName)
            : "Offline";

        // Network Hub Container
        NetworkIpText.Text = $"IP: {snap.LocalIpv4}";
        NetworkHeroGlyph.Text = snap.Glyph;
        NetworkHeroGlyph.Foreground = snap.IsConnected ? ThemeBrushes.Green : ThemeBrushes.Amber;
        NetworkHeroIconBg.Background = snap.IsConnected
            ? ThemeBrushes.CreateFrozen(Color.FromArgb(0x24, 0x30, 0xD1, 0x58))
            : ThemeBrushes.CreateFrozen(Color.FromArgb(0x24, 0xFF, 0x9F, 0x0A));

        NetworkHeroName.Text = snap.ConnectionName;
        if (snap.IsConnected)
        {
            string kind = snap.IsWifi ? $"Wi-Fi · {snap.SignalBars}/4 Bars" : (snap.IsEthernet ? "Ethernet" : "Connected");
            string link = snap.LinkSpeedMbps > 0 ? $" · {snap.LinkSpeedMbps} Mbps" : "";
            NetworkHeroSubtitle.Text = $"{kind}{link}";
        }
        else
        {
            NetworkHeroSubtitle.Text = snap.IsWifiRadioOn ? "No Internet Access" : "Wi-Fi Radio Off";
        }

        NetworkDownSpeedText.Text = snap.DownloadSpeedText;
        NetworkUpSpeedText.Text = snap.UploadSpeedText;

        // Quick-Toggle Cards (Wi-Fi & Bluetooth Radios)
        bool wifiOn = snap.IsWifiRadioOn;
        WifiToggleGlyph.Foreground = wifiOn ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
        WifiToggleStateText.Text = wifiOn ? (snap.IsWifi && snap.IsConnected ? snap.ConnectionName : "On") : "Off";
        WifiToggleStateText.Foreground = wifiOn ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
        WifiToggleCard.Background = wifiOn
            ? ThemeBrushes.CreateFrozen(Color.FromArgb(0x18, 0x30, 0xD1, 0x58))
            : ThemeBrushes.ButtonSubtleBg;
        WifiToggleCard.BorderBrush = wifiOn
            ? ThemeBrushes.CreateFrozen(Color.FromArgb(0x45, 0x30, 0xD1, 0x58))
            : Brushes.Transparent;

        bool btOn = snap.IsBluetoothRadioOn;
        BtToggleGlyph.Foreground = btOn ? ThemeBrushes.Blue : ThemeBrushes.SecondaryText;
        BtToggleStateText.Text = btOn ? "On" : "Off";
        BtToggleStateText.Foreground = btOn ? ThemeBrushes.Blue : ThemeBrushes.SecondaryText;
        BtToggleCard.Background = btOn
            ? ThemeBrushes.CreateFrozen(Color.FromArgb(0x18, 0x0A, 0x84, 0xFF))
            : ThemeBrushes.ButtonSubtleBg;
        BtToggleCard.BorderBrush = btOn
            ? ThemeBrushes.CreateFrozen(Color.FromArgb(0x45, 0x0A, 0x84, 0xFF))
            : Brushes.Transparent;
    }

    private void ClockWifiBadge_Click(object sender, MouseButtonEventArgs e)
    {
        SwitchExpandedView(ExpandedView.Network);
        e.Handled = true;
    }

    private void NetworkIpBadge_Click(object sender, MouseButtonEventArgs e)
    {
        string ip = _networkService.Current.LocalIpv4;
        if (!string.IsNullOrWhiteSpace(ip) && ip != "—")
        {
            try
            {
                Clipboard.SetText(ip);
                _machine.PushToast(new ToastContent(
                    "\uE8C8",
                    "IPv4 Copied",
                    ip,
                    ThemeBrushes.GreenColor,
                    TimeSpan.FromSeconds(2.0),
                    TargetView: "network",
                    Priority: ToastPriority.Feedback,
                    Category: "clipboard"));
            }
            catch { }
        }
        e.Handled = true;
    }

    private void OpenNetworkSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        NetworkService.OpenNetworkSettings();
    }

    private async void WifiToggleCard_Click(object sender, MouseButtonEventArgs e)
    {
        TriggerLiquidImpulse(180, -70, 22);
        bool toggled = await _networkService.ToggleWifiRadioAsync();
        if (toggled)
        {
            bool on = _networkService.Current.IsWifiRadioOn;
            _machine.PushToast(new ToastContent(
                on ? "\uE701" : "\uEB5E",
                on ? "Wi-Fi Enabled" : "Wi-Fi Disabled",
                on ? "Scanning for networks..." : "Wireless radio turned off",
                on ? ThemeBrushes.GreenColor : ThemeBrushes.AmberColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "network",
                Priority: ToastPriority.Feedback,
                Category: "wifi"));
        }
        e.Handled = true;
    }

    private async void BtToggleCard_Click(object sender, MouseButtonEventArgs e)
    {
        TriggerLiquidImpulse(180, -70, 22);
        bool toggled = await _networkService.ToggleBluetoothRadioAsync();
        if (toggled)
        {
            bool on = _networkService.Current.IsBluetoothRadioOn;
            _machine.PushToast(new ToastContent(
                "\uE702",
                on ? "Bluetooth Enabled" : "Bluetooth Disabled",
                on ? "Wireless radio active" : "Bluetooth radio turned off",
                on ? ThemeBrushes.BlueColor : ThemeBrushes.AmberColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "network",
                Priority: ToastPriority.Feedback,
                Category: "bluetooth"));
        }
        e.Handled = true;
    }

    private void WifiNetworksCard_Click(object sender, MouseButtonEventArgs e)
    {
        NetworkService.OpenAvailableNetworksFlyout();
        e.Handled = true;
    }

    private void HotspotCard_Click(object sender, MouseButtonEventArgs e)
    {
        NetworkService.OpenMobileHotspotSettings();
        e.Handled = true;
    }

    // ═════════════════════════════════════════════════════════════════
    //  Display Brightness Control (WMI & DDC/CI)
    // ═════════════════════════════════════════════════════════════════

    private void OnBrightnessChanged(int pct)
    {
        if (BrightnessSlider == null || BrightnessValueText == null) return;
        BrightnessValueText.Text = $"{pct}%";
        if (!BrightnessSlider.IsMouseCaptureWithin)
        {
            BrightnessSlider.Value = pct;
        }
    }

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_brightnessService == null || BrightnessValueText == null) return;
        int pct = (int)Math.Round(e.NewValue);
        BrightnessValueText.Text = $"{pct}%";
        if (BrightnessSlider.IsMouseCaptureWithin)
        {
            _brightnessService.SetBrightness(pct);
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  Notification Center History Drawer & Per-App Mute
    // ═════════════════════════════════════════════════════════════════

    private void OnNotificationHistoryChanged()
    {
        if (NotificationsBadgeText == null) return;
        var items = _notificationListener.History;
        NotificationsBadgeText.Text = items.Count.ToString();
        NotificationsBadgeText.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotificationsCountText.Text = $"{items.Count} item{(items.Count == 1 ? "" : "s")}";
        NotificationsEmptyPlaceholder.Visibility = items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        ClearNotificationsButton.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SyncObservableCollection(_notificationHistoryItems, items);
    }

    private void ClearNotificationsButton_Click(object sender, RoutedEventArgs e)
    {
        _notificationListener.ClearHistory();
    }

    private void RemoveNotificationItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            _notificationListener.RemoveHistoryItem(id);
            e.Handled = true;
        }
    }

    private void MuteAppNotification_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string appName } && !string.IsNullOrWhiteSpace(appName))
        {
            _settingsService.Update(s => s.MutedNotificationApps.Add(appName), nameof(AppSettings.MutedNotificationApps));
            _machine.PushToast(new ToastContent(
                "\uE74F",
                "App Muted",
                $"{appName} notifications silenced in Island",
                ThemeBrushes.AmberColor,
                TimeSpan.FromSeconds(2.2),
                TargetView: "notifications",
                Priority: ToastPriority.Feedback,
                Category: "notifmute"));
            e.Handled = true;
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  Live Weather & Upcoming Calendar Agenda Glance
    // ═════════════════════════════════════════════════════════════════

    private void OnWeatherUpdated(WeatherSnapshot snap)
    {
        if (ClockWeatherBadge == null) return;
        if (snap.IsAvailable)
        {
            ClockWeatherGlyph.Text = snap.Glyph;
            ClockWeatherText.Text = snap.BadgeText;
            ClockWeatherBadge.ToolTip = $"{snap.LocationName} · {snap.Condition} · {snap.SummaryLine}";
            ClockWeatherBadge.Visibility = Visibility.Visible;
        }
    }

    private async void ClockWeatherBadge_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        await _weatherService.RefreshAsync();
        var snap = _weatherService.Current;
        if (snap.IsAvailable)
        {
            _machine.PushToast(new ToastContent(
                snap.Glyph,
                $"{snap.LocationName} · {snap.TempC}°C",
                $"{snap.Condition} · {snap.SummaryLine}",
                ThemeBrushes.AmberColor,
                TimeSpan.FromSeconds(2.5),
                TargetView: null,
                Priority: ToastPriority.Feedback,
                Category: "weather"));
        }
    }

    private void OnCalendarUpdated(CalendarEventInfo? evt)
    {
        if (ClockCalendarBadge == null) return;
        if (evt != null)
        {
            ClockCalendarText.Text = evt.TimeBadgeText;
            ClockCalendarBadge.ToolTip = string.IsNullOrWhiteSpace(evt.Location)
                ? evt.Subject
                : $"{evt.Subject} · {evt.Location}";
            ClockCalendarBadge.Visibility = Visibility.Visible;
        }
        else
        {
            ClockCalendarBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void ClockCalendarBadge_Click(object sender, MouseButtonEventArgs e)
    {
        CalendarService.LaunchEventOrCalendar(_calendarService.NextEvent);
        e.Handled = true;
    }
}
