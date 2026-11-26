using System.Globalization;
using System.Text.Json.Serialization;

namespace DynamicIsland.Core;

/// <summary>
/// Lightweight, allocation-free semantic version parser and comparator adhering to SemVer 2.0.0.
/// Supports standard formats: "0.4.0", "v0.5.0", "1.0.0-beta.1", "1.0.0-rc.2+build123".
/// </summary>
public readonly struct SemVersion : IComparable<SemVersion>, IEquatable<SemVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string PreRelease { get; }

    public bool IsPreRelease => !string.IsNullOrEmpty(PreRelease);

    public SemVersion(int major, int minor, int patch, string? preRelease = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease?.Trim() ?? string.Empty;
    }

    public static SemVersion Parse(string? input)
    {
        if (TryParse(input, out var version))
        {
            return version;
        }
        throw new FormatException($"Invalid semantic version format: '{input}'");
    }

    public static bool TryParse(string? input, out SemVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        ReadOnlySpan<char> span = input.Trim();
        if (span.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            span = span[1..];
        }

        // Strip optional build metadata (+build123)
        int plusIdx = span.IndexOf('+');
        if (plusIdx >= 0)
        {
            span = span[..plusIdx];
        }

        // Separate prerelease (-beta.1)
        string preRelease = string.Empty;
        int dashIdx = span.IndexOf('-');
        if (dashIdx >= 0)
        {
            preRelease = span[(dashIdx + 1)..].ToString();
            span = span[..dashIdx];
        }

        // Parse Major.Minor.Patch
        int firstDot = span.IndexOf('.');
        if (firstDot < 0) return false;

        if (!int.TryParse(span[..firstDot], NumberStyles.None, CultureInfo.InvariantCulture, out int major) || major < 0) return false;

        ReadOnlySpan<char> rest = span[(firstDot + 1)..];
        int secondDot = rest.IndexOf('.');

        int minor, patch;
        if (secondDot < 0)
        {
            if (!int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out minor) || minor < 0) return false;
            patch = 0;
        }
        else
        {
            if (!int.TryParse(rest[..secondDot], NumberStyles.None, CultureInfo.InvariantCulture, out minor) || minor < 0) return false;
            if (!int.TryParse(rest[(secondDot + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out patch) || patch < 0) return false;
        }

        version = new SemVersion(major, minor, patch, preRelease);
        return true;
    }

    public int CompareTo(SemVersion other)
    {
        int majorCompare = Major.CompareTo(other.Major);
        if (majorCompare != 0) return majorCompare;

        int minorCompare = Minor.CompareTo(other.Minor);
        if (minorCompare != 0) return minorCompare;

        int patchCompare = Patch.CompareTo(other.Patch);
        if (patchCompare != 0) return patchCompare;

        // SemVer 2.0.0: A version without a pre-release tag has higher precedence than one with a pre-release tag
        bool thisHasPre = IsPreRelease;
        bool otherHasPre = other.IsPreRelease;

        if (!thisHasPre && !otherHasPre) return 0;
        if (!thisHasPre && otherHasPre) return 1;
        if (thisHasPre && !otherHasPre) return -1;

        // Compare pre-release identifiers lexicographically / numerically
        return ComparePreReleaseIdentifiers(PreRelease ?? string.Empty, other.PreRelease ?? string.Empty);
    }

    private static int ComparePreReleaseIdentifiers(string pre1, string pre2)
    {
        string[] parts1 = pre1.Split('.');
        string[] parts2 = pre2.Split('.');

        int len = Math.Min(parts1.Length, parts2.Length);
        for (int i = 0; i < len; i++)
        {
            string p1 = parts1[i];
            string p2 = parts2[i];

            bool p1IsNum = ulong.TryParse(p1, NumberStyles.None, CultureInfo.InvariantCulture, out ulong num1);
            bool p2IsNum = ulong.TryParse(p2, NumberStyles.None, CultureInfo.InvariantCulture, out ulong num2);

            if (p1IsNum && p2IsNum)
            {
                int cmp = num1.CompareTo(num2);
                if (cmp != 0) return cmp;
            }
            else if (p1IsNum)
            {
                return -1; // Numeric identifiers have lower precedence than non-numeric
            }
            else if (p2IsNum)
            {
                return 1;
            }
            else
            {
                int cmp = string.Compare(p1, p2, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
            }
        }

        return parts1.Length.CompareTo(parts2.Length);
    }

    public bool Equals(SemVersion other) =>
        Major == other.Major && Minor == other.Minor && Patch == other.Patch &&
        string.Equals(PreRelease ?? string.Empty, other.PreRelease ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is SemVersion other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Major, Minor, Patch, StringComparer.OrdinalIgnoreCase.GetHashCode(PreRelease ?? string.Empty));

    public override string ToString() =>
        IsPreRelease ? $"{Major}.{Minor}.{Patch}-{PreRelease}" : $"{Major}.{Minor}.{Patch}";

    public static bool operator ==(SemVersion left, SemVersion right) => left.Equals(right);
    public static bool operator !=(SemVersion left, SemVersion right) => !left.Equals(right);
    public static bool operator <(SemVersion left, SemVersion right) => left.CompareTo(right) < 0;
    public static bool operator <=(SemVersion left, SemVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >(SemVersion left, SemVersion right) => left.CompareTo(right) > 0;
    public static bool operator >=(SemVersion left, SemVersion right) => left.CompareTo(right) >= 0;
}

/// <summary>
/// Distribution release channels for update notifications and auto-downloads.
/// </summary>
public enum UpdateChannel
{
    Stable = 0,
    Beta = 1
}

/// <summary>
/// Status states of the update manager.
/// </summary>
public enum UpdateStatus
{
    Idle,
    Checking,
    UpToDate,
    UpdateAvailable,
    Downloading,
    Verifying,
    ReadyToInstall,
    Installing,
    Failed
}

/// <summary>
/// Specific downloadable asset description in an update release.
/// </summary>
public sealed class UpdateDownloadAsset
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; } = 0;
}

