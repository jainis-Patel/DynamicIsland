using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets.Settings;

public partial class DisplayPowerSettingsPanel : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    public DisplayPowerSettingsPanel()
    {
        InitializeComponent();
    }

    private void VerticalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.VerticalScrollViewer_PreviewMouseWheel(sender, e);

    private void MonitorSwitchBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.MonitorSwitchBtn_Click(sender, e);

    private void FollowMonitorToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.FollowMonitorToggleBtn_Click(sender, e);

    private void DockButton_Click(object sender, RoutedEventArgs e) =>
        Island?.DockButton_Click(sender, e);

    private void NotchBezelToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.NotchBezelToggleBtn_Click(sender, e);

    private void FloatingPiPToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.FloatingPiPToggleBtn_Click(sender, e);

    private void NotchOffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.NotchOffsetSlider_ValueChanged(sender, e);

    private void NotchPresetBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.NotchPresetBtn_Click(sender, e);

    private void NightLightSettingsBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.NightLightSettingsBtn_Click(sender, e);

    private void NightLightWarmthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.NightLightWarmthSlider_ValueChanged(sender, e);

    private void ChargingPulseToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ChargingPulseToggleBtn_Click(sender, e);

    private void LowBattThresholdBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.LowBattThresholdBtn_Click(sender, e);

    private void AutoPowerSaverToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.AutoPowerSaverToggleBtn_Click(sender, e);

    private void AutoPowerAcToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.AutoPowerAcToggleBtn_Click(sender, e);

    private void HighLoadAlertsToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.HighLoadAlertsToggleBtn_Click(sender, e);

    private void HighCpuThresholdBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.HighCpuThresholdBtn_Click(sender, e);

    private void HighRamThresholdBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.HighRamThresholdBtn_Click(sender, e);

    private void ExpandTriggerBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ExpandTriggerBtn_Click(sender, e);

    private void FastSwipeGuardToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.FastSwipeGuardToggleBtn_Click(sender, e);

    private void GhostModifierBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.GhostModifierBtn_Click(sender, e);

    private void QuickTuckToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.QuickTuckToggleBtn_Click(sender, e);

    private void AutoHideModeBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.AutoHideModeBtn_Click(sender, e);
}
