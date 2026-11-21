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
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly HashSet<string> AllowedGitHubHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "api.github.com",
        "objects.githubusercontent.com",
        "raw.githubusercontent.com",
        "release-assets.githubusercontent.com",
        "codeload.github.com"
    };

    private readonly HttpClient _http;
    private readonly HttpClient _downloadHttp;
    private readonly bool _ownsHttpClient;
    private string? _lastETag;
    private volatile string? _token;
    private bool _disposed;

    public const string DefaultOwner = "jainis-Patel";
    public const string DefaultRepo = "DynamicIsland";

    public static bool IsTrustedGitHubUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        return AllowedGitHubHosts.Contains(uri.Host);
    }

    public GitHubReleaseClient(HttpClient? client = null, string? appVersion = null, string? gitHubToken = null)
    {
        string ver = !string.IsNullOrWhiteSpace(appVersion) ? appVersion : InstallerService.AppVersion;
        if (!ProductInfoHeaderValue.TryParse($"DynamicIsland/{ver}", out var productHeader))
        {
            productHeader = new ProductInfoHeaderValue("DynamicIsland", InstallerService.AppVersion);
        }

        _ownsHttpClient = client == null;
        string userAgent = productHeader.ToString();
        _http = client ?? SharedHttpClient.CreateClient(TimeSpan.FromSeconds(15), userAgent);
        _downloadHttp = SharedHttpClient.CreateClient(TimeSpan.FromMinutes(10), userAgent);

        if (!string.IsNullOrWhiteSpace(gitHubToken))
        {
            SetToken(gitHubToken);
        }
    }

    /// <summary>
    /// Updates or clears the GitHub Personal Access Token (applied strictly to api.github.com requests).
    /// </summary>
    public void SetToken(string? token)
    {
        _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    }

    private HttpRequestMessage CreateGetRequest(string url, string? accept = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(accept))
        {
            req.Headers.Accept.ParseAdd(accept);
        }

        string? token = _token;
        if (!string.IsNullOrEmpty(token) &&
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return req;
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
        UpdateManifest? rawManifest = null;

        try
        {
            using var request = CreateGetRequest(rawManifestUrl, "application/json, text/plain, */*");
            if (!string.IsNullOrEmpty(_lastETag) && EntityTagHeaderValue.TryParse(_lastETag, out var parsedEtag))
            {
                request.Headers.IfNoneMatch.Add(parsedEtag);
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                App.Log("[GitHubClient] Manifest not modified (HTTP 304).");
                return null;
            }

            if (response.IsSuccessStatusCode)
            {
                string? candidateEtag = response.Headers.ETag?.Tag;

                string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);

                if (manifest != null && !string.IsNullOrWhiteSpace(manifest.Version))
                {
                    // Pre-flight check: verify that the advertised download asset is reachable and not a 404
                    string? assetUrl = manifest.Downloads?.Installer?.Url ?? manifest.Downloads?.Portable?.Url;
                    if (!string.IsNullOrWhiteSpace(assetUrl))
                    {
                        if (!IsTrustedGitHubUrl(assetUrl))
                        {
                            App.Log($"[GitHubClient] Untrusted manifest asset URL rejected: {assetUrl}");
                        }
                        else if (await CheckAssetExistsAsync(assetUrl, ct).ConfigureAwait(false))
                        {
                            if (!string.IsNullOrEmpty(candidateEtag))
                                _lastETag = candidateEtag;
                            rawManifest = manifest;
                        }
                        else
                        {
                            App.Log($"[GitHubClient] Manifest version {manifest.Version} asset is not yet published on GitHub ({assetUrl}). Falling back to GitHub Releases API.");
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(candidateEtag))
                            _lastETag = candidateEtag;
                        rawManifest = manifest;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            App.Log($"[GitHubClient] Raw manifest check failed ({ex.Message}), falling back to GitHub API.");
        }

        // 2. Secondary Check: Query GitHub Releases API so newly tagged GitHub Releases are picked up
        // even if the raw CDN cache is temporarily stale or rate-limited fallback is needed.
        var apiManifest = await FetchManifestFromReleasesApiAsync(owner, repo, ct).ConfigureAwait(false);
        if (rawManifest != null && apiManifest != null)
        {
            return apiManifest.ParsedVersion > rawManifest.ParsedVersion ? apiManifest : rawManifest;
        }
        return apiManifest ?? rawManifest;
    }

    /// <summary>
    /// Verifies that a remote release asset URL is reachable across GitHub release redirects and does not return HTTP 404.
    /// </summary>
    internal async Task<bool> CheckAssetExistsAsync(string url, CancellationToken ct = default)
    {
        if (!IsTrustedGitHubUrl(url))
            return false;

        try
        {
            using var req = CreateGetRequest(url);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
            return resp.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch
        {
            // For transient network issues / timeouts, avoid blocking valid releases
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
            using var request = CreateGetRequest(apiUrl, "application/vnd.github.v3+json");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                App.Log("[GitHubClient] GitHub API rate limit reached (60 req/hr).");
                return null;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Fallback to /releases?per_page=1 in case all releases are pre-releases
                using var fallbackReq = CreateGetRequest($"https://api.github.com/repos/{owner}/{repo}/releases?per_page=1", "application/vnd.github.v3+json");
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
        if (!string.IsNullOrEmpty(manifestAssetUrl) && IsTrustedGitHubUrl(manifestAssetUrl))
        {
            try
            {
                using var mReq = CreateGetRequest(manifestAssetUrl);
                using var mResp = await _http.SendAsync(mReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (mResp.IsSuccessStatusCode)
                {
                    string json = await mResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);
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

        // 3. Populate SHA-256 hashes from checksums.sha256 if available (covers both Installer & Portable-only releases)
        if (!string.IsNullOrEmpty(checksumsAssetUrl) &&
            IsTrustedGitHubUrl(checksumsAssetUrl) &&
            (fallbackManifest.Downloads.Installer != null || fallbackManifest.Downloads.Portable != null))
        {
            try
            {
                using var cReq = CreateGetRequest(checksumsAssetUrl);
                using var cResp = await _http.SendAsync(cReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (cResp.IsSuccessStatusCode)
                {
                    string checksums = await cResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    if (fallbackManifest.Downloads.Installer != null)
                    {
                        string? hash = ExtractHashFromChecksumsText(checksums, "DynamicIsland-Setup.exe") ??
                                       ExtractHashFromChecksumsText(checksums, ".exe");
                        if (!string.IsNullOrEmpty(hash))
                        {
                            fallbackManifest.Downloads.Installer.Sha256 = hash;
                        }
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
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                App.Log($"[GitHubClient] Could not parse checksums.sha256 asset: {ex.Message}");
            }
        }

        return fallbackManifest;
    }

    private static bool IsValidHex64(string hash)
    {
        if (hash.Length != 64) return false;
        foreach (char c in hash)
        {
            if (!Uri.IsHexDigit(c)) return false;
        }
        return true;
    }

    /// <summary>
    /// Parses a sha256 checksums file text to extract the 64-character hex hash for an exact filename or extension.
    /// </summary>
    public static string? ExtractHashFromChecksumsText(string checksumsContent, string fileNameOrExt)
    {
        if (string.IsNullOrWhiteSpace(checksumsContent) || string.IsNullOrWhiteSpace(fileNameOrExt)) return null;

        string target = fileNameOrExt.Trim();
        bool matchByExtension = target.StartsWith('.');
        string targetBaseName = matchByExtension ? target : Path.GetFileName(target);

        using var reader = new StringReader(checksumsContent);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            string hash = parts[0].Trim();
            if (!IsValidHex64(hash)) continue;

            string entryFile = parts[1].Trim().TrimStart('*');
            if (entryFile.StartsWith("./", StringComparison.Ordinal) || entryFile.StartsWith(".\\", StringComparison.Ordinal))
                entryFile = entryFile[2..];

            string entryBaseName = Path.GetFileName(entryFile);
            if (matchByExtension)
            {
                if (entryBaseName.EndsWith(targetBaseName, StringComparison.OrdinalIgnoreCase))
                    return hash;
            }
            else
            {
                if (string.Equals(entryBaseName, targetBaseName, StringComparison.OrdinalIgnoreCase))
                    return hash;
            }
        }
        return null;
    }

    internal static string? TryExtractAssetDigestSha256(JsonElement asset)
    {
        if (asset.TryGetProperty("digest", out var digestProp) && digestProp.GetString() is string raw)
        {
            string trimmed = raw.Trim();
            if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                string hex = trimmed["sha256:".Length..].Trim();
                if (IsValidHex64(hex))
                {
                    return hex.ToLowerInvariant();
                }
            }
            else if (IsValidHex64(trimmed))
            {
                return trimmed.ToLowerInvariant();
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
                string? digestSha = TryExtractAssetDigestSha256(asset);

                if (name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase) ||
                    (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && manifest.Downloads.Installer == null))
                {
                    manifest.Downloads.Installer = new UpdateDownloadAsset
                    {
                        Url = downloadUrl,
                        SizeBytes = size,
                        Sha256 = digestSha ?? ""
                    };
                }
                else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    manifest.Downloads.Portable = new UpdateDownloadAsset
                    {
                        Url = downloadUrl,
                        SizeBytes = size,
                        Sha256 = digestSha ?? ""
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
            using var req = CreateGetRequest(apiUrl, "application/vnd.github.v3+json");
            using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
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
                string? installerSha = null;
                string? checksumsUrl = null;
                string? manifestUrl = null;
                long size = 0;

                if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase) ||
                            (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && installerUrl == null))
                        {
                            installerUrl = asset.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() : null;
                            size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                            installerSha = TryExtractAssetDigestSha256(asset);
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
                    InstallerSha256: installerSha,
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
        if (!string.IsNullOrWhiteSpace(item.InstallerSha256) && IsValidHex64(item.InstallerSha256.Trim()))
        {
            return item.InstallerSha256.Trim();
        }

        // 1. Try manifestUrl asset
        if (!string.IsNullOrWhiteSpace(item.ManifestUrl) && IsTrustedGitHubUrl(item.ManifestUrl))
        {
            try
            {
                using var req = CreateGetRequest(item.ManifestUrl);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);
                    string? hash = manifest?.Downloads?.Installer?.Sha256;
                    if (!string.IsNullOrWhiteSpace(hash) && IsValidHex64(hash.Trim())) return hash.Trim();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                App.Log($"[GitHubClient] Could not fetch manifest for rollback hash: {ex.Message}");
            }
        }

        // 2. Try checksums.sha256 asset
        if (!string.IsNullOrWhiteSpace(item.ChecksumsUrl) && IsTrustedGitHubUrl(item.ChecksumsUrl))
        {
            try
            {
                using var req = CreateGetRequest(item.ChecksumsUrl);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    string text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    string? hash = ExtractHashFromChecksumsText(text, targetFileName);
                    if (string.IsNullOrWhiteSpace(hash) && targetFileName.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                        hash = ExtractHashFromChecksumsText(text, "DynamicIsland-Setup.exe");
                    if (string.IsNullOrWhiteSpace(hash) && targetFileName.Contains("Portable", StringComparison.OrdinalIgnoreCase))
                        hash = ExtractHashFromChecksumsText(text, "DynamicIsland-Portable.zip");
                    hash ??= ExtractHashFromChecksumsText(text, ".exe");
                    if (!string.IsNullOrWhiteSpace(hash)) return hash;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                App.Log($"[GitHubClient] Could not fetch checksums for rollback hash: {ex.Message}");
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

        if (!IsTrustedGitHubUrl(downloadUrl))
        {
            throw new InvalidOperationException($"Refused untrusted download URL: '{downloadUrl}'");
        }

        string? dir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        try
        {
            using var response = await _downloadHttp.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.RequestMessage?.RequestUri != null &&
                !IsTrustedGitHubUrl(response.RequestMessage.RequestUri.ToString()))
            {
                throw new InvalidOperationException($"Refused untrusted redirect URL: '{response.RequestMessage.RequestUri}'");
            }

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
        if (_ownsHttpClient) _http.Dispose();
        _downloadHttp.Dispose();
    }
}
