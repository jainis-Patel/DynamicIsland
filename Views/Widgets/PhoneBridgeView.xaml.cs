using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland.Views.Widgets;

public partial class PhoneBridgeView : UserControl
{
    private IslandWindow? _window;
    private DispatcherTimer? _countdownTimer;

    public PhoneBridgeView()
    {
        InitializeComponent();
        Loaded += PhoneBridgeView_Loaded;
        Unloaded += PhoneBridgeView_Unloaded;
    }

    private void BackToLauncher_Click(object sender, RoutedEventArgs e) =>
        (_window ?? IslandWindow.Instance)?.NavigateToView(Core.ExpandedView.Launcher);

    private void PhoneBridgeView_Loaded(object sender, RoutedEventArgs e)
    {
        _window = IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;
        if (_window?.LanQuickShare != null)
        {
            _window.LanQuickShare.SessionChanged += OnSessionChanged;
            _window.LanQuickShare.FileReceivedFromPhone += OnFileReceived;
            _window.LanQuickShare.TextReceivedFromPhone += OnTextReceived;
        }

        _countdownTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += CountdownTimer_Tick;

        RefreshSession();
    }

    private void PhoneBridgeView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_window?.LanQuickShare != null)
        {
            _window.LanQuickShare.SessionChanged -= OnSessionChanged;
            _window.LanQuickShare.FileReceivedFromPhone -= OnFileReceived;
            _window.LanQuickShare.TextReceivedFromPhone -= OnTextReceived;
        }

        _countdownTimer?.Stop();
        _countdownTimer = null;
    }

    public void RefreshSession()
    {
        var service = _window?.LanQuickShare;
        if (service == null) return;

        var session = service.CurrentSession;
        if (session != null && !session.IsExpired && service.IsActive)
        {
            SessionStateText.Text = "Bridge Live";
            SessionStateText.Foreground = ThemeBrushes.Green;
            StatusDot.Fill = ThemeBrushes.Green;
            LanIpText.Text = session.Url;
            TokenDisplay.Text = session.Token;
            ToggleSessionText.Text = "Stop Bridge";
            ToggleSessionGlyph.Text = "\uE71A"; // Stop
            ToggleSessionGlyph.Foreground = ThemeBrushes.Red;

            // Generate crisp live QR code with full token URL for camera scanning
            try
            {
                QrCodeImage.Source = QrCode.GenerateBitmap(session.Url, moduleSize: 2, quietZone: 3, ecc: QrCodeEcc.Medium);
                QrCodeImage.Opacity = 1.0;
                DormantOverlay.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                App.Log($"[PhoneBridgeView] Failed to generate live QR: {ex.Message}");
            }

            if (_countdownTimer?.IsEnabled != true) _countdownTimer?.Start();
            UpdateCountdown();
        }
        else
        {
            SessionStateText.Text = "Bridge Dormant";
            SessionStateText.Foreground = ThemeBrushes.SecondaryText;
            StatusDot.Fill = ThemeBrushes.MutedGrey;
            string localIp = LanQuickShareService.GetLocalIpAddress() ?? "127.0.0.1";
            string dormantUrl = $"http://{localIp}:{LanQuickShareService.DefaultPort}";
            LanIpText.Text = dormantUrl;
            TokenDisplay.Text = "Click 'Start Bridge'";
            TimerText.Text = "--:--";
            ToggleSessionText.Text = "Start Bridge";
            ToggleSessionGlyph.Text = "\uE768"; // Play
            ToggleSessionGlyph.Foreground = ThemeBrushes.Green;

            // Generate dormant QR code preview
            try
            {
                QrCodeImage.Source = QrCode.GenerateBitmap(dormantUrl, moduleSize: 2, quietZone: 3, ecc: QrCodeEcc.Medium);
                QrCodeImage.Opacity = 0.35;
                DormantOverlay.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                App.Log($"[PhoneBridgeView] Failed to generate dormant QR: {ex.Message}");
            }

            _countdownTimer?.Stop();
        }
    }

    private void QrCodeCard_Click(object sender, RoutedEventArgs e)
    {
        var service = _window?.LanQuickShare;
        if (service == null) return;

        if (!service.IsActive || service.CurrentSession == null || service.CurrentSession.IsExpired)
        {
            // Auto start if dormant
            ToggleSessionBtn_Click(sender, e);
        }
        else
        {
            // Copy URL and toast if already active
            CopyUrlBtn_Click(sender, e);
        }
    }

    private void CountdownTimer_Tick(object? sender, EventArgs e)
    {
        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        var session = _window?.LanQuickShare?.CurrentSession;
        if (session == null || session.IsExpired)
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

        TimerText.Text = $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
    }

    private void OnSessionChanged(LanShareSessionInfo? session)
    {
        Dispatcher.InvokeAsync(RefreshSession);
    }

    private void OnFileReceived(string fileName)
    {
        Dispatcher.InvokeAsync(() =>
        {
            LastActivityText.Text = $"Received '{Path.GetFileName(fileName)}'";
            LastActivityText.Foreground = ThemeBrushes.Green;
        });
    }

    private void OnTextReceived(string text)
    {
        Dispatcher.InvokeAsync(() =>
        {
            string preview = text.Length > 24 ? text[..24] + "…" : text;
            LastActivityText.Text = $"Synced clip: \"{preview}\"";
            LastActivityText.Foreground = ThemeBrushes.Accent;
        });
    }

    private void ToggleSessionBtn_Click(object sender, RoutedEventArgs e)
    {
        var service = _window?.LanQuickShare;
        if (service == null) return;

        if (service.IsActive)
        {
            service.StopSession();
            _window?.TriggerLiquidImpulse(24, -8, 3);
        }
        else
        {
            try
            {
                var session = service.StartSession(durationMinutes: 15);
                Clipboard.SetText(session.Url);
                _window?.TriggerLiquidImpulse(34, -12, 4);
                _window?.ShowToast(
                    "\uE8EA",
                    "Phone Bridge Started",
                    $"URL copied: {session.Url} (Valid 15m)",
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
            Clipboard.SetText(session.Url);
            _window?.ShowToast(
                "\uE8C8",
                "Link Copied",
                session.Url,
                ThemeBrushes.GreenColor,
                "phonebridge");
            _window?.TriggerLiquidImpulse(18, -6, 2);
        }
        else
        {
            // Auto start if dormant
            ToggleSessionBtn_Click(sender, e);
        }
    }

    private void OpenInBrowserBtn_Click(object sender, RoutedEventArgs e)
    {
        var session = _window?.LanQuickShare?.CurrentSession;
        string url = session != null && !session.IsExpired
            ? session.Url
            : $"http://localhost:{LanQuickShareService.DefaultPort}";

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Log($"[PhoneBridgeView] OpenInBrowser failed: {ex.Message}");
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

    private void BeamDropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && _window?.Shelf != null)
        {
            int added = _window.Shelf.AddRange(files);
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

    private void SyncClipboardBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                _window?.ClipboardService?.HandleClipboardUpdate();
                _window?.ShowToast(
                    "\uE77F",
                    "Clipboard Synced",
                    "Ready on Phone Bridge web portal",
                    ThemeBrushes.AccentColor,
                    "phonebridge");
                _window?.TriggerLiquidImpulse(20, -7, 2);
            }
        }
        catch (Exception ex)
        {
            App.Log($"[PhoneBridgeView] SyncClipboard failed: {ex.Message}");
        }
    }

    private void ViewShelfBtn_Click(object sender, RoutedEventArgs e)
    {
        _window?.NavigateToView(ExpandedView.Shelf);
    }
}
