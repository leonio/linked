using System.Net;
using Guit.Api.Auth;
using Guit.Api.Http;
using Guit.Api.Notifications;
using Guit.Api.Users;
using Guit.Core.Services;
using Refit;

namespace Guit.Core.Tests;

// Hand-written fakes: one small class per interface, shared by every test. No mocking library.

/// <summary>A clock that always reads the same instant.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Sample API objects for tests.</summary>
internal static class Sample
{
    public static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    public static NotificationThread Thread(
        string id,
        string title = "Fix it",
        string type = "PullRequest",
        bool unread = true,
        string? url = "https://api.github.com/repos/octocat/hello/pulls/7")
        => new(
            id,
            unread,
            "review_requested",
            Now.AddHours(-3),
            new NotificationSubject(title, type, url),
            new RepositorySummary(9, "hello", "octocat/hello", false, "https://github.com/octocat/hello",
                new GitHubUser(1, "octocat", "https://avatars.githubusercontent.com/u/1?v=4", "https://github.com/octocat")));

    public static NotificationThread[] Threads(int count, int firstId = 1)
    {
        NotificationThread[] threads = new NotificationThread[count];
        for (int i = 0; i < count; i++)
        {
            threads[i] = Thread((firstId + i).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return threads;
    }
}

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

/// <summary>Returns the queued pages in order (an empty page once they run out) and records every call.</summary>
internal sealed class FakeNotificationsApi(params NotificationThread[][] pages) : INotificationsApi
{
    private readonly Queue<NotificationThread[]> _pages = new(pages);

    /// <summary>When set, the next call fails with this status and the value is cleared.</summary>
    public HttpStatusCode? FailNextWith { get; set; }

    public List<(bool All, bool Participating, int Page, int PerPage)> ListCalls { get; } = [];

    public List<string> MarkedRead { get; } = [];

    public List<string> MarkedDone { get; } = [];

    public void Enqueue(NotificationThread[] page) => _pages.Enqueue(page);

    public Task<NotificationThread[]> ListAsync(bool all, bool participating, int page, int perPage, CancellationToken cancellationToken)
    {
        ListCalls.Add((all, participating, page, perPage));
        return TakeFailure() is { } failure
            ? Task.FromException<NotificationThread[]>(failure)
            : Task.FromResult(_pages.Count > 0 ? _pages.Dequeue() : []);
    }

    public Task MarkAsReadAsync(string threadId, CancellationToken cancellationToken)
    {
        MarkedRead.Add(threadId);
        return TakeFailure() is { } failure ? Task.FromException(failure) : Task.CompletedTask;
    }

    public Task MarkAsDoneAsync(string threadId, CancellationToken cancellationToken)
    {
        MarkedDone.Add(threadId);
        return TakeFailure() is { } failure ? Task.FromException(failure) : Task.CompletedTask;
    }

    private ApiException? TakeFailure()
    {
        if (FailNextWith is not { } status)
        {
            return null;
        }

        FailNextWith = null;
        return Failure.Status(status);
    }
}
