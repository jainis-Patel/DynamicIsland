using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.Core;

namespace DynamicIsland.Views.Widgets;

public partial class MediaView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    public MediaView()
    {
        InitializeComponent();

        BentoMotion.AttachInteractiveSpring(ExpandedArtDisc, hoverScale: 1.04, pressScale: 0.96);
        BentoMotion.AttachInteractiveSpring(PrevButton, hoverScale: 1.08, pressScale: 0.90);
        BentoMotion.AttachInteractiveSpring(PlayPauseButton, hoverScale: 1.10, pressScale: 0.90);
        BentoMotion.AttachInteractiveSpring(NextButton, hoverScale: 1.08, pressScale: 0.90);
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

    private void CycleAlbumArtStyle_Click(object sender, MouseButtonEventArgs e) =>
        Island?.CycleAlbumArtStyle_Click(sender, e);

    private void ClockSystemHudBadge_Click(object sender, MouseButtonEventArgs e) =>
        Island?.ClockSystemHudBadge_Click(sender, e);

    private void AudioOutputQuickBtn_Click(object sender, RoutedEventArgs e)
    {
        Island?.SwitchExpandedView(ExpandedView.Audio);
        e.Handled = true;
    }

    private void DetachPlayer_Click(object sender, RoutedEventArgs e) =>
        Island?.DetachMiniPlayer();
}
