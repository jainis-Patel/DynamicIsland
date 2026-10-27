using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Threading;
using DynamicIsland.Core;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

namespace DynamicIsland.Services;

public sealed record NetworkStatusSnapshot(
    bool IsConnected,
    bool IsWifi,
    bool IsEthernet,
    string ConnectionName,
    string AdapterName,
    int SignalBars,
    string Glyph,
    string LocalIpv4,
    long LinkSpeedMbps,
    double DownloadBytesPerSec,
    double UploadBytesPerSec,
    string DownloadSpeedText,
    string UploadSpeedText,
    bool IsWifiRadioOn,
    bool IsBluetoothRadioOn);

/// <summary>
/// Monitors Wi-Fi SSID, signal strength, Ethernet state, real-time download/upload throughput,
/// local IPv4 address, and Wi-Fi / Bluetooth radio states. Also emits Dynamic Island toasts
/// when network connectivity changes.
/// </summary>
internal sealed class NetworkService : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _sampleTimer;
    private long _prevBytesReceived = -1;
    private long _prevBytesSent = -1;
    private DateTime _prevSampleTime = DateTime.UtcNow;

    private string? _lastToastConnectionKey;
    private DateTime _lastToastTime = DateTime.MinValue;
    private bool _isWifiRadioOn = true;
    private bool _isBluetoothRadioOn = true;

    public NetworkStatusSnapshot Current { get; private set; } = new(
        IsConnected: false,
        IsWifi: false,
        IsEthernet: false,
        ConnectionName: "Checking...",
        AdapterName: "",
        SignalBars: 0,
        Glyph: "\uE701",
        LocalIpv4: "—",
        LinkSpeedMbps: 0,
        DownloadBytesPerSec: 0,
        UploadBytesPerSec: 0,
        DownloadSpeedText: "0 KB/s",
        UploadSpeedText: "0 KB/s",
        IsWifiRadioOn: true,
        IsBluetoothRadioOn: true);

    public event Action<NetworkStatusSnapshot>? Updated;
    public event Action<ToastContent>? ConnectivityAlert;

    public NetworkService(Dispatcher ui)
    {
        _ui = ui;
        _sampleTimer = new DispatcherTimer(DispatcherPriority.Background, _ui)
        {
            Interval = TimeSpan.FromSeconds(1.2)
        };
        _sampleTimer.Tick += (_, _) => Sample();
    }

    public void Start()
    {
        try
        {
            NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
        }
        catch (Exception ex)
        {
            App.Log($"[NetworkService] NetworkStatusChanged subscribe failed: {ex.Message}");
        }

        _ = RefreshRadioStatesAsync();
        Sample(suppressAlert: true);
        _sampleTimer.Start();
    }

    private void OnNetworkStatusChanged(object? sender)
    {
        _ui.InvokeAsync(async () =>
        {
            await RefreshRadioStatesAsync();
            Sample(suppressAlert: false);
        });
    }

    public async Task RefreshRadioStatesAsync()
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            var wifi = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
            var bt = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);

            if (wifi != null) _isWifiRadioOn = wifi.State == RadioState.On;
            if (bt != null) _isBluetoothRadioOn = bt.State == RadioState.On;
        }
        catch (Exception ex)
        {
            App.Log($"[NetworkService] Radio query failed: {ex.Message}");
        }
    }

    public async Task<bool> ToggleWifiRadioAsync()
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            var wifi = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
            if (wifi != null)
            {
                bool targetOn = wifi.State != RadioState.On;
                var result = await wifi.SetStateAsync(targetOn ? RadioState.On : RadioState.Off);
                if (result == RadioAccessStatus.Allowed)
                {
                    _isWifiRadioOn = targetOn;
                    Sample(suppressAlert: true);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[NetworkService] ToggleWifiRadio failed: {ex.Message}");
        }

        // Fallback: open Windows Wi-Fi settings if direct radio control is restricted
        OpenUri("ms-settings:network-wifi");
        return false;
    }

    public async Task<bool> ToggleBluetoothRadioAsync()
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            var bt = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            if (bt != null)
            {
                bool targetOn = bt.State != RadioState.On;
                var result = await bt.SetStateAsync(targetOn ? RadioState.On : RadioState.Off);
                if (result == RadioAccessStatus.Allowed)
                {
                    _isBluetoothRadioOn = targetOn;
                    Sample(suppressAlert: true);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[NetworkService] ToggleBluetoothRadio failed: {ex.Message}");
        }

        OpenUri("ms-settings:bluetooth");
        return false;
    }

    public static void OpenAvailableNetworksFlyout() => OpenUri("ms-availablenetworks:");

    public static void OpenMobileHotspotSettings() => OpenUri("ms-settings:network-mobilehotspot");

    public static void OpenNetworkSettings() => OpenUri("ms-settings:network-status");

    private static void OpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Log($"[NetworkService] Failed to launch '{uri}': {ex.Message}");
        }
    }

    public void Sample(bool suppressAlert = true)
    {
        try
        {
            bool connected = false;
            bool isWifi = false;
            bool isEthernet = false;
            string connName = "Offline";
            string adapterName = "No Active Adapter";
            int signalBars = 0;
            string localIp = "—";
            long linkMbps = 0;

            try
            {
                var profile = NetworkInformation.GetInternetConnectionProfile();
                if (profile != null)
                {
                    var level = profile.GetNetworkConnectivityLevel();
                    connected = level is NetworkConnectivityLevel.InternetAccess
                                      or NetworkConnectivityLevel.ConstrainedInternetAccess
                                      or NetworkConnectivityLevel.LocalAccess;

                    isWifi = profile.IsWlanConnectionProfile;
                    signalBars = (int)(profile.GetSignalBars() ?? (byte)(connected ? 4 : 0));

                    if (isWifi && profile.WlanConnectionProfileDetails != null)
                    {
                        string ssid = profile.WlanConnectionProfileDetails.GetConnectedSsid();
                        connName = !string.IsNullOrWhiteSpace(ssid) ? ssid : profile.ProfileName;
                    }
                    else if (!string.IsNullOrWhiteSpace(profile.ProfileName))
                    {
                        connName = profile.ProfileName;
                    }
                }
            }
            catch
            {
                // Fallback to System.Net.NetworkInformation below
            }

            long totalRx = 0;
            long totalTx = 0;

            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                try
                {
                    var stats = ni.GetIPStatistics();
                    totalRx += stats.BytesReceived;
                    totalTx += stats.BytesSent;
                }
                catch { }

                var ipProps = ni.GetIPProperties();
                if (ipProps.GatewayAddresses.Count == 0) continue;

                if (!connected)
                {
                    connected = true;
                    isWifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                    isEthernet = ni.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet;
                    if (string.IsNullOrWhiteSpace(connName) || connName == "Offline")
                    {
                        connName = isWifi ? "Wi-Fi" : (isEthernet ? "Ethernet" : ni.Name);
                    }
                }
                else if (!isWifi)
                {
                    isEthernet = ni.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet;
                }

                adapterName = ni.Description;
                if (ni.Speed > 0)
                {
                    linkMbps = Math.Max(linkMbps, ni.Speed / 1_000_000);
                }

                foreach (var ua in ipProps.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        localIp = ua.Address.ToString();
                        break;
                    }
                }
            }

            var now = DateTime.UtcNow;
            double dt = (now - _prevSampleTime).TotalSeconds;
            double rxRate = 0;
            double txRate = 0;

            if (_prevBytesReceived >= 0 && dt > 0.1 && totalRx >= _prevBytesReceived && totalTx >= _prevBytesSent)
            {
                rxRate = (totalRx - _prevBytesReceived) / dt;
                txRate = (totalTx - _prevBytesSent) / dt;
            }

            _prevBytesReceived = totalRx;
            _prevBytesSent = totalTx;
            _prevSampleTime = now;

            if (!connected)
            {
                connName = _isWifiRadioOn ? "Disconnected" : "Wi-Fi Off";
                signalBars = 0;
            }
            else if (signalBars <= 0)
            {
                signalBars = 4;
            }

            string glyph = ResolveGlyph(connected, isWifi, isEthernet, signalBars);
            var snapshot = new NetworkStatusSnapshot(
                IsConnected: connected,
                IsWifi: isWifi,
                IsEthernet: isEthernet,
                ConnectionName: connName,
                AdapterName: adapterName,
                SignalBars: Math.Clamp(signalBars, 0, 4),
                Glyph: glyph,
                LocalIpv4: localIp,
                LinkSpeedMbps: linkMbps,
                DownloadBytesPerSec: rxRate,
                UploadBytesPerSec: txRate,
                DownloadSpeedText: FormatBandwidth(rxRate),
                UploadSpeedText: FormatBandwidth(txRate),
                IsWifiRadioOn: _isWifiRadioOn,
                IsBluetoothRadioOn: _isBluetoothRadioOn);

            Current = snapshot;
            Updated?.Invoke(snapshot);

            string stateKey = $"{connected}|{isWifi}|{isEthernet}|{connName}";
            if (_lastToastConnectionKey == null)
            {
                _lastToastConnectionKey = stateKey;
            }
            else if (!suppressAlert && stateKey != _lastToastConnectionKey && (now - _lastToastTime).TotalSeconds > 3.0)
            {
                _lastToastConnectionKey = stateKey;
                _lastToastTime = now;

                if (connected)
                {
                    string title = isWifi ? $"Wi-Fi · {connName}" : "Ethernet Connected";
                    string sub = linkMbps > 0 ? $"{linkMbps} Mbps · {localIp}" : localIp;
                    ConnectivityAlert?.Invoke(new ToastContent(
                        glyph,
                        title,
                        sub,
                        ThemeBrushes.GreenColor,
                        TimeSpan.FromSeconds(3.0),
                        TargetView: "network",
                        Priority: ToastPriority.System,
                        Category: "network"));
                }
                else
                {
                    ConnectivityAlert?.Invoke(new ToastContent(
                        "\uEB5E",
                        "Network Disconnected",
                        connName,
                        ThemeBrushes.AmberColor,
                        TimeSpan.FromSeconds(3.0),
                        TargetView: "network",
                        Priority: ToastPriority.System,
                        Category: "network"));
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[NetworkService] Sample error: {ex.Message}");
        }
    }

    internal static string ResolveGlyph(bool connected, bool isWifi, bool isEthernet, int signalBars)
    {
        if (!connected) return "\uEB5E"; // Wi-Fi disconnected / error glyph
        if (isEthernet && !isWifi) return "\uE839"; // Ethernet port glyph
        return signalBars switch
        {
            <= 1 => "\uE872",
            2 => "\uE873",
            3 => "\uE874",
            _ => "\uE701"
        };
    }

    internal static string FormatBandwidth(double bytesPerSec)
    {
        if (bytesPerSec < 1024)
            return "0 KB/s";
        double kb = bytesPerSec / 1024.0;
        if (kb < 1000)
            return $"{kb:0} KB/s";
        double mb = kb / 1024.0;
        return $"{mb:0.0} MB/s";
    }

    public void Dispose()
    {
        _sampleTimer.Stop();
        try
        {
            NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
        }
        catch { }
    }
}
