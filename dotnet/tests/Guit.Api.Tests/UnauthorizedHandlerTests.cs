using System.Net;
using Guit.Api.Auth;
using Guit.Api.Http;

namespace Guit.Api.Tests;

public sealed class UnauthorizedHandlerTests
{
    [Test]
    [Arguments(HttpStatusCode.Unauthorized, 1)]
    [Arguments(HttpStatusCode.OK, 0)]
    [Arguments(HttpStatusCode.Forbidden, 0)]
    [Arguments(HttpStatusCode.NotFound, 0)]
    public async Task Only_a_401_reply_is_reported(HttpStatusCode status, int expectedCalls)
    {
        Listener listener = new();
        using UnauthorizedDelegatingHandler handler = new(listener) { InnerHandler = new FixedStatusHandler(status) };
        using HttpClient client = new(handler);

        using HttpResponseMessage response = await client.GetAsync(new Uri("https://stub.test/user"));

        await Assert.That(response.StatusCode).IsEqualTo(status);
        await Assert.That(listener.Calls).IsEqualTo(expectedCalls);
    }

    private sealed class Listener : IUnauthorizedHandler
    {
        public int Calls { get; private set; }

        public void OnUnauthorized() => Calls++;
    }

    private sealed class FixedStatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status));
    }
}
