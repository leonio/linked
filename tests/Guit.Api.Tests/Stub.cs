using System.Net;
using System.Text;
using Guit.Api.Http;
using Refit;
using Refit.Testing;

namespace Guit.Api.Tests;

/// <summary>Builds generated Refit clients that never touch the network.</summary>
internal static class Stub
{
    public const string Host = "https://stub.test";

    /// <summary>The normal case: routes and JSON replies declared on a <see cref="StubHttp"/>.</summary>
    public static T Client<T>(StubHttp http)
        where T : class
        => http.CreateGeneratedClient<T>(Host, GitHubRefit.CreateSettings());

    /// <summary>For requests whose body is not JSON (forms): replies with one canned JSON body and records the raw request text.</summary>
    public static T Client<T>(RecordingHandler handler)
        where T : class
        => RestService.ForGenerated<T>(new HttpClient(handler) { BaseAddress = new Uri(Host) }, GitHubRefit.CreateSettings());
}

internal sealed class RecordingHandler(string replyJson) : HttpMessageHandler
{
    public HttpMethod? Method { get; private set; }

    public string? Path { get; private set; }

    public string? Body { get; private set; }

    public string? Accept { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Method = request.Method;
        Path = request.RequestUri?.PathAndQuery;
        Accept = request.Headers.Accept.ToString();
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(replyJson, Encoding.UTF8, "application/json"),
        };
    }
}
