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

internal sealed partial class LanQuickShareService
{
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
}
