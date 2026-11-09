using System.Net;
using System.Text;
using Linked.Api.Users;
using Linked.Core;
using Linked.Core.Services;
using Linked.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

// Runs the same DI registrations the app uses as a Native AOT binary, with the network replaced
// by canned replies. A plain build cannot prove code survives trimming; this can.
// Add one Check for every new API interface and every new view model. Exit code 0 means pass.

ServiceCollection services = new();
services.AddSingleton<ITokenStore, MemoryTokenStore>();
services.AddSingleton<ILauncher, NullLauncher>();
services.AddSingleton(new GitHubOptions("smoke-client"));
services.AddLinkedCore(static () => new CannedHandler());

await using ServiceProvider provider = services.BuildServiceProvider();

SignInViewModel signIn = provider.GetRequiredService<SignInViewModel>();
await signIn.SignInCommand.ExecuteAsync(null);
Check(provider.GetRequiredService<Session>().IsSignedIn, "device flow sign-in");

GitHubUser user = await provider.GetRequiredService<IUsersApi>().GetAuthenticatedAsync(CancellationToken.None);
Check(user is { Login: "octocat", Id: 583231 }, "users api");

ShellViewModel shell = provider.GetRequiredService<ShellViewModel>();
await shell.LoadUserCommand.ExecuteAsync(null);
Check(shell is { IsSignedIn: true, Login: "octocat" }, "shell view model");

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

internal sealed class MemoryTokenStore : ITokenStore
{
    private string? _token;

    public string? Load() => _token;

    public void Save(string token) => _token = token;

    public void Clear() => _token = null;
}

internal sealed class NullLauncher : ILauncher
{
    public Task OpenAsync(Uri uri) => Task.CompletedTask;

    public void CopyToClipboard(string text)
    {
    }
}

/// <summary>Canned replies keyed by path. Authenticated paths require the token the device flow returned.</summary>
internal sealed class CannedHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        bool authorized = request.Headers.Authorization is { Scheme: "Bearer", Parameter: "gho_smoke" };
        string? json = request.RequestUri?.AbsolutePath switch
        {
            "/login/device/code" => """{"device_code":"dev","user_code":"AAAA-BBBB","verification_uri":"https://github.com/login/device","expires_in":900,"interval":0}""",
            "/login/oauth/access_token" => """{"access_token":"gho_smoke","token_type":"bearer","scope":"repo"}""",
            "/user" when authorized => """{"login":"octocat","id":583231,"avatar_url":"https://avatars/1","html_url":"https://github.com/octocat"}""",
            _ => null,
        };

        HttpResponseMessage response = json is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        return Task.FromResult(response);
    }
}
