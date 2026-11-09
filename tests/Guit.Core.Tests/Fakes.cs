using System.Net;
using Guit.Api.Auth;
using Guit.Api.Http;
using Guit.Api.Users;
using Guit.Core.Services;
using Refit;

namespace Guit.Core.Tests;

// Hand-written fakes: one small class per interface, shared by every test. No mocking library.

/// <summary>Builds the exceptions Refit throws, for fakes that simulate a failed call.</summary>
internal static class Failure
{
    /// <summary>GitHub answered with an error status.</summary>
    public static ApiException Status(HttpStatusCode status, bool rateLimited = false)
    {
        HttpResponseMessage response = new(status);
        if (rateLimited)
        {
            response.Headers.Add("x-ratelimit-remaining", "0");
        }

        return ApiException.Create(new HttpRequestMessage(), HttpMethod.Get, response, GitHubRefit.CreateSettings()).GetAwaiter().GetResult();
    }
}

internal sealed class FakeTokenStore(string? token = null) : ITokenStore
{
    public string? Token { get; private set; } = token;

    public string? Load() => Token;

    public void Save(string token) => Token = token;

    public void Clear() => Token = null;
}

internal sealed class FakeLauncher : ILauncher
{
    public Uri? Opened { get; private set; }

    public string? Clipboard { get; private set; }

    public Task OpenAsync(Uri uri)
    {
        Opened = uri;
        return Task.CompletedTask;
    }

    public void CopyToClipboard(string text) => Clipboard = text;
}

internal sealed class FakeUsersApi(GitHubUser? user) : IUsersApi
{
    public Task<GitHubUser> GetAuthenticatedAsync(CancellationToken cancellationToken)
        => user is null
            ? Task.FromException<GitHubUser>(Failure.Status(HttpStatusCode.InternalServerError))
            : Task.FromResult(user);
}

/// <summary>Replies to polls with the queued responses, in order.</summary>
internal sealed class FakeDeviceFlowApi(params DeviceTokenResponse[] polls) : IGitHubDeviceFlowApi
{
    private readonly Queue<DeviceTokenResponse> _polls = new(polls);

    public Task<DeviceCode> RequestCodeAsync(DeviceCodeRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new DeviceCode("dev", "WDJB-MJHT", "https://github.com/login/device", ExpiresIn: 900, Interval: 0));

    public Task<DeviceTokenResponse> PollTokenAsync(DeviceTokenRequest request, CancellationToken cancellationToken)
        => Task.FromResult(_polls.Dequeue());
}
