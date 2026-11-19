using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets;

public partial class AudioView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    public AudioView()
    {
        InitializeComponent();
    }

    private void AppSessionMuteBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.AppSessionMuteBtn_Click(sender, e);

    private void AppSessionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.AppSessionSlider_ValueChanged(sender, e);

    private void AudioEndpointCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.AudioEndpointCard_Click(sender, e);

    private void AudioModeOutputsBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.AudioModeOutputsBtn_Click(sender, e);

    private void AudioModeMixerBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.AudioModeMixerBtn_Click(sender, e);

    private void HardwareMicBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.HardwareMicBtn_Click(sender, e);

    private void HeroMasterMuteBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.HeroMasterMuteBtn_Click(sender, e);

    private void HeroMasterVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.HeroMasterVolumeSlider_ValueChanged(sender, e);

    private void EndpointSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.EndpointSlider_ValueChanged(sender, e);

    private void EndpointMuteBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.EndpointMuteBtn_Click(sender, e);

    private void AudioView_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.AudioView_PreviewMouseWheel(sender, e);
}
