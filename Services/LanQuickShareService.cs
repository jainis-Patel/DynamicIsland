using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

public sealed record LanShareSessionInfo(
    string Url,
    string LocalIp,
    int Port,
    string Token,
    DateTimeOffset ExpiresAt,
    bool RequiresPassword = false,
    string? ManualUrl = null)
{
    public bool IsExpired => DateTimeOffset.Now >= ExpiresAt;
    public int MinutesRemaining => Math.Max(0, (int)Math.Round((ExpiresAt - DateTimeOffset.Now).TotalMinutes));
    public string DisplayAddress => ManualUrl ?? $"http://{LocalIp}:{Port}";
}

public sealed record LanConnectedClient(
    string Id,
    string RemoteIp,
    string DeviceLabel,
    DateTimeOffset ConnectedAt,
    DateTimeOffset LastSeenAt);

/// <summary>
/// Autonomous Phone Bridge & LAN QuickShare:
/// Spins up an ephemeral, token- and optionally password-authenticated local HTTP server using pure user-mode <see cref="TcpListener"/>
/// allowing any mobile phone (iOS Safari, Android Chrome) on the same Wi-Fi network to send/receive files
/// and sync clipboard text directly with Dynamic Island—zero app installation and zero Administrator URL ACL required.
/// </summary>
internal sealed partial class LanQuickShareService : IDisposable
{
    public const int DefaultPort = 8989;
    private const int MaxConcurrentConnections = 16;
    private const int MaxClipboardPayloadBytes = 1_048_576; // 1 MB
    private const int MaxOpenUrlPayloadBytes = 8_192;
    private const int MaxAuthPayloadBytes = 4_096;
    private const int MaxPasswordLength = 128;
    private const long MaxUploadBytes = 2L * 1024 * 1024 * 1024; // 2 GB
    private const long MaxPreviewBytes = 25L * 1024 * 1024; // 25 MB
    private const long MaxZipBundleBytes = 500L * 1024 * 1024; // 500 MB
    private const int MaxAuthFailuresBeforeLockout = 10;
    private const int MaxPasswordFailuresBeforeLockout = 5;
    private static readonly Lazy<string> EmbeddedPwaTemplate = new(LoadEmbeddedPwaTemplate);
    private static readonly TimeSpan AuthLockoutDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HeaderAndApiTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan UploadIdleTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UploadMaxDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TokenClientStaleTimeout = TimeSpan.FromMinutes(3);

