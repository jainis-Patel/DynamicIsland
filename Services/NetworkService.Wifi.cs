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

internal sealed partial class NetworkService
{
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

}
