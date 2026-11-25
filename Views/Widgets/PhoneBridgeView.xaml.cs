using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland.Views.Widgets;

public partial class PhoneBridgeView : UserControl
{
    private static readonly SolidColorBrush BadgeGreenBg = ThemeBrushes.CreateFrozen(Color.FromArgb(0x22, 0x30, 0xD1, 0x58));
    private static readonly SolidColorBrush BadgeGreenBorder = ThemeBrushes.CreateFrozen(Color.FromArgb(0x52, 0x30, 0xD1, 0x58));
    private static readonly SolidColorBrush BadgeRedBg = ThemeBrushes.CreateFrozen(Color.FromArgb(0x24, 0xFF, 0x45, 0x3A));
    private static readonly SolidColorBrush BadgeRedBorder = ThemeBrushes.CreateFrozen(Color.FromArgb(0x55, 0xFF, 0x45, 0x3A));
    private static readonly SolidColorBrush BadgeAccentBg = ThemeBrushes.CreateFrozen(Color.FromArgb(0x24, 0x0A, 0x84, 0xFF));
    private static readonly SolidColorBrush BadgeAccentBorder = ThemeBrushes.CreateFrozen(Color.FromArgb(0x60, 0x0A, 0x84, 0xFF));
    private static readonly SolidColorBrush DropHighlightBg = ThemeBrushes.CreateFrozen(Color.FromArgb(0x18, 0x0A, 0x84, 0xFF));

    private IslandWindow? _window;
    private DispatcherTimer? _countdownTimer;
    private DispatcherTimer? _copyFeedbackTimer;
    private bool _isExpanded;
    private bool _isPasswordVisible;
    private bool _syncingPassword;
    private bool _isDragOver;
    private int _selectedDurationMinutes = 15;
    private int _activeSessionTotalSeconds = 15 * 60;
    private string _appliedSessionPassword = string.Empty;
    private string? _lastQrUrl;

    public PhoneBridgeView()
    {
        InitializeComponent();
        Loaded += PhoneBridgeView_Loaded;
        Unloaded += PhoneBridgeView_Unloaded;
        IsVisibleChanged += PhoneBridgeView_IsVisibleChanged;
    }

    private void BackToLauncher_Click(object sender, RoutedEventArgs e) =>
        (_window ?? IslandWindow.Instance)?.NavigateToView(Core.ExpandedView.Launcher);

    internal void SetExpanded(bool expanded)
    {
        _isExpanded = expanded;
        SyncTimerState();
        if (!expanded)
        {
            _copyFeedbackTimer?.Stop();
            SetDropHighlight(false);
        }
    }

