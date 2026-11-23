using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets.Settings;

public partial class FloraSettingsPanel : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    public FloraSettingsPanel()
    {
        InitializeComponent();
    }

    private void VerticalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.VerticalScrollViewer_PreviewMouseWheel(sender, e);

    private void IslandThemeBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.IslandThemeBtn_Click(sender, e);

    private void NaturePresetBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.NaturePresetBtn_Click(sender, e);

    private void NatureDensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.NatureDensitySlider_ValueChanged(sender, e);

    private void NatureBloomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.NatureBloomSlider_ValueChanged(sender, e);

    private void NatureScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.NatureScaleSlider_ValueChanged(sender, e);

    private void NatureDropSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.NatureDropSlider_ValueChanged(sender, e);

    private void NatureButterflyBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.NatureButterflyBtn_Click(sender, e);

    private void NaturePollenToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.NaturePollenToggleBtn_Click(sender, e);

    private void NaturePollenSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.NaturePollenSlider_ValueChanged(sender, e);
}
