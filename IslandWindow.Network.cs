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
    //  Wi-Fi, Network & Connectivity Hub
    // =================================================================

    private void OnNetworkUpdated(NetworkStatusSnapshot snap)
    {
        if (NetworkButtonGlyph == null) return;

        NetworkButtonGlyph.Text = snap.Glyph;
        NetworkButtonGlyph.Foreground = snap.IsConnected ? ThemeBrushes.PrimaryText : ThemeBrushes.Amber;

        // Hero Clock Wi-Fi & Bandwidth Badges
        ClockWifiGlyph.Text = snap.Glyph;
        ClockWifiGlyph.Foreground = snap.IsConnected ? ThemeBrushes.Green : ThemeBrushes.Amber;
        ClockWifiText.Text = snap.IsConnected ? snap.ConnectionName : "Offline";

        if (ClockNetSpeedText != null)
        {
            ClockNetSpeedText.Text = snap.ShortSpeedText;
        }

        // Idle Capsule Micro Net Speed
        bool showIdleSpeed = _settingsService?.Current?.ShowNetworkSpeedInIdle ?? false;
        if (IdleNetSpeedHost != null)
        {
            IdleNetSpeedHost.Visibility = showIdleSpeed ? Visibility.Visible : Visibility.Collapsed;
        }
        if (IdleNetSpeedText != null && showIdleSpeed)
        {
            IdleNetSpeedText.Text = snap.IsConnected ? snap.ShortSpeedText : "\u2014";
            IdleNetSpeedText.Foreground = snap.IsConnected ? ThemeBrushes.SecondaryText : ThemeBrushes.Amber;
        }
        if (IdleNetSpeedGlyph != null && showIdleSpeed)
        {
            IdleNetSpeedGlyph.Foreground = snap.IsConnected ? ThemeBrushes.Green : ThemeBrushes.Amber;
            IdleNetSpeedGlyph.Text = snap.IsConnected ? "\uE896" : "\uEB5E";
        }

        // Network Hub Container
        NetworkIpText.Text = $"IP: {snap.LocalIpv4}";
        NetworkHeroGlyph.Text = snap.Glyph;
        NetworkHeroGlyph.Foreground = snap.IsConnected ? ThemeBrushes.Green : ThemeBrushes.Amber;
        NetworkHeroIconBg.Background = snap.IsConnected
            ? ThemeBrushes.PlanSaverActiveBg
            : ThemeBrushes.PlanPerfActiveBg;

        NetworkHeroName.Text = snap.ConnectionName;
        if (snap.IsConnected)
        {
            string kind = snap.IsWifi ? $"Wi-Fi \u00B7 {snap.SignalBars}/4 Bars" : (snap.IsEthernet ? "Ethernet" : "Connected");
            string link = snap.LinkSpeedMbps > 0 ? $" \u00B7 {snap.LinkSpeedMbps} Mbps" : "";
            NetworkHeroSubtitle.Text = $"{kind}{link}";
        }
        else
        {
            NetworkHeroSubtitle.Text = snap.IsWifiRadioOn ? "No Internet Access" : "Wi-Fi Radio Off";
        }

        NetworkDownSpeedText.Text = snap.DownloadSpeedText;
        NetworkUpSpeedText.Text = snap.UploadSpeedText;

        if (NetworkPingText != null)
        {
            NetworkPingText.Text = snap.PingText;
        }
        if (NetworkPingDot != null)
        {
            NetworkPingDot.Fill = snap.PingMs switch
            {
                > 0 and < 60 => ThemeBrushes.Green,
                >= 60 and < 150 => ThemeBrushes.Amber,
                >= 150 => ThemeBrushes.Red,
                _ => ThemeBrushes.SecondaryText
            };
        }

        // Quick-Toggle Cards (Wi-Fi & Bluetooth Radios)
        bool wifiOn = snap.IsWifiRadioOn;
        WifiToggleGlyph.Foreground = wifiOn ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
        WifiToggleStateText.Text = wifiOn ? (snap.IsWifi && snap.IsConnected ? snap.ConnectionName : "On") : "Off";
        WifiToggleStateText.Foreground = wifiOn ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
        WifiToggleCard.Background = wifiOn
            ? ThemeBrushes.PlanSaverActiveBg
            : ThemeBrushes.ButtonSubtleBg;
        WifiToggleCard.BorderBrush = wifiOn
            ? ThemeBrushes.GreenBorderActive
            : Brushes.Transparent;

        bool btOn = snap.IsBluetoothRadioOn;
        BtToggleGlyph.Foreground = btOn ? ThemeBrushes.Blue : ThemeBrushes.SecondaryText;
        BtToggleStateText.Text = btOn ? "On" : "Off";
        BtToggleStateText.Foreground = btOn ? ThemeBrushes.Blue : ThemeBrushes.SecondaryText;
        BtToggleCard.Background = btOn
            ? ThemeBrushes.PlanBalancedActiveBg
            : ThemeBrushes.ButtonSubtleBg;
        BtToggleCard.BorderBrush = btOn
            ? ThemeBrushes.BlueBorderActive
            : Brushes.Transparent;
    }

    internal void ClockWifiBadge_Click(object sender, MouseButtonEventArgs e)
    {
        SwitchExpandedView(ExpandedView.Network);
        e.Handled = true;
    }

    internal void NetworkIpBadge_Click(object sender, MouseButtonEventArgs e)
    {
        string ip = _networkService.Current.LocalIpv4;
        if (!string.IsNullOrWhiteSpace(ip) && ip != "\u2014")
        {
            try
            {
                Clipboard.SetText(ip);
                _machine.PushToast(new ToastContent(
                    "\uE8C8",
                    "IPv4 Copied",
                    ip,
                    ThemeBrushes.GreenColor,
                    TimeSpan.FromSeconds(2.0),
                    TargetView: "network",
                    Priority: ToastPriority.Feedback,
                    Category: "clipboard"));
            }
            catch { }
        }
        e.Handled = true;
    }

    internal void OpenNetworkSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        NetworkService.OpenNetworkSettings();
    }

    internal async void WifiToggleCard_Click(object sender, MouseButtonEventArgs e)
    {
        TriggerLiquidImpulse(180, -70, 22);
        bool toggled = await _networkService.ToggleWifiRadioAsync();
        if (toggled)
        {
            bool on = _networkService.Current.IsWifiRadioOn;
            _machine.PushToast(new ToastContent(
                on ? "\uE701" : "\uEB5E",
                on ? "Wi-Fi Enabled" : "Wi-Fi Disabled",
                on ? "Scanning for networks..." : "Wireless radio turned off",
                on ? ThemeBrushes.GreenColor : ThemeBrushes.AmberColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "network",
                Priority: ToastPriority.Feedback,
                Category: "wifi"));
        }
        e.Handled = true;
    }

    internal async void BtToggleCard_Click(object sender, MouseButtonEventArgs e)
    {
        TriggerLiquidImpulse(180, -70, 22);
        bool toggled = await _networkService.ToggleBluetoothRadioAsync();
        if (toggled)
        {
            bool on = _networkService.Current.IsBluetoothRadioOn;
            _machine.PushToast(new ToastContent(
                "\uE702",
                on ? "Bluetooth Enabled" : "Bluetooth Disabled",
                on ? "Wireless radio active" : "Bluetooth radio turned off",
                on ? ThemeBrushes.BlueColor : ThemeBrushes.AmberColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "network",
                Priority: ToastPriority.Feedback,
                Category: "bluetooth"));
        }
        e.Handled = true;
    }

    internal void WifiNetworksCard_Click(object sender, MouseButtonEventArgs e)
    {
        SetWifiScannerMode(true);
        e.Handled = true;
    }

    internal void ScanWifiModeBtn_Click(object sender, RoutedEventArgs e)
    {
        SetWifiScannerMode(!_isWifiScannerMode);
    }

    private async void SetWifiScannerMode(bool scannerMode)
    {
        _isWifiScannerMode = scannerMode;
        if (NetworkSectionTitle != null)
        {
            NetworkSectionTitle.Text = _isWifiScannerMode ? "Available Wi-Fi Networks" : "Wi-Fi & Connectivity";
        }
        if (ScanWifiModeText != null)
        {
            ScanWifiModeText.Text = _isWifiScannerMode ? "\u2190 Overview" : "Scan SSIDs";
        }
        if (NetworkOverviewHost != null && WifiScannerHost != null)
        {
            NetworkOverviewHost.Visibility = _isWifiScannerMode ? Visibility.Collapsed : Visibility.Visible;
            WifiScannerHost.Visibility = _isWifiScannerMode ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_isWifiScannerMode)
        {
            if (WifiScannerStatusPlaceholder != null && _wifiNetworks.Count == 0)
            {
                if (WifiScannerStatusTitle != null) WifiScannerStatusTitle.Text = "Scanning Wi-Fi...";
                if (WifiScannerStatusSubtitle != null) WifiScannerStatusSubtitle.Text = "Searching for nearby wireless networks";
                WifiScannerStatusPlaceholder.Visibility = Visibility.Visible;
            }

            await _networkService.ScanWifiNetworksAsync();

            if (_isWifiScannerMode && WifiScannerStatusPlaceholder != null)
            {
                if (_wifiNetworks.Count == 0)
                {
                    if (WifiScannerStatusTitle != null) WifiScannerStatusTitle.Text = "No Wi-Fi Networks Found";
                    if (WifiScannerStatusSubtitle != null) WifiScannerStatusSubtitle.Text = "Check that Wi-Fi radio and Windows Location are enabled";
                    WifiScannerStatusPlaceholder.Visibility = Visibility.Visible;
                }
                else
                {
                    WifiScannerStatusPlaceholder.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    private void OnWifiNetworksChanged(IReadOnlyList<WifiNetworkInfo> networks)
    {
        SyncObservableCollection(_wifiNetworks, networks);
        if (WifiScannerStatusPlaceholder != null)
        {
            WifiScannerStatusPlaceholder.Visibility = (_isWifiScannerMode && _wifiNetworks.Count == 0)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    internal async void WifiNetworkCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WifiNetworkInfo net }) return;
        e.Handled = true;

        var (ok, msg) = await _networkService.ConnectWifiNetworkAsync(net);
        _machine.PushToast(new ToastContent(
            net.Glyph,
            ok ? net.Ssid : "Wi-Fi Authentication",
            msg,
            ok ? ThemeBrushes.GreenColor : ThemeBrushes.AmberColor,
            TimeSpan.FromSeconds(2.5),
            TargetView: "network",
            Priority: ToastPriority.Feedback,
            Category: "wifi"));
    }

    internal void HotspotCard_Click(object sender, MouseButtonEventArgs e)
    {
        NetworkService.OpenMobileHotspotSettings();
        e.Handled = true;
    }

    // =================================================================
    //  Display Brightness Control (WMI & DDC/CI)
    // =================================================================

    private void OnBrightnessChanged(int pct)
    {
        if (BrightnessSlider == null || BrightnessValueText == null) return;
        BrightnessValueText.Text = $"{pct}%";
        if (!BrightnessSlider.IsMouseCaptureWithin)
        {
            BrightnessSlider.Value = pct;
        }
    }

    internal void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_brightnessService == null || BrightnessValueText == null) return;
        int pct = (int)Math.Round(e.NewValue);
        BrightnessValueText.Text = $"{pct}%";
        if (BrightnessSlider.IsMouseCaptureWithin)
        {
            _brightnessService.SetBrightness(pct);
        }
    }

}
