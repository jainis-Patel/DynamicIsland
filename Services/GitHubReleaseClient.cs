using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

/// <summary>
/// HTTP client dedicated to querying GitHub releases, manifests, and performing resilient downloads.
/// Features ETag conditional caching to bypass GitHub unauthenticated rate limits.
/// </summary>
internal sealed class GitHubReleaseClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly HttpClient _downloadHttp;
    private string? _lastETag;
    private bool _disposed;

    public const string DefaultOwner = "jainis-Patel";
    public const string DefaultRepo = "DynamicIsland";

    public GitHubReleaseClient(HttpClient? client = null)
    {
        _http = client ?? new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(15);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DynamicIsland", "0.4.0"));

        _downloadHttp = new HttpClient();
        _downloadHttp.Timeout = TimeSpan.FromMinutes(10);
        _downloadHttp.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DynamicIsland", "0.4.0"));
    }

    /// <summary>
    /// Clears the cached ETag so the next update check forces a fresh query.
    /// </summary>
    public void ClearETagCache()
    {
        _lastETag = null;
    }

    /// <summary>
    /// Fetches the latest release manifest. Attempts the static raw CDN manifest first to avoid API rate limits.
    /// Returns null if not modified (304) or on network failure.
    /// </summary>
    public async Task<UpdateManifest?> FetchManifestAsync(
        string owner = DefaultOwner,
        string repo = DefaultRepo,
        CancellationToken ct = default)
    {
        // 1. Primary: Fetch raw release-manifest.json from GitHub CDN (Unmetered, no 60 req/hr API rate limit)
        string rawManifestUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/main/release-manifest.json";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, rawManifestUrl);
            request.Headers.Accept.ParseAdd("application/json, text/plain, */*");
            if (!string.IsNullOrEmpty(_lastETag))
            {
                request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(_lastETag));
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                App.Log("[GitHubClient] Manifest not modified (HTTP 304).");
                return null;
            }

            if (response.IsSuccessStatusCode)
            {
                if (response.Headers.ETag != null)
                {
                    _lastETag = response.Headers.ETag.Tag;
                }

                string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (manifest != null && !string.IsNullOrWhiteSpace(manifest.Version))
                {
                    return manifest;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            App.Log($"[GitHubClient] Raw manifest check failed ({ex.Message}), falling back to GitHub API.");
        }

        // 2. Secondary Fallback: Query GitHub Releases API
        return await FetchManifestFromReleasesApiAsync(owner, repo, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Queries the GitHub Releases REST API for the latest release.
    /// Falls back to the latest release in /releases if /releases/latest returns 404 (e.g. pre-releases).
    /// </summary>
    private async Task<UpdateManifest?> FetchManifestFromReleasesApiAsync(string owner, string repo, CancellationToken ct)
    {
        string apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github.v3+json");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                App.Log("[GitHubClient] GitHub API rate limit reached (60 req/hr).");
                return null;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Fallback to /releases?per_page=1 in case all releases are pre-releases
                using var fallbackReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=1");
                fallbackReq.Headers.Accept.ParseAdd("application/vnd.github.v3+json");
                using var fallbackResp = await _http.SendAsync(fallbackReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (fallbackResp.IsSuccessStatusCode)
                {
                    using var fStream = await fallbackResp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var fDoc = await JsonDocument.ParseAsync(fStream, cancellationToken: ct).ConfigureAwait(false);
                    if (fDoc.RootElement.ValueKind == JsonValueKind.Array && fDoc.RootElement.GetArrayLength() > 0)
                    {
                        return ParseReleaseElement(fDoc.RootElement[0]);
                    }
                }
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                App.Log($"[GitHubClient] API check returned HTTP {(int)response.StatusCode}");
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            return ParseReleaseElement(doc.RootElement);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            App.Log($"[GitHubClient] GitHub Releases API query failed: {ex.Message}");
            return null;
        }
    }

    private static UpdateManifest ParseReleaseElement(JsonElement root)
    {
        string tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
        string body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
        string htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? "" : "";
        DateTime publishedAt = root.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTime(out var dt) ? dt : DateTime.UtcNow;

        string cleanVersion = tagName.TrimStart('v', 'V');
        var manifest = new UpdateManifest
        {
            Version = cleanVersion,
            ReleaseTag = tagName,
            Channel = tagName.Contains("beta", StringComparison.OrdinalIgnoreCase) ? "beta" : "stable",
            ReleaseDate = publishedAt,
            Summary = string.IsNullOrWhiteSpace(body) ? $"Dynamic Island {tagName}" : body,
            ChangelogUrl = htmlUrl,
            Downloads = new UpdateDownloads()
        };

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string downloadUrl = asset.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() ?? "" : "";
                long size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;

                if (name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    manifest.Downloads.Installer = new UpdateDownloadAsset
                    {
                        Url = downloadUrl,
                        SizeBytes = size
                    };
                }
                else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    manifest.Downloads.Portable = new UpdateDownloadAsset
                    {
                        Url = downloadUrl,
                        SizeBytes = size
                    };
                }
            }
        }

        return manifest;
    }

    /// <summary>
    /// Fetches the recent release history from GitHub Releases API for the in-app Version Control & Rollback manager.
    /// </summary>
    public async Task<IReadOnlyList<ReleaseHistoryItem>> FetchReleaseHistoryAsync(
        string owner = DefaultOwner,
        string repo = DefaultRepo,
        int maxCount = 5,
        CancellationToken ct = default)
    {
        string apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page={maxCount}";
        var items = new List<ReleaseHistoryItem>();

        try
        {
            using var response = await _http.GetAsync(apiUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return items;

            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            foreach (var release in doc.RootElement.EnumerateArray())
            {
                string tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                string version = tag.TrimStart('v', 'V');
                DateTime publishedAt = release.TryGetProperty("published_at", out var pub) && pub.TryGetDateTime(out var dt) ? dt : DateTime.UtcNow;
                string body = release.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                string htmlUrl = release.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
                bool isPreRelease = release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean();

                string? installerUrl = null;
                long size = 0;

                if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            installerUrl = asset.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() : null;
                            size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                            break;
                        }
                    }
                }

                items.Add(new ReleaseHistoryItem(
                    Version: version,
                    ReleaseTag: tag,
                    ReleaseDate: publishedAt,
                    Summary: body,
                    HtmlUrl: htmlUrl,
                    IsPreRelease: isPreRelease,
                    InstallerUrl: installerUrl,
                    InstallerSha256: null,
                    SizeBytes: size));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            App.Log($"[GitHubClient] Failed to fetch release history: {ex.Message}");
        }

        return items;
    }

    /// <summary>
    /// Resiliently streams a file download to disk with real-time percentage progress and cancellation support.
    /// Automatically deletes the partial destination file if canceled or failed.
    /// </summary>
    public async Task DownloadFileWithProgressAsync(
        string downloadUrl,
        string destinationFilePath,
        IProgress<UpdateProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);

        string? dir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        try
        {
            using var response = await _downloadHttp.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? -1L;

            await using var sourceStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var destinationStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);

            byte[] buffer = new byte[64 * 1024];
            long totalRead = 0;
            int read;

            while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                await destinationStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                totalRead += read;

                if (totalBytes > 0)
                {
                    double percent = Math.Clamp((totalRead * 100.0) / totalBytes, 0.0, 100.0);
                    progress?.Report(new UpdateProgressInfo(
                        Percent: percent,
                        DownloadedBytes: totalRead,
                        TotalBytes: totalBytes,
                        StatusMessage: $"Downloading: {percent:0}% ({totalRead / 1024 / 1024} MB / {totalBytes / 1024 / 1024} MB)"));
                }
                else
                {
                    progress?.Report(new UpdateProgressInfo(
                        Percent: 0,
                        DownloadedBytes: totalRead,
                        TotalBytes: -1,
                        StatusMessage: $"Downloading: {totalRead / 1024 / 1024} MB"));
                }
            }
        }
        catch
        {
            UpdateIntegrityVerifier.TryCleanFile(destinationFilePath);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
        _downloadHttp.Dispose();
    }
}
