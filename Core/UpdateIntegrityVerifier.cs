using System.IO;
using System.Security.Cryptography;

namespace DynamicIsland.Core;

/// <summary>
/// Cryptographic file integrity verification helper for downloaded update packages.
/// Ensures tamper-resistance and guards against network corruption before executing installers.
/// </summary>
public static class UpdateIntegrityVerifier
{
    private const int BufferSize = 64 * 1024; // 64 KB buffer avoids LOH allocations

    /// <summary>
    /// Computes the SHA-256 hash of a file as a lowercase hexadecimal string.
    /// </summary>
    public static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Cannot compute SHA-256: file does not exist.", filePath);
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        using var sha256 = SHA256.Create();

        byte[] hash = await sha256.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Computes SHA-256 synchronously for unit tests or small scratch buffers.
    /// </summary>
    public static string ComputeSha256(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies if the file at <paramref name="filePath"/> matches the expected SHA-256 hash.
    /// If empty or missing, returns false.
    /// </summary>
    public static async Task<bool> VerifyFileAsync(string filePath, string expectedSha256, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256) || !File.Exists(filePath))
        {
            return false;
        }

        string cleanExpected = expectedSha256.Trim();
        if (cleanExpected.Length != 64)
        {
            return false;
        }

        byte[] expectedBytes;
        try
        {
            expectedBytes = Convert.FromHexString(cleanExpected);
        }
        catch (FormatException)
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
            using var sha256 = SHA256.Create();
            byte[] actualBytes = await sha256.ComputeHashAsync(stream, ct).ConfigureAwait(false);
            return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Safely deletes a downloaded file if it is corrupted or failed hash verification.
    /// </summary>
    public static bool TryCleanFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
        }
        catch
        {
            // Suppress non-fatal cleanup failure
        }
        return false;
    }
}
