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
using DynamicIsland.Interop;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

public partial class IslandWindow
{
    // =================================================================
    //  Notification Center History Drawer & Per-App Mute
    // =================================================================

    private readonly HashSet<string> _expandedNotificationApps = new(StringComparer.OrdinalIgnoreCase);

    private void OnNotificationHistoryChanged()
    {
        if (NotificationsBadgeText == null) return;
        var items = _notificationListener.History;
        NotificationsBadgeText.Text = items.Count.ToString();
        NotificationsBadgeText.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (NotificationsBadgeHost != null)
        {
            NotificationsBadgeHost.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        NotificationsCountText.Text = $"{items.Count} new";
        if (NotificationsCountBadge != null)
        {
            NotificationsCountBadge.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (NotificationsDndBadge != null)
        {
            NotificationsDndBadge.Visibility = _settingsService.Current.DoNotDisturb ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateMutedAppsBadgeUI();
        UpdateGroupingButtonUI();
        NotificationsEmptyPlaceholder.Visibility = items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        ClearNotificationsButton.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SyncObservableCollection(_notificationHistoryItems, items);

        List<NotificationAppGroup> groups;
        if (_settingsService.Current.GroupNotificationsByApp)
        {
            groups = NotificationListenerService.GroupNotifications(items);
            foreach (var g in groups)
            {
                if (_expandedNotificationApps.Contains(g.AppName))
                {
                    g.IsExpanded = true;
                }
            }
        }
        else
        {
            groups = items.Select(NotificationAppGroup.FromSingle).ToList();
        }
        SyncObservableCollection(_notificationAppGroups, groups);

        // Dynamically adjust expanded height if currently viewing Notifications
        if (_machine.State == IslandState.Expanded && _currentExpandedView == ExpandedView.Notifications)
        {
            AdjustExpandedNotificationsHeight();
        }
    }

    internal void AdjustExpandedNotificationsHeight()
    {
        var targetShape = GetTargetShape(_machine.State, ExpandedView.Notifications);
        ExpandedPanel.Width = targetShape.Width;
        ExpandedPanel.Height = targetShape.Height;
        _width.Target = targetShape.Width;
        _height.Target = targetShape.Height;
        _radius.Target = targetShape.Radius;
        EnsureRendering();
    }

    internal void ToggleNotificationGrouping()
    {
        bool newGrouping = !_settingsService.Current.GroupNotificationsByApp;
        _settingsService.Update(s => s.GroupNotificationsByApp = newGrouping, nameof(AppSettings.GroupNotificationsByApp));
        UpdateGroupingButtonUI();
        OnNotificationHistoryChanged();
        TriggerLiquidImpulse(120, -50, 15);
        _machine.PushToast(new ToastContent(
            "\uE8B9",
            newGrouping ? "Grouped by App" : "Chronological View",
            newGrouping ? "Multiple notifications from the same app are stacked" : "Showing all notifications individually in timeline order",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "notifications",
            Priority: ToastPriority.Feedback));
    }

    private void UpdateGroupingButtonUI()
    {
        if (ToggleGroupingButton == null || ToggleGroupingText == null || ToggleGroupingGlyph == null) return;
        bool isGrouped = _settingsService.Current.GroupNotificationsByApp;
        ToggleGroupingText.Text = isGrouped ? "Grouped" : "Flat";
        ToggleGroupingGlyph.Text = isGrouped ? "\uE8B9" : "\uE8FD";
        ToggleGroupingButton.ToolTip = isGrouped
            ? "Notifications grouped by app (Click to switch to chronological flat timeline)"
            : "Showing chronological flat timeline (Click to group by app)";
        ToggleGroupingButton.Visibility = _notificationListener.HistoryCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void ToggleNotificationAppGroup(NotificationAppGroup group)
    {
        if (group == null) return;
        group.IsExpanded = !group.IsExpanded;
        if (group.IsExpanded)
        {
            _expandedNotificationApps.Add(group.AppName);
        }
        else
        {
            _expandedNotificationApps.Remove(group.AppName);
        }
        TriggerLiquidImpulse(40, -15, 6);
        AdjustExpandedNotificationsHeight();
    }

    internal void DismissNotificationGroup(NotificationAppGroup group)
    {
        if (group == null) return;
        if (_settingsService.Current.GroupNotificationsByApp && group.HasMultiple)
        {
            _notificationListener.RemoveHistoryItemsByApp(group.AppName);
        }
        else if (!string.IsNullOrEmpty(group.LatestId))
        {
            _notificationListener.RemoveHistoryItem(group.LatestId);
        }
        TriggerLiquidImpulse(100, -40, 14);
    }

    public void OpenQuickReply(NotificationHistoryItem item)
    {
        if (item == null) return;
        QuickReplyContainer.SetNotification(item);
        _machine.SetPinned(true);
        SwitchExpandedView(ExpandedView.QuickReply);
        QuickReplyContainer.FocusInput();
    }

    public void CancelQuickReply()
    {
        _machine.SetPinned(false);
        _machine.CollapseNow();
    }

    public void OpenTargetApp(NotificationHistoryItem item, string? pendingReplyText = null)
    {
        if (item == null) return;

        // If the user entered a reply before opening the app, stage it onto the clipboard
        if (!string.IsNullOrWhiteSpace(pendingReplyText))
        {
            try
            {
                Clipboard.SetText(pendingReplyText.Trim());
            }
            catch { }
        }

        NotificationListenerService.ActivateNotification(item, preferApp: true);
        _machine.SetPinned(false);
        _machine.CollapseNow();

        if (!string.IsNullOrWhiteSpace(pendingReplyText))
        {
            _machine.PushToast(new ToastContent(
                "\uE783",
                "Reply Copied to Clipboard",
                $"Ready to paste in {item.AppName} (Ctrl+V)",
                item.Accent,
                TimeSpan.FromSeconds(2.5),
                Priority: ToastPriority.Feedback));
        }
    }

    public async Task<bool> SendQuickReplyAsync(NotificationHistoryItem item, string text)
    {
        if (item == null || string.IsNullOrWhiteSpace(text)) return false;

        string cleanText = text.Trim();
        IDataObject? savedClipboard = null;

        try
        {
            // 1. Preserve previous clipboard contents so user data is not lost
            try
            {
                savedClipboard = Clipboard.GetDataObject();
            }
            catch (Exception ex)
            {
                App.Log($"[QuickReply] Could not preserve previous clipboard data: {ex.Message}");
            }

            // 2. Stage reply text onto Windows clipboard with transient retry
            bool clipboardSet = false;
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    Clipboard.SetText(cleanText);
                    clipboardSet = true;
                    break;
                }
                catch
                {
                    await Task.Delay(25);
                }
            }

            // 3. Locate the target messenger window via EnumWindows
            IntPtr targetHwnd = NotificationListenerService.FindAppWindow(item.AppName, item.AppUserModelId, item.Title);

            if (targetHwnd == IntPtr.Zero)
            {
                // App is not currently top-level visible; attempt activation via protocol/AUMID
                NotificationListenerService.ActivateNotification(item, preferApp: true);

                // Poll up to 1.2s (12 x 100ms) for the window to appear
                for (int i = 0; i < 12; i++)
                {
                    await Task.Delay(100);
                    targetHwnd = NotificationListenerService.FindAppWindow(item.AppName, item.AppUserModelId, item.Title);
                    if (targetHwnd != IntPtr.Zero) break;
                }
            }

            // 4. Forcibly bring window to foreground and verify input focus
            bool isForeground = false;
            if (targetHwnd != IntPtr.Zero)
            {
                NotificationListenerService.ForceForegroundWindow(targetHwnd);

                // Wait up to 600ms verifying that the window actually became the active foreground window
                for (int i = 0; i < 6; i++)
                {
                    await Task.Delay(100);
                    if (NativeMethods.GetForegroundWindow() == targetHwnd)
                    {
                        isForeground = true;
                        break;
                    }
                }
            }

            // 5. Deliver or gracefully fallback
            if (isForeground && clipboardSet)
            {
                // Target messaging window is verified in the foreground! Inject Ctrl+V and Enter
                NativeMethods.SimulatePasteAndEnter();

                // Wait briefly for the messaging app to process clipboard paste
                await Task.Delay(150);

                // Restore previous clipboard data
                RestoreClipboard(savedClipboard);

                // Tactile liquid impulse feedback
                TriggerLiquidImpulse(180, -90, 25);

                // Dismiss notification from history ring buffer
                _notificationListener.RemoveHistoryItem(item.Id);

                // Collapse Island back
                _machine.SetPinned(false);
                _machine.CollapseNow();

                // Instant feedback confirmation toast
                _machine.PushToast(new ToastContent(
                    "\uE73E",
                    "Reply Sent",
                    $"Delivered to {item.Title}",
                    item.Accent,
                    TimeSpan.FromSeconds(2.0),
                    Priority: ToastPriority.Feedback));

                return true;
            }
            else
            {
                // Fallback: App could not be confirmed in the foreground.
                // NEVER blindly paste into the active window (which could be the user's terminal/editor).
                // Leave the staged reply on the clipboard and notify the user with a helpful prompt.
                _machine.SetPinned(false);
                _machine.CollapseNow();

                string appName = string.IsNullOrWhiteSpace(item.AppName) ? "app" : item.AppName;
                _machine.PushToast(new ToastContent(
                    "\uE783",
                    "Reply Copied to Clipboard",
                    $"Switch to {appName} and press Ctrl+V to send",
                    item.Accent,
                    TimeSpan.FromSeconds(3.5),
                    Priority: ToastPriority.Feedback));

                return false;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[QuickReply] Error sending reply: {ex.Message}");
            _machine.SetPinned(false);
            _machine.CollapseNow();

            _machine.PushToast(new ToastContent(
                "\uE783",
                "Reply Saved to Clipboard",
                cleanText,
                ThemeBrushes.AmberColor,
                TimeSpan.FromSeconds(3.0),
                Priority: ToastPriority.Feedback));
            return false;
        }
    }

    private static void RestoreClipboard(IDataObject? savedData)
    {
        if (savedData == null) return;
        try
        {
            Clipboard.SetDataObject(savedData, true);
        }
        catch { }
    }

    internal void ToastReplyButton_Click(object sender, RoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)?.Tag as NotificationHistoryItem
            ?? _machine.ActiveToast?.HistoryItem
            ?? _notificationListener.History.FirstOrDefault(i => i.IsMessagingApp)
            ?? _notificationListener.History.FirstOrDefault();
        if (item != null)
        {
            OpenQuickReply(item);
        }
        e.Handled = true;
    }

    internal void ToastApproveBtn_Click(object sender, RoutedEventArgs e)
    {
        var active = _machine.ActiveToast;
        active?.OnPromptDecision?.Invoke("Approve");
        if (!string.IsNullOrWhiteSpace(active?.PromptActivityId))
        {
            _liveActivityIpcService?.ResolvePrompt(active.PromptActivityId, "Approve");
        }
        TactileFeedback.PlaySnap();
        _machine.CollapseNow();
        e.Handled = true;
    }

    internal void ToastRejectBtn_Click(object sender, RoutedEventArgs e)
    {
        var active = _machine.ActiveToast;
        active?.OnPromptDecision?.Invoke("Reject");
        if (!string.IsNullOrWhiteSpace(active?.PromptActivityId))
        {
            _liveActivityIpcService?.ResolvePrompt(active.PromptActivityId, "Reject");
        }
        TactileFeedback.PlaySnap();
        _machine.CollapseNow();
        e.Handled = true;
    }

    internal void ToastDiffBtn_Click(object sender, RoutedEventArgs e)
    {
        var active = _machine.ActiveToast;
        if (!string.IsNullOrWhiteSpace(active?.DiffText))
        {
            TermiContainer.StageDiff(
                active.Title,
                active.Subtitle,
                active.DiffText,
                decision =>
                {
                    active.OnPromptDecision?.Invoke(decision);
                    if (!string.IsNullOrWhiteSpace(active.PromptActivityId))
                    {
                        _liveActivityIpcService?.ResolvePrompt(active.PromptActivityId, decision);
                    }
                });
            SwitchExpandedView(ExpandedView.Termi, animateTransition: true);
            _machine.ExpandNow();
        }
        e.Handled = true;
    }

    internal void PushInteractiveToast(
        string glyph,
        string title,
        string subtitle,
        Color accent,
        TimeSpan duration,
        string activityId,
        string? diffText,
        Action<string> onPromptDecision)
    {
        _machine.PushToast(new ToastContent(
            Glyph: glyph,
            Title: title,
            Subtitle: subtitle,
            Accent: accent,
            Duration: duration,
            TargetView: "termi",
            Priority: ToastPriority.Notification,
            Category: "agent_prompt",
            PromptActivityId: activityId,
            DiffText: diffText,
            OnPromptDecision: onPromptDecision));
    }

    internal void TriggerActivePromptDecision(string decision)
    {
        var active = _machine.ActiveToast;
        if (active != null && (active.OnPromptDecision != null || !string.IsNullOrWhiteSpace(active.PromptActivityId)))
        {
            active.OnPromptDecision?.Invoke(decision);
            if (!string.IsNullOrWhiteSpace(active.PromptActivityId))
            {
                _liveActivityIpcService?.ResolvePrompt(active.PromptActivityId, decision);
            }
            TactileFeedback.PlaySnap();
            _machine.CollapseNow();
            return;
        }

        string? latestId = _liveActivityIpcService?.GetLatestPendingPromptId();
        if (!string.IsNullOrWhiteSpace(latestId))
        {
            _liveActivityIpcService?.ResolvePrompt(latestId, decision);
            TactileFeedback.PlaySnap();
        }
    }

    internal void ReplyNotificationItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: NotificationHistoryItem item })
        {
            OpenQuickReply(item);
            e.Handled = true;
        }
    }

    internal void NotificationCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem)
        {
            if (elem.DataContext is NotificationAppGroup group)
            {
                if (group.LatestItem != null)
                {
                    ActivateNotificationItem(group.LatestItem);
                }
                e.Handled = true;
            }
            else if (elem.DataContext is NotificationHistoryItem item)
            {
                ActivateNotificationItem(item);
                e.Handled = true;
            }
        }
    }

    internal void SubNotificationCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NotificationHistoryItem item })
        {
            ActivateNotificationItem(item);
            e.Handled = true;
        }
    }

    private void ActivateNotificationItem(NotificationHistoryItem item)
    {
        bool activated = NotificationListenerService.ActivateNotification(item, preferApp: item.IsMessagingApp);
        if (activated)
        {
            _notificationListener.RemoveHistoryItem(item.Id);
            _machine.CollapseNow();
        }
        else
        {
            // Fallback: copy notification text to clipboard
            try
            {
                string textToCopy = string.IsNullOrWhiteSpace(item.Body) || item.Body == item.Title
                    ? item.Title
                    : $"{item.Title}\n{item.Body}";
                Clipboard.SetText(textToCopy);

                _machine.PushToast(new ToastContent(
                    item.Glyph,
                    item.AppName,
                    "Notification text copied to clipboard",
                    item.Accent,
                    TimeSpan.FromSeconds(2.0),
                    TargetView: "notifications",
                    Priority: ToastPriority.Feedback));
            }
            catch { }
        }
    }

    internal void ClearNotificationsButton_Click(object sender, RoutedEventArgs e)
    {
        _notificationListener.ClearHistory();
        TriggerLiquidImpulse(180, -90, 25);
    }

    internal void RemoveNotificationItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem)
        {
            if (elem.Tag is string id && !string.IsNullOrEmpty(id))
            {
                _notificationListener.RemoveHistoryItem(id);
                TriggerLiquidImpulse(60, -20, 8);
                e.Handled = true;
            }
            else if (elem.Tag is NotificationAppGroup group)
            {
                DismissNotificationGroup(group);
                e.Handled = true;
            }
        }
    }

    internal void MuteAppNotification_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string appName } && !string.IsNullOrWhiteSpace(appName))
        {
            _settingsService.Update(s => s.MutedNotificationApps.Add(appName), nameof(AppSettings.MutedNotificationApps));
            UpdateMutedAppsBadgeUI();
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

    internal void UnmuteAllAppsButton_Click(object sender, RoutedEventArgs e)
    {
        int count = _settingsService.Current.MutedNotificationApps.Count;
        if (count == 0) return;

        _settingsService.Update(s => s.MutedNotificationApps.Clear(), nameof(AppSettings.MutedNotificationApps));
        UpdateMutedAppsBadgeUI();
        _machine.PushToast(new ToastContent(
            "\uE767",
            "Apps Unmuted",
            $"Restored notifications for {count} muted app{(count == 1 ? "" : "s")}",
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(2.2),
            TargetView: "notifications",
            Priority: ToastPriority.Feedback,
            Category: "notifmute"));
    }

    private void UpdateMutedAppsBadgeUI()
    {
        if (UnmuteAllAppsButton == null || UnmuteAllAppsText == null) return;
        var muted = _settingsService.Current.MutedNotificationApps;
        if (muted.Count > 0)
        {
            UnmuteAllAppsText.Text = $"{muted.Count} Muted \u00B7 Unmute";
            UnmuteAllAppsButton.ToolTip = $"Muted: {string.Join(", ", muted)} (Click to unmute all)";
            UnmuteAllAppsButton.Visibility = Visibility.Visible;
        }
        else
        {
            UnmuteAllAppsButton.Visibility = Visibility.Collapsed;
        }
    }

}
