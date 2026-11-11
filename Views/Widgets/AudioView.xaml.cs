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

    private void AudioModeToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.AudioModeToggleBtn_Click(sender, e);

    private void EndpointSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.EndpointSlider_ValueChanged(sender, e);

    private void EndpointMuteBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.EndpointMuteBtn_Click(sender, e);

    private void HorizontalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.HorizontalScrollViewer_PreviewMouseWheel(sender, e);

}
