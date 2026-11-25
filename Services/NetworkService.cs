using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

public sealed class WifiNetworkInfo
{
    public string Ssid { get; init; } = "";
    public int SignalPercent { get; init; }
    public int SignalBars { get; init; }
    public string Glyph { get; init; } = "\uE701";
    public string SecurityText { get; init; } = "WPA2";
    public bool IsConnected { get; init; }
    public bool IsSavedProfile { get; init; }

    public string Subtitle => IsConnected
        ? $"Connected · {SignalPercent}%"
        : IsSavedProfile
            ? $"Saved · {SignalPercent}%"
            : $"{SecurityText} · {SignalPercent}%";

    public string ActionText => IsConnected ? "Active" : (IsSavedProfile ? "Connect" : "Join");
    public Brush ActionForeground => IsConnected ? ThemeBrushes.Green : ThemeBrushes.Blue;
}

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
    bool IsBluetoothRadioOn,
    int PingMs = -1,
    string PingText = "—",
    string ShortSpeedText = "0K");

/// <summary>
/// Monitors Wi-Fi SSID, signal strength, Ethernet state, real-time download/upload throughput,
/// local IPv4 address, and Wi-Fi / Bluetooth radio states. Also emits Dynamic Island toasts
/// when network connectivity changes.
/// </summary>
internal sealed partial class NetworkService : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _sampleTimer;
    private readonly Dictionary<string, (long Rx, long Tx)> _prevAdapterBytes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sampleLock = new();
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
        IsBluetoothRadioOn: true,
        PingMs: -1,
        PingText: "—",
        ShortSpeedText: "0K");

    private int _lastPingMs = -1;
    private DateTime _lastPingTime = DateTime.MinValue;
    private int _pingFailures;

    public event Action<NetworkStatusSnapshot>? Updated;
    public event Action<ToastContent>? ConnectivityAlert;
    public event Action<IReadOnlyList<WifiNetworkInfo>>? WifiNetworksChanged;

    private List<WifiNetworkInfo> _wifiNetworks = new();
    private int _scanningWifi;
    private int _sampling;
    private volatile bool _isExpanded;
    private bool _isSuspended;
    private bool _disposed;
    private readonly Func<AppSettings>? _getSettings;

    public IReadOnlyList<WifiNetworkInfo> WifiNetworks => _wifiNetworks;

    public NetworkService(Dispatcher ui, Func<AppSettings>? getSettings = null)
    {
        _ui = ui;
        _getSettings = getSettings;
        _sampleTimer = new DispatcherTimer(DispatcherPriority.Background, _ui)
        {
            Interval = TimeSpan.FromSeconds(5.0)
        };
        _sampleTimer.Tick += (_, _) => _ = SampleAsync();
    }

    public void UpdatePollingState()
    {
        if (_disposed || _isSuspended)
        {
            _sampleTimer.Stop();
            return;
        }

        if (_isExpanded)
        {
            _sampleTimer.Interval = TimeSpan.FromSeconds(1.2);
            if (!_sampleTimer.IsEnabled) _sampleTimer.Start();
        }
        else if (_getSettings?.Invoke()?.ShowNetworkSpeedInIdle ?? false)
        {
            _sampleTimer.Interval = TimeSpan.FromSeconds(1.5);
            if (!_sampleTimer.IsEnabled) _sampleTimer.Start();
        }
        else
        {
            _sampleTimer.Stop();
        }
    }

    public void SetExpanded(bool isExpanded)
    {
        _isExpanded = isExpanded;
        UpdatePollingState();
        if (isExpanded && !_isSuspended)
        {
            _ = SampleAsync();
        }
    }

    public void SetSuspended(bool suspended)
    {
        _isSuspended = suspended;
        UpdatePollingState();
        if (!suspended && (_isExpanded || (_getSettings?.Invoke()?.ShowNetworkSpeedInIdle ?? false)))
        {
            _ = SampleAsync();
        }
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
        UpdatePollingState();
    }

    private void OnNetworkStatusChanged(object? sender)
    {
        if (_disposed) return;
        Task.Run(async () =>
        {
            await RefreshRadioStatesAsync().ConfigureAwait(false);
            await SampleAsync(suppressAlert: false).ConfigureAwait(false);
        }).FireAndForget("Network.Refresh");
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

    public async Task SampleAsync(bool suppressAlert = true)
    {
        if (_disposed || Interlocked.CompareExchange(ref _sampling, 1, 0) != 0)
            return;

        try
        {
            var result = await Task.Run(() => CollectNetworkSnapshot(suppressAlert)).ConfigureAwait(false);
            if (_disposed || result == null) return;

            var (snapshot, alert) = result.Value;
            await _ui.InvokeAsync(() =>
            {
                if (_disposed) return;
                Current = snapshot;
                Updated?.Invoke(snapshot);
                if (alert != null)
                {
                    ConnectivityAlert?.Invoke(alert);
                }
            });
        }
        catch (Exception ex)
        {
            App.Log($"[NetworkService] SampleAsync error: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _sampling, 0);
        }
    }

    public void Sample(bool suppressAlert = true)
    {
        try
        {
            var result = CollectNetworkSnapshot(suppressAlert);
            if (result == null) return;

            var (snapshot, alert) = result.Value;
            Current = snapshot;
            Updated?.Invoke(snapshot);
            if (alert != null)
            {
                ConnectivityAlert?.Invoke(alert);
            }
        }
        catch (Exception ex)
        {
            App.Log($"[NetworkService] Sample error: {ex.Message}");
        }
    }

    private (NetworkStatusSnapshot Snapshot, ToastContent? Alert)? CollectNetworkSnapshot(bool suppressAlert)
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

            long deltaRx = 0;
            long deltaTx = 0;
            var currentAdapters = new Dictionary<string, (long Rx, long Tx)>(StringComparer.OrdinalIgnoreCase);

            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                try
                {
                    var stats = ni.GetIPStatistics();
                    currentAdapters[ni.Id] = (stats.BytesReceived, stats.BytesSent);
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

            DateTime now;
            double dt;
            double rxRate = 0;
            double txRate = 0;

            lock (_sampleLock)
            {
                now = DateTime.UtcNow;
                dt = (now - _prevSampleTime).TotalSeconds;
                if (_prevAdapterBytes.Count > 0 && dt > 0.1)
                {
                    foreach (var kvp in currentAdapters)
                    {
                        if (_prevAdapterBytes.TryGetValue(kvp.Key, out var prev) &&
                            kvp.Value.Rx >= prev.Rx && kvp.Value.Tx >= prev.Tx)
                        {
                            deltaRx += kvp.Value.Rx - prev.Rx;
                            deltaTx += kvp.Value.Tx - prev.Tx;
                        }
                    }

                    rxRate = deltaRx / dt;
                    txRate = deltaTx / dt;
                }

                _prevAdapterBytes.Clear();
                foreach (var kvp in currentAdapters)
                {
                    _prevAdapterBytes[kvp.Key] = kvp.Value;
                }
                _prevSampleTime = now;
            }

            if (!connected)
            {
                connName = _isWifiRadioOn ? "Disconnected" : "Wi-Fi Off";
                signalBars = 0;
            }
            else if (signalBars <= 0)
            {
                signalBars = 4;
            }

            if (connected && _isExpanded)
            {
                _ = UpdatePingAsync();
            }

            string glyph = ResolveGlyph(connected, isWifi, isEthernet, signalBars);
            string pingText = _lastPingMs >= 0 ? $"{_lastPingMs} ms" : "—";
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
                IsBluetoothRadioOn: _isBluetoothRadioOn,
                PingMs: _lastPingMs,
                PingText: pingText,
                ShortSpeedText: FormatShortBandwidth(Math.Max(rxRate, txRate)));

            ToastContent? alert = null;
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
                    alert = new ToastContent(
                        glyph,
                        title,
                        sub,
                        ThemeBrushes.GreenColor,
                        TimeSpan.FromSeconds(3.0),
                        TargetView: "network",
                        Priority: ToastPriority.System,
                        Category: "network");
                }
                else
                {
                    alert = new ToastContent(
                        "\uEB5E",
                        "Network Disconnected",
                        connName,
                        ThemeBrushes.AmberColor,
                        TimeSpan.FromSeconds(3.0),
                        TargetView: "network",
                        Priority: ToastPriority.System,
                        Category: "network");
                }
            }

            return (snapshot, alert);
        }
        catch (Exception ex)
        {
            App.Log($"[NetworkService] CollectNetworkSnapshot error: {ex.Message}");
            return null;
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

    private async Task UpdatePingAsync()
    {
        double backoffSec = _pingFailures == 0 ? 4.0 : Math.Min(60.0, 4.0 * Math.Pow(2, _pingFailures));
        if ((DateTime.UtcNow - _lastPingTime).TotalSeconds < backoffSec) return;
        _lastPingTime = DateTime.UtcNow;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("1.1.1.1", 1200).ConfigureAwait(false);
            if (reply.Status == IPStatus.Success)
            {
                _lastPingMs = (int)reply.RoundtripTime;
                _pingFailures = 0;
            }
            else
            {
                _lastPingMs = -1;
                _pingFailures = Math.Min(_pingFailures + 1, 5);
            }
        }
        catch
        {
            _lastPingMs = -1;
            _pingFailures = Math.Min(_pingFailures + 1, 5);
        }
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

    internal static string FormatShortBandwidth(double bytesPerSec)
    {
        if (bytesPerSec < 1024) return "0K";
        double kb = bytesPerSec / 1024.0;
        if (kb < 1000) return $"{kb:0}K";
        double mb = kb / 1024.0;
        return $"{mb:0.0}M";
    }

    public void Dispose()
    {
        _disposed = true;
        _sampleTimer.Stop();
        try
        {
            NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
        }
        catch { }
    }
}
