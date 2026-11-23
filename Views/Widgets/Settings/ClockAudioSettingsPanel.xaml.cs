using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets.Settings;

public partial class ClockAudioSettingsPanel : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    public ClockAudioSettingsPanel()
    {
        InitializeComponent();
    }

    private void VerticalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.VerticalScrollViewer_PreviewMouseWheel(sender, e);

    private void HorizontalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.HorizontalScrollViewer_PreviewMouseWheel(sender, e);

    private void ClockScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        if (ClockStylesScrollViewer != null)
            Island?.SmoothScrollHorizontal(ClockStylesScrollViewer, -230);
    }

    private void ClockScrollRight_Click(object sender, RoutedEventArgs e)
    {
        if (ClockStylesScrollViewer != null)
            Island?.SmoothScrollHorizontal(ClockStylesScrollViewer, 230);
    }

    private void ClockStyleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ClockStyleBtn_Click(sender, e);

    private void VisualizerStyleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.VisualizerStyleBtn_Click(sender, e);

    private void CameraLensToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.CameraLensToggleBtn_Click(sender, e);

    private void LyricsToggleBtnSettings_Click(object sender, RoutedEventArgs e) =>
        Island?.LyricsToggleBtnSettings_Click(sender, e);

    private void CompactLyricsToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.CompactLyricsToggleBtn_Click(sender, e);

    private void NetSpeedIdleToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.NetSpeedIdleToggleBtn_Click(sender, e);

    private void SecondaryDropletToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.SecondaryDropletToggleBtn_Click(sender, e);
}
