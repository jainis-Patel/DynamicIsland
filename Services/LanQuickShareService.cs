using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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
    DateTimeOffset ExpiresAt)
{
    public bool IsExpired => DateTimeOffset.Now >= ExpiresAt;
    public int MinutesRemaining => Math.Max(0, (int)Math.Round((ExpiresAt - DateTimeOffset.Now).TotalMinutes));
}

/// <summary>
/// Autonomous Phone Bridge & LAN QuickShare:
/// Spins up an ephemeral, token-authenticated local HTTP server using pure user-mode <see cref="TcpListener"/>
/// allowing any mobile phone (iOS Safari, Android Chrome) on the same Wi-Fi network to send/receive files
/// and sync clipboard text directly with Dynamic Island—zero app installation and zero Administrator URL ACL required.
/// </summary>
internal sealed class LanQuickShareService : IDisposable
{
    public const int DefaultPort = 8989;

    private readonly Dispatcher _ui;
    private readonly DropShelfService _shelf;
    private readonly ClipboardHistoryService _clipboard;
    private readonly object _lock = new();

    private TcpListener? _tcpListener;
    private CancellationTokenSource? _cts;
    private LanShareSessionInfo? _currentSession;
    private bool _disposed;

    public event Action<LanShareSessionInfo?>? SessionChanged;
    public event Action<string>? FileReceivedFromPhone;
    public event Action<string>? TextReceivedFromPhone;

    public bool IsActive => _tcpListener != null && _currentSession != null && !_currentSession.IsExpired;
    public LanShareSessionInfo? CurrentSession => _currentSession;

    public LanQuickShareService(
        Dispatcher ui,
        DropShelfService shelf,
        ClipboardHistoryService clipboard)
    {
        _ui = ui;
        _shelf = shelf;
        _clipboard = clipboard;
    }

    public static string? GetLocalIpAddress()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var ipProps = ni.GetIPProperties();
                foreach (var addr in ipProps.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ip = addr.Address.ToString();
                        if (!ip.StartsWith("127.") && !ip.StartsWith("169.254."))
                        {
                            return ip;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[LanQuickShare] GetLocalIpAddress error: {ex.Message}");
        }

        return "127.0.0.1";
    }

    public LanShareSessionInfo StartSession(int durationMinutes = 15, int port = DefaultPort)
    {
        StopSession();

        string ip = GetLocalIpAddress() ?? "127.0.0.1";
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

        TcpListener? listener = null;
        int activePort = port;
        SocketException? lastEx = null;

        for (int p = port; p <= port + 10; p++)
        {
            try
            {
                var testListener = new TcpListener(IPAddress.Any, p);
                testListener.Start();
                listener = testListener;
                activePort = p;
                break;
            }
            catch (SocketException ex)
            {
                lastEx = ex;
                App.Log($"[LanQuickShare] Port {p} unavailable ({ex.SocketErrorCode}), probing next port...");
            }
        }

        if (listener == null)
        {
            App.Log($"[LanQuickShare] Failed to bind any port in range {port}..{port + 10}: {lastEx?.Message}");
            throw lastEx ?? new SocketException((int)SocketError.AddressAlreadyInUse);
        }

        string url = $"http://{ip}:{activePort}/?t={token}";
        var session = new LanShareSessionInfo(url, ip, activePort, token, DateTimeOffset.Now.AddMinutes(durationMinutes));

        _currentSession = session;
        _cts = new CancellationTokenSource();
        _tcpListener = listener;

        Task.Run(() => AcceptConnectionsAsync(_tcpListener, _cts.Token));
        _ui.InvokeAsync(() => SessionChanged?.Invoke(session));
        App.Log($"[LanQuickShare] Started TCP HTTP server on 0.0.0.0:{activePort} for token {token}");

        return session;
    }

    public void StopSession()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (_tcpListener != null)
        {
            try { _tcpListener.Stop(); } catch { }
            _tcpListener = null;
        }

