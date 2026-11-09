using System.Net;
using System.Text;
using Linked.Api.Auth;
using Linked.Api.Identity;
using Linked.Core;
using Linked.Core.Services;
using Linked.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

// Exercises the same DI registrations the app uses, with the network replaced by canned replies.
// Add one check here for every new API area (feed, messaging, ...). Exit code 0 means pass.

ServiceCollection services = new();
services.AddSingleton<ISessionService, SmokeSession>();
services.AddLinkedCore(static () => new CannedHandler());

await using ServiceProvider provider = services.BuildServiceProvider();

OAuthToken token = await provider.GetRequiredService<ILinkedInOAuthApi>().ExchangeCodeAsync(
    new AuthorizationCodeTokenRequest { Code = "c", RedirectUri = "http://127.0.0.1/", ClientId = "id", CodeVerifier = "v" },
    CancellationToken.None);
Check(token.AccessToken == "tok", "token exchange");

UserInfo member = await provider.GetRequiredService<IIdentityApi>().GetUserInfoAsync(CancellationToken.None);
Check(member.GivenName == "Ada", "userinfo");

ShellViewModel shell = provider.GetRequiredService<ShellViewModel>();
await shell.SignInCommand.ExecuteAsync(null);
Check(shell is { IsSignedIn: true, DisplayName: "Ada Lovelace" }, "shell view model");

Console.WriteLine("AOT smoke: all checks passed");
return 0;

static void Check(bool condition, string name)
{
    if (!condition)
    {
        Console.Error.WriteLine($"AOT smoke FAILED: {name}");
        Environment.Exit(1);
    }

    Console.WriteLine($"ok: {name}");
}

internal sealed class SmokeSession : ISessionService
{
    public bool IsSignedIn { get; private set; }

    public ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken) => new("tok");

    public Task<bool> SignInAsync(CancellationToken cancellationToken)
    {
        IsSignedIn = true;
        return Task.FromResult(true);
    }

    public Task SignOutAsync(CancellationToken cancellationToken)
    {
        IsSignedIn = false;
        return Task.CompletedTask;
    }
}

internal sealed class CannedHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? json = request.RequestUri?.AbsolutePath switch
        {
            "/oauth/v2/accessToken" => """{"access_token":"tok","expires_in":3600}""",
            "/v2/userinfo" when request.Headers.Authorization is { Scheme: "Bearer", Parameter: "tok" }
                => """{"sub":"1","name":"Ada Lovelace","given_name":"Ada"}""",
            _ => null,
        };

        HttpResponseMessage response = json is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        return Task.FromResult(response);
    }
}
