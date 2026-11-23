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
}
