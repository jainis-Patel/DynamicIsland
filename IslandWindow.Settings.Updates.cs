using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

public partial class IslandWindow
{
    // =================================================================
    //  GitHub Releases & Version Control Manager
    // =================================================================

    private void CheckPostUpdateLaunchStatus()
    {
        if (App.IsPostUpdateLaunch)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(1200);
                _machine.PushToast(new ToastContent(
                    Glyph: "\uE73E",
                    Title: "Updated to v" + _updateService.CurrentVersionString,
                    Subtitle: "Dynamic Island has been successfully upgraded.",
                    Accent: ThemeBrushes.GreenColor,
                    Duration: TimeSpan.FromSeconds(4.5),
                    Priority: ToastPriority.Feedback
                ));
            });
        }
    }

    private void OnUpdateAvailable(UpdateManifest manifest)
    {
        if (manifest.IsCritical)
        {
            _machine.PushToast(new ToastContent(
                Glyph: "\uE7BA",
                Title: $"⚠️ Critical Update: v{manifest.Version}",
                Subtitle: string.IsNullOrWhiteSpace(manifest.Summary) ? "Mandatory security update required. Click to install." : manifest.Summary,
                Accent: Color.FromRgb(0xFF, 0x45, 0x3A),
                Duration: TimeSpan.FromSeconds(10.0),
                TargetView: "settings",
                Priority: ToastPriority.Critical
            ));
        }
        else
        {
            _machine.PushToast(new ToastContent(
                Glyph: "\uE896",
                Title: $"Dynamic Island v{manifest.Version} Available",
                Subtitle: string.IsNullOrWhiteSpace(manifest.Summary) ? "Click to open settings and install" : manifest.Summary,
                Accent: Color.FromRgb(0x0A, 0x84, 0xFF),
                Duration: TimeSpan.FromSeconds(6.0),
                TargetView: "settings",
                Priority: ToastPriority.Feedback
            ));
        }

        UpdateSettingsVersionAndStatusUI();
    }

    private void OnUpdateStatusChanged(UpdateStatus status, string? message)
    {
        if (status == UpdateStatus.ReadyToInstall && _updateService.LatestManifest != null)
        {
            _machine.PushToast(new ToastContent(
                Glyph: "\uE73E",
                Title: $"Dynamic Island v{_updateService.LatestManifest.Version} Ready",
                Subtitle: "Update downloaded & verified. Click to restart and update.",
                Accent: ThemeBrushes.GreenColor,
                Duration: TimeSpan.FromSeconds(7.0),
                TargetView: "settings",
                Priority: ToastPriority.Feedback
            ));
        }

        UpdateSettingsVersionAndStatusUI();
    }

    private void OnUpdateDownloadProgressChanged(UpdateProgressInfo info)
    {
        var su = SettingsContainer?.SystemUpdatesPanel;
        if (su?.UpdateProgressBar != null)
        {
            su.UpdateProgressBar.Visibility = Visibility.Visible;
            su.UpdateProgressBar.Value = info.Percent;
        }

        if (su?.UpdateProgressText != null)
        {
            su.UpdateProgressText.Visibility = Visibility.Visible;
            su.UpdateProgressText.Text = info.StatusMessage;
        }
    }

    internal void UpdateSettingsVersionAndStatusUI()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(UpdateSettingsVersionAndStatusUI);
            return;
        }

        if (SettingsContainer?.AppVersionText != null)
        {
            SettingsContainer.AppVersionText.Text = _updateService.CurrentVersionString;
        }

        var su = SettingsContainer?.SystemUpdatesPanel;
        if (su != null)
        {
            if (su.UpdateChannelText != null)
            {
                su.UpdateChannelText.Text = _settingsService.Current.UpdateChannel;
            }

            if (su.AutoCheckToggleSwitch != null)
            {
                su.AutoCheckToggleSwitch.IsChecked = _settingsService.Current.AutoCheckUpdates;
            }

            if (su.AutoDownloadToggleSwitch != null)
            {
                su.AutoDownloadToggleSwitch.IsChecked = _settingsService.Current.AutoDownloadUpdates;
            }

            if (su.GitHubTokenBox != null && !su.GitHubTokenBox.IsFocused && !su.GitHubTokenBox.IsKeyboardFocused)
            {
                string stored = _settingsService.Current.GitHubToken ?? "";
                if (!string.Equals(su.GitHubTokenBox.Password, stored, StringComparison.Ordinal))
                {
                    su.GitHubTokenBox.Password = stored;
                }
            }

            if (su.LastCheckTimestampText != null)
            {
                var last = _settingsService.Current.LastUpdateCheckTime;
                su.LastCheckTimestampText.Text = last.HasValue
                    ? $"Last checked: {last.Value.ToLocalTime():g}"
                    : "Last checked: Never";
            }

            if (su.UpdateStatusSubtitleText != null)
            {
                su.UpdateStatusSubtitleText.Text = _updateService.Status switch
                {
                    UpdateStatus.Checking => "Connecting to GitHub...",
                    UpdateStatus.Downloading => "Downloading update package...",
                    UpdateStatus.Verifying => "Verifying package integrity (SHA-256)...",
                    UpdateStatus.ReadyToInstall => "Update package verified and ready to install.",
                    UpdateStatus.Installing => "Restarting Dynamic Island...",
                    UpdateStatus.Failed => _updateService.LastErrorMessage ?? "Update check failed.",
                    UpdateStatus.UpToDate => $"Dynamic Island {_updateService.CurrentVersionString} is up to date.",
                    _ => "Check releases, switch channels, or rollback"
                };
            }

            if (_updateService.Status is UpdateStatus.Idle or UpdateStatus.UpToDate)
            {
                if (su.UpdateProgressBar != null) su.UpdateProgressBar.Visibility = Visibility.Collapsed;
                if (su.UpdateProgressText != null) su.UpdateProgressText.Visibility = Visibility.Collapsed;
            }

            if (su.UpdateAvailableBanner != null)
            {
                bool hasUpdate = _updateService.LatestManifest != null &&
                                 _updateService.LatestManifest.ParsedVersion > _updateService.CurrentVersion;

                su.UpdateAvailableBanner.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;

                if (hasUpdate && _updateService.LatestManifest != null)
                {
                    if (su.UpdateVersionTitleText != null)
                    {
                        su.UpdateVersionTitleText.Text = _updateService.LatestManifest.IsCritical
                            ? $"Critical Update v{_updateService.LatestManifest.Version}"
                            : $"v{_updateService.LatestManifest.Version} Available";
                    }

                    if (su.UpdateSummaryText != null)
                    {
                        su.UpdateSummaryText.Text = string.IsNullOrWhiteSpace(_updateService.LatestManifest.Summary)
                            ? "New release published on GitHub."
                            : _updateService.LatestManifest.Summary;
                    }

                    if (su.ViewChangelogBtn != null)
                    {
                        bool hasChangelog = !string.IsNullOrWhiteSpace(_updateService.LatestManifest.ChangelogUrl);
                        su.ViewChangelogBtn.Visibility = hasChangelog ? Visibility.Visible : Visibility.Collapsed;
                    }

                    if (su.InstallUpdateBtnText != null)
                    {
                        su.InstallUpdateBtnText.Text = _updateService.Status switch
                        {
                            UpdateStatus.Downloading => "Downloading...",
                            UpdateStatus.Verifying => "Verifying...",
                            UpdateStatus.ReadyToInstall => "Restart & Update",
                            UpdateStatus.Installing => "Updating...",
                            _ => "Install & Restart"
                        };
                    }
                }
            }
        }

        // Update notification badges (Island Pill dot, Dock Settings icon badge, & Left-Rail System badge)
        bool hasUpdateDot = _updateService.Status == UpdateStatus.UpdateAvailable ||
                            _updateService.Status == UpdateStatus.ReadyToInstall;

        if (SettingsContainer?.SettingsRailUpdateBadgeDot != null)
        {
            SettingsContainer.SettingsRailUpdateBadgeDot.Visibility = hasUpdateDot ? Visibility.Visible : Visibility.Collapsed;
            if (hasUpdateDot)
            {
                SettingsContainer.SettingsRailUpdateBadgeDot.Background = _updateService.Status == UpdateStatus.ReadyToInstall
                    ? ThemeBrushes.Green
                    : ThemeBrushes.Blue;
            }
        }

        if (SettingsUpdateBadgeDot != null)
        {
            SettingsUpdateBadgeDot.Visibility = hasUpdateDot ? Visibility.Visible : Visibility.Collapsed;
            if (hasUpdateDot)
            {
                SettingsUpdateBadgeDot.Background = _updateService.Status == UpdateStatus.ReadyToInstall
                    ? ThemeBrushes.Green
                    : ThemeBrushes.Blue;
            }
        }

        if (UpdateAvailableDot != null)
        {
            UpdateAvailableDot.Visibility = hasUpdateDot ? Visibility.Visible : Visibility.Collapsed;
            if (hasUpdateDot)
            {
                bool isReady = _updateService.Status == UpdateStatus.ReadyToInstall;
                if (UpdateDotCore != null)
                {
                    UpdateDotCore.Fill = isReady ? ThemeBrushes.Green : ThemeBrushes.Blue;
                }
                if (UpdateDotGlowStop != null)
                {
                    UpdateDotGlowStop.Color = isReady
                        ? Color.FromArgb(0x80, 0x30, 0xD1, 0x58)
                        : Color.FromArgb(0x80, 0x0A, 0x84, 0xFF);
                }
                UpdateAvailableDot.ToolTip = isReady
                    ? $"Dynamic Island v{_updateService.LatestManifest?.Version} Ready to Install · Click to open System & Updates"
                    : $"Dynamic Island v{_updateService.LatestManifest?.Version} Available · Click to open System & Updates";
            }
        }
    }

    internal void UpdateAvailableDot_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _machine.ExpandNow();
        SwitchExpandedView(ExpandedView.Settings, true);
        SwitchSettingsTab("Updates", impulse: false);
    }

    internal void ViewChangelogBtn_Click(object sender, RoutedEventArgs e)
    {
        string? url = _updateService.LatestManifest?.ChangelogUrl;
        if (!string.IsNullOrWhiteSpace(url))
        {
            SafeProcessLauncher.TryOpenUrl(url);
        }
    }

    private bool _isLoadingVersionHistory;

    internal async void CheckUpdatesBtn_Click(object sender, RoutedEventArgs e)
    {
        var su = SettingsContainer?.SystemUpdatesPanel;
        if (su?.CheckUpdatesBtnText != null) su.CheckUpdatesBtnText.Text = "Checking...";
        try
        {
            var manifest = await _updateService.CheckForUpdatesAsync(silent: false, force: true);
            if (_isClosing) return;
            if (manifest != null && (_updateService.Status == UpdateStatus.UpdateAvailable || _updateService.Status == UpdateStatus.ReadyToInstall))
            {
                _machine.PushToast(new ToastContent(
                    "\uE896",
                    manifest.IsCritical ? $"Critical Update v{manifest.Version}" : $"Update v{manifest.Version} Available",
                    _updateService.Status == UpdateStatus.ReadyToInstall ? "Ready to install from Settings" : "Click Install & Restart in Settings",
                    ThemeBrushes.BlueColor,
                    TimeSpan.FromSeconds(3.5),
                    TargetView: "settings",
                    Priority: ToastPriority.Feedback
                ));
            }
            else if (manifest == null && _updateService.Status == UpdateStatus.UpToDate)
            {
                _machine.PushToast(new ToastContent(
                    "\uE73E",
                    "Up to Date",
                    $"Dynamic Island v{_updateService.CurrentVersionString} is the latest version",
                    ThemeBrushes.GreenColor,
                    TimeSpan.FromSeconds(2.5),
                    TargetView: "settings",
                    Priority: ToastPriority.Feedback
                ));
            }
            else if (_updateService.Status == UpdateStatus.Failed)
            {
                _machine.PushToast(new ToastContent(
                    "\uE783",
                    "Update Check Failed",
                    _updateService.LastErrorMessage ?? "Could not connect to GitHub.",
                    ThemeBrushes.AmberColor,
                    TimeSpan.FromSeconds(3.5),
                    TargetView: "settings",
                    Priority: ToastPriority.Feedback
                ));
            }
        }
        catch (Exception ex)
        {
            App.Log($"[Settings] CheckUpdatesBtn_Click error: {ex}");
            if (!_isClosing)
            {
                _machine.PushToast(new ToastContent(
                    "\uE783",
                    "Update Check Failed",
                    ex.Message,
                    ThemeBrushes.AmberColor,
                    TimeSpan.FromSeconds(3.5),
                    TargetView: "settings",
                    Priority: ToastPriority.Feedback
                ));
            }
        }
        finally
        {
            if (!_isClosing)
            {
                var suDone = SettingsContainer?.SystemUpdatesPanel;
                if (suDone?.CheckUpdatesBtnText != null) suDone.CheckUpdatesBtnText.Text = "Check";
                UpdateSettingsVersionAndStatusUI();
            }
        }
    }

    internal void UpdateChannelBtn_Click(object sender, RoutedEventArgs e)
    {
        string next = string.Equals(_settingsService.Current.UpdateChannel, "Stable", StringComparison.OrdinalIgnoreCase) ? "Beta" : "Stable";
        _settingsService.Update(s => s.UpdateChannel = next, nameof(AppSettings.UpdateChannel));
        _machine.PushToast(new ToastContent(
            "\uE713",
            "Update Channel Changed",
            $"Switched to {next} release channel",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback
        ));
    }

    internal async void InstallUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        var su = SettingsContainer?.SystemUpdatesPanel;
        try
        {
            if (_updateService.Status == UpdateStatus.ReadyToInstall)
            {
                _updateService.ApplyUpdateAndRestart();
                return;
            }

            if (_updateService.Status == UpdateStatus.UpdateAvailable || _updateService.Status == UpdateStatus.Failed)
            {
                if (su?.InstallUpdateBtnText != null) su.InstallUpdateBtnText.Text = "Downloading...";
                if (su?.UpdateProgressBar != null) su.UpdateProgressBar.Visibility = Visibility.Visible;
                if (su?.UpdateProgressText != null) su.UpdateProgressText.Visibility = Visibility.Visible;

                bool ok = await _updateService.DownloadUpdateAsync();
                if (_isClosing) return;

                if (ok)
                {
                    if (su?.InstallUpdateBtnText != null) su.InstallUpdateBtnText.Text = "Restart Now";
                    if (su?.UpdateProgressText != null) su.UpdateProgressText.Text = "Verified & ready to install";
                    _machine.PushToast(new ToastContent(
                        "\uE896",
                        "Ready to Install",
                        "Click 'Restart Now' to finish upgrading Dynamic Island",
                        ThemeBrushes.GreenColor,
                        TimeSpan.FromSeconds(4.0),
                        TargetView: "settings",
                        Priority: ToastPriority.Feedback
                    ));
                }
                else
                {
                    if (su?.InstallUpdateBtnText != null) su.InstallUpdateBtnText.Text = "Retry Download";
                    if (su?.UpdateProgressText != null) su.UpdateProgressText.Text = _updateService.LastErrorMessage ?? "Download failed";
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[Settings] InstallUpdateBtn_Click error: {ex}");
            if (!_isClosing)
            {
                if (su?.InstallUpdateBtnText != null) su.InstallUpdateBtnText.Text = "Retry Download";
                if (su?.UpdateProgressText != null) su.UpdateProgressText.Text = ex.Message;
            }
        }
    }

    internal void AutoCheckToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.AutoCheckUpdates;
        _settingsService.Update(s => s.AutoCheckUpdates = next, nameof(AppSettings.AutoCheckUpdates));
    }

    internal void AutoDownloadToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool next = !_settingsService.Current.AutoDownloadUpdates;
        _settingsService.Update(s => s.AutoDownloadUpdates = next, nameof(AppSettings.AutoDownloadUpdates));
    }

    internal void GitHubTokenBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitGitHubToken();
            e.Handled = true;
        }
    }

    internal void GitHubTokenBox_LostFocus(object sender, RoutedEventArgs e) => CommitGitHubToken();

    private void CommitGitHubToken()
    {
        var tokenBox = SettingsContainer?.SystemUpdatesPanel?.GitHubTokenBox;
        if (tokenBox != null)
        {
            string? token = string.IsNullOrWhiteSpace(tokenBox.Password) ? null : tokenBox.Password.Trim();
            if (_settingsService.Current.GitHubToken != token)
            {
                _settingsService.Update(s => s.GitHubToken = token, nameof(AppSettings.GitHubToken));
                _machine.PushToast(new ToastContent(
                    Glyph: "\uE73E",
                    Title: "GitHub Token Saved",
                    Subtitle: string.IsNullOrEmpty(token) ? "Token cleared (standard 60 req/hr rate limit)" : "Token activated (5,000 req/hr rate limit)",
                    Accent: ThemeBrushes.GreenColor,
                    Duration: TimeSpan.FromSeconds(2.5),
                    TargetView: "settings",
                    Priority: ToastPriority.Feedback
                ));
            }
        }
    }

    internal async void ToggleVersionHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        var su = SettingsContainer?.SystemUpdatesPanel;
        if (su?.VersionHistoryContainer == null) return;
        bool isVisible = su.VersionHistoryContainer.Visibility == Visibility.Visible;
        su.VersionHistoryContainer.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
        if (su.ToggleVersionHistoryText != null)
        {
            su.ToggleVersionHistoryText.Text = isVisible ? "View Releases" : "Hide Releases";
        }

        if (!isVisible && su.VersionHistoryList != null && _updateService.ReleaseHistory.Count == 0 && !_isLoadingVersionHistory)
        {
            _isLoadingVersionHistory = true;
            try
            {
                await PopulateVersionHistoryUIAsync();
            }
            catch (Exception ex)
            {
                App.Log($"[Settings] ToggleVersionHistoryBtn_Click error: {ex}");
            }
            finally
            {
                _isLoadingVersionHistory = false;
            }
        }
    }

    private async Task PopulateVersionHistoryUIAsync()
    {
        var su = SettingsContainer?.SystemUpdatesPanel;
        if (su?.VersionHistoryList == null || _isClosing) return;

        su.VersionHistoryList.Children.Clear();
        var loadingText = new TextBlock
        {
            Text = "Fetching release history from GitHub...",
            FontSize = 7.5,
            Foreground = ThemeBrushes.SecondaryText,
            Margin = new Thickness(4, 2, 4, 2)
        };
        su.VersionHistoryList.Children.Add(loadingText);

        var history = await _updateService.FetchVersionHistoryAsync();
        if (_isClosing || su.VersionHistoryList == null) return;
        su.VersionHistoryList.Children.Clear();

        if (history.Count == 0)
        {
            su.VersionHistoryList.Children.Add(new TextBlock
            {
                Text = "No previous releases found or GitHub rate limit reached.",
                FontSize = 7.5,
                Foreground = ThemeBrushes.SecondaryText,
                Margin = new Thickness(4, 2, 4, 2)
            });
            return;
        }

        foreach (var item in history)
        {
            var dock = new DockPanel { Margin = new Thickness(2, 3, 2, 3), LastChildFill = false };
            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            bool isCurrent = string.Equals(item.Version, _updateService.CurrentVersionString, StringComparison.OrdinalIgnoreCase);

            var titleBlock = new TextBlock
            {
                Text = $"{item.ReleaseTag}{(isCurrent ? " (Installed)" : "")}",
                FontSize = 8.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = isCurrent ? ThemeBrushes.Green : ThemeBrushes.PrimaryText
            };
            var dateBlock = new TextBlock
            {
                Text = $"{item.ReleaseDate.ToLocalTime():yyyy-MM-dd} • {(item.IsPreRelease ? "Beta" : "Stable")}",
                FontSize = 7,
                Foreground = ThemeBrushes.SecondaryText
            };

            textStack.Children.Add(titleBlock);
            textStack.Children.Add(dateBlock);
            dock.Children.Add(textStack);

            if (!isCurrent && !string.IsNullOrEmpty(item.InstallerUrl))
            {
                string actionText = SemVersion.TryParse(item.Version, out var v) && v > _updateService.CurrentVersion ? "Upgrade" : "Rollback";
                var btnLabel = new TextBlock
                {
                    Text = actionText,
                    FontSize = 7.5,
                    FontWeight = FontWeights.Medium
                };

                var rollbackBtn = new Button
                {
                    Content = btnLabel,
                    Height = 18,
                    Padding = new Thickness(6, 0, 6, 0),
                    Style = (Style)FindResource("IslandButton"),
                    Tag = item
                };
                DockPanel.SetDock(rollbackBtn, Dock.Right);

                rollbackBtn.Click += async (s, _) =>
                {
                    if (s is not Button btn || btn.Tag is not ReleaseHistoryItem targetRelease || string.IsNullOrEmpty(targetRelease.InstallerUrl))
                    {
                        return;
                    }

                    string originalAction = actionText;
                    btn.IsEnabled = false;
                    btnLabel.Text = "Starting...";

                    string tempDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DynamicIsland", "Updates"));
                    Directory.CreateDirectory(tempDir);
                    string safeVer = System.Text.RegularExpressions.Regex.Replace(targetRelease.Version ?? "unknown", @"[^0-9A-Za-z._-]", "_");
                    if (string.IsNullOrWhiteSpace(safeVer)) safeVer = "unknown";

                    string dest = string.Empty;
                    try
                    {
                        if (!GitHubReleaseClient.IsTrustedGitHubUrl(targetRelease.InstallerUrl))
                        {
                            throw new InvalidOperationException("Untrusted release download URL rejected.");
                        }

                        string urlPath = new Uri(targetRelease.InstallerUrl).AbsolutePath;
                        string ext = Path.GetExtension(urlPath);
                        if (string.IsNullOrEmpty(ext)) ext = ".exe";
                        string targetFileName = $"DynamicIsland-Setup-{safeVer}{ext}";
                        dest = Path.GetFullPath(Path.Combine(tempDir, targetFileName));

                        _machine.PushToast(new ToastContent(
                            "\uE896",
                            $"{originalAction} Started",
                            $"Resolving checksum for {targetRelease.ReleaseTag}...",
                            ThemeBrushes.BlueColor,
                            TimeSpan.FromSeconds(3.5),
                            TargetView: "settings",
                            Priority: ToastPriority.Feedback
                        ));

                        var progress = new Progress<UpdateProgressInfo>(info =>
                        {
                            Dispatcher.InvokeAsync(() =>
                            {
                                if (_isClosing) return;
                                btnLabel.Text = $"{info.Percent:0}%";
                                if (su.UpdateProgressBar != null)
                                {
                                    su.UpdateProgressBar.Visibility = Visibility.Visible;
                                    su.UpdateProgressBar.Value = info.Percent;
                                }
                                if (su.UpdateProgressText != null)
                                {
                                    su.UpdateProgressText.Visibility = Visibility.Visible;
                                    su.UpdateProgressText.Text = $"{originalAction} {targetRelease.ReleaseTag}: {info.Percent:0}%";
                                }
                            });
                        });

                        // 1. Fetch expected cryptographic SHA-256 hash BEFORE downloading and fail closed if absent
                        btnLabel.Text = "Resolving...";
                        string? expectedHash = await _updateService.FetchSha256ForReleaseAsync(targetRelease, "DynamicIsland-Setup.exe").ConfigureAwait(true);
                        if (_isClosing) return;

                        if (string.IsNullOrWhiteSpace(expectedHash))
                        {
                            _machine.PushToast(new ToastContent(
                                "\uE783",
                                "Security Verification Blocked",
                                $"No SHA-256 checksum published for {targetRelease.ReleaseTag}. Aborting {originalAction.ToLowerInvariant()}.",
                                ThemeBrushes.AmberColor,
                                TimeSpan.FromSeconds(5.0),
                                TargetView: "settings",
                                Priority: ToastPriority.Feedback
                            ));
                            btnLabel.Text = originalAction;
                            btn.IsEnabled = true;
                            return;
                        }

                        // 2. Download package with real-time percentage progress (using user's GitHub token)
                        btnLabel.Text = "Downloading...";
                        using var client = new GitHubReleaseClient(
                            appVersion: _updateService.CurrentVersionString,
                            gitHubToken: _settingsService.Current.GitHubToken);
                        await client.DownloadFileWithProgressAsync(targetRelease.InstallerUrl, dest, progress).ConfigureAwait(true);
                        if (_isClosing) return;

                        // 3. Mandatory cryptographic SHA-256 integrity verification
                        btnLabel.Text = "Verifying...";
                        bool verified = await UpdateIntegrityVerifier.VerifyFileAsync(dest, expectedHash).ConfigureAwait(true);
                        if (!verified)
                        {
                            UpdateIntegrityVerifier.TryCleanFile(dest);
                            _machine.PushToast(new ToastContent(
                                "\uE783",
                                "Integrity Check Failed",
                                $"SHA-256 mismatch for {targetRelease.ReleaseTag}. Package was discarded for security.",
                                ThemeBrushes.AmberColor,
                                TimeSpan.FromSeconds(5.0),
                                TargetView: "settings",
                                Priority: ToastPriority.Feedback
                            ));
                            btnLabel.Text = originalAction;
                            btn.IsEnabled = true;
                            return;
                        }

                        // 4. Verification passed: launch installer handoff and restart
                        btnLabel.Text = "Launching...";
                        _machine.PushToast(new ToastContent(
                            "\uE73E",
                            $"{originalAction} Ready",
                            $"Launching {targetRelease.ReleaseTag} installer...",
                            ThemeBrushes.GreenColor,
                            TimeSpan.FromSeconds(3.0),
                            TargetView: "settings",
                            Priority: ToastPriority.Feedback
                        ));

                        UpdateHandoffEngine.ExecuteHandoff(dest, isInstaller: true);
                    }
                    catch (Exception ex)
                    {
                        if (!string.IsNullOrEmpty(dest)) UpdateIntegrityVerifier.TryCleanFile(dest);
                        App.Log($"[UpdateManager] {originalAction} failed: {ex.Message}");
                        if (!_isClosing)
                        {
                            _machine.PushToast(new ToastContent(
                                "\uE783",
                                $"{originalAction} Failed",
                                ex.Message,
                                ThemeBrushes.AmberColor,
                                TimeSpan.FromSeconds(4.5),
                                TargetView: "settings",
                                Priority: ToastPriority.Feedback
                            ));
                            btnLabel.Text = originalAction;
                            btn.IsEnabled = true;
                        }
                    }
                };
                dock.Children.Add(rollbackBtn);
            }

            su.VersionHistoryList.Children.Add(dock);
        }
    }
}
