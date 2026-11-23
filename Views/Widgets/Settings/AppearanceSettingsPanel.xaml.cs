using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets.Settings;

public partial class AppearanceSettingsPanel : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    public AppearanceSettingsPanel()
    {
        InitializeComponent();
    }

    private void VerticalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.VerticalScrollViewer_PreviewMouseWheel(sender, e);

    private void ThemeStyleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ThemeStyleBtn_Click(sender, e);

    private void ThemeModeBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ThemeModeBtn_Click(sender, e);

    private void ThemeSwatch_Click(object sender, RoutedEventArgs e) =>
        Island?.ThemeSwatch_Click(sender, e);

    private void LiquidIntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.LiquidIntensitySlider_ValueChanged(sender, e);

    private void AnimationSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.AnimationSpeedSlider_ValueChanged(sender, e);

    private void IslandScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.IslandScaleSlider_ValueChanged(sender, e);

    private void BackdropOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.BackdropOpacitySlider_ValueChanged(sender, e);

    private void GlassBlurSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.GlassBlurSlider_ValueChanged(sender, e);

    private void GlassRefractionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.GlassRefractionSlider_ValueChanged(sender, e);

    private void EdgeBorderToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.EdgeBorderToggleBtn_Click(sender, e);

    private void HaloToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.HaloToggleBtn_Click(sender, e);

    private void MotionModeBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.MotionModeBtn_Click(sender, e);

    private void LiquidPulseBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.LiquidPulseBtn_Click(sender, e);
}
