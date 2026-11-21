using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DynamicIsland.Core;

namespace DynamicIsland.Views;

/// <summary>
/// First-party calm, quiet, confident installer and updater for Dynamic Island.
/// Adheres strictly to the single-accent design tokens, progressive disclosure,
/// zero-clutter principles, and seamless 1-click upgrades.
/// </summary>
public partial class SetupWindow : Window
{
    public enum SetupState
    {
        Ready,
        Progress,
        Done,
        Error
    }

    private SetupState _currentState = SetupState.Ready;
    private readonly bool _isUpgrade;
    private string _customInstallDir;
    private string? _lastErrorDetails;
    private bool _isInstalling;
    private bool _isPreview;

    public SetupWindow()
    {
        InitializeComponent();

        // 1. Detect existing installation to determine Upgrade vs Fresh Install flow
        var existing = InstallerService.GetExistingInstallation();
        _isUpgrade = existing.IsInstalled;

        if (_isUpgrade && !string.IsNullOrEmpty(existing.Location))
        {
            _customInstallDir = existing.Location;
        }
        else
        {
            _customInstallDir = InstallerService.GetDefaultInstallDir();
        }

        // 2. Set strings according to strict copy guidelines
        if (_isUpgrade)
        {
            ReadyTitleText.Text = "Update Dynamic Island";
            ReadySubtitleText.Text = $"Version {InstallerService.AppVersion} is ready. Your settings and clipboard history will be kept.";
            ActionBtn.Content = "Update";
            ProgressTitleText.Text = "Updating Dynamic Island…";
            ProgressStatusText.Text = "Updating…";
            DoneSubtitleText.Text = "Dynamic Island is up to date.";
        }
        else
        {
            ReadyTitleText.Text = "Install Dynamic Island";
            ReadySubtitleText.Text = $"Version {InstallerService.AppVersion} is ready.";
            ActionBtn.Content = "Install";
            ProgressTitleText.Text = "Installing Dynamic Island…";
            ProgressStatusText.Text = "Installing…";
            DoneSubtitleText.Text = "Dynamic Island is ready to use.";
        }

        // 3. Initialize Options fields
        InstallPathText.Text = _customInstallDir;
        UpdateDriveSpaceDisplay();

        Closed += (_, _) =>
        {
            if (!_isPreview) Application.Current?.Shutdown();
        };

        // Focus primary button on startup
        Loaded += (_, _) => ActionBtn.Focus();
    }

