using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets.Settings;

public partial class NotificationsSettingsPanel : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    public NotificationsSettingsPanel()
    {
        InitializeComponent();
    }

    private void VerticalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.VerticalScrollViewer_PreviewMouseWheel(sender, e);

    private void OpenNotifCenterBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.OpenNotifCenterBtn_Click(sender, e);

    private void DndToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DndToggleBtn_Click(sender, e);

    private void ReplaceBannersToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ReplaceBannersToggleBtn_Click(sender, e);

    private void GroupNotifsToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.GroupNotifsToggleBtn_Click(sender, e);

    private void UnmuteAppBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.UnmuteAppBtn_Click(sender, e);
}
