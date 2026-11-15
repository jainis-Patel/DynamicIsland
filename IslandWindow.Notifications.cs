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
        NotificationsEmptyPlaceholder.Visibility = items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        ClearNotificationsButton.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SyncObservableCollection(_notificationHistoryItems, items);

        // Dynamically adjust expanded height if currently viewing Notifications
        if (_machine.State == IslandState.Expanded && _currentExpandedView == ExpandedView.Notifications)
        {
            var targetShape = GetTargetShape(_machine.State, ExpandedView.Notifications);
            ExpandedPanel.Width = targetShape.Width;
            ExpandedPanel.Height = targetShape.Height;
            _width.Target = targetShape.Width;
            _height.Target = targetShape.Height;
            _radius.Target = targetShape.Radius;
            EnsureRendering();
        }
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

    public void OpenTargetApp(NotificationHistoryItem item)
    {
        if (item == null) return;
        NotificationListenerService.ActivateNotification(item);
        _machine.SetPinned(false);
        _machine.CollapseNow();
    }

    public async Task<bool> SendQuickReplyAsync(NotificationHistoryItem item, string text)
    {
        if (item == null || string.IsNullOrWhiteSpace(text)) return false;

        string cleanText = text.Trim();
        try
        {
            // 1. Stage text onto Windows clipboard with transient retry
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    Clipboard.SetText(cleanText);
                    break;
                }
                catch
                {
                    await Task.Delay(25);
                }
            }

            // 2. Focus the target messaging application window
            bool activated = NotificationListenerService.ActivateNotification(item);

            // 3. Wait for the window to finish foreground transition and accept input
            await Task.Delay(200);

            // 4. Inject Ctrl+V and Return to deliver the staged message
            NativeMethods.SimulatePasteAndEnter();

            // 5. Tactile liquid impulse feedback
            TriggerLiquidImpulse(180, -90, 25);

            // 6. Dismiss the notification from history ring buffer
            _notificationListener.RemoveHistoryItem(item.Id);

            // 7. Collapse Island back
            _machine.SetPinned(false);
            _machine.CollapseNow();

            // 8. Surface instant feedback confirmation toast
            _machine.PushToast(new ToastContent(
                "\uE73E",
                "Reply Sent",
                $"Delivered to {item.Title}",
                item.Accent,
                TimeSpan.FromSeconds(2.0),
                Priority: ToastPriority.Feedback));

            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[QuickReply] Error sending reply: {ex.Message}");
            _machine.PushToast(new ToastContent(
                "\uE783",
                "Reply Saved to Clipboard",
                cleanText,
                ThemeBrushes.AmberColor,
                TimeSpan.FromSeconds(2.5),
                Priority: ToastPriority.Feedback));
            return false;
        }
    }

    internal void ToastReplyButton_Click(object sender, RoutedEventArgs e)
    {
        var item = _machine.ActiveToast?.HistoryItem ?? _notificationListener.History.FirstOrDefault();
        if (item != null)
        {
            OpenQuickReply(item);
        }
        e.Handled = true;
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
        if (sender is FrameworkElement { DataContext: NotificationHistoryItem item })
        {
            bool activated = NotificationListenerService.ActivateNotification(item);
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
            e.Handled = true;
        }
    }

    internal void ClearNotificationsButton_Click(object sender, RoutedEventArgs e)
    {
        _notificationListener.ClearHistory();
        TriggerLiquidImpulse(180, -90, 25);
    }

    internal void RemoveNotificationItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            _notificationListener.RemoveHistoryItem(id);
            TriggerLiquidImpulse(60, -20, 8);
            e.Handled = true;
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
