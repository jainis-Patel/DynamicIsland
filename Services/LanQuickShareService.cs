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
internal sealed class LanQuickShareService : IDisposable
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
    private bool _disposed;

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
                catch { }

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

            _ = Task.Run(() => AcceptConnectionsAsync(listener, cts));
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

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessClientAsync(client, ct);
                    }
                    finally
                    {
                        try { _connectionGate.Release(); } catch (ObjectDisposedException) { }
                    }
                }, CancellationToken.None);
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

    private async Task ProcessClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            string remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";
            try
            {
                using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                headerCts.CancelAfter(HeaderAndApiTimeout);

                var (requestLine, headers, remainingBody) = await ReadHeadersAsync(stream, headerCts.Token);
                if (string.IsNullOrWhiteSpace(requestLine)) return;

                string[] reqParts = requestLine.Split(' ', 3);
                if (reqParts.Length < 2) return;

                string method = reqParts[0].ToUpperInvariant();
                string rawUrl = reqParts[1];

                // Parse path and query parameters
                int qIndex = rawUrl.IndexOf('?');
                string path = (qIndex >= 0 ? rawUrl[..qIndex] : rawUrl).ToLowerInvariant();
                string query = qIndex >= 0 ? rawUrl[(qIndex + 1)..] : "";

                if (IsIpLockedOut(remoteIp, out int retryAfterSec))
                {
                    if (path.StartsWith("/api/", StringComparison.Ordinal))
                    {
                        string lockJson = JsonSerializer.Serialize(new
                        {
                            ok = false,
                            lockedOut = true,
                            retryAfterSeconds = retryAfterSec,
                            error = $"Too many failed attempts. Try again in {retryAfterSec}s."
                        });
                        await WriteResponseAsync(stream, 429, "Too Many Requests", "application/json; charset=utf-8", lockJson, headerCts.Token, $"Retry-After: {retryAfterSec}\r\n");
                    }
                    else
                    {
                        await WriteResponseAsync(stream, 429, "Too Many Requests", "text/plain; charset=utf-8", "Too many failed pairing attempts. Try again later.", headerCts.Token, $"Retry-After: {retryAfterSec}\r\n");
                    }
                    return;
                }

                LanShareSessionInfo? sessionSnapshot;
                byte[]? saltSnapshot;
                byte[]? digestSnapshot;
                lock (_lock)
                {
                    sessionSnapshot = _currentSession;
                    saltSnapshot = _passwordSalt;
                    digestSnapshot = _passwordDigest;
                }

                if (sessionSnapshot == null || sessionSnapshot.IsExpired)
                {
                    if (path.StartsWith("/api/", StringComparison.Ordinal))
                    {
                        await WriteResponseAsync(stream, 403, "Forbidden", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Session expired or stopped on PC.\",\"expired\":true}", headerCts.Token);
                    }
                    else
                    {
                        await WriteResponseAsync(stream, 403, "Forbidden", "text/plain; charset=utf-8", "Access Denied: Invalid or expired pairing session.", headerCts.Token);
                    }
                    return;
                }

                var queryParams = ParseQueryString(query);
                var cookies = ParseCookies(headers.GetValueOrDefault("Cookie"));
                string pairingToken = headers.TryGetValue("X-DI-Token", out var headerToken) && !string.IsNullOrWhiteSpace(headerToken)
                    ? headerToken.Trim()
                    : queryParams.GetValueOrDefault("t", "");
                string authToken = headers.TryGetValue("X-DI-Auth", out var headerAuth) && !string.IsNullOrWhiteSpace(headerAuth)
                    ? headerAuth.Trim()
                    : !string.IsNullOrWhiteSpace(cookies.GetValueOrDefault("di_auth"))
                        ? cookies["di_auth"]
                        : queryParams.GetValueOrDefault("a", "");
                string userAgent = headers.GetValueOrDefault("User-Agent", "");

                if (path is "/" or "/index.html")
                {
                    if (method != "GET")
                    {
                        await WriteResponseAsync(stream, 405, "Method Not Allowed", "text/plain; charset=utf-8", "Method Not Allowed", headerCts.Token);
                        return;
                    }

                    if (sessionSnapshot.RequiresPassword)
                    {
                        if (!string.IsNullOrWhiteSpace(pairingToken) && !ConstantTimeTokenEquals(pairingToken, sessionSnapshot.Token))
                        {
                            RecordAuthFailure(remoteIp, MaxAuthFailuresBeforeLockout);
                            await WriteResponseAsync(stream, 403, "Forbidden", "text/plain; charset=utf-8", "Access Denied: Invalid pairing token.", headerCts.Token);
                            return;
                        }

                        bool alreadyUnlocked = !string.IsNullOrWhiteSpace(authToken) && _authorizedClients.ContainsKey(authToken);
                        string safeToken = ConstantTimeTokenEquals(pairingToken, sessionSnapshot.Token) ? sessionSnapshot.Token : "";
                        string pwaHtml = GetEmbeddedPwaHtml(safeToken, requirePassword: !alreadyUnlocked, pcName: Environment.MachineName, passwordProtected: true);
                        await WriteResponseAsync(stream, 200, "OK", "text/html; charset=utf-8", pwaHtml, headerCts.Token);
                        return;
                    }

                    if (!ConstantTimeTokenEquals(pairingToken, sessionSnapshot.Token))
                    {
                        RecordAuthFailure(remoteIp, MaxAuthFailuresBeforeLockout);
                        await WriteResponseAsync(stream, 403, "Forbidden", "text/plain; charset=utf-8", "Access Denied: Invalid or expired pairing token.", headerCts.Token);
                        return;
                    }

                    _authFailures.TryRemove(remoteIp, out _);
                    string html = GetEmbeddedPwaHtml(sessionSnapshot.Token, requirePassword: false, pcName: Environment.MachineName, passwordProtected: false);
                    await WriteResponseAsync(stream, 200, "OK", "text/html; charset=utf-8", html, headerCts.Token);
                    return;
                }

                if (path == "/api/auth")
                {
                    if (method != "POST")
                    {
                        await WriteResponseAsync(stream, 405, "Method Not Allowed", "text/plain; charset=utf-8", "Method Not Allowed", headerCts.Token);
                        return;
                    }

                    if (!sessionSnapshot.RequiresPassword)
                    {
                        if (!ConstantTimeTokenEquals(pairingToken, sessionSnapshot.Token))
                        {
                            RecordAuthFailure(remoteIp, MaxAuthFailuresBeforeLockout);
                            await WriteResponseAsync(stream, 403, "Forbidden", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Invalid pairing token\"}", headerCts.Token);
                            return;
                        }

                        string openJson = JsonSerializer.Serialize(new
                        {
                            ok = true,
                            requiresPassword = false,
                            pcName = Environment.MachineName,
                            expiresAtMs = sessionSnapshot.ExpiresAt.ToUnixTimeMilliseconds()
                        });
                        await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", openJson, headerCts.Token);
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(pairingToken) && !ConstantTimeTokenEquals(pairingToken, sessionSnapshot.Token))
                    {
                        RecordAuthFailure(remoteIp, MaxAuthFailuresBeforeLockout);
                        await WriteResponseAsync(stream, 403, "Forbidden", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Invalid pairing token\"}", headerCts.Token);
                        return;
                    }

                    long authLenLong = remainingBody.Length;
                    if (headers.TryGetValue("Content-Length", out var aclStr))
                    {
                        if (!long.TryParse(aclStr, NumberStyles.None, CultureInfo.InvariantCulture, out authLenLong))
                        {
                            await WriteResponseAsync(stream, 400, "Bad Request", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Invalid Content-Length\"}", headerCts.Token);
                            return;
                        }
                        if (authLenLong > MaxAuthPayloadBytes)
                        {
                            await WriteResponseAsync(stream, 413, "Payload Too Large", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Payload too large\"}", headerCts.Token);
                            return;
                        }
                    }
                    else if (authLenLong > MaxAuthPayloadBytes)
                    {
                        authLenLong = MaxAuthPayloadBytes;
                    }

                    int authCl = (int)authLenLong;
                    byte[] authBodyBytes = new byte[authCl];
                    int authCopied = Math.Min(remainingBody.Length, authCl);
                    if (authCopied > 0) Array.Copy(remainingBody, 0, authBodyBytes, 0, authCopied);
                    int authRead = authCopied;
                    while (authRead < authCl)
                    {
                        int r = await stream.ReadAsync(authBodyBytes.AsMemory(authRead, authCl - authRead), headerCts.Token);
                        if (r == 0) break;
                        authRead += r;
                    }

                    string? providedPassword;
                    try
                    {
                        using var authDoc = JsonDocument.Parse(Encoding.UTF8.GetString(authBodyBytes, 0, authRead));
                        providedPassword = authDoc.RootElement.TryGetProperty("password", out var pwProp) && pwProp.ValueKind == JsonValueKind.String
                            ? pwProp.GetString()
                            : null;
                    }
                    catch (JsonException)
                    {
                        await WriteResponseAsync(stream, 400, "Bad Request", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Invalid JSON\"}", headerCts.Token);
                        return;
                    }

                    if (!ConstantTimePasswordEquals(providedPassword, saltSnapshot, digestSnapshot))
                    {
                        await Task.Delay(250, headerCts.Token);
                        var (failCount, lockedUntil) = RecordAuthFailure(remoteIp, MaxPasswordFailuresBeforeLockout);
                        if (lockedUntil > DateTimeOffset.UtcNow)
                        {
                            int lockSec = Math.Max(1, (int)Math.Ceiling((lockedUntil - DateTimeOffset.UtcNow).TotalSeconds));
                            string lockoutJson = JsonSerializer.Serialize(new
                            {
                                ok = false,
                                locked = true,
                                lockedOut = true,
                                retryAfterSeconds = lockSec,
                                error = $"Too many incorrect passwords. Locked for {Math.Max(1, lockSec / 60)}m."
                            });
                            await WriteResponseAsync(stream, 429, "Too Many Requests", "application/json; charset=utf-8", lockoutJson, headerCts.Token, $"Retry-After: {lockSec}\r\n");
                        }
                        else
                        {
                            int remainingAttempts = Math.Max(0, MaxPasswordFailuresBeforeLockout - failCount);
                            string wrongJson = JsonSerializer.Serialize(new
                            {
                                ok = false,
                                locked = true,
                                attemptsRemaining = remainingAttempts,
                                error = remainingAttempts > 0
                                    ? $"Incorrect password ({remainingAttempts} attempt{(remainingAttempts == 1 ? "" : "s")} remaining)"
                                    : "Incorrect password"
                            });
                            await WriteResponseAsync(stream, 401, "Unauthorized", "application/json; charset=utf-8", wrongJson, headerCts.Token);
                        }
                        return;
                    }

                    _authFailures.TryRemove(remoteIp, out _);
                    string newAuthToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                    string deviceLabel = ParseUserAgentDeviceLabel(userAgent);
                    var nowUtc = DateTimeOffset.UtcNow;
                    _authorizedClients[newAuthToken] = new LanConnectedClient(newAuthToken[..12], remoteIp, deviceLabel, nowUtc, nowUtc);
                    NotifyConnectedClientsChanged();

                    string successJson = JsonSerializer.Serialize(new
                    {
                        ok = true,
                        authToken = newAuthToken,
                        pcName = Environment.MachineName,
                        expiresAtMs = sessionSnapshot.ExpiresAt.ToUnixTimeMilliseconds(),
                        deviceLabel,
                        passwordProtected = true
                    });
                    await WriteResponseAsync(
                        stream,
                        200,
                        "OK",
                        "application/json; charset=utf-8",
                        successJson,
                        headerCts.Token,
                        $"Set-Cookie: di_auth={newAuthToken}; Path=/; HttpOnly; SameSite=Strict\r\n");
                    return;
                }

                if (path == "/api/logout")
                {
                    if (method != "POST")
                    {
                        await WriteResponseAsync(stream, 405, "Method Not Allowed", "text/plain; charset=utf-8", "Method Not Allowed", headerCts.Token);
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(authToken) && _authorizedClients.TryRemove(authToken, out _))
                    {
                        NotifyConnectedClientsChanged();
                    }

                    await WriteResponseAsync(
                        stream,
                        200,
                        "OK",
                        "application/json; charset=utf-8",
                        "{\"ok\":true}",
                        headerCts.Token,
                        "Set-Cookie: di_auth=; Path=/; Max-Age=0; HttpOnly; SameSite=Strict\r\n");
                    return;
                }

                // Protected API routes
                if (sessionSnapshot.RequiresPassword)
                {
                    if (string.IsNullOrWhiteSpace(authToken) || !_authorizedClients.TryGetValue(authToken, out var authClient))
                    {
                        await WriteResponseAsync(stream, 401, "Unauthorized", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Password required\",\"locked\":true}", headerCts.Token);
                        return;
                    }

                    _authorizedClients[authToken] = authClient with { LastSeenAt = DateTimeOffset.UtcNow };
                }
                else
                {
                    if (!ConstantTimeTokenEquals(pairingToken, sessionSnapshot.Token))
                    {
                        RecordAuthFailure(remoteIp, MaxAuthFailuresBeforeLockout);
                        await WriteResponseAsync(stream, 403, "Forbidden", "text/plain; charset=utf-8", "Access Denied: Invalid or expired pairing token.", headerCts.Token);
                        return;
                    }

                    _authFailures.TryRemove(remoteIp, out _);
                    if (path == "/api/status")
                    {
                        TouchTokenClient(remoteIp, userAgent);
                    }
                }

                switch (path)
                {
                    case "/api/status":
                        if (method != "GET")
                        {
                            await WriteResponseAsync(stream, 405, "Method Not Allowed", "text/plain; charset=utf-8", "Method Not Allowed", headerCts.Token);
                            break;
                        }
                        int shelfCount = await _ui.InvokeAsync(() => _shelf.Count, DispatcherPriority.Normal, headerCts.Token);
                        int remSec = Math.Max(0, (int)Math.Floor((sessionSnapshot.ExpiresAt - DateTimeOffset.Now).TotalSeconds));
                        string statusJson = JsonSerializer.Serialize(new
                        {
                            ok = true,
                            pcName = Environment.MachineName,
                            expiresAtMs = sessionSnapshot.ExpiresAt.ToUnixTimeMilliseconds(),
                            remainingSeconds = remSec,
                            shelfCount,
                            passwordProtected = sessionSnapshot.RequiresPassword,
                            connectedClients = ConnectedClientCount
                        });
                        await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", statusJson, headerCts.Token);
                        break;

                    case "/api/shelf":
                        if (method != "GET")
                        {
                            await WriteResponseAsync(stream, 405, "Method Not Allowed", "text/plain; charset=utf-8", "Method Not Allowed", headerCts.Token);
                            break;
                        }
                        var items = await _ui.InvokeAsync(() => _shelf.Items.Select(i =>
                        {
                            long sizeBytes = 0;
                            try { if (File.Exists(i.FullPath)) sizeBytes = new FileInfo(i.FullPath).Length; } catch { }
                            return new
                            {
                                name = i.FileName,
                                size = i.FileSizeText,
                                sizeBytes,
                                ext = i.ExtensionTag,
                                isImage = i.IsImage && GetImagePreviewContentType(i.FullPath) != null,
                                isPinned = i.IsPinned
                            };
                        }).ToList(), DispatcherPriority.Normal, headerCts.Token);
                        string json = JsonSerializer.Serialize(items);
                        await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", json, headerCts.Token);
                        break;

                    case "/api/clipboard/history":
                        if (method != "GET")
                        {
                            await WriteResponseAsync(stream, 405, "Method Not Allowed", "text/plain; charset=utf-8", "Method Not Allowed", headerCts.Token);
                            break;
                        }
                        var historyItems = await _ui.InvokeAsync(() => _clipboard.Items
                            .Where(i => !i.IsSecret && !string.IsNullOrWhiteSpace(i.RawText) && !ClipboardHistoryService.DetectSecret(i.RawText!, out _))
                            .Take(15)
                            .Select(i => new
                            {
                                id = i.Id,
                                type = i.Type.ToString(),
                                tag = i.TypeTag,
                                title = i.Title,
                                text = i.RawText,
                                time = i.TimeAgo,
                                color = i.SwatchColor.HasValue
                                    ? $"#{i.SwatchColor.Value.R:X2}{i.SwatchColor.Value.G:X2}{i.SwatchColor.Value.B:X2}"
                                    : null,
                                isPinned = i.IsPinned
                            }).ToList(), DispatcherPriority.Normal, headerCts.Token);
                        string histJson = JsonSerializer.Serialize(historyItems);
                        await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", histJson, headerCts.Token);
                        break;

                    case "/api/open-url":
                        if (method != "POST")
                        {
                            await WriteResponseAsync(stream, 405, "Method Not Allowed", "text/plain; charset=utf-8", "Method Not Allowed", headerCts.Token);
                            break;
                        }
                        {
                            long urlLenLong = remainingBody.Length;
                            if (headers.TryGetValue("Content-Length", out var uclStr))
                            {
                                if (!long.TryParse(uclStr, NumberStyles.None, CultureInfo.InvariantCulture, out urlLenLong))
                                {
                                    await WriteResponseAsync(stream, 400, "Bad Request", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Invalid Content-Length\"}", headerCts.Token);
                                    break;
                                }
                                if (urlLenLong > MaxOpenUrlPayloadBytes)
                                {
                                    await WriteResponseAsync(stream, 413, "Payload Too Large", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Payload too large\"}", headerCts.Token);
                                    break;
                                }
                            }
                            else if (urlLenLong > MaxOpenUrlPayloadBytes)
                            {
                                urlLenLong = MaxOpenUrlPayloadBytes;
                            }

                            int urlCl = (int)urlLenLong;
                            byte[] urlBodyBytes = new byte[urlCl];
                            int urlCopied = Math.Min(remainingBody.Length, urlCl);
                            if (urlCopied > 0) Array.Copy(remainingBody, 0, urlBodyBytes, 0, urlCopied);
                            int urlRead = urlCopied;
                            while (urlRead < urlCl)
                            {
                                int r = await stream.ReadAsync(urlBodyBytes.AsMemory(urlRead, urlCl - urlRead), headerCts.Token);
                                if (r == 0) break;
                                urlRead += r;
                            }

                            try
                            {
                                using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(urlBodyBytes, 0, urlRead));
                                string? targetUrl = doc.RootElement.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String
                                    ? urlProp.GetString()
                                    : null;

                                if (!IsSafeHttpUrl(targetUrl, out var parsedUri) || parsedUri == null)
                                {
                                    await WriteResponseAsync(stream, 400, "Bad Request", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Only http and https URLs are allowed\"}", headerCts.Token);
                                    break;
                                }

                                using var proc = Process.Start(new ProcessStartInfo(parsedUri.AbsoluteUri) { UseShellExecute = true });
                                await _ui.InvokeAsync(() => TextReceivedFromPhone?.Invoke(parsedUri.AbsoluteUri), DispatcherPriority.Normal, headerCts.Token);
                                await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", "{\"ok\":true}", headerCts.Token);
                            }
                            catch (JsonException)
                            {
                                await WriteResponseAsync(stream, 400, "Bad Request", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Invalid JSON\"}", headerCts.Token);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                App.Log($"[LanQuickShare] Open URL error: {ex.Message}");
                                await WriteResponseAsync(stream, 500, "Internal Server Error", "application/json; charset=utf-8", "{\"ok\":false}", headerCts.Token);
                            }
                        }
                        break;

                    case "/api/clipboard":
                        if (method == "POST")
                        {
                            long clLong = remainingBody.Length;
                            if (headers.TryGetValue("Content-Length", out var clStr))
                            {
                                if (!long.TryParse(clStr, NumberStyles.None, CultureInfo.InvariantCulture, out clLong))
                                {
                                    await WriteResponseAsync(stream, 400, "Bad Request", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Invalid Content-Length\"}", headerCts.Token);
                                    break;
                                }
                                if (clLong > MaxClipboardPayloadBytes)
                                {
                                    await WriteResponseAsync(stream, 413, "Payload Too Large", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Clipboard payload too large\"}", headerCts.Token);
                                    break;
                                }
                            }
                            else if (clLong > MaxClipboardPayloadBytes)
                            {
                                clLong = MaxClipboardPayloadBytes;
                            }

                            int cl = (int)clLong;
                            byte[] bodyBytes = new byte[cl];
                            int copied = Math.Min(remainingBody.Length, cl);
                            if (copied > 0)
                                Array.Copy(remainingBody, 0, bodyBytes, 0, copied);

                            int readTotal = copied;
                            while (readTotal < cl)
                            {
                                int r = await stream.ReadAsync(bodyBytes.AsMemory(readTotal, cl - readTotal), headerCts.Token);
                                if (r == 0) break;
                                readTotal += r;
                            }

                            try
                            {
                                string bodyText = Encoding.UTF8.GetString(bodyBytes, 0, readTotal);
                                using var doc = JsonDocument.Parse(bodyText);
                                if (doc.RootElement.TryGetProperty("text", out var textProp) &&
                                    textProp.ValueKind == JsonValueKind.String)
                                {
                                    string clipText = textProp.GetString() ?? "";
                                    if (!string.IsNullOrWhiteSpace(clipText))
                                    {
                                        await _ui.InvokeAsync(() =>
                                        {
                                            System.Windows.Clipboard.SetText(clipText);
                                            TextReceivedFromPhone?.Invoke(clipText);
                                        }, DispatcherPriority.Normal, headerCts.Token);
                                    }
                                }
                            }
                            catch (JsonException ex)
                            {
                                App.Log($"[LanQuickShare] Clipboard JSON error: {ex.Message}");
                                await WriteResponseAsync(stream, 400, "Bad Request", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Invalid JSON\"}", headerCts.Token);
                                break;
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                App.Log($"[LanQuickShare] Clipboard update error: {ex.Message}");
                                await WriteResponseAsync(stream, 500, "Internal Server Error", "application/json; charset=utf-8", "{\"ok\":false}", headerCts.Token);
                                break;
                            }

                            await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", "{\"ok\":true}", headerCts.Token);
                        }
                        else if (method == "GET")
                        {
                            bool sensitiveOrExcluded = false;
                            string clipText = "";
                            await _ui.InvokeAsync(() =>
                            {
                                try
                                {
                                    var dataObj = System.Windows.Clipboard.GetDataObject();
                                    if (ClipboardHistoryService.ShouldExcludeClipboardData(dataObj))
                                    {
                                        sensitiveOrExcluded = true;
                                        return;
                                    }

                                    if (System.Windows.Clipboard.ContainsText())
                                    {
                                        string candidate = System.Windows.Clipboard.GetText();
                                        if (ClipboardHistoryService.DetectSecret(candidate, out _))
                                        {
                                            sensitiveOrExcluded = true;
                                            return;
                                        }
                                        clipText = candidate;
                                    }
                                }
                                catch (System.Runtime.InteropServices.COMException)
                                {
                                    sensitiveOrExcluded = true;
                                }
                            }, DispatcherPriority.Normal, headerCts.Token);

                            if (sensitiveOrExcluded)
                            {
                                await WriteResponseAsync(stream, 403, "Forbidden", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Clipboard content is protected.\"}", headerCts.Token);
                                break;
                            }

                            string clipJson = JsonSerializer.Serialize(new { text = clipText });
                            await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", clipJson, headerCts.Token);
                        }
                        else
                        {
                            await WriteResponseAsync(stream, 405, "Method Not Allowed", "text/plain; charset=utf-8", "Method Not Allowed", headerCts.Token);
                        }
                        break;

                    case "/api/upload":
                        if (method != "POST")
                        {
                            await WriteResponseAsync(stream, 405, "Method Not Allowed", "text/plain; charset=utf-8", "Method Not Allowed", headerCts.Token);
                            break;
                        }

                        long uploadLen = -1;
                        if (headers.TryGetValue("Content-Length", out var ulStr))
                        {
                            if (!long.TryParse(ulStr, NumberStyles.None, CultureInfo.InvariantCulture, out uploadLen))
                            {
                                await WriteResponseAsync(stream, 400, "Bad Request", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Invalid Content-Length\"}", headerCts.Token);
                                break;
                            }
                            if (uploadLen > MaxUploadBytes)
                            {
                                await WriteResponseAsync(stream, 413, "Payload Too Large", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"File exceeds maximum allowed size\"}", headerCts.Token);
                                break;
                            }
                        }

                        string uploadDir = Path.Combine(AppPaths.DataDirectory, "ReceivedFromPhone");
                        Directory.CreateDirectory(uploadDir);

                        headers.TryGetValue("X-File-Name", out var xfn);
                        var (destPath, createdStream) = OpenUniqueUploadFile(uploadDir, xfn);

                        bool uploadSuccess = false;
                        try
                        {
                            using var uploadTotalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            uploadTotalCts.CancelAfter(UploadMaxDuration);

                            using (createdStream)
                            {
                                long totalWritten = 0;
                                if (remainingBody.Length > 0)
                                {
                                    int writeFirst = uploadLen >= 0
                                        ? (int)Math.Min(remainingBody.Length, uploadLen)
                                        : remainingBody.Length;
                                    if (writeFirst > 0)
                                    {
                                        if (totalWritten + writeFirst > MaxUploadBytes)
                                            throw new InvalidDataException("PAYLOD_TOO_LARGE");
                                        await createdStream.WriteAsync(remainingBody.AsMemory(0, writeFirst), uploadTotalCts.Token);
                                        totalWritten += writeFirst;
                                    }
                                }

                                if (uploadLen >= 0)
                                {
                                    long remainingBytes = uploadLen - totalWritten;
                                    byte[] buf = new byte[65536];
                                    while (remainingBytes > 0)
                                    {
                                        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(uploadTotalCts.Token);
                                        idleCts.CancelAfter(UploadIdleTimeout);

                                        int toRead = (int)Math.Min(buf.Length, remainingBytes);
                                        int r = await stream.ReadAsync(buf.AsMemory(0, toRead), idleCts.Token);
                                        if (r == 0)
                                            throw new EndOfStreamException("Truncated upload stream.");
                                        await createdStream.WriteAsync(buf.AsMemory(0, r), uploadTotalCts.Token);
                                        remainingBytes -= r;
                                        totalWritten += r;
                                    }
                                }
                                else
                                {
                                    byte[] buf = new byte[65536];
                                    while (true)
                                    {
                                        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(uploadTotalCts.Token);
                                        idleCts.CancelAfter(UploadIdleTimeout);

                                        int r = await stream.ReadAsync(buf.AsMemory(0, buf.Length), idleCts.Token);
                                        if (r == 0) break;
                                        if (totalWritten + r > MaxUploadBytes)
                                            throw new InvalidDataException("PAYLOAD_TOO_LARGE");
                                        await createdStream.WriteAsync(buf.AsMemory(0, r), uploadTotalCts.Token);
                                        totalWritten += r;
                                    }
                                }

                                await createdStream.FlushAsync(uploadTotalCts.Token);
                            }

                            uploadSuccess = true;
                        }
                        catch (InvalidDataException ex) when (ex.Message.Contains("TOO_LARGE", StringComparison.Ordinal))
                        {
                            try { File.Delete(destPath); } catch { }
                            await WriteResponseAsync(stream, 413, "Payload Too Large", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"File exceeds maximum allowed size\"}", ct);
                            break;
                        }
                        catch (EndOfStreamException)
                        {
                            try { File.Delete(destPath); } catch { }
                            await WriteResponseAsync(stream, 400, "Bad Request", "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Incomplete upload stream\"}", ct);
                            break;
                        }
                        catch
                        {
                            try { File.Delete(destPath); } catch { }
                            throw;
                        }

                        if (uploadSuccess)
                        {
                            await _ui.InvokeAsync(() =>
                            {
                                _shelf.AddPaths(new[] { destPath });
                                FileReceivedFromPhone?.Invoke(destPath);
                            }, DispatcherPriority.Normal, ct);

                            await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", "{\"ok\":true,\"saved\":true}", ct);
                        }
                        break;

                    case "/api/preview":
                        if (method != "GET")
                        {
                            await WriteResponseAsync(stream, 405, "Method Not Allowed", "text/plain; charset=utf-8", "Method Not Allowed", headerCts.Token);
                            break;
                        }
                        {
                            string pvName = queryParams.GetValueOrDefault("file", "");
                            string? pvFullPath = await _ui.InvokeAsync(() =>
                                _shelf.Items.FirstOrDefault(i => string.Equals(i.FileName, pvName, StringComparison.OrdinalIgnoreCase))?.FullPath,
                                DispatcherPriority.Normal,
                                headerCts.Token);

                            string? contentType = pvFullPath != null ? GetImagePreviewContentType(pvFullPath) : null;
                            if (string.IsNullOrWhiteSpace(pvFullPath) || contentType == null || !File.Exists(pvFullPath))
                            {
                                await WriteResponseAsync(stream, 404, "Not Found", "text/plain; charset=utf-8", "Preview unavailable", headerCts.Token);
                                break;
                            }

                            var pvInfo = new FileInfo(pvFullPath);
                            if (pvInfo.Length > MaxPreviewBytes)
                            {
                                await WriteResponseAsync(stream, 413, "Payload Too Large", "text/plain; charset=utf-8", "Image too large for inline preview", headerCts.Token);
                                break;
                            }

                            using var pvFs = new FileStream(pvFullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            string pvHeader = $"HTTP/1.1 200 OK\r\n" +
                                              $"Content-Type: {contentType}\r\n" +
                                              $"Content-Length: {pvFs.Length}\r\n" +
                                              "Content-Disposition: inline\r\n" +
                                              "X-Content-Type-Options: nosniff\r\n" +
                                              "Cache-Control: private, max-age=60\r\n" +
                                              "Connection: close\r\n\r\n";
                            await stream.WriteAsync(Encoding.ASCII.GetBytes(pvHeader), headerCts.Token);

                            using var pvCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            pvCts.CancelAfter(UploadMaxDuration);
                            await StreamFileSliceAsync(pvFs, stream, 0, pvFs.Length, pvCts.Token);
                            await stream.FlushAsync(headerCts.Token);
                        }
                        break;

                    case "/api/download":
                        if (method != "GET")
                        {
                            await WriteResponseAsync(stream, 405, "Method Not Allowed", "text/plain; charset=utf-8", "Method Not Allowed", headerCts.Token);
                            break;
                        }

                        string dlName = queryParams.GetValueOrDefault("file", "");
                        string? dlFullPath = await _ui.InvokeAsync(() =>
                            _shelf.Items.FirstOrDefault(i => string.Equals(i.FileName, dlName, StringComparison.OrdinalIgnoreCase))?.FullPath,
                            DispatcherPriority.Normal,
                            headerCts.Token);

                        if (string.IsNullOrWhiteSpace(dlFullPath) || !File.Exists(dlFullPath))
                        {
                            await WriteResponseAsync(stream, 404, "Not Found", "text/plain; charset=utf-8", "File not found", headerCts.Token);
                        }
                        else
                        {
                            using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            downloadCts.CancelAfter(UploadMaxDuration);

                            using var dlFs = new FileStream(dlFullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            long fileLen = dlFs.Length;
                            string safeName = SanitizeContentDispositionFileName(Path.GetFileName(dlFullPath));

                            if (headers.TryGetValue("Range", out var rangeHeader) && !string.IsNullOrWhiteSpace(rangeHeader))
                            {
                                if (!TryParseByteRange(rangeHeader, fileLen, out long rangeStart, out long rangeEnd))
                                {
                                    string invalidRangeHdr = $"HTTP/1.1 416 Range Not Satisfiable\r\n" +
                                                             $"Content-Range: bytes */{fileLen}\r\n" +
                                                             "Content-Length: 0\r\n" +
                                                             "Connection: close\r\n\r\n";
                                    await stream.WriteAsync(Encoding.ASCII.GetBytes(invalidRangeHdr), headerCts.Token);
                                    await stream.FlushAsync(headerCts.Token);
                                    break;
                                }

                                long sliceLen = rangeEnd - rangeStart + 1;
                                string partialHeader = $"HTTP/1.1 206 Partial Content\r\n" +
                                                       "Content-Type: application/octet-stream\r\n" +
                                                       "Accept-Ranges: bytes\r\n" +
                                                       $"Content-Range: bytes {rangeStart}-{rangeEnd}/{fileLen}\r\n" +
                                                       $"Content-Length: {sliceLen}\r\n" +
                                                       $"Content-Disposition: attachment; filename=\"{safeName}\"\r\n" +
                                                       "X-Content-Type-Options: nosniff\r\n" +
                                                       "Cache-Control: no-store\r\n" +
                                                       "Connection: close\r\n\r\n";
                                await stream.WriteAsync(Encoding.ASCII.GetBytes(partialHeader), headerCts.Token);
                                await StreamFileSliceAsync(dlFs, stream, rangeStart, sliceLen, downloadCts.Token);
                                await stream.FlushAsync(headerCts.Token);
                            }
                            else
                            {
                                string dlHeader = $"HTTP/1.1 200 OK\r\n" +
                                                  "Content-Type: application/octet-stream\r\n" +
                                                  "Accept-Ranges: bytes\r\n" +
                                                  $"Content-Length: {fileLen}\r\n" +
                                                  $"Content-Disposition: attachment; filename=\"{safeName}\"\r\n" +
                                                  "X-Content-Type-Options: nosniff\r\n" +
                                                  "Cache-Control: no-store\r\n" +
                                                  "Connection: close\r\n\r\n";
                                await stream.WriteAsync(Encoding.ASCII.GetBytes(dlHeader), headerCts.Token);
                                await StreamFileSliceAsync(dlFs, stream, 0, fileLen, downloadCts.Token);
                                await stream.FlushAsync(headerCts.Token);
                            }
                        }
                        break;

                    case "/api/download-all":
                        if (method != "GET")
                        {
                            await WriteResponseAsync(stream, 405, "Method Not Allowed", "text/plain; charset=utf-8", "Method Not Allowed", headerCts.Token);
                            break;
                        }
                        {
                            var shelfPaths = await _ui.InvokeAsync(() =>
                                _shelf.Items.Select(i => i.FullPath).ToList(),
                                DispatcherPriority.Normal,
                                headerCts.Token);

                            var validFiles = new List<FileInfo>();
                            long totalBytes = 0;
                            foreach (var p in shelfPaths)
                            {
                                try
                                {
                                    if (File.Exists(p))
                                    {
                                        var fi = new FileInfo(p);
                                        totalBytes += fi.Length;
                                        if (totalBytes > MaxZipBundleBytes)
                                        {
                                            await WriteResponseAsync(stream, 413, "Payload Too Large", "text/plain; charset=utf-8", "Shelf exceeds 500 MB ZIP bundle limit", headerCts.Token);
                                            validFiles.Clear();
                                            totalBytes = -1;
                                            break;
                                        }
                                        validFiles.Add(fi);
                                    }
                                }
                                catch { }
                            }

                            if (totalBytes == -1) break;
                            if (validFiles.Count == 0)
                            {
                                await WriteResponseAsync(stream, 404, "Not Found", "text/plain; charset=utf-8", "No downloadable files on shelf", headerCts.Token);
                                break;
                            }

                            string tempZipPath = Path.Combine(Path.GetTempPath(), $"di_shelf_{Guid.NewGuid():N}.zip");
                            try
                            {
                                using (var zipFs = new FileStream(tempZipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                                using (var archive = new ZipArchive(zipFs, ZipArchiveMode.Create, leaveOpen: false))
                                {
                                    var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    foreach (var fi in validFiles)
                                    {
                                        string entryName = fi.Name;
                                        if (!usedNames.Add(entryName))
                                        {
                                            string stem = Path.GetFileNameWithoutExtension(entryName);
                                            string ext = Path.GetExtension(entryName);
                                            int n = 1;
                                            do { entryName = $"{stem} ({n++}){ext}"; } while (!usedNames.Add(entryName));
                                        }
                                        archive.CreateEntryFromFile(fi.FullName, entryName, CompressionLevel.Fastest);
                                    }
                                }

                                using var zipRead = new FileStream(tempZipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                                string zipHeader = $"HTTP/1.1 200 OK\r\n" +
                                                   "Content-Type: application/zip\r\n" +
                                                   $"Content-Length: {zipRead.Length}\r\n" +
                                                   "Content-Disposition: attachment; filename=\"DynamicIsland-Shelf.zip\"\r\n" +
                                                   "X-Content-Type-Options: nosniff\r\n" +
                                                   "Cache-Control: no-store\r\n" +
                                                   "Connection: close\r\n\r\n";
                                await stream.WriteAsync(Encoding.ASCII.GetBytes(zipHeader), headerCts.Token);

                                using var zipCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                                zipCts.CancelAfter(UploadMaxDuration);
                                await StreamFileSliceAsync(zipRead, stream, 0, zipRead.Length, zipCts.Token);
                                await stream.FlushAsync(headerCts.Token);
                            }
                            finally
                            {
                                try { if (File.Exists(tempZipPath)) File.Delete(tempZipPath); } catch { }
                            }
                        }
                        break;

                    default:
                        await WriteResponseAsync(stream, 404, "Not Found", "text/plain; charset=utf-8", "Not Found", headerCts.Token);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // Request timeout or server stopped
            }
            catch (Exception ex)
            {
                App.Log($"[LanQuickShare] ProcessClient error: {ex.Message}");
            }
        }
    }

    private void NotifyConnectedClientsChanged()
    {
        var clients = GetConnectedClients();
        _ = _ui.InvokeAsync(() => ConnectedClientsChanged?.Invoke(clients.Count, clients));
    }

    private void TouchTokenClient(string remoteIp, string userAgent)
    {
        var now = DateTimeOffset.UtcNow;
        bool isNew = !_recentTokenClients.ContainsKey(remoteIp);
        string label = ParseUserAgentDeviceLabel(userAgent);
        _recentTokenClients.AddOrUpdate(
            remoteIp,
            _ => new LanConnectedClient(remoteIp, remoteIp, label, now, now),
            (_, existing) => existing with
            {
                DeviceLabel = label != "Web Browser" ? label : existing.DeviceLabel,
                LastSeenAt = now
            });

        if (isNew)
        {
            NotifyConnectedClientsChanged();
        }
    }

    private bool IsIpLockedOut(string remoteIp, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        if (!_authFailures.TryGetValue(remoteIp, out var entry))
            return false;

        var now = DateTimeOffset.UtcNow;
        if (entry.LockedUntil > now)
        {
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((entry.LockedUntil - now).TotalSeconds));
            return true;
        }

        if (entry.LockedUntil != DateTimeOffset.MinValue && entry.LockedUntil <= now)
        {
            _authFailures.TryRemove(remoteIp, out _);
        }
        return false;
    }

    private (int Count, DateTimeOffset LockedUntil) RecordAuthFailure(string remoteIp, int maxFailures = MaxAuthFailuresBeforeLockout)
    {
        var now = DateTimeOffset.UtcNow;
        if (_authFailures.Count > 256)
        {
            foreach (var kv in _authFailures)
            {
                if (kv.Value.LockedUntil != DateTimeOffset.MinValue && kv.Value.LockedUntil <= now)
                    _authFailures.TryRemove(kv.Key, out _);
            }
        }

        return _authFailures.AddOrUpdate(
            remoteIp,
            _ =>
            {
                DateTimeOffset lockedUntil = maxFailures <= 1 ? now.Add(AuthLockoutDuration) : DateTimeOffset.MinValue;
                return (1, lockedUntil);
            },
            (_, prev) =>
            {
                if (prev.LockedUntil > now)
                    return prev;

                int nextCount = (prev.LockedUntil != DateTimeOffset.MinValue ? 0 : prev.Count) + 1;
                DateTimeOffset lockedUntil = nextCount >= maxFailures
                    ? now.Add(AuthLockoutDuration)
                    : DateTimeOffset.MinValue;
                return (nextCount, lockedUntil);
            });
    }

    private static bool ConstantTimeTokenEquals(string? provided, string? expected)
    {
        if (string.IsNullOrWhiteSpace(provided) || string.IsNullOrWhiteSpace(expected))
            return false;

        byte[] providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }

    private static bool ConstantTimePasswordEquals(string? provided, byte[]? salt, byte[]? expectedDigest)
    {
        if (string.IsNullOrEmpty(provided) || salt == null || expectedDigest == null)
            return false;

        string candidate = provided.Trim();
        if (candidate.Length == 0 || candidate.Length > MaxPasswordLength)
            return false;

        byte[] providedDigest = HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(candidate));
        return CryptographicOperations.FixedTimeEquals(providedDigest, expectedDigest);
    }

    private static Dictionary<string, string> ParseCookies(string? cookieHeader)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(cookieHeader))
            return result;

        string[] parts = cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string name = part[..eq].Trim();
            string val = part[(eq + 1)..].Trim();
            if (!string.IsNullOrEmpty(name))
                result[name] = val;
        }
        return result;
    }

    internal static string ParseUserAgentDeviceLabel(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua))
            return "Web Browser";

        string lower = ua.ToLowerInvariant();
        string device = lower.Contains("iphone", StringComparison.Ordinal) ? "iPhone"
            : lower.Contains("ipad", StringComparison.Ordinal) ? "iPad"
            : lower.Contains("android", StringComparison.Ordinal) ? "Android"
            : lower.Contains("windows", StringComparison.Ordinal) ? "Windows"
            : lower.Contains("macintosh", StringComparison.Ordinal) || lower.Contains("mac os x", StringComparison.Ordinal) ? "Mac"
            : string.Empty;

        string browser = lower.Contains("edg", StringComparison.Ordinal) ? "Edge"
            : lower.Contains("crios", StringComparison.Ordinal) || lower.Contains("chrome", StringComparison.Ordinal) ? "Chrome"
            : lower.Contains("fxios", StringComparison.Ordinal) || lower.Contains("firefox", StringComparison.Ordinal) ? "Firefox"
            : lower.Contains("safari", StringComparison.Ordinal) ? "Safari"
            : string.Empty;

        if (!string.IsNullOrEmpty(device) && !string.IsNullOrEmpty(browser))
            return $"{device} · {browser}";
        if (!string.IsNullOrEmpty(device))
            return device;
        if (!string.IsNullOrEmpty(browser))
            return browser;
        return "Web Browser";
    }

    private static (string DestPath, FileStream Stream) OpenUniqueUploadFile(string uploadDir, string? rawHeaderFileName)
    {
        string canonicalRoot = Path.GetFullPath(uploadDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        string decoded = "";
        if (!string.IsNullOrWhiteSpace(rawHeaderFileName))
        {
            try { decoded = Uri.UnescapeDataString(rawHeaderFileName); }
            catch { decoded = rawHeaderFileName; }
        }

        string filePart = Path.GetFileName(decoded.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
        char[] invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(filePart.Length);
        foreach (char c in filePart)
        {
            if (c >= 32 && Array.IndexOf(invalidChars, c) < 0 && c != '/' && c != '\\')
                sb.Append(c);
            else
                sb.Append('_');
        }

        string sanitized = sb.ToString().Trim().TrimEnd('.');
        string stem = Path.GetFileNameWithoutExtension(sanitized);
        string ext = Path.GetExtension(sanitized);

        if (string.IsNullOrWhiteSpace(sanitized) || sanitized == "." || sanitized == ".." ||
            string.IsNullOrWhiteSpace(stem) || ReservedWindowsNames.Contains(stem))
        {
            stem = $"Upload_{DateTime.Now:yyyyMMdd_HHmmss}";
            ext = string.IsNullOrWhiteSpace(ext) ? ".bin" : ext;
        }

        if (ext.Length > 24) ext = ext[..24];
        if (stem.Length > 150) stem = stem[..150].TrimEnd('.');
        if (string.IsNullOrWhiteSpace(stem)) stem = "Upload";

        for (int attempt = 0; attempt < 1000; attempt++)
        {
            string candidateName = attempt == 0 ? $"{stem}{ext}" : $"{stem} ({attempt}){ext}";
            string candidatePath = Path.GetFullPath(Path.Combine(canonicalRoot, candidateName));
            if (!candidatePath.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Resolved upload path escaped destination directory.");

            try
            {
                var fs = new FileStream(candidatePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                return (candidatePath, fs);
            }
            catch (IOException) when (File.Exists(candidatePath))
            {
                continue;
            }
        }

        string fallbackPath = Path.GetFullPath(Path.Combine(canonicalRoot, $"{stem}_{Guid.NewGuid():N}{ext}"));
        if (!fallbackPath.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Resolved upload path escaped destination directory.");
        var fallbackStream = new FileStream(fallbackPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        return (fallbackPath, fallbackStream);
    }

    private static string SanitizeContentDispositionFileName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (c is >= (char)32 and <= (char)126 && c != '"' && c != '\\' && c != '/' && c != ';')
                sb.Append(c);
            else
                sb.Append('_');
        }
        return sb.Length > 0 ? sb.ToString() : "download.bin";
    }

    private static async Task<(string RequestLine, Dictionary<string, string> Headers, byte[] RemainingBody)> ReadHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        byte[] buffer = new byte[8192];
        int totalRead = 0;
        int headerEnd = -1;

        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0) break;
            totalRead += read;

            // Check for \r\n\r\n (13, 10, 13, 10)
            for (int i = 0; i < totalRead - 3; i++)
            {
                if (buffer[i] == 13 && buffer[i + 1] == 10 && buffer[i + 2] == 13 && buffer[i + 3] == 10)
                {
                    headerEnd = i;
                    break;
                }
            }
            if (headerEnd >= 0) break;
        }

        if (headerEnd < 0) return ("", new Dictionary<string, string>(), Array.Empty<byte>());

        string headerText = Encoding.UTF8.GetString(buffer, 0, headerEnd);
        string[] lines = headerText.Split("\r\n");
        string requestLine = lines[0];

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            int colon = line.IndexOf(':');
            if (colon > 0)
            {
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        int bodyStartIndex = headerEnd + 4;
        int remainingLength = totalRead - bodyStartIndex;
        byte[] remaining = remainingLength > 0 ? buffer[bodyStartIndex..totalRead] : Array.Empty<byte>();

        return (requestLine, headers, remaining);
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query)) return result;

        string[] pairs = query.Split('&');
        foreach (var pair in pairs)
        {
            int eq = pair.IndexOf('=');
            if (eq > 0)
            {
                string key = Uri.UnescapeDataString(pair[..eq]);
                string val = Uri.UnescapeDataString(pair[(eq + 1)..]);
                result[key] = val;
            }
        }
        return result;
    }

    private static async Task WriteResponseAsync(
        Stream stream,
        int statusCode,
        string statusText,
        string contentType,
        string body,
        CancellationToken ct,
        string? extraHeaders = null)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        string safeExtra = string.IsNullOrEmpty(extraHeaders)
            ? string.Empty
            : (extraHeaders.EndsWith("\r\n", StringComparison.Ordinal) ? extraHeaders : extraHeaders + "\r\n");
        string header = $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                        $"Content-Type: {contentType}\r\n" +
                        $"Content-Length: {bytes.Length}\r\n" +
                        "X-Content-Type-Options: nosniff\r\n" +
                        "Cache-Control: no-store\r\n" +
                        safeExtra +
                        "Connection: close\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, ct);
        if (bytes.Length > 0)
        {
            await stream.WriteAsync(bytes, ct);
        }
        await stream.FlushAsync(ct);
    }

    private static string? GetImagePreviewContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => null
        };
    }

    public static bool IsSafeHttpUrl(string? rawUrl, out Uri? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(rawUrl))
            return false;

        string trimmed = rawUrl.Trim();
        if (trimmed.Length > 2048 || trimmed.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            return false;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        parsed = uri;
        return true;
    }

    internal static bool TryParseByteRange(string? rangeHeader, long fileLength, out long start, out long end)
    {
        start = 0;
        end = 0;
        if (string.IsNullOrWhiteSpace(rangeHeader) || fileLength <= 0)
            return false;

        string trimmed = rangeHeader.Trim();
        if (!trimmed.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            return false;

        string spec = trimmed["bytes=".Length..].Trim();
        // Only handle single range (reject multi-range)
        if (spec.Contains(','))
            return false;

        int dash = spec.IndexOf('-');
        if (dash < 0)
            return false;

        string startPart = spec[..dash].Trim();
        string endPart = spec[(dash + 1)..].Trim();

        if (string.IsNullOrEmpty(startPart))
        {
            // Suffix range: bytes=-N
            if (!long.TryParse(endPart, NumberStyles.None, CultureInfo.InvariantCulture, out long suffix) || suffix <= 0)
                return false;
            start = Math.Max(0, fileLength - suffix);
            end = fileLength - 1;
            return true;
        }

        if (!long.TryParse(startPart, NumberStyles.None, CultureInfo.InvariantCulture, out start) || start >= fileLength)
            return false;

        if (string.IsNullOrEmpty(endPart))
        {
            end = fileLength - 1;
            return true;
        }

        if (!long.TryParse(endPart, NumberStyles.None, CultureInfo.InvariantCulture, out end) || end < start)
            return false;

        if (end >= fileLength)
            end = fileLength - 1;

        return true;
    }

    private static async Task StreamFileSliceAsync(FileStream fileStream, Stream targetStream, long offset, long length, CancellationToken ct)
    {
        if (offset > 0)
            fileStream.Seek(offset, SeekOrigin.Begin);

        byte[] buf = new byte[65536];
        long remaining = length;
        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buf.Length, remaining);
            int read = await fileStream.ReadAsync(buf.AsMemory(0, toRead), ct);
            if (read <= 0) break;

            using var writeIdleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            writeIdleCts.CancelAfter(UploadIdleTimeout);
            await targetStream.WriteAsync(buf.AsMemory(0, read), writeIdleCts.Token);
            remaining -= read;
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
