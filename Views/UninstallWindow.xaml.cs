using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Core;

namespace DynamicIsland.Views;

public partial class UninstallWindow : Window
{
    private bool _isUninstalled;
    private bool _isUninstalling;

    public UninstallWindow()
    {
        InitializeComponent();
        ApplySystemTheme();
        Closed += (_, _) => Application.Current?.Shutdown();
    }

    private void ApplySystemTheme()
    {
        if (ThemeManager.IsSystemInLightMode())
        {
            UninstallWindowFrame.Background = ThemeBrushes.CreateFrozen(Color.FromRgb(0xF8, 0xF9, 0xFD));
            UninstallWindowFrame.BorderBrush = ThemeBrushes.CreateFrozen(Color.FromRgb(0xD2, 0xD6, 0xE4));
            HeaderTitleText.Foreground = ThemeBrushes.CreateFrozen(Color.FromRgb(0x1C, 0x1C, 0x1E));
            ContentCardBorder.Background = ThemeBrushes.CreateFrozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
            ContentCardBorder.BorderBrush = ThemeBrushes.CreateFrozen(Color.FromRgb(0xD2, 0xD6, 0xE4));
            PromptBodyText.Foreground = ThemeBrushes.CreateFrozen(Color.FromRgb(0x2C, 0x2C, 0x2E));
            RemoveUserDataCheck.Foreground = ThemeBrushes.CreateFrozen(Color.FromRgb(0x3A, 0x3A, 0x3C));
            if (UninstallShadow != null)
            {
                UninstallShadow.Opacity = 0.22;
                UninstallShadow.BlurRadius = 36;
            }
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isUninstalling)
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isUninstalling) return;
        Application.Current?.Shutdown();
    }

    private void KeepBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isUninstalling) return;
        Application.Current?.Shutdown();
    }

    private async void UninstallBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isUninstalled)
        {
            Application.Current?.Shutdown();
            return;
        }

        if (_isUninstalling) return;
        _isUninstalling = true;

        UninstallBtn.IsEnabled = false;
        KeepBtn.IsEnabled = false;
        RemoveUserDataCheck.IsEnabled = false;

        StatusContainer.Visibility = Visibility.Visible;
        ProgressBarContainer.Visibility = Visibility.Visible;
        UninstallProgressBar.IsIndeterminate = true;
        StatusText.Text = "Removing Dynamic Island files and shortcuts...";
        StatusText.Foreground = ThemeBrushes.Red;

        bool removeData = RemoveUserDataCheck.IsChecked == true;

        try
        {
            var result = await Task.Run(() =>
            {
                bool success = InstallerService.Uninstall(removeData, out string? err);
                return (success, err);
            });

            if (result.success)
            {
                _isUninstalled = true;
                ProgressBarContainer.Visibility = Visibility.Collapsed;
                StatusContainer.Visibility = Visibility.Collapsed;
                SuccessCard.Visibility = Visibility.Visible;

                KeepBtn.Visibility = Visibility.Collapsed;
                UninstallBtn.MinWidth = 120;
                UninstallBtnText.Text = "Close";
                UninstallBtn.Background = ThemeBrushes.CreateFrozen(Color.FromRgb(0x28, 0x2A, 0x38));
                UninstallBtn.IsEnabled = true;
            }
            else
            {
                ProgressBarContainer.Visibility = Visibility.Collapsed;
                StatusText.Text = $"Uninstall error: {result.err ?? "Unknown error"}";
                UninstallBtn.IsEnabled = true;
                KeepBtn.IsEnabled = true;
                RemoveUserDataCheck.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            App.LogDebug("UninstallWindow.Uninstall", ex);
            ProgressBarContainer.Visibility = Visibility.Collapsed;
            StatusText.Text = $"Uninstall error: {ex.Message}";
            UninstallBtn.IsEnabled = true;
            KeepBtn.IsEnabled = true;
            RemoveUserDataCheck.IsEnabled = true;
        }
        finally
        {
            _isUninstalling = false;
        }
    }
}
