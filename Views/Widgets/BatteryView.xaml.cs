using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets;

public partial class BatteryView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    private void BackToLauncher_Click(object sender, RoutedEventArgs e) =>
        Island?.NavigateToView(Core.ExpandedView.Launcher);

    public BatteryView()
    {
        InitializeComponent();
    }

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.BrightnessSlider_ValueChanged(sender, e);

    private void NightLightQuickBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.NightLightQuickBtn_Click(sender, e);

    private void CaffeineQuickBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.CaffeineQuickBtn_Click(sender, e);

    private void CaffeineQuickBtn_RightClick(object sender, MouseButtonEventArgs e) =>
        Island?.CaffeineQuickBtn_RightClick(sender, e);

    private void PlanBalancedCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.PlanBalancedCard_Click(sender, e);

    private void PlanPerfCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.PlanPerfCard_Click(sender, e);

    private void PlanSaverCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.PlanSaverCard_Click(sender, e);

}