        _currentSession = null;
        _ui.InvokeAsync(() => SessionChanged?.Invoke(null));
    }

    private async Task AcceptConnectionsAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => ProcessClientAsync(client, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) break;
                App.Log($"[LanQuickShare] Accept error: {ex.Message}");
            }
        }
    }

    private async Task ProcessClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            try
            {
                stream.ReadTimeout = 15000;
                stream.WriteTimeout = 15000;

                var (requestLine, headers, remainingBody) = await ReadHeadersAsync(stream, ct);
                if (string.IsNullOrWhiteSpace(requestLine)) return;

                string[] reqParts = requestLine.Split(' ', 3);
                if (reqParts.Length < 2) return;

                string method = reqParts[0].ToUpperInvariant();
                string rawUrl = reqParts[1];

                // Parse path and query parameters
                int qIndex = rawUrl.IndexOf('?');
                string path = qIndex >= 0 ? rawUrl[..qIndex] : rawUrl;
                string query = qIndex >= 0 ? rawUrl[(qIndex + 1)..] : "";

                var queryParams = ParseQueryString(query);
                string token = queryParams.GetValueOrDefault("t", "");

                // Token authentication
                if (string.IsNullOrWhiteSpace(token) || _currentSession == null ||
                    !string.Equals(token, _currentSession.Token, StringComparison.Ordinal) ||
                    _currentSession.IsExpired)
                {
                    await WriteResponseAsync(stream, 403, "Forbidden", "text/plain; charset=utf-8", "Access Denied: Invalid or expired pairing token.", ct);
                    return;
                }

                path = path.ToLowerInvariant();
                switch (path)
                {
                    case "/":
                    case "/index.html":
                        string html = GetEmbeddedPwaHtml(_currentSession.Token);
                        await WriteResponseAsync(stream, 200, "OK", "text/html; charset=utf-8", html, ct);
                        break;

                    case "/api/shelf":
                        var items = _shelf.Items.Select(i => new
                        {
                            name = i.FileName,
                            size = i.FileSizeText,
                            path = i.FullPath
                        });
                        string json = JsonSerializer.Serialize(items);
                        await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", json, ct);
                        break;

                    case "/api/clipboard":
                        if (method == "POST")
                        {
                            headers.TryGetValue("Content-Length", out var clStr);
                            int cl = int.TryParse(clStr, out int l) ? l : remainingBody.Length;

                            byte[] bodyBytes = new byte[cl];
                            int copied = Math.Min(remainingBody.Length, cl);
                            if (copied > 0)
                                Array.Copy(remainingBody, 0, bodyBytes, 0, copied);

                            int readTotal = copied;
                            while (readTotal < cl)
                            {
                                int r = await stream.ReadAsync(bodyBytes.AsMemory(readTotal, cl - readTotal), ct);
                                if (r == 0) break;
                                readTotal += r;
                            }

                            try
                            {
                                string bodyText = Encoding.UTF8.GetString(bodyBytes);
                                var doc = JsonDocument.Parse(bodyText);
                                if (doc.RootElement.TryGetProperty("text", out var textProp))
                                {
                                    string clipText = textProp.GetString() ?? "";
                                    if (!string.IsNullOrWhiteSpace(clipText))
                                    {
                                        await _ui.InvokeAsync(() =>
                                        {
                                            System.Windows.Clipboard.SetText(clipText);
                                            TextReceivedFromPhone?.Invoke(clipText);
                                        });
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                App.Log($"[LanQuickShare] Clipboard parse error: {ex.Message}");
                            }

                            await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", "{\"ok\":true}", ct);
                        }
                        else
                        {
                            string clipText = "";
                            await _ui.InvokeAsync(() =>
                            {
                                if (System.Windows.Clipboard.ContainsText())
                                    clipText = System.Windows.Clipboard.GetText();
                            });
                            string clipJson = JsonSerializer.Serialize(new { text = clipText });
                            await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", clipJson, ct);
                        }
                        break;

                    case "/api/upload":
                        string uploadDir = Path.Combine(AppPaths.DataDirectory, "ReceivedFromPhone");
                        Directory.CreateDirectory(uploadDir);

                        string rawName = headers.TryGetValue("X-File-Name", out var xfn)
                            ? Uri.UnescapeDataString(xfn)
                            : $"Upload_{DateTime.Now:yyyyMMdd_HHmmss}.bin";
                        string fileName = Path.GetFileName(rawName);
                        string destPath = Path.Combine(uploadDir, fileName);

                        headers.TryGetValue("Content-Length", out var ulStr);
                        long uploadLen = long.TryParse(ulStr, out long ul) ? ul : -1;

                        using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            if (remainingBody.Length > 0)
                            {
                                int writeFirst = uploadLen > 0 ? (int)Math.Min((long)remainingBody.Length, uploadLen) : remainingBody.Length;
                                await fs.WriteAsync(remainingBody.AsMemory(0, writeFirst), ct);
                            }

                            if (uploadLen > 0)
                            {
                                long remainingBytes = uploadLen - remainingBody.Length;
                                byte[] buf = new byte[65536];
                                while (remainingBytes > 0)
                                {
                                    int toRead = (int)Math.Min((long)buf.Length, remainingBytes);
                                    int r = await stream.ReadAsync(buf.AsMemory(0, toRead), ct);
                                    if (r == 0) break;
                                    await fs.WriteAsync(buf.AsMemory(0, r), ct);
                                    remainingBytes -= r;
                                }
                            }
                            else
                            {
                                await stream.CopyToAsync(fs, ct);
                            }
                        }

                        await _ui.InvokeAsync(() =>
                        {
                            _shelf.AddPaths(new[] { destPath });
                            FileReceivedFromPhone?.Invoke(destPath);
                        });

                        await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", "{\"ok\":true,\"saved\":true}", ct);
                        break;

                    case "/api/download":
                        string dlName = queryParams.GetValueOrDefault("file", "");
                        var dlItem = _shelf.Items.FirstOrDefault(i => string.Equals(i.FileName, dlName, StringComparison.OrdinalIgnoreCase));
                        if (dlItem == null || !File.Exists(dlItem.FullPath))
                        {
                            await WriteResponseAsync(stream, 404, "Not Found", "text/plain; charset=utf-8", "File not found", ct);
                        }
                        else
                        {
                            long fileLen = new FileInfo(dlItem.FullPath).Length;
                            string safeName = Path.GetFileName(dlItem.FullPath);
                            string dlHeader = $"HTTP/1.1 200 OK\r\n" +
                                              $"Content-Type: application/octet-stream\r\n" +
                                              $"Content-Length: {fileLen}\r\n" +
                                              $"Content-Disposition: attachment; filename=\"{safeName}\"\r\n" +
                                              "Access-Control-Allow-Origin: *\r\n" +
                                              "Connection: close\r\n\r\n";
                            byte[] dlHeaderBytes = Encoding.ASCII.GetBytes(dlHeader);
                            await stream.WriteAsync(dlHeaderBytes, ct);
                            using var dlFs = new FileStream(dlItem.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            await dlFs.CopyToAsync(stream, ct);
                            await stream.FlushAsync(ct);
                        }
                        break;

                    default:
                        await WriteResponseAsync(stream, 404, "Not Found", "text/plain; charset=utf-8", "Not Found", ct);
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Log($"[LanQuickShare] ProcessClient error: {ex.Message}");
            }
        }
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
        CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        string header = $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                        $"Content-Type: {contentType}\r\n" +
                        $"Content-Length: {bytes.Length}\r\n" +
                        "Access-Control-Allow-Origin: *\r\n" +
                        "Connection: close\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, ct);
        if (bytes.Length > 0)
        {
            await stream.WriteAsync(bytes, ct);
        }
        await stream.FlushAsync(ct);
    }

    public static string GetEmbeddedPwaHtml(string token)
    {
        return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no">
            <title>Dynamic Island · Phone Bridge</title>
            <style>
                :root { --bg: #000000; --card: #1c1c1e; --accent: #0A84FF; --text: #ffffff; --sub: #8e8e93; }
                body { margin: 0; background: var(--bg); color: var(--text); font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; padding: 20px; display: flex; flex-direction: column; align-items: center; }
                .capsule { width: 140px; height: 32px; background: #111; border-radius: 16px; margin: 10px 0 24px 0; display: flex; align-items: center; justify-content: center; font-size: 11px; font-weight: 600; color: var(--accent); border: 1px solid #333; }
                .card { background: var(--card); border-radius: 18px; padding: 18px; width: 100%; max-width: 400px; box-sizing: border-box; margin-bottom: 16px; border: 1px solid #2c2c2e; }
                h2 { margin: 0 0 12px 0; font-size: 16px; font-weight: 600; display: flex; align-items: center; gap: 8px; }
                button { background: var(--accent); color: white; border: none; border-radius: 12px; padding: 12px 18px; font-size: 14px; font-weight: 600; cursor: pointer; width: 100%; margin-top: 8px; }
                button.secondary { background: #2c2c2e; color: #fff; }
                textarea { width: 100%; height: 80px; background: #0c0c0e; border: 1px solid #333; border-radius: 10px; color: #fff; padding: 10px; box-sizing: border-box; font-size: 14px; resize: none; margin-bottom: 8px; }
                .file-list { margin-top: 10px; }
                .file-item { display: flex; justify-content: space-between; align-items: center; padding: 8px 0; border-bottom: 1px solid #2c2c2e; font-size: 13px; }
                .file-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 220px; }
                .dl-link { color: var(--accent); text-decoration: none; font-weight: 600; }
                #toast { position: fixed; bottom: 20px; background: rgba(48,209,88,0.9); color: black; padding: 10px 20px; border-radius: 20px; font-weight: 600; font-size: 13px; display: none; }
            </style>
        </head>
        <body>
            <div class="capsule">DYNAMIC ISLAND</div>
            <div class="card">
                <h2>📤 Send Files to PC</h2>
                <input type="file" id="filePicker" multiple style="display:none" onchange="uploadFiles(this.files)">
                <button onclick="document.getElementById('filePicker').click()">Select Photos or Files</button>
            </div>
            <div class="card">
                <h2>📋 Sync Clipboard</h2>
                <textarea id="clipText" placeholder="Type or paste text here to push to PC clipboard..."></textarea>
                <button onclick="sendClipboard()">Push Text to PC</button>
                <button class="secondary" style="margin-top:8px" onclick="pullClipboard()">Copy PC Clipboard to Phone</button>
            </div>
            <div class="card">
                <h2>📥 Download from Shelf</h2>
                <div id="shelfList" class="file-list">Loading files...</div>
            </div>
            <div id="toast"></div>
            <script>
                const token = "{{token}}";
                function showToast(msg) {
                    const t = document.getElementById('toast');
                    t.innerText = msg;
                    t.style.display = 'block';
                    setTimeout(() => t.style.display = 'none', 3000);
                }
                async function uploadFiles(files) {
                    if (!files.length) return;
                    for (const f of files) {
                        showToast(`Uploading ${f.name}...`);
                        await fetch(`/api/upload?t=${token}`, {
                            method: 'POST',
                            headers: { 'X-File-Name': encodeURIComponent(f.name) },
                            body: f
                        });
                    }
                    showToast('Transferred to Dynamic Island Shelf!');
                    loadShelf();
                }
                async function sendClipboard() {
                    const text = document.getElementById('clipText').value;
                    if (!text) return;
                    await fetch(`/api/clipboard?t=${token}`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ text })
                    });
                    showToast('Pushed to PC Clipboard!');
                }
                async function pullClipboard() {
                    const res = await fetch(`/api/clipboard?t=${token}`);
                    const data = await res.json();
                    document.getElementById('clipText').value = data.text || '';
                    if (navigator.clipboard) navigator.clipboard.writeText(data.text || '');
                    showToast('Copied from PC Clipboard!');
                }
                async function loadShelf() {
                    try {
                        const res = await fetch(`/api/shelf?t=${token}`);
                        const items = await res.json();
                        const host = document.getElementById('shelfList');
                        if (!items.length) { host.innerHTML = '<span style="color:#666">No files on Drop Shelf</span>'; return; }
                        host.innerHTML = items.map(i => `
                            <div class="file-item">
                                <span class="file-name">${i.name} (${i.size})</span>
                                <a class="dl-link" href="/api/download?t=${token}&file=${encodeURIComponent(i.name)}">Download</a>
                            </div>
                        `).join('');
                    } catch(e) {}
                }
                loadShelf();
            </script>
        </body>
        </html>
        """;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopSession();
    }
}
