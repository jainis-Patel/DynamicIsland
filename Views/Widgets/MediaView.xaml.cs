using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets;

public partial class MediaView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    public MediaView()
    {
        InitializeComponent();
    }

    // ── Forwarding Click Events to IslandWindow ──────────────────────────
    private void ClockCalendarBadge_Click(object sender, MouseButtonEventArgs e) =>
        Island?.ClockCalendarBadge_Click(sender, e);

    private void ClockWeatherBadge_Click(object sender, MouseButtonEventArgs e) =>
        Island?.ClockWeatherBadge_Click(sender, e);

    private void ClockWifiBadge_Click(object sender, MouseButtonEventArgs e) =>
        Island?.ClockWifiBadge_Click(sender, e);

    private void HorizontalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.HorizontalScrollViewer_PreviewMouseWheel(sender, e);

    private void MediaLyricsBadge_Click(object sender, MouseButtonEventArgs e) =>
        Island?.MediaLyricsBadge_Click(sender, e);

    private void MediaSourceBadge_Click(object sender, MouseButtonEventArgs e) =>
        Island?.MediaSourceBadge_Click(sender, e);

    private void ToggleClockMediaHeader_Click(object sender, MouseButtonEventArgs e) =>
        Island?.ToggleClockMediaHeader_Click(sender, e);

    private void PomodoroButton_RightClick(object sender, MouseButtonEventArgs e) =>
        Island?.PomodoroButton_RightClick(sender, e);

    private void ExpandedCallMicBadge_Click(object sender, MouseButtonEventArgs e) =>
        Island?.ExpandedCallMicBadge_Click(sender, e);

    private void ExpandedCallTitle_Click(object sender, MouseButtonEventArgs e) =>
        Island?.ExpandedCallTitle_Click(sender, e);

    private void FocusMediaPlayer_Click(object sender, MouseButtonEventArgs e) =>
        Island?.FocusMediaPlayer_Click(sender, e);

    private void ClockSystemHudBadge_Click(object sender, MouseButtonEventArgs e) =>
        Island?.ClockSystemHudBadge_Click(sender, e);

    private void DetachPlayer_Click(object sender, RoutedEventArgs e) =>
        Island?.DetachMiniPlayer();
}