    private static readonly string[] VirtualAdapterHints =
    {
        "vethernet", "wsl", "hyper-v", "vmware", "virtualbox", "docker",
        "bluetooth", "loopback", "tap", "npcap", "zerotier", "tailscale", "vpn"
    };

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly Dispatcher _ui;
    private readonly DropShelfService _shelf;
    private readonly ClipboardHistoryService _clipboard;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _connectionGate = new(MaxConcurrentConnections, MaxConcurrentConnections);
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset LockedUntil)> _authFailures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LanConnectedClient> _authorizedClients = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LanConnectedClient> _recentTokenClients = new(StringComparer.Ordinal);

    private TcpListener? _tcpListener;
    private CancellationTokenSource? _cts;
    private LanShareSessionInfo? _currentSession;
    private byte[]? _passwordSalt;
    private byte[]? _passwordDigest;
    private byte[] _trustedTokenKey = RandomNumberGenerator.GetBytes(32);
    private bool _disposed;

    public string IssueTrustedDeviceToken(TimeSpan ttl)
    {
        long exp = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();
        byte[] keySnapshot;
        lock (_lock)
        {
            keySnapshot = _trustedTokenKey;
        }
        string expStr = exp.ToString(CultureInfo.InvariantCulture);
        byte[] sig = HMACSHA256.HashData(keySnapshot, Encoding.UTF8.GetBytes(expStr));
        return $"{expStr}.{Convert.ToHexString(sig).ToLowerInvariant()}";
    }

    public bool ValidateTrustedDeviceToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        int dot = token.IndexOf('.');
        if (dot <= 0 || dot >= token.Length - 1) return false;

        if (!long.TryParse(token[..dot], NumberStyles.None, CultureInfo.InvariantCulture, out long exp))
            return false;

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp)
            return false;

        byte[] keySnapshot;
        lock (_lock)
        {
            keySnapshot = _trustedTokenKey;
        }

        try
        {
            byte[] providedSig = Convert.FromHexString(token[(dot + 1)..]);
            byte[] expectedSig = HMACSHA256.HashData(keySnapshot, Encoding.UTF8.GetBytes(token[..dot]));
            return providedSig.Length == expectedSig.Length &&
                   CryptographicOperations.FixedTimeEquals(providedSig, expectedSig);
        }
        catch
        {
            return false;
        }
    }

    public void RevokeTrustedDevices()
    {
        lock (_lock)
        {
            _trustedTokenKey = RandomNumberGenerator.GetBytes(32);
        }
        _authorizedClients.Clear();
        NotifyConnectedClientsChanged();
    }

    public event Action<LanShareSessionInfo?>? SessionChanged;
    public event Action<string>? FileReceivedFromPhone;
    public event Action<string>? TextReceivedFromPhone;
    public event Action<int, IReadOnlyList<LanConnectedClient>>? ConnectedClientsChanged;

    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return !_disposed && _tcpListener != null && _currentSession != null && !_currentSession.IsExpired;
            }
        }
    }

    public LanShareSessionInfo? CurrentSession
    {
        get
        {
            lock (_lock)
            {
                return _currentSession;
            }
        }
    }

    public LanQuickShareService(
        Dispatcher ui,
        DropShelfService shelf,
        ClipboardHistoryService clipboard)
    {
        _ui = ui;
        _shelf = shelf;
        _clipboard = clipboard;
    }

    public IReadOnlyList<LanConnectedClient> GetConnectedClients()
    {
        LanShareSessionInfo? snapshot;
        lock (_lock)
        {
            snapshot = _currentSession;
        }

        if (_disposed || snapshot == null || snapshot.IsExpired)
            return Array.Empty<LanConnectedClient>();

        var now = DateTimeOffset.UtcNow;
        if (snapshot.RequiresPassword)
        {
            return _authorizedClients.Values
                .OrderByDescending(c => c.LastSeenAt)
                .ToList();
        }

        foreach (var kv in _recentTokenClients)
        {
            if (now - kv.Value.LastSeenAt > TokenClientStaleTimeout)
                _recentTokenClients.TryRemove(kv.Key, out _);
        }

        return _recentTokenClients.Values
            .OrderByDescending(c => c.LastSeenAt)
            .ToList();
    }

    public int ConnectedClientCount => GetConnectedClients().Count;

    public static string GeneratePasscode(int digits = 6)
    {
        int len = Math.Clamp(digits, 4, 16);
        Span<char> chars = stackalloc char[len];
        for (int i = 0; i < len; i++)
        {
            chars[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        }
        return new string(chars);
    }

    public static string? GetLocalIpAddress()
    {
        try
        {
            string? bestIp = null;
            int bestScore = int.MinValue;

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var ipProps = ni.GetIPProperties();
                bool hasGateway = false;
                try
                {
                    hasGateway = ipProps.GatewayAddresses.Any(g =>
                        g.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.Any.Equals(g.Address));
                }
                catch (Exception ex) { App.LogDebug("LanQuickShare.GatewayCheck", ex); }

                string adapterMeta = $"{ni.Name} {ni.Description}".ToLowerInvariant();
                bool isVirtualAdapter = VirtualAdapterHints.Any(h => adapterMeta.Contains(h, StringComparison.Ordinal));

                int ifaceScore = ni.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 => 40,
                    NetworkInterfaceType.Ethernet => 35,
                    _ => 0
                };
                if (hasGateway) ifaceScore += 50;
                if (isVirtualAdapter) ifaceScore -= 100;

                foreach (var addr in ipProps.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    string ip = addr.Address.ToString();
                    if (ip.StartsWith("127.", StringComparison.Ordinal) ||
                        ip.StartsWith("169.254.", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int score = ifaceScore;
                    if (ip.StartsWith("192.168.", StringComparison.Ordinal))
                        score += 25;
                    else if (ip.StartsWith("10.", StringComparison.Ordinal))
                        score += 20;
                    else if (IsRfc1918_172(addr.Address))
                        score += 10;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIp = ip;
                    }
                }
            }

            if (!string.IsNullOrEmpty(bestIp))
                return bestIp;
        }
        catch (Exception ex)
        {
            App.Log($"[LanQuickShare] GetLocalIpAddress error: {ex.Message}");
        }

        return "127.0.0.1";
    }

    private static bool IsRfc1918_172(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }

    public LanShareSessionInfo StartSession(int durationMinutes = 15, int port = DefaultPort, string? password = null)
    {
        LanShareSessionInfo session;
        TcpListener listener;
        CancellationTokenSource cts;
        int activePort = port;

        lock (_lock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LanQuickShareService));

            StopSessionLocked(notify: false);
            _authFailures.Clear();
            _authorizedClients.Clear();
            _recentTokenClients.Clear();

            string trimmedPass = password?.Trim() ?? string.Empty;
            if (trimmedPass.Length > MaxPasswordLength)
                trimmedPass = trimmedPass[..MaxPasswordLength];

            bool requiresPassword = !string.IsNullOrEmpty(trimmedPass);
            if (requiresPassword)
            {
                _passwordSalt = RandomNumberGenerator.GetBytes(32);
                _passwordDigest = HMACSHA256.HashData(_passwordSalt, Encoding.UTF8.GetBytes(trimmedPass));
            }
            else
            {
                _passwordSalt = null;
                _passwordDigest = null;
            }

            string ip = GetLocalIpAddress() ?? "127.0.0.1";
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

            TcpListener? boundListener = null;
            SocketException? lastEx = null;

            for (int p = port; p <= port + 10; p++)
            {
                try
                {
                    var testListener = new TcpListener(IPAddress.Any, p);
                    testListener.Start();
                    boundListener = testListener;
                    activePort = p;
                    break;
                }
                catch (SocketException ex)
                {
                    lastEx = ex;
                    App.Log($"[LanQuickShare] Port {p} unavailable ({ex.SocketErrorCode}), probing next port...");
                }
            }

            if (boundListener == null)
            {
                _passwordSalt = null;
                _passwordDigest = null;
                App.Log($"[LanQuickShare] Failed to bind any port in range {port}..{port + 10}: {lastEx?.Message}");
                throw lastEx ?? new SocketException((int)SocketError.AddressAlreadyInUse);
            }

            listener = boundListener;
            string manualUrl = $"http://{ip}:{activePort}";
            string url = $"{manualUrl}/?t={token}";
            session = new LanShareSessionInfo(url, ip, activePort, token, DateTimeOffset.Now.AddMinutes(durationMinutes), requiresPassword, manualUrl);

            cts = new CancellationTokenSource();
            if (durationMinutes > 0)
            {
                cts.CancelAfter(TimeSpan.FromMinutes(durationMinutes));
            }

            _currentSession = session;
            _cts = cts;
            _tcpListener = listener;

            Task.Run(() => AcceptConnectionsAsync(listener, cts)).FireAndForget("LanShare.Accept");
        }

        _ui.InvokeAsync(() =>
        {
            SessionChanged?.Invoke(session);
            ConnectedClientsChanged?.Invoke(0, Array.Empty<LanConnectedClient>());
        });
        App.Log($"[LanQuickShare] Started TCP HTTP server on 0.0.0.0:{activePort} (session TTL: {durationMinutes}m, passwordProtected: {session.RequiresPassword}).");

        return session;
    }

    public bool UpdateSessionPassword(string? newPassword)
    {
        LanShareSessionInfo updated;
        lock (_lock)
        {
            if (_disposed || _tcpListener == null || _currentSession == null || _currentSession.IsExpired)
                return false;

            string trimmedPass = newPassword?.Trim() ?? string.Empty;
            if (trimmedPass.Length > MaxPasswordLength)
                trimmedPass = trimmedPass[..MaxPasswordLength];

            bool requiresPassword = !string.IsNullOrEmpty(trimmedPass);
            if (requiresPassword)
            {
                _passwordSalt = RandomNumberGenerator.GetBytes(32);
                _passwordDigest = HMACSHA256.HashData(_passwordSalt, Encoding.UTF8.GetBytes(trimmedPass));
            }
            else
            {
                _passwordSalt = null;
                _passwordDigest = null;
            }

            _authorizedClients.Clear();
            _recentTokenClients.Clear();
            _authFailures.Clear();

            updated = _currentSession with { RequiresPassword = requiresPassword };
            _currentSession = updated;
        }

        _ui.InvokeAsync(() =>
        {
            SessionChanged?.Invoke(updated);
            ConnectedClientsChanged?.Invoke(0, Array.Empty<LanConnectedClient>());
        });
        App.Log($"[LanQuickShare] Updated session security (passwordProtected: {updated.RequiresPassword}).");
        return true;
    }

    public bool ExtendSession(int extraMinutes = 15)
    {
        LanShareSessionInfo updated;
        lock (_lock)
        {
            if (_disposed || _tcpListener == null || _currentSession == null || _cts == null)
                return false;

            DateTimeOffset now = DateTimeOffset.Now;
            DateTimeOffset baseTime = _currentSession.ExpiresAt > now ? _currentSession.ExpiresAt : now;
            DateTimeOffset maxExpiry = now.AddMinutes(180);
            DateTimeOffset newExpiry = baseTime.AddMinutes(Math.Clamp(extraMinutes, 1, 120));
            if (newExpiry > maxExpiry)
                newExpiry = maxExpiry;

            TimeSpan delay = newExpiry - now;
            if (delay <= TimeSpan.Zero)
                delay = TimeSpan.FromMinutes(1);

            try { _cts.CancelAfter(delay); } catch (ObjectDisposedException) { return false; }

            updated = _currentSession with { ExpiresAt = newExpiry };
            _currentSession = updated;
        }

        _ui.InvokeAsync(() => SessionChanged?.Invoke(updated));
        return true;
    }

    public int RevokeAllClients()
    {
        int count = _authorizedClients.Count + _recentTokenClients.Count;
        _authorizedClients.Clear();
        _recentTokenClients.Clear();
        _ui.InvokeAsync(() => ConnectedClientsChanged?.Invoke(0, Array.Empty<LanConnectedClient>()));
        return count;
    }

    public void StopSession()
    {
        bool hadSession;
        lock (_lock)
        {
            hadSession = _tcpListener != null || _currentSession != null || _cts != null;
            StopSessionLocked(notify: false);
        }

        if (hadSession)
        {
            _ui.InvokeAsync(() =>
            {
                SessionChanged?.Invoke(null);
                ConnectedClientsChanged?.Invoke(0, Array.Empty<LanConnectedClient>());
            });
        }
    }

    private void StopSessionLocked(bool notify)
    {
        if (_cts != null)
        {
            try { _cts.Cancel(); } catch { }
            try { _cts.Dispose(); } catch { }
            _cts = null;
        }

        if (_tcpListener != null)
        {
            try { _tcpListener.Stop(); } catch { }
            _tcpListener = null;
        }

        _currentSession = null;
        _passwordSalt = null;
        _passwordDigest = null;
        _authFailures.Clear();
        _authorizedClients.Clear();
        _recentTokenClients.Clear();

        if (notify)
        {
            _ui.InvokeAsync(() =>
            {
                SessionChanged?.Invoke(null);
                ConnectedClientsChanged?.Invoke(0, Array.Empty<LanConnectedClient>());
            });
        }
    }

    private async Task AcceptConnectionsAsync(TcpListener listener, CancellationTokenSource sessionCts)
    {
        CancellationToken ct;
        try
        {
            ct = sessionCts.Token;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _connectionGate.WaitAsync(ct);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }

                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(ct);
                }
                catch
                {
                    try { _connectionGate.Release(); } catch (ObjectDisposedException) { }
                    throw;
                }

                Task.Run(async () =>
                {
                    try
                    {
                        await ProcessClientAsync(client, ct);
                    }
                    finally
                    {
                        try { _connectionGate.Release(); } catch (ObjectDisposedException) { }
                    }
                }, CancellationToken.None).FireAndForget("LanShare.Client");
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                App.Log($"[LanQuickShare] Accept loop ended: {ex.Message}");
            }
        }
        finally
        {
            bool stoppedCurrent = false;
            lock (_lock)
            {
                if (ReferenceEquals(_cts, sessionCts) || ReferenceEquals(_tcpListener, listener))
                {
                    StopSessionLocked(notify: false);
                    stoppedCurrent = true;
                }
                else
                {
                    try { listener.Stop(); } catch { }
                }
            }

            if (stoppedCurrent)
            {
                _ = _ui.InvokeAsync(() =>
                {
                    SessionChanged?.Invoke(null);
                    ConnectedClientsChanged?.Invoke(0, Array.Empty<LanConnectedClient>());
                });
            }
        }
    }

    private static string LoadEmbeddedPwaTemplate()
    {
        const string resourceName = "DynamicIsland.Resources.PhoneBridge.html";
        using var resStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (resStream != null)
        {
            using var reader = new StreamReader(resStream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        App.Log($"[LanQuickShare] Missing embedded resource '{resourceName}', falling back to minimal template.");
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Dynamic Island</title></head><body><div>DYNAMIC ISLAND</div><script>const token=__DI_TOKEN_JSON__;let isLocked=__DI_REQUIRE_PASSWORD__;let passwordProtected=__DI_PASSWORD_PROTECTED__;const initialPcName=__DI_PC_NAME_JSON__;/* /api/auth /api/upload /api/clipboard */</script></body></html>";
    }

    public static string GetEmbeddedPwaHtml(
        string token,
        bool requirePassword = false,
        string? pcName = null,
        bool passwordProtected = false)
    {
        string tokenJson = JsonSerializer.Serialize(token ?? string.Empty);
        string pcNameJson = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(pcName) ? Environment.MachineName : pcName);
        return EmbeddedPwaTemplate.Value
            .Replace("__DI_TOKEN_JSON__", tokenJson, StringComparison.Ordinal)
            .Replace("__DI_REQUIRE_PASSWORD__", requirePassword ? "true" : "false", StringComparison.Ordinal)
            .Replace("__DI_PASSWORD_PROTECTED__", (passwordProtected || requirePassword) ? "true" : "false", StringComparison.Ordinal)
            .Replace("__DI_PC_NAME_JSON__", pcNameJson, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            StopSessionLocked(notify: false);
        }
        try { _connectionGate.Dispose(); } catch { }
    }
}
