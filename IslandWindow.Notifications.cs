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
