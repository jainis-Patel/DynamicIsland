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

    public GitHubReleaseClient(HttpClient? client = null, string? appVersion = null, string? gitHubToken = null)
    {
        string ver = !string.IsNullOrWhiteSpace(appVersion) ? appVersion : InstallerService.AppVersion;
        if (!ProductInfoHeaderValue.TryParse($"DynamicIsland/{ver}", out var productHeader))
        {
            productHeader = new ProductInfoHeaderValue("DynamicIsland", "0.6.0");
        }

        _http = client ?? new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(15);
        _http.DefaultRequestHeaders.UserAgent.Add(productHeader);

        _downloadHttp = new HttpClient();
        _downloadHttp.Timeout = TimeSpan.FromMinutes(10);
        _downloadHttp.DefaultRequestHeaders.UserAgent.Add(productHeader);

        if (!string.IsNullOrWhiteSpace(gitHubToken))
        {
            SetToken(gitHubToken);
        }
    }

    /// <summary>
    /// Updates or clears the GitHub Personal Access Token authorization header.
    /// </summary>
    public void SetToken(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }
        else
        {
            _http.DefaultRequestHeaders.Authorization = null;
        }
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
                    // Pre-flight check: verify that the advertised download asset is reachable and not a 404
                    string? assetUrl = manifest.Downloads?.Installer?.Url ?? manifest.Downloads?.Portable?.Url;
                    if (!string.IsNullOrWhiteSpace(assetUrl))
                    {
                        bool exists = await CheckAssetExistsAsync(assetUrl, ct).ConfigureAwait(false);
                        if (exists)
                        {
                            return manifest;
                        }

                        App.Log($"[GitHubClient] Manifest version {manifest.Version} asset is not yet published on GitHub ({assetUrl}). Falling back to GitHub Releases API.");
                    }
                    else
                    {
                        return manifest;
                    }
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
    /// Verifies that a remote release asset URL is reachable and does not return HTTP 404.
    /// </summary>
    internal async Task<bool> CheckAssetExistsAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var headReq = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await _http.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
            return true;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch
        {
            // For other network issues / timeouts, don't block
            return true;
        }
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
                        return await TryParseOrFetchReleaseAsync(fDoc.RootElement[0], ct).ConfigureAwait(false);
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
            return await TryParseOrFetchReleaseAsync(doc.RootElement, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            App.Log($"[GitHubClient] GitHub Releases API query failed: {ex.Message}");
            return null;
        }
    }

    private async Task<UpdateManifest?> TryParseOrFetchReleaseAsync(JsonElement root, CancellationToken ct)
    {
        string? manifestAssetUrl = null;
        string? checksumsAssetUrl = null;

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (string.Equals(name, "release-manifest.json", StringComparison.OrdinalIgnoreCase))
                {
                    manifestAssetUrl = asset.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() : null;
                }
                else if (string.Equals(name, "checksums.sha256", StringComparison.OrdinalIgnoreCase))
                {
                    checksumsAssetUrl = asset.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() : null;
                }
            }
        }

        // 1. Download the exact release-manifest.json published as a release asset
        if (!string.IsNullOrEmpty(manifestAssetUrl))
        {
            try
            {
                using var mResp = await _http.GetAsync(manifestAssetUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (mResp.IsSuccessStatusCode)
                {
                    string json = await mResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
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
                App.Log($"[GitHubClient] Could not fetch release-manifest.json asset: {ex.Message}");
            }
        }

        // 2. Parse from GitHub Release metadata
        var fallbackManifest = ParseReleaseElement(root);

        // 3. Populate SHA-256 hashes from checksums.sha256 if available
        if (!string.IsNullOrEmpty(checksumsAssetUrl) && fallbackManifest.Downloads.Installer != null)
        {
            try
            {
                string checksums = await _http.GetStringAsync(checksumsAssetUrl, ct).ConfigureAwait(false);
                string? hash = ExtractHashFromChecksumsText(checksums, "DynamicIsland-Setup.exe") ??
                               ExtractHashFromChecksumsText(checksums, ".exe");
                if (!string.IsNullOrEmpty(hash))
                {
                    fallbackManifest.Downloads.Installer.Sha256 = hash;
                }

                if (fallbackManifest.Downloads.Portable != null)
                {
                    string? portHash = ExtractHashFromChecksumsText(checksums, "DynamicIsland-Portable.zip") ??
                                       ExtractHashFromChecksumsText(checksums, ".zip");
                    if (!string.IsNullOrEmpty(portHash))
                    {
                        fallbackManifest.Downloads.Portable.Sha256 = portHash;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                App.Log($"[GitHubClient] Could not parse checksums.sha256 asset: {ex.Message}");
            }
        }

        return fallbackManifest;
    }

    /// <summary>
    /// Parses a sha256 checksums file text to extract the 64-character hash for a specific file name or extension.
    /// </summary>
    public static string? ExtractHashFromChecksumsText(string checksumsContent, string fileNameOrExt)
    {
        if (string.IsNullOrWhiteSpace(checksumsContent)) return null;

        using var reader = new StringReader(checksumsContent);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                string hash = parts[0].Trim();
                string file = parts[1].Trim();
                if (hash.Length == 64 && (file.Contains(fileNameOrExt, StringComparison.OrdinalIgnoreCase) ||
                                          fileNameOrExt.Contains(file, StringComparison.OrdinalIgnoreCase)))
                {
                    return hash;
                }
            }
        }
        return null;
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
                string? checksumsUrl = null;
                string? manifestUrl = null;
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
                        }
                        else if (name.Equals("checksums.sha256", StringComparison.OrdinalIgnoreCase))
                        {
                            checksumsUrl = asset.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() : null;
                        }
                        else if (name.Equals("release-manifest.json", StringComparison.OrdinalIgnoreCase))
                        {
                            manifestUrl = asset.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() : null;
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
                    SizeBytes: size,
                    ChecksumsUrl: checksumsUrl,
                    ManifestUrl: manifestUrl));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            App.Log($"[GitHubClient] Failed to fetch release history: {ex.Message}");
        }

        return items;
    }

    /// <summary>
    /// Resolves the expected cryptographic SHA-256 for a historical release asset by querying
    /// its release-manifest.json, checksums.sha256, or release body.
    /// </summary>
    public async Task<string?> FetchExpectedSha256Async(
        ReleaseHistoryItem item,
        string targetFileName,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(item.InstallerSha256))
        {
            return item.InstallerSha256;
        }

        // 1. Try manifestUrl asset
        if (!string.IsNullOrWhiteSpace(item.ManifestUrl))
        {
            try
            {
                using var resp = await _http.GetAsync(item.ManifestUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    string? hash = manifest?.Downloads?.Installer?.Sha256;
                    if (!string.IsNullOrWhiteSpace(hash)) return hash;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                App.Log($"[GitHubClient] Could not fetch manifest for rollback hash: {ex.Message}");
            }
        }

        // 2. Try checksums.sha256 asset
        if (!string.IsNullOrWhiteSpace(item.ChecksumsUrl))
        {
            try
            {
                string text = await _http.GetStringAsync(item.ChecksumsUrl, ct).ConfigureAwait(false);
                string? hash = ExtractHashFromChecksumsText(text, targetFileName) ?? ExtractHashFromChecksumsText(text, ".exe");
                if (!string.IsNullOrWhiteSpace(hash)) return hash;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                App.Log($"[GitHubClient] Could not fetch checksums for rollback hash: {ex.Message}");
            }
        }

        // 3. Fallback: search body for 64-char hex string
        if (!string.IsNullOrWhiteSpace(item.Summary))
        {
            var match = System.Text.RegularExpressions.Regex.Match(item.Summary, @"\b[0-9a-fA-F]{64}\b");
            if (match.Success)
            {
                return match.Value;
            }
        }

        return null;
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
