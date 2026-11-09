using Linked.Api.Auth;
using Linked.Api.Identity;
using Refit.Testing;

namespace Linked.Api.Tests;

public sealed class OAuthApiTests
{
    [Test]
    public async Task ExchangeCode_posts_a_form_and_reads_the_snake_case_reply()
    {
        using RecordingHandler http = new("""{"access_token":"tok","expires_in":5184000,"scope":"openid profile","id_token":"jwt"}""");
        ILinkedInOAuthApi api = Stub.Client<ILinkedInOAuthApi>(http, LinkedInOAuthJsonContext.Default);

        OAuthToken token = await api.ExchangeCodeAsync(
            new AuthorizationCodeTokenRequest
            {
                Code = "abc",
                RedirectUri = "http://127.0.0.1:5000/",
                ClientId = "client",
                CodeVerifier = "verifier",
            },
            CancellationToken.None);

        await Assert.That(token.AccessToken).IsEqualTo("tok");
        await Assert.That(token.ExpiresIn).IsEqualTo(5184000);
        await Assert.That(token.IdToken).IsEqualTo("jwt");

        await Assert.That(http.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(http.Path).IsEqualTo("/oauth/v2/accessToken");
        await Assert.That(http.Body).Contains("grant_type=authorization_code");
        await Assert.That(http.Body).Contains("code_verifier=verifier");
        await Assert.That(http.Body).Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A5000%2F");
    }

    [Test]
    public async Task GetUserInfo_maps_given_name_and_picture()
    {
        using StubHttp http = new()
        {
            {
                Route.Get("/v2/userinfo"),
                Reply.Json("""{"sub":"782bbtaQ","name":"Ada Lovelace","given_name":"Ada","picture":"https://media/x.jpg","email_verified":true}""")
            },
        };
        IIdentityApi api = Stub.Client<IIdentityApi>(http, LinkedInOAuthJsonContext.Default);

        UserInfo member = await api.GetUserInfoAsync(CancellationToken.None);

        await Assert.That(member.Sub).IsEqualTo("782bbtaQ");
        await Assert.That(member.GivenName).IsEqualTo("Ada");
        await Assert.That(member.Picture).IsEqualTo("https://media/x.jpg");
        await Assert.That(member.EmailVerified is true).IsTrue();
        http.VerifyAllCalled();
    }
}
