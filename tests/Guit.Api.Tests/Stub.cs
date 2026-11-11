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

    public string? IfModifiedSince { get; private set; }

    /// <summary>Status of the reply. Anything but 200 is sent without a body.</summary>
    public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

    /// <summary>Extra reply headers, as "Name: value".</summary>
    public string[] ReplyHeaders { get; init; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Method = request.Method;
        Path = request.RequestUri?.PathAndQuery;
        Accept = request.Headers.Accept.ToString();
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        IfModifiedSince = request.Headers.TryGetValues("If-Modified-Since", out IEnumerable<string>? values) ? string.Join(",", values) : null;

        HttpResponseMessage response = new(Status) { RequestMessage = request };
        if (Status == HttpStatusCode.OK)
        {
            response.Content = new StringContent(replyJson, Encoding.UTF8, "application/json");
        }

        foreach (string header in ReplyHeaders)
        {
            int colon = header.IndexOf(':', StringComparison.Ordinal);
            string name = header[..colon];
            string value = header[(colon + 1)..].Trim();
            if (!response.Headers.TryAddWithoutValidation(name, value))
            {
                response.Content?.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return response;
    }
}
