using System.Net;
using Guit.Api.Users;
using Refit;
using Refit.Testing;

namespace Guit.Api.Tests;

public sealed class ErrorTests
{
    [Test]
    public async Task A_non_success_status_throws_ApiException_with_the_status_code()
    {
        using StubHttp http = new()
        {
            { Route.Get("/user"), Reply.Status(HttpStatusCode.NotFound) },
        };
        IUsersApi api = Stub.Client<IUsersApi>(http);

        Exception? thrown = null;
        try
        {
            await api.GetAuthenticatedAsync(CancellationToken.None);
        }
        catch (ApiException ex)
        {
            thrown = ex;
        }

        await Assert.That(thrown).IsNotNull();
        await Assert.That(((ApiException)thrown!).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        // View models rely on this: Refit's exceptions are ApiExceptionBase, not HttpRequestException.
        await Assert.That(thrown is ApiExceptionBase).IsTrue();
        await Assert.That(thrown is HttpRequestException).IsFalse();
    }
}
