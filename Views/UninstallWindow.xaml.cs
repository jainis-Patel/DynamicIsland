using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Core;

namespace DynamicIsland.Views;

public partial class UninstallWindow : Window
{
    private bool _isUninstalled;

    public UninstallWindow()
    {
        InitializeComponent();
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

    private void KeepBtn_Click(object sender, RoutedEventArgs e)
    {
        Application.Current?.Shutdown();
    }

    private async void UninstallBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isUninstalled)
        {
            Application.Current.Shutdown();
            return;
        }

        UninstallBtn.IsEnabled = false;
        KeepBtn.IsEnabled = false;
        RemoveUserDataCheck.IsEnabled = false;

        UninstallProgressBar.Visibility = Visibility.Visible;
        UninstallProgressBar.IsIndeterminate = true;
        StatusText.Text = "Removing Dynamic Island...";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));

        bool removeData = RemoveUserDataCheck.IsChecked == true;

        var result = await Task.Run(() =>
        {
            bool success = InstallerService.Uninstall(removeData, out string? err);
            return (success, err);
        });

        UninstallProgressBar.IsIndeterminate = false;
        UninstallProgressBar.Value = 100;

        if (result.success)
        {
            _isUninstalled = true;
            StatusText.Text = "✓ Dynamic Island has been successfully uninstalled.";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
            KeepBtn.Visibility = Visibility.Collapsed;
            UninstallBtn.Content = "Close";
            UninstallBtn.Background = new SolidColorBrush(Color.FromRgb(0x48, 0x48, 0x4A));
            UninstallBtn.IsEnabled = true;
        }
        else
        {
            StatusText.Text = $"Uninstall error: {result.err ?? "Unknown error"}";
            UninstallBtn.IsEnabled = true;
            KeepBtn.IsEnabled = true;
        }
    }
}
