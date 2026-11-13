using System.Net;
using Guit.Api.Auth;

namespace Guit.Api.Http;

/// <summary>Reports HTTP 401 replies from the REST API, so a revoked token signs the member out.</summary>
internal sealed class UnauthorizedDelegatingHandler(IUnauthorizedHandler unauthorized) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            unauthorized.OnUnauthorized();
        }

        return response;
    }
}
