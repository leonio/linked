using Triage.Api.Users;
using Refit.Testing;

namespace Triage.Api.Tests;

public sealed class UsersApiTests
{
    [Test]
    public async Task GetAuthenticated_maps_the_fields_the_app_shows()
    {
        using StubHttp http = new()
        {
            {
                Route.Get("/user"),
                Reply.Json("""{"login":"octocat","id":583231,"avatar_url":"https://avatars.githubusercontent.com/u/583231","html_url":"https://github.com/octocat","name":null,"site_admin":false}""")
            },
        };
        IUsersApi api = Stub.Client<IUsersApi>(http);

        GitHubUser user = await api.GetAuthenticatedAsync(CancellationToken.None);

        await Assert.That(user.Login).IsEqualTo("octocat");
        await Assert.That(user.Id).IsEqualTo(583231L);
        await Assert.That(user.AvatarUrl).IsEqualTo("https://avatars.githubusercontent.com/u/583231");
        await Assert.That(user.Name).IsNull();
        http.VerifyAllCalled();
    }
}
