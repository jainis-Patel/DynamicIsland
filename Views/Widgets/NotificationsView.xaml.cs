using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets;

public partial class NotificationsView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    public NotificationsView()
    {
        InitializeComponent();
    }

    private void ClearNotificationsButton_Click(object sender, RoutedEventArgs e) =>
        Island?.ClearNotificationsButton_Click(sender, e);

    private void MuteAppNotification_Click(object sender, RoutedEventArgs e) =>
        Island?.MuteAppNotification_Click(sender, e);

    private void NotificationCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NotificationCard_Click(sender, e);

    private void RemoveNotificationItem_Click(object sender, RoutedEventArgs e) =>
        Island?.RemoveNotificationItem_Click(sender, e);

    private void VerticalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.VerticalScrollViewer_PreviewMouseWheel(sender, e);

    private void UnmuteAllAppsButton_Click(object sender, RoutedEventArgs e) =>
        Island?.UnmuteAllAppsButton_Click(sender, e);

}
