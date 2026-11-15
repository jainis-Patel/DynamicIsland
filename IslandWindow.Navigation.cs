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
    //  View Navigation & Dock Strip
    // =================================================================

    private DateTime _lastViewSwitchTime = DateTime.MinValue;

    private void SwitchExpandedView(ExpandedView view, bool animateTransition = true)
    {
        if (_machine.State != IslandState.Expanded)
        {
            _machine.ExpandNow();
        }

        var previousView = _currentExpandedView;
        if (_currentExpandedView == view && view != ExpandedView.Media)
        {
            if ((DateTime.UtcNow - _lastViewSwitchTime).TotalMilliseconds > 250)
            {
                view = ExpandedView.Media;
            }
            else
            {
                return;
            }
        }
        _lastViewSwitchTime = DateTime.UtcNow;

        var (targetContainer, targetButton) = _viewRegistry.TryGetValue(view, out var entry)
            ? entry
            : _viewRegistry[ExpandedView.Media];

        SetDockActive(targetButton);
        if (animateTransition && previousView != view)
        {
            foreach (var c in _allContainers)
            {
                if (c != targetContainer && c.Visibility == Visibility.Visible)
                {
                    var target = c;
                    var fadeOut = new DoubleAnimation(target.Opacity, 0, TimeSpan.FromMilliseconds(100));
                    fadeOut.Completed += (_, _) =>
                    {
                        if (target != targetContainer)
                            target.Visibility = Visibility.Collapsed;
                    };
                    target.BeginAnimation(OpacityProperty, fadeOut);
                }
            }
            targetContainer.Visibility = Visibility.Visible;
            int prevIdx = _orderedViews.IndexOf(previousView);
            int targetIdx = _orderedViews.IndexOf(view);
            int direction = targetIdx >= prevIdx ? 1 : -1;
            AnimateContainerEntrance(targetContainer, direction);
        }
        else
        {
            AnimateContainerSwitch(targetContainer);
        }
        _currentExpandedView = view;
        _recordingHotkeyAction = null;
        SetKeyboardFocusable(view is ExpandedView.Calc
                                 or ExpandedView.Clipboard
                                 or ExpandedView.Notes
                                 or ExpandedView.Calendar
                                 or ExpandedView.Launcher
                                 or ExpandedView.Termi
                                 or ExpandedView.Converter
                                 or ExpandedView.QuickReply);

        // Morph the pill smoothly to target shape (496x338 for Settings, 496x192 for Media/Clock, etc.)
        // using smooth spring target easing without jarring velocity spikes.
        SyncSpringMotionPreferences();
        var targetShape = GetTargetShape(_machine.State, view);
        ExpandedPanel.Width = targetShape.Width;
        ExpandedPanel.Height = targetShape.Height;
        _width.Target = targetShape.Width;
        _height.Target = targetShape.Height;
        _radius.Target = targetShape.Radius;

                if (view == ExpandedView.SystemHud)
        {
            _systemPerfService?.SetHudExpanded(true);
            if (HudHardwareSubtitle != null && _systemPerfService != null)
                HudHardwareSubtitle.Text = _systemPerfService.HardwareSummary;
        }
        else if (previousView == ExpandedView.SystemHud)
        {
            _systemPerfService?.SetHudExpanded(false);
        }

        if (view == ExpandedView.Network)
        {
            _networkService?.SetExpanded(true);
        }
        else if (previousView == ExpandedView.Network)
        {
            _networkService?.SetExpanded(false);
        }

        if (view == ExpandedView.Launcher)
        {
            LauncherContainer?.RefreshItems();
        }
        else if (view == ExpandedView.Calendar)
        {
            CalendarContainer?.RefreshAll();
        }
        else if (view == ExpandedView.Termi)
        {
            TermiContainer?.PopulateSnippetPills();
        }
        else if (view == ExpandedView.PhoneBridge)
        {
            PhoneBridgeContainer?.RefreshSession();
        }
        else if (view == ExpandedView.MechSounds)
        {
            MechSoundsContainer?.RefreshUI();
        }
        else if (view == ExpandedView.CaptureStudio)
        {
            CaptureStudioContainer?.UpdatePinnedCountBadge();
        }

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
        else if (view == ExpandedView.Launcher)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                LauncherContainer?.FocusSearch();
            }));
        }
        else if (view == ExpandedView.Termi)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                TermiContainer?.FocusInput();
            }));
        }
        else if (view == ExpandedView.Notes)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                NotesContainer?.FocusEditor();
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
            if (_networkService != null)
            {
                _ = _networkService.RefreshRadioStatesAsync();
                _networkService.Sample();
            }
        }
        else if (view == ExpandedView.Clipboard)
        {
            Dispatcher.InvokeAsync(UpdateClipboardScrollButtons, System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else if (view == ExpandedView.Shelf)
        {
            Dispatcher.InvokeAsync(UpdateShelfScrollButtons, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void SecondaryDroplet_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
            return;

        TriggerLiquidImpulse(110, -40, 14);
        switch (_secondaryCompactHost)
        {
            case CompactActivityKind.Media:
                _mediaService.TogglePlayPause();
                break;

            case CompactActivityKind.Call:
                ToggleMicMute();
                break;

            case CompactActivityKind.Countdown:
            case CompactActivityKind.Stopwatch:
                ToggleStopwatch();
                break;

            case CompactActivityKind.Recording:
                ToggleVoiceRecording();
                break;

            case CompactActivityKind.Download:
                _machine.ExpandNow();
                SwitchExpandedView(ExpandedView.Shelf);
                break;
        }

        e.Handled = true;
    }

    private void SecondaryDroplet_Click(object sender, MouseButtonEventArgs e)
    {
        var secondaryKind = _secondaryCompactHost;
        if (secondaryKind == CompactActivityKind.LiveActivity)
        {
            _liveActivityHub?.CycleNext();
            EvaluateActivity();
            e.Handled = true;
            return;
        }

        var targetView = ActivityResolver.ResolveCompactTargetView(secondaryKind);

        if (targetView == ExpandedView.Media && string.IsNullOrWhiteSpace(_privacyService.ActiveCallApp))
        {
            bool hasMedia = _mediaService.Current != null && !string.IsNullOrWhiteSpace(_mediaService.Current.Title);
            if (hasMedia)
            {
                _preferClockHeaderInMedia = secondaryKind is CompactActivityKind.Countdown or CompactActivityKind.Stopwatch;
                ExpandedMediaHeader.Visibility = _preferClockHeaderInMedia ? Visibility.Collapsed : Visibility.Visible;
                ExpandedClockHeader.Visibility = _preferClockHeaderInMedia ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        _machine.ExpandNow();
        if (targetView != ExpandedView.Media)
        {
            SwitchExpandedView(targetView);
        }
        e.Handled = true;
    }

    public static readonly DependencyProperty AnimatedHorizontalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "AnimatedHorizontalOffset",
            typeof(double),
            typeof(IslandWindow),
            new PropertyMetadata(0.0, (d, e) =>
            {
                if (d is ScrollViewer sv)
                {
                    sv.ScrollToHorizontalOffset((double)e.NewValue);
                }
            }));

    public static readonly DependencyProperty AnimatedVerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "AnimatedVerticalOffset",
            typeof(double),
            typeof(IslandWindow),
            new PropertyMetadata(0.0, (d, e) =>
            {
                if (d is ScrollViewer sv)
                {
                    sv.ScrollToVerticalOffset((double)e.NewValue);
                }
            }));

    private void SmoothScrollTo(ScrollViewer sv, double targetOffset)
    {
        if (sv == null) return;
        targetOffset = Math.Clamp(targetOffset, 0, sv.ScrollableWidth);
        sv.SetValue(AnimatedHorizontalOffsetProperty, sv.HorizontalOffset);
        var anim = new DoubleAnimation
        {
            From = sv.HorizontalOffset,
            To = targetOffset,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = SharedCubicOut
        };
        sv.BeginAnimation(AnimatedHorizontalOffsetProperty, anim);
    }

    private void SmoothScrollToVertical(ScrollViewer sv, double targetOffset)
    {
        if (sv == null) return;
        targetOffset = Math.Clamp(targetOffset, 0, sv.ScrollableHeight);
        sv.SetValue(AnimatedVerticalOffsetProperty, sv.VerticalOffset);
        var anim = new DoubleAnimation
        {
            From = sv.VerticalOffset,
            To = targetOffset,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = SharedCubicOut
        };
        sv.BeginAnimation(AnimatedVerticalOffsetProperty, anim);
    }

    private void SmoothScrollHorizontal(ScrollViewer sv, double delta)
    {
        if (sv == null) return;
        double target = Math.Clamp(sv.HorizontalOffset + delta, 0, sv.ScrollableWidth);
        sv.ScrollToHorizontalOffset(target);
    }

    private void SmoothScrollVertical(ScrollViewer sv, double delta)
    {
        if (sv == null) return;
        if (sv.ScrollableHeight <= 0 && sv.ExtentHeight <= 0)
        {
            sv.UpdateLayout();
        }
        double target = Math.Clamp(sv.VerticalOffset + delta, 0, Math.Max(0, sv.ScrollableHeight));
        sv.ScrollToVerticalOffset(target);
    }

    internal void HorizontalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            SmoothScrollHorizontal(sv, -e.Delta * 0.85);
            e.Handled = true;
        }
    }

    internal void VerticalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            SmoothScrollVertical(sv, -e.Delta * 0.85);
            e.Handled = true;
        }
    }

    internal void HandleMouseWheelDelta(int delta)
    {
        if (_machine.State != IslandState.Expanded)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                if (delta > 0) _mediaService.Next();
                else if (delta < 0) _mediaService.Previous();
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            {
                int pct = _brightnessService.StepBrightness(delta > 0 ? 5 : -5);
                _machine.PushToast(new ToastContent(
                    "\uE706",
                    "Brightness",
                    $"{pct}%",
                    ThemeBrushes.AmberColor,
                    TimeSpan.FromSeconds(1.8),
                    TargetView: null,
                    Priority: ToastPriority.Feedback,
                    Category: "brightness"));
            }
            else
            {
                float step = delta > 0 ? 0.02f : -0.02f;
                _volumeService.StepVolume(step);
            }
            return;
        }

        Point pos = default;
        try { pos = Mouse.GetPosition(Pill); } catch { }
        double dockThreshold = Pill.ActualHeight > 0 ? Pill.ActualHeight - 34 : 156;
        if (pos.Y >= dockThreshold && DockScrollViewer != null)
        {
            SmoothScrollHorizontal(DockScrollViewer, -delta * 0.85);
            return;
        }

        if (_currentExpandedView == ExpandedView.Notifications && NotificationsScrollViewer != null)
        {
            SmoothScrollVertical(NotificationsScrollViewer, -delta * 0.85);
            return;
        }

        if (_currentExpandedView == ExpandedView.Settings)
        {
            ScrollViewer? activeSettingsSv = _currentSettingsTab switch
            {
                "Themes" => SettingsThemesTab,
                "Droplets" => SettingsDropletsTab,
                "Dock" => SettingsDockTab,
                "Clock" => SettingsClockAudioTab,
                "Screen" => SettingsScreenTab,
                "Pet" => SettingsPetTab,
                "Shortcuts" => SettingsShortcutsTab,
                "Notifications" => SettingsNotifTab,
                _ => SettingsAppearanceTab
            };
            if (activeSettingsSv != null)
            {
                SmoothScrollVertical(activeSettingsSv, -delta * 0.85);
            }
            return;
        }

        if (_currentExpandedView == ExpandedView.Launcher && LauncherContainer?.LauncherScrollViewer != null)
        {
            SmoothScrollVertical(LauncherContainer.LauncherScrollViewer, -delta * 0.85);
            return;
        }

        if (_currentExpandedView == ExpandedView.Calendar && CalendarContainer?.TasksScrollViewer != null)
        {
            SmoothScrollVertical(CalendarContainer.TasksScrollViewer, -delta * 0.85);
            return;
        }

        if (_currentExpandedView == ExpandedView.VoiceMemo && VoiceMemoContainer?.VoiceMemosScrollViewer != null)
        {
            SmoothScrollVertical(VoiceMemoContainer.VoiceMemosScrollViewer, -delta * 0.85);
            return;
        }

        if (_currentExpandedView == ExpandedView.Termi && TermiContainer?.ConsoleScrollViewer != null)
        {
            SmoothScrollVertical(TermiContainer.ConsoleScrollViewer, -delta * 0.85);
            return;
        }

        if (_currentExpandedView == ExpandedView.Notes && NotesContainer?.NotesScrollViewer != null)
        {
            SmoothScrollVertical(NotesContainer.NotesScrollViewer, -delta * 0.85);
            return;
        }

        ScrollViewer? targetSv = _currentExpandedView switch
        {
            ExpandedView.Clipboard => ClipboardScrollViewer,
            ExpandedView.Shelf => ShelfScrollViewer,
            ExpandedView.Audio => AudioAppMixerScrollViewer?.Visibility == Visibility.Visible ? AudioAppMixerScrollViewer : AudioEndpointsScrollViewer,
            ExpandedView.Peripherals => PeripheralsScrollViewer,
            ExpandedView.Network => WifiNetworksScrollViewer,
            ExpandedView.Calc => CalcContainer?.CalcHistoryScrollViewer,
            _ => null
        };

        if (targetSv != null)
        {
            SmoothScrollHorizontal(targetSv, -delta * 0.85);
        }
    }

    private Point _middlePanStart;
    private double _middlePanStartOffset;
    private ScrollViewer? _middlePanTarget;

    internal void ScrollViewer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && sender is ScrollViewer sv)
        {
            _middlePanTarget = sv;
            _middlePanStart = e.GetPosition(this);
            _middlePanStartOffset = sv.ScrollableHeight > 0 ? sv.VerticalOffset : sv.HorizontalOffset;
            sv.CaptureMouse();
            sv.Cursor = sv.ScrollableHeight > 0 ? Cursors.SizeNS : Cursors.SizeWE;
            e.Handled = true;
        }
    }

    internal void ScrollViewer_MouseMove(object sender, MouseEventArgs e)
    {
        if (_middlePanTarget != null && e.MiddleButton == MouseButtonState.Pressed)
        {
            Point cur = e.GetPosition(this);
            if (_middlePanTarget.ScrollableHeight > 0)
            {
                double diff = cur.Y - _middlePanStart.Y;
                _middlePanTarget.ScrollToVerticalOffset(_middlePanStartOffset - diff);
            }
            else
            {
                double diff = cur.X - _middlePanStart.X;
                _middlePanTarget.ScrollToHorizontalOffset(_middlePanStartOffset - diff);
            }
            e.Handled = true;
        }
    }

    internal void ScrollViewer_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_middlePanTarget != null && e.ChangedButton == MouseButton.Middle)
        {
            _middlePanTarget.ReleaseMouseCapture();
            _middlePanTarget.Cursor = null;
            _middlePanTarget = null;
            e.Handled = true;
        }
    }

    internal void ShelfScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        if (ShelfScrollViewer != null)
        {
            SmoothScrollTo(ShelfScrollViewer, ShelfScrollViewer.HorizontalOffset - 260);
        }
    }

    internal void ShelfScrollRight_Click(object sender, RoutedEventArgs e)
    {
        if (ShelfScrollViewer != null)
        {
            SmoothScrollTo(ShelfScrollViewer, ShelfScrollViewer.HorizontalOffset + 260);
        }
    }

    internal void ShelfScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateShelfScrollButtons();

    private void UpdateShelfScrollButtons()
    {
        if (ShelfScrollViewer == null || ShelfScrollLeftBtn == null || ShelfScrollRightBtn == null) return;
        bool hasOverflow = ShelfScrollViewer.ScrollableWidth > 2 || _shelfItems.Count > 3;
        var vis = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
        ShelfScrollLeftBtn.Visibility = vis;
        ShelfScrollRightBtn.Visibility = vis;

        if (hasOverflow)
        {
            ShelfScrollLeftBtn.IsEnabled = ShelfScrollViewer.HorizontalOffset > 2;
            ShelfScrollLeftBtn.Opacity = ShelfScrollLeftBtn.IsEnabled ? 1.0 : 0.35;
            ShelfScrollRightBtn.IsEnabled = ShelfScrollViewer.HorizontalOffset < (ShelfScrollViewer.ScrollableWidth - 2);
            ShelfScrollRightBtn.Opacity = ShelfScrollRightBtn.IsEnabled ? 1.0 : 0.35;
        }
    }

    internal void ClipboardScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        if (ClipboardScrollViewer != null)
        {
            SmoothScrollTo(ClipboardScrollViewer, ClipboardScrollViewer.HorizontalOffset - 260);
        }
    }

    internal void ClipboardScrollRight_Click(object sender, RoutedEventArgs e)
    {
        if (ClipboardScrollViewer != null)
        {
            SmoothScrollTo(ClipboardScrollViewer, ClipboardScrollViewer.HorizontalOffset + 260);
        }
    }

    internal void ClipboardScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateClipboardScrollButtons();

    private void UpdateClipboardScrollButtons()
    {
        if (ClipboardScrollViewer == null || ClipboardScrollLeftBtn == null || ClipboardScrollRightBtn == null) return;
        bool hasOverflow = ClipboardScrollViewer.ScrollableWidth > 2 || _clipboardItems.Count > 3;
        var vis = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
        ClipboardScrollLeftBtn.Visibility = vis;
        ClipboardScrollRightBtn.Visibility = vis;

        if (hasOverflow)
        {
            ClipboardScrollLeftBtn.IsEnabled = ClipboardScrollViewer.HorizontalOffset > 2;
            ClipboardScrollLeftBtn.Opacity = ClipboardScrollLeftBtn.IsEnabled ? 1.0 : 0.35;
            ClipboardScrollRightBtn.IsEnabled = ClipboardScrollViewer.HorizontalOffset < (ClipboardScrollViewer.ScrollableWidth - 2);
            ClipboardScrollRightBtn.Opacity = ClipboardScrollRightBtn.IsEnabled ? 1.0 : 0.35;
        }
    }

    private void PinExpandedButton_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_machine.IsPinned;
        _machine.SetPinned(next);
        UpdatePinExpandedUI();
    }

    private void UpdatePinExpandedUI()
    {
        if (PinExpandedButton == null || PinExpandedGlyph == null) return;
        bool pinned = _machine.IsPinned;
        PinExpandedGlyph.Text = pinned ? "\uE840" : "\uE718";
        PinExpandedGlyph.Foreground = pinned ? ThemeBrushes.Amber : ThemeBrushes.SecondaryText;
        PinExpandedButton.Background = pinned ? ThemeBrushes.DockActiveBg : ThemeBrushes.DockInactiveBg;
        PinExpandedButton.BorderBrush = pinned ? ThemeBrushes.DockActiveBorder : ThemeBrushes.DockInactiveBg;
        PinExpandedButton.ToolTip = pinned ? "Unpin Island (Locked Open)" : "Pin Expanded (Stay Open on Mouse Leave)";
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        _machine.CollapseNow();
        UpdatePinExpandedUI();
    }

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
                        $"{action} \u2192 Ctrl+Alt+{key}",
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
            if (_currentExpandedView == ExpandedView.QuickReply)
            {
                CancelQuickReply();
                e.Handled = true;
                return;
            }

            if (_currentExpandedView != ExpandedView.Media)
            {
                SwitchExpandedView(ExpandedView.Media);
            }
            else
            {
                _machine.CollapseNow();
                UpdatePinExpandedUI();
            }

            e.Handled = true;
            return;
        }

        if (_machine.State == IslandState.Expanded && (Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
            e.Key is >= Key.D1 and <= Key.D9)
        {
            int targetIdx = e.Key - Key.D1;
            int currentVisibleIdx = 0;
            for (int i = 0; i < _orderedViews.Count; i++)
            {
                var v = _orderedViews[i];
                if (_viewRegistry.TryGetValue(v, out var entry) && entry.DockButton?.Visibility == Visibility.Visible)
                {
                    if (currentVisibleIdx == targetIdx)
                    {
                        SwitchExpandedView(v);
                        e.Handled = true;
                        return;
                    }
                    currentVisibleIdx++;
                }
            }
        }

        if (_machine.State == IslandState.Expanded && _currentExpandedView is ExpandedView.Clipboard or ExpandedView.Shelf)
        {
            var sv = _currentExpandedView == ExpandedView.Clipboard ? ClipboardScrollViewer : ShelfScrollViewer;
            if (sv != null)
            {
                if (e.Key == Key.Left)
                {
                    SmoothScrollTo(sv, sv.HorizontalOffset - 260);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Right)
                {
                    SmoothScrollTo(sv, sv.HorizontalOffset + 260);
                    e.Handled = true;
                    return;
                }
            }
        }

        if (_machine.State == IslandState.Expanded && _currentExpandedView == ExpandedView.Notifications)
        {
            if (NotificationsScrollViewer != null)
            {
                if (e.Key == Key.Up)
                {
                    SmoothScrollVertical(NotificationsScrollViewer, -58);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Down)
                {
                    SmoothScrollVertical(NotificationsScrollViewer, 58);
                    e.Handled = true;
                    return;
                }
            }
        }

        if (_machine.State == IslandState.Expanded && _currentExpandedView == ExpandedView.Settings)
        {
            ScrollViewer? activeSettingsSv = _currentSettingsTab switch
            {
                "Themes" => SettingsThemesTab,
                "Droplets" => SettingsDropletsTab,
                "Dock" => SettingsDockTab,
                "Clock" => SettingsClockAudioTab,
                "Screen" => SettingsScreenTab,
                "Pet" => SettingsPetTab,
                "Shortcuts" => SettingsShortcutsTab,
                "Notifications" => SettingsNotifTab,
                _ => SettingsAppearanceTab
            };
            if (activeSettingsSv != null)
            {
                if (e.Key == Key.Up || e.Key == Key.PageUp)
                {
                    SmoothScrollVertical(activeSettingsSv, e.Key == Key.PageUp ? -180 : -58);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Down || e.Key == Key.PageDown)
                {
                    SmoothScrollVertical(activeSettingsSv, e.Key == Key.PageDown ? 180 : 58);
                    e.Handled = true;
                    return;
                }
            }
        }
    }

}
