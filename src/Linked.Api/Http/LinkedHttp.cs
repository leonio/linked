using System.Net;

namespace Linked.Api.Http;

internal static class LinkedHttp
{
    /// <summary>
    /// One tuned handler configuration for every client: HTTP/2 with multiple connections,
    /// long-lived pooled connections, and transparent Brotli/gzip decompression.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AutomaticDecompression = DecompressionMethods.All,
        EnableMultipleHttp2Connections = true,
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        UseCookies = false,
    };

    public static void Configure(HttpClient client, Uri baseAddress)
    {
        client.BaseAddress = baseAddress;
        client.DefaultRequestVersion = HttpVersion.Version20;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        client.Timeout = TimeSpan.FromSeconds(30);
    }
}
