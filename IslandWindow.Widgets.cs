using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland;

public partial class IslandWindow
{
    private Point _dragStartPoint;

    // ═════════════════════════════════════════════════════════════════
    //  View Navigation & Dock Strip
    // ═════════════════════════════════════════════════════════════════

    private void SwitchExpandedView(string view)
    {
        if (_machine.State != IslandState.Expanded)
        {
            _machine.ExpandNow();
        }

        if (_currentExpandedView == view && view != "media")
        {
            view = "media";
        }

        FrameworkElement targetContainer = view switch
        {
            "shelf" => ShelfContainer,
            "clipboard" => ClipboardContainer,
            "calc" => CalcContainer,
            "settings" => SettingsContainer,
            "audio" => AudioContainer,
            "battery" => BatteryContainer,
            "voicememo" => VoiceMemoContainer,
            "peripherals" => PeripheralsContainer,
            _ => MediaContainer
        };

        Button? targetButton = view switch
        {
            "shelf" => ShelfButton,
            "clipboard" => ClipboardButton,
            "calc" => CalcButton,
            "settings" => SettingsButton,
            "audio" => AudioButton,
            "battery" => BatteryButton,
            "voicememo" => RecordButton,
            "peripherals" => PeripheralsButton,
            _ => MediaHomeButton
        };

        SetDockActive(targetButton);
        AnimateContainerSwitch(targetContainer);
        _currentExpandedView = view;
        SetKeyboardFocusable(view == "calc");

        if (view == "calc")
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                Keyboard.Focus(CalcInputBox);
                CalcInputBox.Focus();
                CalcInputBox.SelectAll();
            }));
        }
        else if (view == "peripherals")
        {
            _ = _peripheralService.RefreshAsync();
        }
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
        if (e.Key == Key.Escape && _machine.State == IslandState.Expanded)
        {
            if (_currentExpandedView != "media")
                SwitchExpandedView("media");
            else
                _machine.CollapseNow();

            e.Handled = true;
        }
    }

    private void ToggleShelfView() => SwitchExpandedView("shelf");
    private void ToggleClipboardView() => SwitchExpandedView("clipboard");
    private void ToggleCalcView() => SwitchExpandedView("calc");
    private void ToggleSettingsView() => SwitchExpandedView("settings");
    private void ToggleAudioView() => SwitchExpandedView("audio");
    private void ToggleBatteryView() => SwitchExpandedView("battery");
    private void ToggleVoiceMemoView() => SwitchExpandedView("voicememo");
    private void TogglePeripheralsView() => SwitchExpandedView("peripherals");

    // ═════════════════════════════════════════════════════════════════
    //  Media, Stopwatch & Telemetry
    // ═════════════════════════════════════════════════════════════════

    private void CommitSeek()
    {
        var media = _mediaService.Current;
        if (media is null || media.Duration <= TimeSpan.Zero) return;
        double targetSec = (TimelineSlider.Value / 100.0) * media.Duration.TotalSeconds;
        _mediaService.Seek(TimeSpan.FromSeconds(targetSec));
    }

    private void OnTrackChanged()
    {
        var media = _mediaService.Current;
        if (media is null || string.IsNullOrWhiteSpace(media.Title)) return;

        if (_machine.State != IslandState.Expanded)
        {
            var accent = media.Accent ?? ThemeBrushes.AmberColor;
            _machine.PushToast(new ToastContent("\uE8D6", media.Title, media.Artist, accent, TimeSpan.FromSeconds(2.8)));
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
            ExpandedSourceApp.Text = string.IsNullOrWhiteSpace(media.Source) ? "Media" : media.Source;

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
        if (_stopwatch.IsRunning) _stopwatch.Stop();
        else _stopwatch.Start();
        RefreshStopwatchUI();
    }

    private void ResetStopwatch()
    {
        _stopwatch.Reset();
        _lastTimerText = null;
        RefreshStopwatchUI();
    }

    private void RefreshStopwatchUI()
    {
        bool running = _stopwatch.IsRunning;
        StartPauseGlyph.Text = running ? "\uE769" : "\uE768";
        StartPauseLabel.Text = running ? "Pause" : _stopwatch.Elapsed > TimeSpan.Zero ? "Resume" : "Start";

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

        _machine.PushToast(new ToastContent(glyph, title, subtitle, accent, TimeSpan.FromSeconds(1.8)));
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
        var shape = Shapes[_machine.State];
        _width.Target = shape.Width;
        _height.Target = shape.Height;
        EnsureRendering();
    }

    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        var shape = Shapes[_machine.State];
        _width.Target = shape.Width;
        _height.Target = shape.Height;
        EnsureRendering();

        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            _shelfService.AddPaths(files);
            _machine.PushToast(new ToastContent("\uE8B7", "Drop Shelf", $"{files.Length} item{(files.Length == 1 ? "" : "s")} added", ThemeBrushes.GreenColor, TimeSpan.FromSeconds(2.8), "shelf"));
        }
    }

    private void OnShelfChanged()
    {
        ShelfBadgeText.Text = _shelfService.Count.ToString();
        ShelfBadgeText.Visibility = _shelfService.HasItems ? Visibility.Visible : Visibility.Collapsed;
        ShelfCountText.Text = $"{_shelfService.Count} item{(_shelfService.Count == 1 ? "" : "s")}";
        ShelfEmptyPlaceholder.Visibility = _shelfService.HasItems ? Visibility.Collapsed : Visibility.Visible;
        SyncObservableCollection(_shelfItems, _shelfService.Items);
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
            _machine.PushToast(new ToastContent("\uE8C8", "Path Copied", Path.GetFileName(path), ThemeBrushes.BlueColor, TimeSpan.FromSeconds(2)));
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

        _machine.PushToast(new ToastContent(glyph, title, sub, accent, TimeSpan.FromSeconds(2.0)));
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
            _machine.PushToast(new ToastContent("\uE8C8", "Copied to Clipboard", preview, ThemeBrushes.BlueColor, TimeSpan.FromSeconds(1.8)));
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
        _machine.PushToast(new ToastContent("\uE896", "Download Complete", fileName, ThemeBrushes.GreenColor, TimeSpan.FromSeconds(3.5), "shelf"));
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
            _machine.PushToast(new ToastContent("\uE73E", "Copied to Clipboard", res, ThemeBrushes.GreenColor, TimeSpan.FromSeconds(2.0)));
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  Customization Settings & Laptop Notch Offset
    // ═════════════════════════════════════════════════════════════════

    private void ApplySettings()
    {
        var cur = _settingsService.Current;
        NotchOffsetSlider.Value = cur.NotchYOffset;
        NotchOffsetText.Text = $"{cur.NotchYOffset} px";
        UpdateDockButtonsUI();
        UpdateHaloUI();
        UpdateMonitorUI();
        ApplyTheme(cur.ThemeName);
        UpdateSpectrumActivity();
        PositionWindow();
    }

    private void OnSettingsChanged() => ApplySettings();

    private void ApplyTheme(string themeName)
    {
        var palette = SettingsService.AvailableThemes.FirstOrDefault(t => t.Name == themeName)
                      ?? SettingsService.AvailableThemes[0];

        SelectedThemeName.Text = palette.Name;
        Resources["AccentBrush"] = ThemeBrushes.CreateFrozen(palette.Accent);
        PillGlowColor.Color = palette.Glow;

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
        _settingsService.Update(s => s.MonitorIndex = nextIdx);
        _machine.PushToast(new ToastContent("\uE7F4", "Display Switched", $"Moved to Screen {nextIdx + 1} of {count}", ThemeBrushes.BlueColor, TimeSpan.FromSeconds(1.8), "settings"));
    }

    private void NotchOffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settingsService == null || NotchOffsetText == null) return;
        double val = Math.Round(e.NewValue);
        if (Math.Abs(_settingsService.Current.NotchYOffset - val) < 0.01) return;
        NotchOffsetText.Text = $"{val} px";
        _settingsService.Update(s => s.NotchYOffset = val);
        ApplyGeometry();
    }

    private void DockButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string dock })
        {
            _settingsService.Update(s => s.DockPosition = dock);
            _machine.PushToast(new ToastContent("\uE713", "Dock Position", $"Docked {dock}", ThemeBrushes.BlueColor, TimeSpan.FromSeconds(1.5)));
        }
    }

    private void HaloToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.EnableAmbientGlow;
        _settingsService.Update(s => s.EnableAmbientGlow = next);
        EnsureRendering();
        _machine.PushToast(new ToastContent("\uE713", "Ambient Backlight", next ? "Halo Glow Enabled" : "Halo Glow Disabled", ThemeBrushes.BlueColor, TimeSpan.FromSeconds(1.5)));
    }

    private void ThemeSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string themeName })
        {
            _settingsService.Update(s => s.ThemeName = themeName);
            _machine.PushToast(new ToastContent("\uE790", "Theme Applied", themeName, ThemeBrushes.GreenColor, TimeSpan.FromSeconds(1.8)));
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
        _machine.PushToast(new ToastContent(newDef.Glyph, "Audio Output Switched", newDef.Name, ThemeBrushes.BlueColor, TimeSpan.FromSeconds(2.8), "audio"));
    }

    private void AudioEndpointCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AudioEndpointInfo { IsDefault: false } endpoint })
        {
            _audioEndpointService.SetDefaultEndpoint(endpoint.Id);
            _machine.PushToast(new ToastContent(endpoint.Glyph, "Audio Output", $"Active: {endpoint.Name}", ThemeBrushes.GreenColor, TimeSpan.FromSeconds(2.0)));
        }
    }

    private void EndpointSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider { Tag: string deviceId, IsMouseCaptureWithin: true })
        {
            _audioEndpointService.SetEndpointVolume(deviceId, (float)e.NewValue);
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
        BatteryLevelFill.Width = pct * 72;

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
        _machine.PushToast(new ToastContent("\uE945", "Charging", $"{percent}% · {timeEstimate}", ThemeBrushes.GreenColor, TimeSpan.FromSeconds(3.5), "battery"));
        PillGlowColor.Color = ThemeBrushes.GreenColor;
        PillGlow.Opacity = 0.95;
        EnsureRendering();
    }

    private void OnPowerDisconnectedTriggered(int percent, string timeEstimate)
    {
        _machine.PushToast(new ToastContent("\uEBB5", "On Battery", $"{percent}% · {timeEstimate}", ThemeBrushes.MutedGreyColor, TimeSpan.FromSeconds(3.0), "battery"));
    }

    private void OnLowBatteryTriggered(int percent)
    {
        _machine.PushToast(new ToastContent("\uEBA0", "Battery Low", $"{percent}% remaining · Plug in soon", ThemeBrushes.RedColor, TimeSpan.FromSeconds(4.0), "battery"));
    }

    private void PlanPerfCard_Click(object sender, MouseButtonEventArgs e)
    {
        _powerPlanService.SetActiveScheme("Performance");
        _machine.PushToast(new ToastContent("\uE945", "Best Performance", "Windows Power Scheme Applied", ThemeBrushes.AmberColor, TimeSpan.FromSeconds(2.0)));
    }

    private void PlanBalancedCard_Click(object sender, MouseButtonEventArgs e)
    {
        _powerPlanService.SetActiveScheme("Balanced");
        _machine.PushToast(new ToastContent("\uE74C", "Balanced Mode", "Recommended Scheme Applied", ThemeBrushes.BlueColor, TimeSpan.FromSeconds(2.0)));
    }

    private void PlanSaverCard_Click(object sender, MouseButtonEventArgs e)
    {
        _powerPlanService.SetActiveScheme("PowerSaver");
        _machine.PushToast(new ToastContent("\uEC0A", "Power Saver", "Battery Saver Scheme Applied", ThemeBrushes.GreenColor, TimeSpan.FromSeconds(2.0)));
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
            _machine.PushToast(new ToastContent("\uE7C8", "Recording Started", "Audio dictation memo active", ThemeBrushes.RedColor, TimeSpan.FromSeconds(2.0)));
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
        _machine.PushToast(new ToastContent("\uE7C8", "Voice Memo Saved", $"{info.FileName} · {FormatTime(info.Duration)}", ThemeBrushes.RedColor, TimeSpan.FromSeconds(3.5), "voicememo"));
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
            _machine.PushToast(new ToastContent("\uE8B7", "Added to Shelf", Path.GetFileName(_voiceMemoService.LastSavedFilePath), ThemeBrushes.GreenColor, TimeSpan.FromSeconds(2.5)));
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
        catch { }
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
        _machine.PushToast(new ToastContent("\uE851", dev.Name, $"Battery low · {dev.BatteryPercent}% remaining", ThemeBrushes.RedColor, TimeSpan.FromSeconds(4.5), "peripherals"));
    }

    private async void RefreshPeripheralsBtn_Click(object sender, RoutedEventArgs e)
    {
        await _peripheralService.RefreshAsync();
        _machine.PushToast(new ToastContent("\uE72C", "Peripherals Refreshed", $"{_peripheralService.ConnectedCount} devices found", ThemeBrushes.BlueColor, TimeSpan.FromSeconds(2.0)));
    }
}
