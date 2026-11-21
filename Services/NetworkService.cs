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
internal sealed class NetworkService : IDisposable
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
        _ = Task.Run(async () =>
        {
            await RefreshRadioStatesAsync().ConfigureAwait(false);
            await SampleAsync(suppressAlert: false).ConfigureAwait(false);
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
                var parsed = TryScanWifiNative(activeSsid);
                if (parsed == null || parsed.Count == 0)
                {
                    var saved = GetSavedWlanProfiles();
                    string netshOut = RunNetshCommand("wlan show networks mode=bssid");
                    parsed = ParseNetshNetworksOutput(netshOut, saved, activeSsid);
                }

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

        bool connectedViaWlan = await Task.Run(() =>
        {
            if (network.IsSavedProfile && TryConnectWifiNative(network.Ssid))
                return true;

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

        if (connectedViaWlan)
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

    private static List<WifiNetworkInfo>? TryScanWifiNative(string? activeSsid)
    {
        IntPtr client = IntPtr.Zero;
        IntPtr ifaceListPtr = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out client) != 0 || client == IntPtr.Zero)
                return null;

            if (WlanEnumInterfaces(client, IntPtr.Zero, out ifaceListPtr) != 0 || ifaceListPtr == IntPtr.Zero)
                return null;

            uint ifaceCount = (uint)Marshal.ReadInt32(ifaceListPtr);
            if (ifaceCount == 0) return null;

            int ifaceSize = Marshal.SizeOf<WLAN_INTERFACE_INFO>();
            int netSize = Marshal.SizeOf<WLAN_AVAILABLE_NETWORK>();
            var bySsid = new Dictionary<string, WifiNetworkInfo>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < ifaceCount; i++)
            {
                var iface = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(IntPtr.Add(ifaceListPtr, 8 + i * ifaceSize));
                Guid ifaceGuid = iface.InterfaceGuid;
                IntPtr netListPtr = IntPtr.Zero;
                try
                {
                    if (WlanGetAvailableNetworkList(client, ref ifaceGuid, 0, IntPtr.Zero, out netListPtr) != 0 ||
                        netListPtr == IntPtr.Zero)
                    {
                        continue;
                    }

                    uint netCount = (uint)Marshal.ReadInt32(netListPtr);
                    for (int n = 0; n < netCount; n++)
                    {
                        var net = Marshal.PtrToStructure<WLAN_AVAILABLE_NETWORK>(IntPtr.Add(netListPtr, 8 + n * netSize));
                        int ssidLen = (int)Math.Min(net.dot11Ssid.uSSIDLength, 32u);
                        if (ssidLen <= 0 || net.dot11Ssid.ucSSID == null) continue;

                        string ssid = Encoding.UTF8.GetString(net.dot11Ssid.ucSSID, 0, ssidLen).Trim('\0').Trim();
                        if (string.IsNullOrWhiteSpace(ssid)) continue;

                        int pct = (int)Math.Clamp(net.wlanSignalQuality, 5u, 100u);
                        int bars = pct >= 78 ? 4 : pct >= 55 ? 3 : pct >= 32 ? 2 : 1;
                        bool isConn = (net.dwFlags & WLAN_AVAILABLE_NETWORK_CONNECTED) != 0 ||
                                      (!string.IsNullOrWhiteSpace(activeSsid) &&
                                       string.Equals(ssid, activeSsid, StringComparison.OrdinalIgnoreCase));
                        bool isSaved = isConn || (net.dwFlags & WLAN_AVAILABLE_NETWORK_HAS_PROFILE) != 0;

                        string sec = MapWlanSecurityText(net.bSecurityEnabled, net.dot11DefaultAuthAlgorithm);
                        var info = new WifiNetworkInfo
                        {
                            Ssid = ssid,
                            SignalPercent = pct,
                            SignalBars = bars,
                            Glyph = ResolveGlyph(true, true, false, bars),
                            SecurityText = sec,
                            IsConnected = isConn,
                            IsSavedProfile = isSaved
                        };

                        if (!bySsid.TryGetValue(ssid, out var existing) ||
                            info.IsConnected && !existing.IsConnected ||
                            info.SignalPercent > existing.SignalPercent)
                        {
                            bySsid[ssid] = info;
                        }
                    }
                }
                finally
                {
                    if (netListPtr != IntPtr.Zero) WlanFreeMemory(netListPtr);
                }
            }

            return bySsid.Values
                .OrderByDescending(r => r.IsConnected)
                .ThenByDescending(r => r.IsSavedProfile)
                .ThenByDescending(r => r.SignalPercent)
                .ToList();
        }
        catch
        {
            return null;
        }
        finally
        {
            if (ifaceListPtr != IntPtr.Zero) WlanFreeMemory(ifaceListPtr);
            if (client != IntPtr.Zero) WlanCloseHandle(client, IntPtr.Zero);
        }
    }

    private static string MapWlanSecurityText(bool securityEnabled, int authAlg)
    {
        if (!securityEnabled) return "Open";
        return authAlg switch
        {
            9 or 10 => "WPA3",
            6 or 7 => "WPA2",
            4 => "WPA2",
            1 or 2 => "Open",
            _ => "WPA2"
        };
    }

    private static bool TryConnectWifiNative(string ssid)
    {
        IntPtr client = IntPtr.Zero;
        IntPtr ifaceListPtr = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out client) != 0 || client == IntPtr.Zero)
                return false;

            if (WlanEnumInterfaces(client, IntPtr.Zero, out ifaceListPtr) != 0 || ifaceListPtr == IntPtr.Zero)
                return false;

            uint ifaceCount = (uint)Marshal.ReadInt32(ifaceListPtr);
            int ifaceSize = Marshal.SizeOf<WLAN_INTERFACE_INFO>();
            int netSize = Marshal.SizeOf<WLAN_AVAILABLE_NETWORK>();

            for (int i = 0; i < ifaceCount; i++)
            {
                var iface = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(IntPtr.Add(ifaceListPtr, 8 + i * ifaceSize));
                Guid ifaceGuid = iface.InterfaceGuid;
                string? matchingProfile = ssid;

                IntPtr netListPtr = IntPtr.Zero;
                try
                {
                    if (WlanGetAvailableNetworkList(client, ref ifaceGuid, 0, IntPtr.Zero, out netListPtr) == 0 &&
                        netListPtr != IntPtr.Zero)
                    {
                        uint netCount = (uint)Marshal.ReadInt32(netListPtr);
                        for (int n = 0; n < netCount; n++)
                        {
                            var net = Marshal.PtrToStructure<WLAN_AVAILABLE_NETWORK>(IntPtr.Add(netListPtr, 8 + n * netSize));
                            int ssidLen = (int)Math.Min(net.dot11Ssid.uSSIDLength, 32u);
                            if (ssidLen <= 0 || net.dot11Ssid.ucSSID == null) continue;
                            string candidateSsid = Encoding.UTF8.GetString(net.dot11Ssid.ucSSID, 0, ssidLen).Trim('\0').Trim();
                            if (string.Equals(candidateSsid, ssid, StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrWhiteSpace(net.strProfileName))
                            {
                                matchingProfile = net.strProfileName;
                                break;
                            }
                        }
                    }
                }
                finally
                {
                    if (netListPtr != IntPtr.Zero) WlanFreeMemory(netListPtr);
                }

                var connParams = new WLAN_CONNECTION_PARAMETERS
                {
                    wlanConnectionMode = WLAN_CONNECTION_MODE_PROFILE,
                    strProfile = matchingProfile,
                    pDot11Ssid = IntPtr.Zero,
                    pDesiredBssidList = IntPtr.Zero,
                    dot11BssType = DOT11_BSS_TYPE_INFRASTRUCTURE,
                    dwFlags = 0
                };

                if (WlanConnect(client, ref ifaceGuid, ref connParams, IntPtr.Zero) == 0)
                    return true;
            }
        }
        catch { }
        finally
        {
            if (ifaceListPtr != IntPtr.Zero) WlanFreeMemory(ifaceListPtr);
            if (client != IntPtr.Zero) WlanCloseHandle(client, IntPtr.Zero);
        }
        return false;
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
        SafeProcessLauncher.TryOpenShellUri(uri);
    }

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