/// <summary>
/// Available download formats in an update release.
/// </summary>
public sealed class UpdateDownloads
{
    [JsonPropertyName("installer")]
    public UpdateDownloadAsset? Installer { get; set; }

    [JsonPropertyName("portable")]
    public UpdateDownloadAsset? Portable { get; set; }
}

/// <summary>
/// Canonical release manifest metadata generated during CI release push.
/// </summary>
public sealed class UpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("releaseTag")]
    public string ReleaseTag { get; set; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "stable";

    [JsonPropertyName("releaseDate")]
    public DateTime ReleaseDate { get; set; }

    [JsonPropertyName("minWindowsBuild")]
    public string MinWindowsBuild { get; set; } = "10.0.19041.0";

    [JsonPropertyName("changelogUrl")]
    public string? ChangelogUrl { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("isCritical")]
    public bool IsCritical { get; set; } = false;

    [JsonPropertyName("downloads")]
    public UpdateDownloads Downloads { get; set; } = new();

    [JsonIgnore]
    public SemVersion ParsedVersion => SemVersion.TryParse(Version, out var v) ? v : default;

    [JsonIgnore]
    public UpdateChannel ParsedChannel =>
        string.Equals(Channel, "beta", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Channel, "canary", StringComparison.OrdinalIgnoreCase)
            ? UpdateChannel.Beta
            : UpdateChannel.Stable;
}

/// <summary>
/// Version history entry for the user Version Control / Rollback manager.
/// </summary>
public sealed record ReleaseHistoryItem(
    string Version,
    string ReleaseTag,
    DateTime ReleaseDate,
    string Summary,
    string HtmlUrl,
    bool IsPreRelease,
    string? InstallerUrl,
    string? InstallerSha256 = null,
    long SizeBytes = 0,
    string? ChecksumsUrl = null,
    string? ManifestUrl = null,
    string? PortableUrl = null,
    string? PortableSha256 = null,
    long PortableSizeBytes = 0)
{
    public bool HasDownloadableAsset =>
        !string.IsNullOrWhiteSpace(InstallerUrl) || !string.IsNullOrWhiteSpace(PortableUrl);
}

/// <summary>
/// Outcome status for fetching the latest update manifest from GitHub.
/// </summary>
public enum ManifestFetchStatus
{
    Success,
    NotModified,
    RateLimited,
    NetworkError,
    NoReleases
}

/// <summary>
/// Detailed result for update manifest queries, preserving rate-limit and error diagnostics.
/// </summary>
public sealed record ManifestFetchResult(
    ManifestFetchStatus Status,
    UpdateManifest? Manifest,
    string? ErrorMessage = null,
    DateTime? RateLimitResetUtc = null,
    string? Source = null);

/// <summary>
/// Detailed result for release history queries, preserving rate-limit and error diagnostics.
/// </summary>
public sealed record ReleaseHistoryResult(
    IReadOnlyList<ReleaseHistoryItem> Items,
    bool IsSuccess,
    bool IsRateLimited = false,
    DateTime? RateLimitResetUtc = null,
    string? ErrorMessage = null);

/// <summary>
/// Progress reporting snapshot during file download and hash verification.
/// </summary>
public sealed record UpdateProgressInfo(
    double Percent,
    long DownloadedBytes,
    long TotalBytes,
    string StatusMessage);