    private void UpdateDriveSpaceDisplay()
    {
        DriveSpaceText.Text = InstallerService.GetDriveFreeSpace(_customInstallDir);

        // Low disk space warning (< 1.0 GB) uses amber accent warning text
        try
        {
            string root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(_customInstallDir)) ?? "C:\\";
            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.AvailableFreeSpace < 1024L * 1024L * 1024L)
            {
                DriveSpaceText.Foreground = (Brush)FindResource("BrushAccent");
                DriveSpaceText.Text += " · Low disk space";
                return;
            }
        }
        catch { }

        DriveSpaceText.Foreground = (Brush)FindResource("BrushTextDisabled");
    }

    private void ShowState(SetupState state)
    {
        _currentState = state;

        ReadyStatePanel.Visibility = (state == SetupState.Ready) ? Visibility.Visible : Visibility.Collapsed;
        ProgressStatePanel.Visibility = (state == SetupState.Progress) ? Visibility.Visible : Visibility.Collapsed;
        DoneStatePanel.Visibility = (state == SetupState.Done) ? Visibility.Visible : Visibility.Collapsed;
        ErrorStatePanel.Visibility = (state == SetupState.Error) ? Visibility.Visible : Visibility.Collapsed;

        FrameworkElement? target = state switch
        {
            SetupState.Ready => ReadyStatePanel,
            SetupState.Progress => ProgressStatePanel,
            SetupState.Done => DoneStatePanel,
            SetupState.Error => ErrorStatePanel,
            _ => null
        };

        if (target != null)
        {
            AnimateEntrance(target);
        }

        // Dynamic state polish
        if (state == SetupState.Done)
        {
            LaunchBtn.Focus();

            // Subtle one-time draw animation for neutral checkmark (respects reduced motion)
            if (SystemParameters.ClientAreaAnimation && DoneCheckPath != null)
            {
                DoneCheckPath.StrokeDashArray = new DoubleCollection { 40, 40 };
                DoneCheckPath.StrokeDashOffset = 40;
                var checkAnim = new DoubleAnimation(40, 0, new Duration(TimeSpan.FromMilliseconds(220)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                DoneCheckPath.BeginAnimation(Shape.StrokeDashOffsetProperty, checkAnim);
            }
        }
        else if (state == SetupState.Ready)
        {
            ActionBtn.Focus();
        }
    }

    private static void AnimateEntrance(FrameworkElement element)
    {
        if (!SystemParameters.ClientAreaAnimation) return;

        var translate = element.RenderTransform as TranslateTransform;
        if (translate == null)
        {
            translate = new TranslateTransform();
            element.RenderTransform = translate;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(180));

        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8.0, 0.0, duration) { EasingFunction = ease });
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isInstalling && !_isPreview)
        {
            e.Cancel = true;
            return;
        }

        InstallProgressBar?.BeginAnimation(RangeBase.ValueProperty, null);
        base.OnClosing(e);
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling) return;
        Application.Current?.Shutdown();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling) return;
        Application.Current?.Shutdown();
    }

    private void OpenOptions_Click(object sender, RoutedEventArgs e)
    {
        MainViewContainer.Visibility = Visibility.Collapsed;
        OptionsViewContainer.Visibility = Visibility.Visible;
        OptionsBackBtn.Visibility = Visibility.Visible;
        AnimateEntrance(OptionsViewContainer);
    }

    private void BackToMain_Click(object sender, RoutedEventArgs e)
    {
        OptionsViewContainer.Visibility = Visibility.Collapsed;
        MainViewContainer.Visibility = Visibility.Visible;
        OptionsBackBtn.Visibility = Visibility.Collapsed;
        AnimateEntrance(MainViewContainer);
    }

    private void BrowseFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Installation Folder",
                InitialDirectory = Directory.Exists(_customInstallDir)
                    ? _customInstallDir
                    : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            };

            if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                _customInstallDir = dialog.FolderName;
                InstallPathText.Text = _customInstallDir;
                UpdateDriveSpaceDisplay();
            }
        }
        catch (Exception ex)
        {
            App.Log($"[SetupWindow] Folder picker notice: {ex.Message}");
        }
    }

    private async void ActionBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await PerformInstallAsync();
        }
        catch (Exception ex)
        {
            App.Log($"[SetupWindow] ActionBtn failed: {ex.Message}");
        }
    }

    private async Task PerformInstallAsync()
    {
        if (_isInstalling) return;
        _isInstalling = true;

        try
        {
            ShowState(SetupState.Progress);

            InstallProgressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
            InstallProgressBar.Value = 0;
            ProgressStatusText.Text = _isUpgrade ? "Updating…" : "Installing…";

            bool autoStart = AutoStartToggle.IsChecked == true;
            bool desktop = DesktopShortcutToggle.IsChecked == true;
            bool startMenu = StartMenuToggle.IsChecked == true;

            var progress = new Progress<(int Percent, string StepMessage)>(report =>
            {
                AnimateProgress(report.Percent);
                if (report.Percent >= 60)
                {
                    ProgressStatusText.Text = "Almost done…";
                }
                else
                {
                    ProgressStatusText.Text = _isUpgrade ? "Updating…" : "Installing…";
                }
            });

            var result = await Task.Run(() =>
            {
                bool success = InstallerService.Install(
                    targetDir: _customInstallDir,
                    createDesktopShortcut: desktop,
                    createStartMenuShortcut: startMenu,
                    autoStartOnBoot: autoStart,
                    errorMessage: out string? err,
                    progress: progress,
                    initialSettings: null); // Preserves existing user settings, themes, clipboard history on upgrade

                return (success, err);
            });

            if (result.success)
            {
                AnimateProgress(100);
                await Task.Delay(260); // Brief natural settling time
                ShowState(SetupState.Done);
            }
            else
            {
                _lastErrorDetails = result.err;
                ErrorTitleText.Text = _isUpgrade ? "Couldn't finish the update." : "Couldn't finish the installation.";
                ErrorMessageText.Text = CleanErrorMessage(result.err);
                ShowState(SetupState.Error);
            }
        }
        catch (Exception ex)
        {
            _lastErrorDetails = ex.ToString();
            ErrorTitleText.Text = _isUpgrade ? "Couldn't finish the update." : "Couldn't finish the installation.";
            ErrorMessageText.Text = CleanErrorMessage(ex.Message);
            ShowState(SetupState.Error);
        }
        finally
        {
            _isInstalling = false;
        }
    }

    private void AnimateProgress(double targetValue)
    {
        if (SystemParameters.ClientAreaAnimation)
        {
            var anim = new DoubleAnimation(targetValue, new Duration(TimeSpan.FromMilliseconds(200)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            InstallProgressBar.BeginAnimation(RangeBase.ValueProperty, anim);
        }
        else
        {
            InstallProgressBar.Value = targetValue;
        }
    }

    private static string CleanErrorMessage(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "Close any running instances of Dynamic Island and try again.";
        }

        if (rawError.Contains("used by another process", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("access", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("locked", StringComparison.OrdinalIgnoreCase))
        {
            return "Close any running instances of Dynamic Island and try again.";
        }

        if (rawError.Contains("disk", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("space", StringComparison.OrdinalIgnoreCase))
        {
            return "There isn't enough disk space to finish the installation.";
        }

        return "An unexpected error occurred. Close running apps and try again.";
    }

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string targetExe = System.IO.Path.Combine(_customInstallDir, "DynamicIsland.exe");
            if (!File.Exists(targetExe))
            {
                targetExe = InstallerService.GetInstalledExePath();
            }

            if (File.Exists(targetExe))
            {
                Process.Start(new ProcessStartInfo(targetExe) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            App.Log($"[SetupWindow] Launch failed: {ex.Message}");
        }

        Application.Current?.Shutdown();
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await PerformInstallAsync();
        }
        catch (Exception ex)
        {
            App.Log($"[SetupWindow] Retry failed: {ex.Message}");
        }
    }

    private void CopyDetails_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string details = $"Dynamic Island v{InstallerService.AppVersion} Setup Report\n" +
                             $"OS: {Environment.OSVersion}\n" +
                             $"Install Directory: {_customInstallDir}\n" +
                             $"Error: {_lastErrorDetails ?? "Unknown error."}";

            Clipboard.SetText(details);
            CopyDetailsBtn.Content = "Copied to clipboard";

            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                Dispatcher.Invoke(() => CopyDetailsBtn.Content = "Copy details");
            });
        }
        catch (Exception ex)
        {
            App.Log($"[SetupWindow] Copy details failed: {ex.Message}");
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (OptionsViewContainer.Visibility == Visibility.Visible)
            {
                BackToMain_Click(sender, e);
            }
            else
            {
                CloseBtn_Click(sender, e);
            }
            e.Handled = true;
        }
    }

    /// <summary>
    /// Configures the window state for offline visual testing and preview image generation.
    /// </summary>
    public void SetStateForPreview(SetupState state, bool isUpgrade, double progress = 65, bool showOptions = false)
    {
        if (showOptions)
        {
            MainViewContainer.Visibility = Visibility.Collapsed;
            OptionsViewContainer.Visibility = Visibility.Visible;
            OptionsBackBtn.Visibility = Visibility.Visible;
            return;
        }

        MainViewContainer.Visibility = Visibility.Visible;
        OptionsViewContainer.Visibility = Visibility.Collapsed;
        OptionsBackBtn.Visibility = Visibility.Collapsed;

        if (isUpgrade)
        {
            ReadyTitleText.Text = "Update Dynamic Island";
            ReadySubtitleText.Text = $"Version {InstallerService.AppVersion} is ready. Your settings and clipboard history will be kept.";
            ActionBtn.Content = "Update";
            ProgressTitleText.Text = "Updating Dynamic Island…";
            ProgressStatusText.Text = progress >= 60 ? "Almost done…" : "Updating…";
            DoneSubtitleText.Text = "Dynamic Island is up to date.";
        }
        else
        {
            ReadyTitleText.Text = "Install Dynamic Island";
            ReadySubtitleText.Text = $"Version {InstallerService.AppVersion} is ready.";
            ActionBtn.Content = "Install";
            ProgressTitleText.Text = "Installing Dynamic Island…";
            ProgressStatusText.Text = progress >= 60 ? "Almost done…" : "Installing…";
            DoneSubtitleText.Text = "Dynamic Island is ready to use.";
        }

        InstallProgressBar.Value = progress;
        ShowState(state);
    }

    /// <summary>
    /// Renders high-fidelity 96-DPI PNG screenshots of every state to disk.
    /// </summary>
    public static void RenderPreviews(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var win = new SetupWindow { _isPreview = true };
        try
        {
            void Capture(string fileName)
            {
                win.SetupWindowFrame.Measure(new Size(480, 360));
                win.SetupWindowFrame.Arrange(new Rect(0, 0, 480, 360));
                win.SetupWindowFrame.UpdateLayout();

                var rtb = new RenderTargetBitmap(480, 360, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(win.SetupWindowFrame);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(System.IO.Path.Combine(outputDir, fileName));
                encoder.Save(fs);
            }

            // 1. Upgrade Ready
            win.SetStateForPreview(SetupState.Ready, isUpgrade: true);
            Capture("state1_upgrade_ready.png");

            // 2. Fresh Install Ready
            win.SetStateForPreview(SetupState.Ready, isUpgrade: false);
            Capture("state1_fresh_ready.png");

            // 3. Updating Progress
            win.SetStateForPreview(SetupState.Progress, isUpgrade: true, progress: 68);
            Capture("state2_updating.png");

            // 4. Done
            win.SetStateForPreview(SetupState.Done, isUpgrade: true);
            Capture("state3_done.png");

            // 5. Options
            win.SetStateForPreview(SetupState.Ready, isUpgrade: true, showOptions: true);
            Capture("state_options.png");

            // 6. Error
            win.SetStateForPreview(SetupState.Error, isUpgrade: true);
            Capture("state4_error.png");
        }
        finally
        {
            win.Close();
        }
    }
}