    private void PhoneBridgeView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SyncTimerState();
        if (!IsVisible)
        {
            _copyFeedbackTimer?.Stop();
            SetDropHighlight(false);
        }
    }

    private void PhoneBridgeView_Loaded(object sender, RoutedEventArgs e)
    {
        _window = IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;
        if (_window?.LanQuickShare != null)
        {
            _window.LanQuickShare.SessionChanged -= OnSessionChanged;
            _window.LanQuickShare.FileReceivedFromPhone -= OnFileReceived;
            _window.LanQuickShare.TextReceivedFromPhone -= OnTextReceived;
            _window.LanQuickShare.ConnectedClientsChanged -= OnConnectedClientsChanged;

            _window.LanQuickShare.SessionChanged += OnSessionChanged;
            _window.LanQuickShare.FileReceivedFromPhone += OnFileReceived;
            _window.LanQuickShare.TextReceivedFromPhone += OnTextReceived;
            _window.LanQuickShare.ConnectedClientsChanged += OnConnectedClientsChanged;
        }

        if (_window?.Shelf != null)
        {
            _window.Shelf.Changed -= OnShelfChanged;
            _window.Shelf.Changed += OnShelfChanged;
        }

        if (_countdownTimer == null)
        {
            _countdownTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _countdownTimer.Tick += CountdownTimer_Tick;
        }
        else
        {
            _countdownTimer.Stop();
        }

        var settings = _window?.Settings?.Current;
        _selectedDurationMinutes = settings?.PhoneBridgeDurationMinutes is 15 or 30 or 60
            ? settings.PhoneBridgeDurationMinutes
            : 15;

        string savedPassword = settings?.PhoneBridgePassword ?? string.Empty;
        SetPasswordVisible(false);
        SetEnteredPassword(savedPassword);

        var service = _window?.LanQuickShare;
        _appliedSessionPassword = service is { IsActive: true, CurrentSession.RequiresPassword: true }
            ? savedPassword
            : string.Empty;

        UpdateDurationChipsUI();
        UpdateShelfBadgeUI();
        RefreshSession();
    }

    private void PhoneBridgeView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_window?.LanQuickShare != null)
        {
            _window.LanQuickShare.SessionChanged -= OnSessionChanged;
            _window.LanQuickShare.FileReceivedFromPhone -= OnFileReceived;
            _window.LanQuickShare.TextReceivedFromPhone -= OnTextReceived;
            _window.LanQuickShare.ConnectedClientsChanged -= OnConnectedClientsChanged;
        }

        if (_window?.Shelf != null)
        {
            _window.Shelf.Changed -= OnShelfChanged;
        }

        _countdownTimer?.Stop();
        _copyFeedbackTimer?.Stop();
        SetDropHighlight(false);
    }

    private void SyncTimerState()
    {
        var service = _window?.LanQuickShare;
        bool shouldTick = IsVisible && _isExpanded && service is { IsActive: true };
        if (shouldTick)
        {
            if (_countdownTimer != null && !_countdownTimer.IsEnabled)
                _countdownTimer.Start();
        }
        else
        {
            _countdownTimer?.Stop();
        }
    }

    public void RefreshSession()
    {
        var service = _window?.LanQuickShare;
        if (service == null) return;

        var session = service.CurrentSession;
        bool isLive = session != null && !session.IsExpired && service.IsActive;

        if (isLive && session != null)
        {
            _activeSessionTotalSeconds = Math.Max(60, Math.Max(_selectedDurationMinutes * 60, (int)Math.Ceiling((session.ExpiresAt - DateTimeOffset.Now).TotalSeconds)));

            StatusDot.Fill = ThemeBrushes.Green;
            HeaderLockGlyph.Visibility = session.RequiresPassword ? Visibility.Visible : Visibility.Collapsed;
            QrLockBadge.Visibility = session.RequiresPassword ? Visibility.Visible : Visibility.Collapsed;

            LanIpText.Text = $"{session.LocalIp}:{session.Port}";
            PortMetaText.Text = $"Local Wi-Fi · Port {session.Port}";
            QrHelperText.Text = session.RequiresPassword
                ? "Scan QR · Password required"
                : "Scan with Phone Camera";

            ToggleSessionText.Text = "Stop";
            ToggleSessionGlyph.Text = "\uE71A";
            ToggleSessionGlyph.Foreground = ThemeBrushes.Red;
            ToggleSessionBtn.Background = BadgeRedBg;
            ToggleSessionBtn.BorderBrush = BadgeRedBorder;
            ExtendSessionBtn.Visibility = Visibility.Visible;

            DurationSelectorPanel.Visibility = Visibility.Collapsed;
            SessionProgressPanel.Visibility = Visibility.Visible;

            if (!string.Equals(_lastQrUrl, session.Url, StringComparison.Ordinal))
            {
                _lastQrUrl = session.Url;
                try
                {
                    QrCodeImage.Source = QrCode.GenerateBitmap(session.Url, moduleSize: 3, quietZone: 2, ecc: QrCodeEcc.Medium);
                }
                catch (Exception ex)
                {
                    App.Log($"[PhoneBridgeView] Failed to generate live QR: {ex.Message}");
                }
            }
            QrCodeImage.Opacity = 1.0;
            DormantOverlay.Visibility = Visibility.Collapsed;

            UpdateConnectedClientsUI(service.GetConnectedClients());
            UpdateSecurityBadgeUI(session.RequiresPassword, isLive: true);
            UpdateCountdown();
        }
        else
        {
            StatusDot.Fill = ThemeBrushes.MutedGrey;
            HeaderStatusText.Text = "Idle";
            HeaderStatusText.Foreground = ThemeBrushes.SecondaryText;
            HeaderStatusPill.ClearValue(Border.BackgroundProperty);
            HeaderStatusPill.ClearValue(Border.BorderBrushProperty);
            HeaderLockGlyph.Visibility = Visibility.Collapsed;
            QrLockBadge.Visibility = Visibility.Collapsed;

            string localIp = LanQuickShareService.GetLocalIpAddress() ?? "127.0.0.1";
            string dormantUrl = $"http://{localIp}:{LanQuickShareService.DefaultPort}";
            LanIpText.Text = $"{localIp}:{LanQuickShareService.DefaultPort}";
            PortMetaText.Text = $"Local Wi-Fi · Port {LanQuickShareService.DefaultPort}";
            QrHelperText.Text = "Scan QR after starting";

            ToggleSessionText.Text = "Start Bridge";
            ToggleSessionGlyph.Text = "\uE768";
            ToggleSessionGlyph.Foreground = ThemeBrushes.Green;
            ToggleSessionBtn.Background = BadgeGreenBg;
            ToggleSessionBtn.BorderBrush = BadgeGreenBorder;
            ExtendSessionBtn.Visibility = Visibility.Collapsed;

            DurationSelectorPanel.Visibility = Visibility.Visible;
            SessionProgressPanel.Visibility = Visibility.Collapsed;
            SessionProgressScale.ScaleX = 1.0;

            if (!string.Equals(_lastQrUrl, dormantUrl, StringComparison.Ordinal))
            {
                _lastQrUrl = dormantUrl;
                try
                {
                    QrCodeImage.Source = QrCode.GenerateBitmap(dormantUrl, moduleSize: 3, quietZone: 2, ecc: QrCodeEcc.Medium);
                }
                catch (Exception ex)
                {
                    App.Log($"[PhoneBridgeView] Failed to generate dormant QR: {ex.Message}");
                }
            }
            QrCodeImage.Opacity = 0.28;
            DormantOverlay.Visibility = Visibility.Visible;

            UpdateConnectedClientsUI(Array.Empty<LanConnectedClient>());
            UpdateSecurityBadgeUI(!string.IsNullOrEmpty(GetEnteredPassword()), isLive: false);
        }

        UpdatePasswordInputState();
        SyncTimerState();
    }

    private void UpdateDurationChipsUI()
    {
        ApplyDurationChipStyle(Duration15Btn, _selectedDurationMinutes == 15);
        ApplyDurationChipStyle(Duration30Btn, _selectedDurationMinutes == 30);
        ApplyDurationChipStyle(Duration60Btn, _selectedDurationMinutes == 60);
    }

    private static void ApplyDurationChipStyle(Button btn, bool active)
    {
        if (active)
        {
            btn.Background = BadgeAccentBg;
            btn.BorderBrush = BadgeAccentBorder;
            btn.Foreground = ThemeBrushes.Accent;
        }
        else
        {
            btn.ClearValue(Control.BackgroundProperty);
            btn.ClearValue(Control.BorderBrushProperty);
            btn.Foreground = ThemeBrushes.SecondaryText;
        }
    }

    private void UpdateSecurityBadgeUI(bool hasPassword, bool isLive)
    {
        if (hasPassword)
        {
            LockStatusGlyph.Text = "\uE72E";
            LockStatusGlyph.Foreground = ThemeBrushes.Green;
            SecurityModeText.Text = "Password On";
            SecurityModeText.Foreground = ThemeBrushes.Green;
            SecurityModeBadge.Background = BadgeGreenBg;
            SecurityModeBadge.BorderBrush = BadgeGreenBorder;
        }
        else
        {
            LockStatusGlyph.Text = "\uE785";
            LockStatusGlyph.Foreground = ThemeBrushes.SecondaryText;
            SecurityModeText.Text = "Token Only";
            SecurityModeText.Foreground = ThemeBrushes.SecondaryText;
            SecurityModeBadge.ClearValue(Border.BackgroundProperty);
            SecurityModeBadge.ClearValue(Border.BorderBrushProperty);
        }

        int clientCount = _window?.LanQuickShare?.ConnectedClientCount ?? 0;
        RevokeClientsBtn.Visibility = isLive && hasPassword && clientCount > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateConnectedClientsUI(IReadOnlyList<LanConnectedClient> clients)
    {
        int count = clients.Count;
        if (count > 0)
        {
            string primaryLabel = clients[0].DeviceLabel;
            DevicesChipText.Text = count == 1
                ? $"1 · {primaryLabel}"
                : $"{count} devices";
            DevicesChipText.Foreground = ThemeBrushes.Green;
            DevicesChipGlyph.Foreground = ThemeBrushes.Green;
            DevicesChip.Background = BadgeGreenBg;
            DevicesChip.BorderBrush = BadgeGreenBorder;
            DevicesChip.ToolTip = string.Join("\n", clients.Select(c => $"{c.DeviceLabel} ({c.RemoteIp})"));
        }
        else
        {
            DevicesChipText.Text = "0 devices";
            DevicesChipText.Foreground = ThemeBrushes.SecondaryText;
            DevicesChipGlyph.Foreground = ThemeBrushes.SecondaryText;
            DevicesChip.ClearValue(Border.BackgroundProperty);
            DevicesChip.ClearValue(Border.BorderBrushProperty);
            DevicesChip.ToolTip = "No phone sessions currently active";
        }

        var session = _window?.LanQuickShare?.CurrentSession;
        bool isLive = session != null && !session.IsExpired && (_window?.LanQuickShare?.IsActive == true);
        if (isLive && session != null)
        {
            RevokeClientsBtn.Visibility = session.RequiresPassword && count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        else
        {
            RevokeClientsBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateShelfBadgeUI()
    {
        int count = _window?.Shelf?.Count ?? 0;
        ShelfReadyText.Text = count == 0 ? "0 staged" : $"{count} ready";
        if (count > 0)
        {
            ShelfReadyText.Foreground = ThemeBrushes.Accent;
            ShelfReadyPill.Background = BadgeAccentBg;
            ShelfReadyPill.BorderBrush = BadgeAccentBorder;
            ShelfBtnBadge.Visibility = Visibility.Visible;
            ShelfBtnBadgeText.Text = count > 99 ? "99+" : count.ToString();
        }
        else
        {
            ShelfReadyText.Foreground = ThemeBrushes.SecondaryText;
            ShelfReadyPill.ClearValue(Border.BackgroundProperty);
            ShelfReadyPill.ClearValue(Border.BorderBrushProperty);
            ShelfBtnBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void CountdownTimer_Tick(object? sender, EventArgs e) => UpdateCountdown();

    private void UpdateCountdown()
    {
        var service = _window?.LanQuickShare;
        var session = service?.CurrentSession;
        if (service == null || session == null || session.IsExpired)
        {
            RefreshSession();
            return;
        }

        var remaining = session.ExpiresAt - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            RefreshSession();
            return;
        }

        string clockText = $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";
        int clientCount = service.ConnectedClientCount;
        if (clientCount > 0)
        {
            HeaderStatusText.Text = $"{clientCount} Connected · {clockText}";
            HeaderStatusText.Foreground = ThemeBrushes.Green;
            HeaderStatusPill.Background = BadgeGreenBg;
            HeaderStatusPill.BorderBrush = BadgeGreenBorder;
        }
        else
        {
            HeaderStatusText.Text = $"Live · {clockText}";
            HeaderStatusText.Foreground = ThemeBrushes.Green;
            HeaderStatusPill.Background = BadgeGreenBg;
            HeaderStatusPill.BorderBrush = BadgeGreenBorder;
        }

        double ratio = Math.Clamp(remaining.TotalSeconds / Math.Max(60.0, _activeSessionTotalSeconds), 0.02, 1.0);
        SessionProgressScale.ScaleX = ratio;
        SessionProgressFill.Background = remaining.TotalSeconds <= 120 ? ThemeBrushes.Amber : ThemeBrushes.Green;
    }

    private void OnSessionChanged(LanShareSessionInfo? session) =>
        Dispatcher.InvokeAsync(RefreshSession);

    private void OnConnectedClientsChanged(int count, IReadOnlyList<LanConnectedClient> clients) =>
        Dispatcher.InvokeAsync(() =>
        {
            UpdateConnectedClientsUI(clients);
            if (_window?.LanQuickShare?.IsActive == true)
                UpdateCountdown();
        });

    private void OnShelfChanged() =>
        Dispatcher.InvokeAsync(UpdateShelfBadgeUI);

    private void OnFileReceived(string fileName) =>
        Dispatcher.InvokeAsync(() =>
        {
            ActivityDot.Fill = ThemeBrushes.Green;
            LastActivityText.Text = $"Received '{Path.GetFileName(fileName)}'";
            LastActivityText.Foreground = ThemeBrushes.Green;
        });

    private void OnTextReceived(string text) =>
        Dispatcher.InvokeAsync(() =>
        {
            string clean = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            string preview = clean.Length > 28 ? clean[..28] + "…" : clean;
            ActivityDot.Fill = ThemeBrushes.Accent;
            LastActivityText.Text = $"Synced clip: \"{preview}\"";
            LastActivityText.Foreground = ThemeBrushes.Accent;
        });

    private string GetEnteredPassword() =>
        (_isPasswordVisible ? BridgePasswordPlainBox.Text : BridgePasswordBox.Password).Trim();

    private void SetEnteredPassword(string value)
    {
        _syncingPassword = true;
        try
        {
            BridgePasswordBox.Password = value;
            BridgePasswordPlainBox.Text = value;
        }
        finally
        {
            _syncingPassword = false;
        }
        UpdatePasswordInputState();
    }

    private void SetPasswordVisible(bool visible)
    {
        if (_isPasswordVisible == visible)
            return;

        _syncingPassword = true;
        try
        {
            if (visible)
            {
                BridgePasswordPlainBox.Text = BridgePasswordBox.Password;
                BridgePasswordBox.Visibility = Visibility.Collapsed;
                BridgePasswordPlainBox.Visibility = Visibility.Visible;
                PasswordVisGlyph.Text = "\uED1A";
                PasswordVisGlyph.Foreground = ThemeBrushes.Accent;
                TogglePasswordVisibilityBtn.ToolTip = "Hide password";
            }
            else
            {
                BridgePasswordBox.Password = BridgePasswordPlainBox.Text;
                BridgePasswordPlainBox.Visibility = Visibility.Collapsed;
                BridgePasswordBox.Visibility = Visibility.Visible;
                PasswordVisGlyph.Text = "\uE7B3";
                PasswordVisGlyph.Foreground = ThemeBrushes.SecondaryText;
                TogglePasswordVisibilityBtn.ToolTip = "Show password";
            }
            _isPasswordVisible = visible;
        }
        finally
        {
            _syncingPassword = false;
        }
    }

    private void UpdatePasswordInputState()
    {
        string raw = _isPasswordVisible ? BridgePasswordPlainBox.Text : BridgePasswordBox.Password;
        string trimmed = raw.Trim();
        bool hasText = raw.Length > 0;

        PasswordPlaceholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        ClearPasswordBtn.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;

        var service = _window?.LanQuickShare;
        bool isLive = service is { IsActive: true };
        bool pendingLiveApply = isLive && !string.Equals(trimmed, _appliedSessionPassword, StringComparison.Ordinal);
        ApplyPasswordBtn.Visibility = pendingLiveApply ? Visibility.Visible : Visibility.Collapsed;

        if (!isLive)
        {
            UpdateSecurityBadgeUI(!string.IsNullOrEmpty(trimmed), isLive: false);
        }
    }

    private void BridgePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPassword) return;
        _syncingPassword = true;
        try { BridgePasswordPlainBox.Text = BridgePasswordBox.Password; }
        finally { _syncingPassword = false; }
        UpdatePasswordInputState();
    }

    private void BridgePasswordPlainBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingPassword) return;
        _syncingPassword = true;
        try { BridgePasswordBox.Password = BridgePasswordPlainBox.Text; }
        finally { _syncingPassword = false; }
        UpdatePasswordInputState();
    }

    private void PasswordInput_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        PasswordInputBorder.BorderBrush = BadgeAccentBorder;
    }

    private void PasswordInput_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        PasswordInputBorder.ClearValue(Border.BorderBrushProperty);
        PersistPasswordSetting(GetEnteredPassword());
    }

    private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitPasswordChange(showFeedback: true);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void TogglePasswordVisibilityBtn_Click(object sender, RoutedEventArgs e)
    {
        SetPasswordVisible(!_isPasswordVisible);
        if (_isPasswordVisible)
        {
            BridgePasswordPlainBox.Focus();
            BridgePasswordPlainBox.CaretIndex = BridgePasswordPlainBox.Text.Length;
        }
        else
        {
            BridgePasswordBox.Focus();
        }
    }

    private void GeneratePinBtn_Click(object sender, RoutedEventArgs e)
    {
        string pin = LanQuickShareService.GeneratePasscode(6);
        SetPasswordVisible(true);
        SetEnteredPassword(pin);
        PersistPasswordSetting(pin);

        var service = _window?.LanQuickShare;
        if (service is { IsActive: true })
        {
            service.UpdateSessionPassword(pin);
            _appliedSessionPassword = pin;
            RefreshSession();
            _window?.ShowToast(
                "\uE72E",
                "6-Digit PIN Active",
                "Live bridge updated · connected phones must re-enter",
                ThemeBrushes.GreenColor,
                "phonebridge");
        }
        else
        {
            _window?.ShowToast(
                "\uE8D7",
                "6-Digit PIN Ready",
                "Bridge will require this PIN when started",
                ThemeBrushes.AccentColor,
                "phonebridge");
        }

        _window?.TriggerLiquidImpulse(18, -6, 2);
    }

    private void ClearPasswordBtn_Click(object sender, RoutedEventArgs e)
    {
        SetEnteredPassword(string.Empty);
        PersistPasswordSetting(null);

        var service = _window?.LanQuickShare;
        if (service is { IsActive: true })
        {
            service.UpdateSessionPassword(null);
            _appliedSessionPassword = string.Empty;
            RefreshSession();
            _window?.ShowToast(
                "\uE785",
                "Password Removed",
                "Bridge switched to pairing-token access",
                ThemeBrushes.AmberColor,
                "phonebridge");
        }
        _window?.TriggerLiquidImpulse(14, -5, 2);
    }

    private void ApplyPasswordBtn_Click(object sender, RoutedEventArgs e) =>
        CommitPasswordChange(showFeedback: true);

    private void CommitPasswordChange(bool showFeedback)
    {
        string pass = GetEnteredPassword();
        PersistPasswordSetting(string.IsNullOrEmpty(pass) ? null : pass);

        var service = _window?.LanQuickShare;
        if (service is { IsActive: true })
        {
            if (!string.Equals(pass, _appliedSessionPassword, StringComparison.Ordinal))
            {
                service.UpdateSessionPassword(pass);
                _appliedSessionPassword = pass;
                RefreshSession();
                if (showFeedback)
                {
                    _window?.ShowToast(
                        string.IsNullOrEmpty(pass) ? "\uE785" : "\uE72E",
                        string.IsNullOrEmpty(pass) ? "Password Removed" : "Bridge Password Applied",
                        string.IsNullOrEmpty(pass)
                            ? "Bridge switched to pairing-token access"
                            : "Connected phones must re-enter the new password",
                        string.IsNullOrEmpty(pass) ? ThemeBrushes.AmberColor : ThemeBrushes.GreenColor,
                        "phonebridge");
                }
            }
        }
        else if (showFeedback)
        {
            _window?.ShowToast(
                string.IsNullOrEmpty(pass) ? "\uE785" : "\uE72E",
                string.IsNullOrEmpty(pass) ? "Token-Only Mode" : "Password Saved",
                string.IsNullOrEmpty(pass)
                    ? "No password required when bridge starts"
                    : "Will lock incoming phone connections when started",
                ThemeBrushes.AccentColor,
                "phonebridge");
        }
    }

    private void PersistPasswordSetting(string? password)
    {
        var settings = _window?.Settings;
        if (settings == null) return;

        string? normalized = string.IsNullOrWhiteSpace(password) ? null : password.Trim();
        if (!string.Equals(settings.Current.PhoneBridgePassword, normalized, StringComparison.Ordinal))
        {
            settings.Update(s => s.PhoneBridgePassword = normalized, nameof(AppSettings.PhoneBridgePassword));
        }
    }

    private void DurationChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out int mins))
        {
            _selectedDurationMinutes = mins is 15 or 30 or 60 ? mins : 15;
            _window?.Settings?.Update(
                s => s.PhoneBridgeDurationMinutes = _selectedDurationMinutes,
                nameof(AppSettings.PhoneBridgeDurationMinutes));
            UpdateDurationChipsUI();
            _window?.TriggerLiquidImpulse(14, -4, 2);
        }
    }

    private void ExtendSessionBtn_Click(object sender, RoutedEventArgs e)
    {
        var service = _window?.LanQuickShare;
        if (service == null || !service.IsActive) return;

        if (service.ExtendSession(15))
        {
            _activeSessionTotalSeconds = Math.Max(_activeSessionTotalSeconds, (int)Math.Ceiling((service.CurrentSession!.ExpiresAt - DateTimeOffset.Now).TotalSeconds));
            UpdateCountdown();
            _window?.TriggerLiquidImpulse(20, -6, 3);
            _window?.ShowToast(
                "\uE916",
                "Session Extended",
                $"Added +15m · {service.CurrentSession.MinutesRemaining}m remaining",
                ThemeBrushes.AccentColor,
                "phonebridge");
        }
    }

    private void RevokeClientsBtn_Click(object sender, RoutedEventArgs e)
    {
        var service = _window?.LanQuickShare;
        if (service == null) return;

        int revoked = service.RevokeAllClients();
        RefreshSession();
        _window?.TriggerLiquidImpulse(22, -7, 3);
        _window?.ShowToast(
            "\uE72E",
            "Devices Locked",
            revoked > 0
                ? $"Disconnected {revoked} active phone session(s)"
                : "All clients locked",
            ThemeBrushes.AmberColor,
            "phonebridge");
    }

    private void QrCodeCard_Click(object sender, RoutedEventArgs e)
    {
        var service = _window?.LanQuickShare;
        if (service == null) return;

        if (!service.IsActive || service.CurrentSession == null || service.CurrentSession.IsExpired)
        {
            ToggleSessionBtn_Click(sender, e);
        }
        else
        {
            CopyUrlBtn_Click(sender, e);
        }
    }

    private void ToggleSessionBtn_Click(object sender, RoutedEventArgs e)
    {
        var service = _window?.LanQuickShare;
        if (service == null) return;

        if (service.IsActive)
        {
            service.StopSession();
            _appliedSessionPassword = string.Empty;
            _window?.TriggerLiquidImpulse(24, -8, 3);
        }
        else
        {
            try
            {
                string password = GetEnteredPassword();
                PersistPasswordSetting(string.IsNullOrEmpty(password) ? null : password);

                var session = service.StartSession(
                    durationMinutes: _selectedDurationMinutes,
                    password: string.IsNullOrEmpty(password) ? null : password);
                _appliedSessionPassword = password;
                _activeSessionTotalSeconds = _selectedDurationMinutes * 60;

                try { SafeClipboard.TrySetText(session.Url); } catch { }
                _window?.TriggerLiquidImpulse(34, -12, 4);
                string securityTag = session.RequiresPassword ? " · Password protected" : string.Empty;
                _window?.ShowToast(
                    "\uE8EA",
                    "Phone Bridge Started",
                    $"{session.DisplayAddress} copied ({_selectedDurationMinutes}m{securityTag})",
                    ThemeBrushes.GreenColor,
                    "phonebridge");
            }
            catch (Exception ex)
            {
                _window?.ShowToast(
                    "\uE783",
                    "Bridge Start Failed",
                    ex.Message,
                    ThemeBrushes.RedColor,
                    "phonebridge");
            }
        }
        RefreshSession();
    }

    private void CopyUrlBtn_Click(object sender, RoutedEventArgs e)
    {
        var session = _window?.LanQuickShare?.CurrentSession;
        if (session != null && !session.IsExpired)
        {
            try { SafeClipboard.TrySetText(session.Url); } catch { }
            ShowCopyConfirmation();
            _window?.ShowToast(
                "\uE8C8",
                "Pairing Link Copied",
                session.RequiresPassword ? $"{session.DisplayAddress} (Password required)" : session.Url,
                ThemeBrushes.GreenColor,
                "phonebridge");
            _window?.TriggerLiquidImpulse(18, -6, 2);
        }
        else
        {
            ToggleSessionBtn_Click(sender, e);
        }
    }

    private void ShowCopyConfirmation()
    {
        CopyAddressGlyph.Text = "\uE73E";
        CopyAddressGlyph.Foreground = ThemeBrushes.Green;

        _copyFeedbackTimer ??= new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _copyFeedbackTimer.Tick -= CopyFeedbackTimer_Tick;
        _copyFeedbackTimer.Tick += CopyFeedbackTimer_Tick;
        _copyFeedbackTimer.Stop();
        _copyFeedbackTimer.Start();
    }

    private void CopyFeedbackTimer_Tick(object? sender, EventArgs e)
    {
        _copyFeedbackTimer?.Stop();
        CopyAddressGlyph.Text = "\uE8C8";
        CopyAddressGlyph.Foreground = ThemeBrushes.SecondaryText;
    }

    private void OpenInBrowserBtn_Click(object sender, RoutedEventArgs e)
    {
        var session = _window?.LanQuickShare?.CurrentSession;
        string url = session != null && !session.IsExpired
            ? session.Url
            : $"http://localhost:{LanQuickShareService.DefaultPort}";

        try
        {
            using var proc = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Log($"[PhoneBridgeView] OpenInBrowser failed: {ex.Message}");
        }
    }

    private void SetDropHighlight(bool active)
    {
        if (_isDragOver == active) return;
        _isDragOver = active;
        if (active)
        {
            BeamDropZone.Background = DropHighlightBg;
            BeamDropZone.BorderBrush = BadgeAccentBorder;
            BeamDropIconBg.Background = BadgeAccentBg;
            BeamDropIconBg.BorderBrush = BadgeAccentBorder;
        }
        else
        {
            BeamDropZone.ClearValue(Border.BackgroundProperty);
            BeamDropZone.ClearValue(Border.BorderBrushProperty);
            BeamDropIconBg.ClearValue(Border.BackgroundProperty);
            BeamDropIconBg.ClearValue(Border.BorderBrushProperty);
        }
    }

    private void BeamDropZone_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            SetDropHighlight(true);
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void BeamDropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void BeamDropZone_DragLeave(object sender, DragEventArgs e) =>
        SetDropHighlight(false);

    private void BeamDropZone_Drop(object sender, DragEventArgs e)
    {
        SetDropHighlight(false);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && _window?.Shelf != null)
        {
            int added = _window.Shelf.AddRange(files);
            UpdateShelfBadgeUI();
            _window.TriggerLiquidImpulse(30, -10, 4);
            _window.ShowToast(
                "\uE8EA",
                "Beaming to Phone",
                $"Staged {added} file(s) on Drop Shelf for connected devices",
                ThemeBrushes.GreenColor,
                "phonebridge");
            e.Handled = true;
        }
    }

    private void BeamDropZone_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _window?.Shelf == null) return;

        try
        {
            _window.SetModalDialogOpen(true);
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Title = "Select files to beam to Phone"
            };
            if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
            {
                int added = _window.Shelf.AddRange(dlg.FileNames);
                UpdateShelfBadgeUI();
                _window.TriggerLiquidImpulse(26, -9, 3);
                _window.ShowToast(
                    "\uE8EA",
                    "Beaming to Phone",
                    $"Staged {added} file(s) on Drop Shelf",
                    ThemeBrushes.GreenColor,
                    "phonebridge");
            }
        }
        catch (Exception ex)
        {
            App.Log($"[PhoneBridgeView] File picker failed: {ex.Message}");
        }
        finally
        {
            _window.SetModalDialogOpen(false);
        }
    }

    private void SyncClipboardBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                _window?.ClipboardService?.HandleClipboardUpdate();
                string clean = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
                string preview = clean.Length > 28 ? clean[..28] + "…" : clean;
                ActivityDot.Fill = ThemeBrushes.Accent;
                LastActivityText.Text = string.IsNullOrEmpty(preview)
                    ? "Pushed clipboard to portal"
                    : $"Shared clip: \"{preview}\"";
                LastActivityText.Foreground = ThemeBrushes.Accent;

                _window?.ShowToast(
                    "\uE77F",
                    "Clipboard Synced",
                    "Ready on Phone Bridge web portal",
                    ThemeBrushes.AccentColor,
                    "phonebridge");
                _window?.TriggerLiquidImpulse(20, -7, 2);
            }
            else
            {
                _window?.ShowToast(
                    "\uE77F",
                    "Clipboard Empty",
                    "Copy text on Windows first to push to phone",
                    ThemeBrushes.AmberColor,
                    "phonebridge");
            }
        }
        catch (Exception ex)
        {
            App.Log($"[PhoneBridgeView] SyncClipboard failed: {ex.Message}");
        }
    }

    private void ViewShelfBtn_Click(object sender, RoutedEventArgs e) =>
        _window?.NavigateToView(ExpandedView.Shelf);
}
