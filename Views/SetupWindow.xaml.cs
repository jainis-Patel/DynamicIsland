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
            Application.Current.Shutdown();
            return;
        }

        // Begin installation
        ActionBtn.IsEnabled = false;
        CancelBtn.IsEnabled = false;
        AutoStartCheck.IsEnabled = false;
        StartMenuCheck.IsEnabled = false;
        DesktopCheck.IsEnabled = false;

        InstallProgressBar.Visibility = Visibility.Visible;
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

        InstallProgressBar.IsIndeterminate = false;
        InstallProgressBar.Value = 100;

        if (result.success)
        {
            _isInstalled = true;
            StatusText.Text = "✓ Installation complete! Dynamic Island is ready.";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
            CancelBtn.Visibility = Visibility.Collapsed;
            ActionBtn.Content = "Launch Dynamic Island";
            ActionBtn.Background = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
            ActionBtn.IsEnabled = true;
        }
        else
        {
            StatusText.Text = $"Installation failed: {result.err ?? "Unknown error"}";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
            ActionBtn.IsEnabled = true;
            CancelBtn.IsEnabled = true;
        }
    }
}
