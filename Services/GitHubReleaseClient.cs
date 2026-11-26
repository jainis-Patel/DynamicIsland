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
    private string? _lastRawETag;
    private UpdateManifest? _cachedRawManifest;
    private string? _lastApiETag;
    private string? _lastApiEndpoint;
    private UpdateManifest? _cachedApiManifest;
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
        _downloadHttp = client ?? SharedHttpClient.CreateClient(TimeSpan.FromMinutes(10), userAgent);

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

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string? accept = null)
    {
        var req = new HttpRequestMessage(method, url);
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

    private HttpRequestMessage CreateGetRequest(string url, string? accept = null) =>
        CreateRequest(HttpMethod.Get, url, accept);

    /// <summary>
    /// Clears all cached ETags and manifests so the next update check forces a fresh query.
    /// </summary>
    public void ClearETagCache()
    {
        _lastRawETag = null;
        _cachedRawManifest = null;
        _lastApiETag = null;
        _lastApiEndpoint = null;
        _cachedApiManifest = null;
    }

    private static DateTime? ParseRateLimitResetUtc(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values))
        {
            foreach (string raw in values)
            {
                if (long.TryParse(raw.Trim(), out long epoch) && epoch > 0)
                {
                    return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
                }
            }
        }
        return null;
    }

    private static string FormatRateLimitMessage(DateTime? resetUtc) =>
        resetUtc.HasValue
            ? $"GitHub API rate limit reached (resets at {resetUtc.Value.ToLocalTime():t}). Add a GitHub token in Settings for 5,000 req/hr."
            : "GitHub API rate limit reached. Add a GitHub token in Settings for 5,000 req/hr.";

    /// <summary>
    /// Fetches the latest release manifest. Attempts the channel-aware GitHub Releases API (with ETag conditional caching)
    /// and falls back to the raw CDN release-manifest.json if the API is rate-limited or unreachable.
    /// </summary>
    public async Task<UpdateManifest?> FetchManifestAsync(
        string owner = DefaultOwner,
        string repo = DefaultRepo,
        CancellationToken ct = default)
    {
        var result = await FetchLatestManifestDetailedAsync(
            includePrereleases: false,
            force: false,
            owner: owner,
            repo: repo,
            ct: ct).ConfigureAwait(false);
        return result.Manifest;
    }

    /// <summary>
    /// Channel-aware, ETag-cached manifest query returning detailed status, rate-limit diagnostics, and source metadata.
    /// </summary>
    public async Task<ManifestFetchResult> FetchLatestManifestDetailedAsync(
        bool includePrereleases = false,
        bool force = false,
        string owner = DefaultOwner,
        string repo = DefaultRepo,
        CancellationToken ct = default)
    {
        if (force)
        {
            ClearETagCache();
        }

        // 1. Primary: Channel-aware GitHub Releases API (conditional ETag 304 responses do not count against rate limit)
        var apiOutcome = await FetchFromReleasesApiDetailedAsync(includePrereleases, owner, repo, ct).ConfigureAwait(false);
        if (apiOutcome.Manifest != null)
        {
            return apiOutcome;
        }

        // 2. Fallback: Raw CDN release-manifest.json (unmetered, bypasses API rate limits)
        var rawOutcome = await FetchRawManifestDetailedAsync(includePrereleases, owner, repo, ct).ConfigureAwait(false);
        if (rawOutcome.Manifest != null)
        {
            return rawOutcome;
        }

        if (apiOutcome.Status == ManifestFetchStatus.RateLimited)
        {
            return apiOutcome;
        }

        if (apiOutcome.Status == ManifestFetchStatus.NoReleases && rawOutcome.Status == ManifestFetchStatus.NoReleases)
        {
            return new ManifestFetchResult(ManifestFetchStatus.NoReleases, null, "No published releases found on GitHub.");
        }

        return new ManifestFetchResult(
            ManifestFetchStatus.NetworkError,
            null,
            apiOutcome.ErrorMessage ?? rawOutcome.ErrorMessage ?? "Unable to reach GitHub.");
    }

    private async Task<ManifestFetchResult> FetchRawManifestDetailedAsync(
        bool includePrereleases,
        string owner,
        string repo,
        CancellationToken ct)
    {
        string rawManifestUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/main/release-manifest.json";
        try
        {
            using var request = CreateGetRequest(rawManifestUrl, "application/json, text/plain, */*");
            if (!string.IsNullOrEmpty(_lastRawETag) && EntityTagHeaderValue.TryParse(_lastRawETag, out var parsedEtag))
            {
                request.Headers.IfNoneMatch.Add(parsedEtag);
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                App.Log("[GitHubClient] Raw manifest not modified (HTTP 304).");
                if (_cachedRawManifest != null && MatchesChannel(_cachedRawManifest, includePrereleases))
                {
                    return new ManifestFetchResult(ManifestFetchStatus.NotModified, _cachedRawManifest, Source: "RawManifest");
                }
                return new ManifestFetchResult(ManifestFetchStatus.NotModified, null, Source: "RawManifest");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ManifestFetchResult(ManifestFetchStatus.NoReleases, null, "No release-manifest.json found on GitHub.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ManifestFetchResult(ManifestFetchStatus.NetworkError, null, $"Raw manifest returned HTTP {(int)response.StatusCode}.");
            }

            string? candidateEtag = response.Headers.ETag?.Tag;
            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);

            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
            {
                return new ManifestFetchResult(ManifestFetchStatus.NoReleases, null, "Raw manifest is empty or invalid.");
            }

            if (!MatchesChannel(manifest, includePrereleases))
            {
                return new ManifestFetchResult(ManifestFetchStatus.NoReleases, null, "Raw manifest belongs to a different release channel.");
            }

            string? assetUrl = manifest.Downloads?.Installer?.Url ?? manifest.Downloads?.Portable?.Url;
            if (!string.IsNullOrWhiteSpace(assetUrl))
            {
                if (!IsTrustedGitHubUrl(assetUrl))
                {
                    App.Log($"[GitHubClient] Untrusted manifest asset URL rejected: {assetUrl}");
                    return new ManifestFetchResult(ManifestFetchStatus.NetworkError, null, "Untrusted manifest asset URL rejected.");
                }

                if (!await CheckAssetExistsAsync(assetUrl, ct).ConfigureAwait(false))
                {
                    App.Log($"[GitHubClient] Manifest version {manifest.Version} asset is not yet published on GitHub ({assetUrl}).");
                    return new ManifestFetchResult(ManifestFetchStatus.NoReleases, null, "Manifest asset is not published yet.");
                }
            }

            if (!string.IsNullOrEmpty(candidateEtag))
            {
                _lastRawETag = candidateEtag;
            }
            _cachedRawManifest = manifest;
            return new ManifestFetchResult(ManifestFetchStatus.Success, manifest, Source: "RawManifest");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            App.Log($"[GitHubClient] Raw manifest check failed: {ex.Message}");
            return new ManifestFetchResult(ManifestFetchStatus.NetworkError, null, ex.Message);
        }
    }

    private static bool MatchesChannel(UpdateManifest manifest, bool includePrereleases)
    {
        if (includePrereleases) return true;
        return !manifest.ParsedVersion.IsPreRelease && manifest.ParsedChannel != UpdateChannel.Beta;
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
            using var headReq = CreateRequest(HttpMethod.Head, url);
            using var headResp = await _http.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (headResp.RequestMessage?.RequestUri != null &&
                !IsTrustedGitHubUrl(headResp.RequestMessage.RequestUri.ToString()))
            {
                return false;
            }

            if (headResp.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }

            if (headResp.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.Forbidden)
            {
                using var rangeReq = CreateRequest(HttpMethod.Get, url);
                rangeReq.Headers.Range = new RangeHeaderValue(0, 0);
                using var rangeResp = await _http.SendAsync(rangeReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                if (rangeResp.RequestMessage?.RequestUri != null &&
                    !IsTrustedGitHubUrl(rangeResp.RequestMessage.RequestUri.ToString()))
                {
                    return false;
                }

                if (rangeResp.StatusCode == HttpStatusCode.NotFound)
                {
                    return false;
                }

                return rangeResp.IsSuccessStatusCode || rangeResp.StatusCode == HttpStatusCode.PartialContent;
            }

            return headResp.IsSuccessStatusCode;
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
    /// Queries the GitHub Releases REST API for the latest release on the requested channel.
    /// Uses /releases/latest for Stable and /releases?per_page=10 for Beta (including prereleases).
    /// </summary>
    private async Task<ManifestFetchResult> FetchFromReleasesApiDetailedAsync(
        bool includePrereleases,
        string owner,
        string repo,
        CancellationToken ct)
    {
        string apiUrl = includePrereleases
            ? $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=10"
            : $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

        try
        {
            using var request = CreateGetRequest(apiUrl, "application/vnd.github.v3+json");
            if (string.Equals(_lastApiEndpoint, apiUrl, StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(_lastApiETag) &&
                EntityTagHeaderValue.TryParse(_lastApiETag, out var parsedEtag))
            {
                request.Headers.IfNoneMatch.Add(parsedEtag);
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                App.Log("[GitHubClient] GitHub API release not modified (HTTP 304).");
                return new ManifestFetchResult(ManifestFetchStatus.NotModified, _cachedApiManifest, Source: "GitHubApi");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                DateTime? resetUtc = ParseRateLimitResetUtc(response);
                string msg = FormatRateLimitMessage(resetUtc);
                App.Log($"[GitHubClient] {msg}");
                return new ManifestFetchResult(ManifestFetchStatus.RateLimited, null, msg, resetUtc);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ManifestFetchResult(ManifestFetchStatus.NoReleases, null, "No published releases found on GitHub.");
            }

            if (!response.IsSuccessStatusCode)
            {
                string msg = $"GitHub API returned HTTP {(int)response.StatusCode}.";
                App.Log($"[GitHubClient] {msg}");
                return new ManifestFetchResult(ManifestFetchStatus.NetworkError, null, msg);
            }

            string? candidateEtag = response.Headers.ETag?.Tag;
            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            UpdateManifest? resolved = null;
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var releaseEl in doc.RootElement.EnumerateArray())
                {
                    if (releaseEl.TryGetProperty("draft", out var draftProp) && draftProp.ValueKind == JsonValueKind.True)
                        continue;

                    var candidate = await TryParseOrFetchReleaseAsync(releaseEl, ct).ConfigureAwait(false);
                    if (candidate == null) continue;
                    if (!MatchesChannel(candidate, includePrereleases)) continue;

                    if (resolved == null || candidate.ParsedVersion > resolved.ParsedVersion)
                    {
                        resolved = candidate;
                    }
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (!(doc.RootElement.TryGetProperty("draft", out var draftProp) && draftProp.ValueKind == JsonValueKind.True))
                {
                    var candidate = await TryParseOrFetchReleaseAsync(doc.RootElement, ct).ConfigureAwait(false);
                    if (candidate != null && MatchesChannel(candidate, includePrereleases))
                    {
                        resolved = candidate;
                    }
                }
            }

            if (resolved == null)
            {
                return new ManifestFetchResult(ManifestFetchStatus.NoReleases, null, "No matching releases found on GitHub.");
            }

            _lastApiEndpoint = apiUrl;
            _lastApiETag = candidateEtag;
            _cachedApiManifest = resolved;

            return new ManifestFetchResult(ManifestFetchStatus.Success, resolved, Source: "GitHubApi");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            App.Log($"[GitHubClient] GitHub Releases API query failed: {ex.Message}");
            return new ManifestFetchResult(ManifestFetchStatus.NetworkError, null, ex.Message);
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
                        bool installerUrlValid = string.IsNullOrWhiteSpace(manifest.Downloads?.Installer?.Url) ||
                                                 IsTrustedGitHubUrl(manifest.Downloads.Installer.Url);
                        bool portableUrlValid = string.IsNullOrWhiteSpace(manifest.Downloads?.Portable?.Url) ||
                                                IsTrustedGitHubUrl(manifest.Downloads.Portable.Url);
                        if (installerUrlValid && portableUrlValid)
                        {
                            if (string.IsNullOrWhiteSpace(manifest.ReleaseTag) && root.TryGetProperty("tag_name", out var tagProp))
                            {
                                manifest.ReleaseTag = tagProp.GetString() ?? "";
                            }
                            if (string.IsNullOrWhiteSpace(manifest.ChangelogUrl) && root.TryGetProperty("html_url", out var urlProp))
                            {
                                manifest.ChangelogUrl = urlProp.GetString();
                            }
                            return manifest;
                        }
                        App.Log("[GitHubClient] Rejected release-manifest.json asset with untrusted download URL.");
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
        bool isPreReleaseFlag = root.TryGetProperty("prerelease", out var preProp) && preProp.ValueKind == JsonValueKind.True;

        string cleanVersion = tagName.TrimStart('v', 'V');
        bool isPreRelease = isPreReleaseFlag ||
                            tagName.Contains("-beta", StringComparison.OrdinalIgnoreCase) ||
                            tagName.Contains("-rc", StringComparison.OrdinalIgnoreCase) ||
                            tagName.Contains("-alpha", StringComparison.OrdinalIgnoreCase) ||
                            tagName.Contains("-preview", StringComparison.OrdinalIgnoreCase) ||
                            tagName.Contains("beta", StringComparison.OrdinalIgnoreCase);
        var manifest = new UpdateManifest
        {
            Version = cleanVersion,
            ReleaseTag = tagName,
            Channel = isPreRelease ? "beta" : "stable",
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
        int maxCount = 15,
        CancellationToken ct = default)
    {
        var result = await FetchReleaseHistoryDetailedAsync(owner, repo, maxCount, ct).ConfigureAwait(false);
        return result.Items;
    }

    /// <summary>
    /// Fetches recent release history with structured rate-limit and error diagnostics.
    /// </summary>
    public async Task<ReleaseHistoryResult> FetchReleaseHistoryDetailedAsync(
        string owner = DefaultOwner,
        string repo = DefaultRepo,
        int maxCount = 15,
        CancellationToken ct = default)
    {
        string apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page={Math.Clamp(maxCount, 1, 30)}";
        var items = new List<ReleaseHistoryItem>();

        try
        {
            using var req = CreateGetRequest(apiUrl, "application/vnd.github.v3+json");
            using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                DateTime? resetUtc = ParseRateLimitResetUtc(response);
                string msg = FormatRateLimitMessage(resetUtc);
                App.Log($"[GitHubClient] Release history rate-limited: {msg}");
                return new ReleaseHistoryResult(items, IsSuccess: false, IsRateLimited: true, RateLimitResetUtc: resetUtc, ErrorMessage: msg);
            }

            if (!response.IsSuccessStatusCode)
            {
                string msg = $"GitHub API returned HTTP {(int)response.StatusCode}.";
                return new ReleaseHistoryResult(items, IsSuccess: false, ErrorMessage: msg);
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out var draftProp) && draftProp.ValueKind == JsonValueKind.True)
                    continue;

                string tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                string version = tag.TrimStart('v', 'V');
                DateTime publishedAt = release.TryGetProperty("published_at", out var pub) && pub.TryGetDateTime(out var dt) ? dt : DateTime.UtcNow;
                string body = release.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                string htmlUrl = release.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
                bool isPreRelease = (release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) ||
                                    tag.Contains("-beta", StringComparison.OrdinalIgnoreCase) ||
                                    tag.Contains("-rc", StringComparison.OrdinalIgnoreCase) ||
                                    tag.Contains("-alpha", StringComparison.OrdinalIgnoreCase);

                string? installerUrl = null;
                string? installerSha = null;
                string? portableUrl = null;
                string? portableSha = null;
                string? checksumsUrl = null;
                string? manifestUrl = null;
                long size = 0;
                long portableSize = 0;

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
                        else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && portableUrl == null)
                        {
                            portableUrl = asset.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() : null;
                            portableSize = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                            portableSha = TryExtractAssetDigestSha256(asset);
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
                    ManifestUrl: manifestUrl,
                    PortableUrl: portableUrl,
                    PortableSha256: portableSha,
                    PortableSizeBytes: portableSize));
            }

            return new ReleaseHistoryResult(items, IsSuccess: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            App.Log($"[GitHubClient] Failed to fetch release history: {ex.Message}");
            return new ReleaseHistoryResult(items, IsSuccess: false, ErrorMessage: ex.Message);
        }
    }

    /// <summary>
    /// Resolves the expected cryptographic SHA-256 for a historical release asset by querying
    /// its release-manifest.json or checksums.sha256.
    /// </summary>
    public async Task<string?> FetchExpectedSha256Async(
        ReleaseHistoryItem item,
        string targetFileName,
        CancellationToken ct = default)
    {
        bool isPortable = targetFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                          targetFileName.Contains("Portable", StringComparison.OrdinalIgnoreCase);

        string? directSha = isPortable ? item.PortableSha256 : item.InstallerSha256;
        if (!string.IsNullOrWhiteSpace(directSha) && IsValidHex64(directSha.Trim()))
        {
            return directSha.Trim();
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
                    string? hash = isPortable
                        ? manifest?.Downloads?.Portable?.Sha256
                        : manifest?.Downloads?.Installer?.Sha256;
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
                    if (string.IsNullOrWhiteSpace(hash) && isPortable)
                        hash = ExtractHashFromChecksumsText(text, "DynamicIsland-Portable.zip") ??
                               ExtractHashFromChecksumsText(text, ".zip");
                    if (string.IsNullOrWhiteSpace(hash) && !isPortable)
                        hash = ExtractHashFromChecksumsText(text, "DynamicIsland-Setup.exe") ??
                               ExtractHashFromChecksumsText(text, ".exe");
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
            long startTick = Environment.TickCount64;
            long lastReportTick = startTick - 150;

            void ReportProgress(bool forceReport)
            {
                if (progress == null) return;
                long now = Environment.TickCount64;
                if (!forceReport && (now - lastReportTick) < 100) return;
                lastReportTick = now;

                double elapsedSec = Math.Max((now - startTick) / 1000.0, 0.001);
                string speedSuffix = "";
                if (elapsedSec > 0.25 && totalRead > 0)
                {
                    double bytesPerSec = totalRead / elapsedSec;
                    speedSuffix = bytesPerSec >= 1024 * 1024
                        ? $" · {bytesPerSec / (1024 * 1024):0.0} MB/s"
                        : $" · {bytesPerSec / 1024:0} KB/s";
                }

                double readMb = totalRead / (1024.0 * 1024.0);
                if (totalBytes > 0)
                {
                    double totalMb = totalBytes / (1024.0 * 1024.0);
                    double percent = Math.Clamp((totalRead * 100.0) / totalBytes, 0.0, 100.0);
                    progress.Report(new UpdateProgressInfo(
                        Percent: percent,
                        DownloadedBytes: totalRead,
                        TotalBytes: totalBytes,
                        StatusMessage: $"Downloading: {percent:0}% ({readMb:0.0} / {totalMb:0.0} MB{speedSuffix})"));
                }
                else
                {
                    progress.Report(new UpdateProgressInfo(
                        Percent: 0,
                        DownloadedBytes: totalRead,
                        TotalBytes: -1,
                        StatusMessage: $"Downloading: {readMb:0.0} MB{speedSuffix}"));
                }
            }

            while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                await destinationStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                totalRead += read;
                ReportProgress(forceReport: false);
            }

            ReportProgress(forceReport: true);
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
        if (_ownsHttpClient)
        {
            _http.Dispose();
            _downloadHttp.Dispose();
        }
    }
}
