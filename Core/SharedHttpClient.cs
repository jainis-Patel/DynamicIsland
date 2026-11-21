using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace DynamicIsland.Core;

/// <summary>
/// Shared pooled HTTP infrastructure across Dynamic Island services to avoid socket exhaustion
/// and ensure DNS rotation, automatic decompression, and consistent connect timeouts.
/// </summary>
internal static class SharedHttpClient
{
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
        ConnectTimeout = TimeSpan.FromSeconds(5),
        AutomaticDecompression = DecompressionMethods.All
    };

    public static HttpClient CreateClient(TimeSpan timeout, string? userAgent = null)
    {
        var client = new HttpClient(SharedHandler, disposeHandler: false)
        {
            Timeout = timeout
        };

        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            client.DefaultRequestHeaders.UserAgent.TryParseAdd(userAgent);
        }

        return client;
    }
}
