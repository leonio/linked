using System.Net;
using Linked.Core.Services;

namespace Linked.Core.Tests;

public sealed class ApiErrorsTests
{
    [Test]
    [Arguments(HttpStatusCode.Unauthorized, false, "Your GitHub session has expired. Sign in again.")]
    [Arguments(HttpStatusCode.Forbidden, true, "GitHub rate limit reached. Try again in a few minutes.")]
    [Arguments(HttpStatusCode.TooManyRequests, false, "GitHub rate limit reached. Try again in a few minutes.")]
    [Arguments(HttpStatusCode.Forbidden, false, "GitHub refused this request. The app may need more permissions.")]
    [Arguments(HttpStatusCode.NotFound, false, "Not found. It may have been deleted, or you may not have access.")]
    [Arguments(HttpStatusCode.BadGateway, false, "GitHub is having problems. Try again shortly.")]
    public async Task Describe_maps_status_codes(HttpStatusCode status, bool rateLimited, string expected)
    {
        await Assert.That(ApiErrors.Describe(Failure.Status(status, rateLimited))).IsEqualTo(expected);
    }

    [Test]
    public async Task Describe_treats_transport_failures_as_connection_problems()
    {
        await Assert.That(ApiErrors.Describe(new HttpRequestException("dns"))).IsEqualTo("Could not reach GitHub. Check your connection.");
    }
}
