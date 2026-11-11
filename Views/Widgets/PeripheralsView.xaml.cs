using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets;

public partial class PeripheralsView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    public PeripheralsView()
    {
        InitializeComponent();
    }

    private void OpenBluetoothSettingsBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.OpenBluetoothSettingsBtn_Click(sender, e);

    private void PeripheralCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.PeripheralCard_Click(sender, e);

    private void RefreshPeripheralsBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.RefreshPeripheralsBtn_Click(sender, e);

    private void ScrollViewer_MouseUp(object sender, MouseButtonEventArgs e) =>
        Island?.ScrollViewer_MouseUp(sender, e);

    private void PeripheralsFilterBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PeripheralsFilterBtn_Click(sender, e);

    private void HorizontalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.HorizontalScrollViewer_PreviewMouseWheel(sender, e);

}
