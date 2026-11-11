using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

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
        IsBluetoothRadioOn: true,
        PingMs: -1,
        PingText: "—",
        ShortSpeedText: "0K");

    private int _lastPingMs = -1;
    private DateTime _lastPingTime = DateTime.MinValue;

    public event Action<NetworkStatusSnapshot>? Updated;
    public event Action<ToastContent>? ConnectivityAlert;
    public event Action<IReadOnlyList<WifiNetworkInfo>>? WifiNetworksChanged;

    private List<WifiNetworkInfo> _wifiNetworks = new();
    private int _scanningWifi;
    private volatile bool _isExpanded;
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
        _sampleTimer.Tick += (_, _) => Sample();
    }

    public void SetExpanded(bool isExpanded)
    {
        _isExpanded = isExpanded;
        if (isExpanded)
        {
            _sampleTimer.Interval = TimeSpan.FromSeconds(1.2);
            Sample();
        }
        else
        {
            bool showSpeedInIdle = _getSettings?.Invoke()?.ShowNetworkSpeedInIdle ?? false;
            _sampleTimer.Interval = showSpeedInIdle ? TimeSpan.FromSeconds(1.5) : TimeSpan.FromSeconds(5.0);
        }
    }

    public void SetSuspended(bool suspended)
    {
        if (suspended)
        {
            _sampleTimer.Stop();
        }
        else if (!_sampleTimer.IsEnabled)
        {
            _sampleTimer.Start();
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

    public async Task<IReadOnlyList<WifiNetworkInfo>> ScanWifiNetworksAsync()
    {
        if (Interlocked.CompareExchange(ref _scanningWifi, 1, 0) != 0)
            return _wifiNetworks;

        try
        {
            string? activeSsid = Current.IsWifi && Current.IsConnected ? Current.ConnectionName : null;
            var networks = await Task.Run(() =>
            {
                var saved = GetSavedWlanProfiles();
                string netshOut = RunNetshCommand("wlan show networks mode=bssid");
                var parsed = ParseNetshNetworksOutput(netshOut, saved, activeSsid);

                // Ensure the currently connected Wi-Fi network is always present at the top
                if (!string.IsNullOrWhiteSpace(activeSsid) &&
                    parsed.All(n => !string.Equals(n.Ssid, activeSsid, StringComparison.OrdinalIgnoreCase)))
                {
                    int bars = Math.Clamp(Current.SignalBars, 1, 4);
                    parsed.Insert(0, new WifiNetworkInfo
                    {
                        Ssid = activeSsid,
                        SignalBars = bars,
                        SignalPercent = bars * 25,
                        Glyph = ResolveGlyph(true, true, false, bars),
                        SecurityText = "Connected",
                        IsConnected = true,
                        IsSavedProfile = true
                    });
                }

                return parsed;
            }).ConfigureAwait(false);

            await _ui.InvokeAsync(() =>
            {
                _wifiNetworks = networks;
                WifiNetworksChanged?.Invoke(_wifiNetworks);
            });

            return networks;
        }
        catch (Exception ex)
        {
            App.Log($"[NetworkService] ScanWifiNetworksAsync failed: {ex.Message}");
            return _wifiNetworks;
        }
        finally
        {
            Interlocked.Exchange(ref _scanningWifi, 0);
        }
    }

    public async Task<(bool Success, string Message)> ConnectWifiNetworkAsync(WifiNetworkInfo network)
    {
        if (network == null || string.IsNullOrWhiteSpace(network.Ssid))
            return (false, "Invalid network");

        if (network.IsConnected)
            return (true, $"Already connected to {network.Ssid}");

        bool connectedViaNetsh = await Task.Run(() =>
        {
            try
            {
                string safeSsid = network.Ssid.Replace("\"", "");
                string output = RunNetshCommand($"wlan connect name=\"{safeSsid}\" ssid=\"{safeSsid}\"");
                return output.Contains("successfully", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }).ConfigureAwait(false);

        if (connectedViaNetsh)
        {
            await Task.Delay(900).ConfigureAwait(false);
            _ = _ui.InvokeAsync(() =>
            {
                Sample(suppressAlert: false);
                _ = ScanWifiNetworksAsync();
            });
            return (true, $"Connecting to {network.Ssid}...");
        }

        // If no saved profile exists for this SSID (requires password entry), open Windows Wi-Fi flyout
        OpenAvailableNetworksFlyout();
        return (false, $"Enter password for {network.Ssid} in Windows Wi-Fi prompt");
    }

    private static HashSet<string> GetSavedWlanProfiles()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string output = RunNetshCommand("wlan show profiles");
        if (string.IsNullOrWhiteSpace(output)) return set;

        foreach (string rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = rawLine.IndexOf(':');
            if (colon > 0 && rawLine.Contains("Profile", StringComparison.OrdinalIgnoreCase))
            {
                string profile = rawLine[(colon + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(profile))
                {
                    set.Add(profile);
                }
            }
        }
        return set;
    }

    private static string RunNetshCommand(string args)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo("netsh", args)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);
            return output;
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Deterministic parser for <c>netsh wlan show networks mode=bssid</c> output.
    /// Extracts SSIDs, max signal percentage across BSSIDs, security type, and saved/connected state.
    /// </summary>
    internal static List<WifiNetworkInfo> ParseNetshNetworksOutput(
        string netshOutput,
        IReadOnlySet<string> savedProfiles,
        string? connectedSsid)
    {
        var results = new List<WifiNetworkInfo>();
        if (string.IsNullOrWhiteSpace(netshOutput))
            return results;

        string? currentSsid = null;
        string currentAuth = "WPA2";
        int bestSignalPct = 0;

        void FlushCurrent()
        {
            if (string.IsNullOrWhiteSpace(currentSsid)) return;
            int pct = bestSignalPct > 0 ? Math.Clamp(bestSignalPct, 5, 100) : 65;
            int bars = pct >= 78 ? 4 : pct >= 55 ? 3 : pct >= 32 ? 2 : 1;
            bool isConn = !string.IsNullOrWhiteSpace(connectedSsid) &&
                          string.Equals(currentSsid, connectedSsid, StringComparison.OrdinalIgnoreCase);
            bool isSaved = isConn || savedProfiles.Contains(currentSsid);

            var existingIdx = results.FindIndex(r => string.Equals(r.Ssid, currentSsid, StringComparison.OrdinalIgnoreCase));
            var info = new WifiNetworkInfo
            {
                Ssid = currentSsid,
                SignalPercent = pct,
                SignalBars = bars,
                Glyph = ResolveGlyph(true, true, false, bars),
                SecurityText = currentAuth,
                IsConnected = isConn,
                IsSavedProfile = isSaved
            };

            if (existingIdx >= 0)
            {
                if (info.SignalPercent > results[existingIdx].SignalPercent || info.IsConnected)
                    results[existingIdx] = info;
            }
            else
            {
                results.Add(info);
            }
        }

        foreach (string rawLine in netshOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (Regex.IsMatch(line, @"^SSID\s+\d+\s*:", RegexOptions.IgnoreCase))
            {
                FlushCurrent();
                int colon = line.IndexOf(':');
                currentSsid = colon >= 0 ? line[(colon + 1)..].Trim() : "";
                currentAuth = "WPA2";
                bestSignalPct = 0;
            }
            else if (currentSsid != null && line.StartsWith("Authentication", StringComparison.OrdinalIgnoreCase))
            {
                int colon = line.IndexOf(':');
                if (colon >= 0)
                {
                    currentAuth = line[(colon + 1)..].Trim();
                }
            }
            else if (currentSsid != null && line.Contains('%'))
            {
                var m = Regex.Match(line, @"(\d{1,3})\s*%");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int sig))
                {
                    bestSignalPct = Math.Max(bestSignalPct, sig);
                }
            }
        }

        FlushCurrent();

        return results
            .OrderByDescending(r => r.IsConnected)
            .ThenByDescending(r => r.IsSavedProfile)
            .ThenByDescending(r => r.SignalPercent)
            .ToList();
    }

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

    private async Task UpdatePingAsync()
    {
        if ((DateTime.UtcNow - _lastPingTime).TotalSeconds < 4.0) return;
        _lastPingTime = DateTime.UtcNow;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("1.1.1.1", 1200);
            if (reply.Status == IPStatus.Success)
            {
                _lastPingMs = (int)reply.RoundtripTime;
            }
            else
            {
                _lastPingMs = -1;
            }
        }
        catch
        {
            _lastPingMs = -1;
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
        _sampleTimer.Stop();
        try
        {
            NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
        }
        catch { }
    }
}
