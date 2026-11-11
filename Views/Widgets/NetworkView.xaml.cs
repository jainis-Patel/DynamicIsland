using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets;

public partial class NetworkView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    private void BackToLauncher_Click(object sender, RoutedEventArgs e) =>
        Island?.NavigateToView(Core.ExpandedView.Launcher);

    public NetworkView()
    {
        InitializeComponent();
    }

    private void BtToggleCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.BtToggleCard_Click(sender, e);

    private void HotspotCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.HotspotCard_Click(sender, e);

    private void NetSpeedIdleToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.NetSpeedIdleToggleBtn_Click(sender, e);

    private void NetworkIpBadge_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NetworkIpBadge_Click(sender, e);

    private void OpenNetworkSettingsBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.OpenNetworkSettingsBtn_Click(sender, e);

    private void ScanWifiModeBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ScanWifiModeBtn_Click(sender, e);

    private void ScrollViewer_MouseUp(object sender, MouseButtonEventArgs e) =>
        Island?.ScrollViewer_MouseUp(sender, e);

    private void WifiNetworkCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.WifiNetworkCard_Click(sender, e);

    private void WifiNetworksCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.WifiNetworksCard_Click(sender, e);

    private void WifiToggleCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.WifiToggleCard_Click(sender, e);

    private void HorizontalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.HorizontalScrollViewer_PreviewMouseWheel(sender, e);

}
