using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Core;

namespace DynamicIsland.Views;

public partial class SetupWindow : Window
{
    private bool _isInstalled;

    public SetupWindow()
    {
        InitializeComponent();
        InstallPathText.Text = InstallerService.GetDefaultInstallDir();
        Closed += (_, _) => Application.Current?.Shutdown();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Application.Current?.Shutdown();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        Application.Current?.Shutdown();
    }

    private async void ActionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalled)
        {
            // Launch installed app and close installer
            string installedExe = InstallerService.GetInstalledExePath();
            if (File.Exists(installedExe))
            {
                Process.Start(new ProcessStartInfo(installedExe) { UseShellExecute = true });
            }
            Application.Current?.Shutdown();
            return;
        }

        // Lock options during installation
        ActionBtn.IsEnabled = false;
        CancelBtn.IsEnabled = false;
        AutoStartCheck.IsEnabled = false;
        StartMenuCheck.IsEnabled = false;
        DesktopCheck.IsEnabled = false;

        StatusDot.Visibility = Visibility.Visible;
        ProgressBarContainer.Visibility = Visibility.Visible;
        InstallProgressBar.IsIndeterminate = true;
        StatusText.Text = "Installing Dynamic Island...";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));

        bool autoStart = AutoStartCheck.IsChecked == true;
        bool startMenu = StartMenuCheck.IsChecked == true;
        bool desktop = DesktopCheck.IsChecked == true;

        var result = await Task.Run(() =>
        {
            bool success = InstallerService.Install(
                targetDir: null,
                createDesktopShortcut: desktop,
                createStartMenuShortcut: startMenu,
                autoStartOnBoot: autoStart,
                out string? err);
            return (success, err);
        });

        if (result.success)
        {
            _isInstalled = true;
            // Hide progress bar and status line to completely prevent messy orange/green clash
            ProgressBarContainer.Visibility = Visibility.Collapsed;
            StatusContainer.Visibility = Visibility.Collapsed;
            SuccessCard.Visibility = Visibility.Visible;

            CancelBtn.Visibility = Visibility.Collapsed;
            ActionBtn.MinWidth = 210;
            ActionBtn.Background = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
            ActionBtnText.Text = "Launch Dynamic Island";
            ActionBtnIcon.Visibility = Visibility.Visible;
            ActionBtn.IsEnabled = true;
        }
        else
        {
            StatusDot.Visibility = Visibility.Collapsed;
            ProgressBarContainer.Visibility = Visibility.Collapsed;
            StatusText.Text = $"Installation failed: {result.err ?? "Unknown error"}";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
            ActionBtn.IsEnabled = true;
            CancelBtn.IsEnabled = true;
        }
    }
}
