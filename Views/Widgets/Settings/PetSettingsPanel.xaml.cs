using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets.Settings;

public partial class PetSettingsPanel : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    public PetSettingsPanel()
    {
        InitializeComponent();
    }

    private void VerticalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.VerticalScrollViewer_PreviewMouseWheel(sender, e);

    private void PetCompanion_Click(object sender, MouseButtonEventArgs e) =>
        Island?.PetCompanion_Click(sender, e);

    private void PetNameBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetNameBtn_Click(sender, e);

    private void PetToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetToggleBtn_Click(sender, e);

    private void PetSpeciesPickBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetSpeciesPickBtn_Click(sender, e);

    private void PetAccessoryPickBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetAccessoryPickBtn_Click(sender, e);

    private void PetCustomNameBox_KeyDown(object sender, KeyEventArgs e) =>
        Island?.PetCustomNameBox_KeyDown(sender, e);

    private void PetCustomNameBox_LostFocus(object sender, RoutedEventArgs e) =>
        Island?.PetCustomNameBox_LostFocus(sender, e);

    private void PetAutoRoamToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetAutoRoamToggleBtn_Click(sender, e);

    private void PetContextToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetContextToggleBtn_Click(sender, e);

    private void OpenPetPlaygroundBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.OpenPetPlaygroundBtn_Click(sender, e);

    private void RoamDesktopBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.RoamDesktopBtn_Click(sender, e);

    private void PetReminderCycleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetReminderCycleBtn_Click(sender, e);

    private void PetInteractBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetInteractBtn_Click(sender, e);

    private void PetTreatBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetTreatBtn_Click(sender, e);

    private void PetTrickBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetTrickBtn_Click(sender, e);

    private void PetHydrateBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetHydrateBtn_Click(sender, e);

    private void PetStretchBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetStretchBtn_Click(sender, e);
}
