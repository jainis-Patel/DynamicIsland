using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets;

public partial class SystemHudView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    private void BackToLauncher_Click(object sender, RoutedEventArgs e) =>
        Island?.NavigateToView(Core.ExpandedView.Launcher);

    public SystemHudView()
    {
        InitializeComponent();
    }

    private void HudKillHog1_Click(object sender, RoutedEventArgs e) =>
        Island?.HudKillHog1_Click(sender, e);

    private void HudKillHog2_Click(object sender, RoutedEventArgs e) =>
        Island?.HudKillHog2_Click(sender, e);

    private void HudKillHog3_Click(object sender, RoutedEventArgs e) =>
        Island?.HudKillHog3_Click(sender, e);

    private void HudPurgeRamButton_Click(object sender, RoutedEventArgs e) =>
        Island?.HudPurgeRamButton_Click(sender, e);

    private void HudPurgeRunawaysButton_Click(object sender, RoutedEventArgs e) =>
        Island?.HudPurgeRunawaysButton_Click(sender, e);

    private void HudKillRunaway_Click(object sender, RoutedEventArgs e) =>
        Island?.HudKillRunaway_Click(sender, e);

    private void HudTaskManagerButton_Click(object sender, RoutedEventArgs e) =>
        Island?.HudTaskManagerButton_Click(sender, e);

    private void HudSnoozeResetBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.HudSnoozeResetBtn_Click(sender, e);

}
