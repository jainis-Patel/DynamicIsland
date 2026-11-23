using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets.Settings;

public partial class DockSettingsPanel : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    public DockSettingsPanel()
    {
        InitializeComponent();
    }

    private void VerticalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.VerticalScrollViewer_PreviewMouseWheel(sender, e);

    private void DockPinAllBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DockPinAllBtn_Click(sender, e);

    private void DockPinMinimalBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DockPinMinimalBtn_Click(sender, e);

    private void DockPinResetBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DockPinResetBtn_Click(sender, e);

    private void DockPinToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DockPinToggleBtn_Click(sender, e);
}
