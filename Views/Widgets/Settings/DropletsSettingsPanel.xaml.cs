using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets.Settings;

public partial class DropletsSettingsPanel : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    public DropletsSettingsPanel()
    {
        InitializeComponent();
    }

    private void VerticalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.VerticalScrollViewer_PreviewMouseWheel(sender, e);

    private void DropletEnableAllBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DropletEnableAllBtn_Click(sender, e);

    private void DropletDisableAllBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DropletDisableAllBtn_Click(sender, e);

    private void DropletResetBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DropletResetBtn_Click(sender, e);

    private void DropletSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        Island?.DropletSearchBox_TextChanged(sender, e);

    private void DropletFilterBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DropletFilterBtn_Click(sender, e);

    private void DropletActionBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DropletActionBtn_Click(sender, e);

    private void DropletToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DropletToggleBtn_Click(sender, e);
}
