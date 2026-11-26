using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets.Settings;

public partial class SystemUpdatesSettingsPanel : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    public SystemUpdatesSettingsPanel()
    {
        InitializeComponent();
    }

    private void VerticalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.VerticalScrollViewer_PreviewMouseWheel(sender, e);

    private void StartupToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.StartupToggleBtn_Click(sender, e);

    private void WeatherCityBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (WeatherCityPlaceholder != null && WeatherCityBox != null)
        {
            WeatherCityPlaceholder.Visibility = string.IsNullOrEmpty(WeatherCityBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void WeatherCityBox_LostFocus(object sender, RoutedEventArgs e) =>
        Island?.WeatherCityBox_LostFocus(sender, e);

    private void WeatherCityBox_KeyDown(object sender, KeyEventArgs e) =>
        Island?.WeatherCityBox_KeyDown(sender, e);

    private void LowMemoryModeToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.LowMemoryModeToggleBtn_Click(sender, e);

    private void LottieAnimToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.LottieAnimToggleBtn_Click(sender, e);

    private void ExportSettingsBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ExportSettingsBtn_Click(sender, e);

    private void ImportSettingsBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ImportSettingsBtn_Click(sender, e);

    private void ResetSettingsBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ResetSettingsBtn_Click(sender, e);

    private void CheckUpdatesBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.CheckUpdatesBtn_Click(sender, e);

    private void UpdateChannelBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.UpdateChannelBtn_Click(sender, e);

    private void ViewChangelogBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ViewChangelogBtn_Click(sender, e);

    private void SkipUpdateBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.SkipUpdateBtn_Click(sender, e);

    private void CancelDownloadBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.CancelDownloadBtn_Click(sender, e);

    private void InstallUpdateBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.InstallUpdateBtn_Click(sender, e);

    private void AutoCheckToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.AutoCheckToggleBtn_Click(sender, e);

    private void AutoDownloadToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.AutoDownloadToggleBtn_Click(sender, e);

    private void GitHubTokenBox_LostFocus(object sender, RoutedEventArgs e) =>
        Island?.GitHubTokenBox_LostFocus(sender, e);

    private void GitHubTokenBox_KeyDown(object sender, KeyEventArgs e) =>
        Island?.GitHubTokenBox_KeyDown(sender, e);

    private void ToggleVersionHistoryBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ToggleVersionHistoryBtn_Click(sender, e);

    private void RefreshVersionHistoryBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.RefreshVersionHistoryBtn_Click(sender, e);

    private void OpenReleasesPageBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.OpenReleasesPageBtn_Click(sender, e);

    private void ExportAiHooksBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ExportAiAgentHooksWithToast();
}
