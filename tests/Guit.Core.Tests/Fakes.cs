using System.Net;
using Guit.Api.Auth;
using Guit.Api.Http;
using Guit.Api.Issues;
using Guit.Api.Notifications;
using Guit.Api.Reactions;
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
                Octocat));

    public static readonly GitHubUser Octocat = new(1, "octocat", "https://avatars.githubusercontent.com/u/1?v=4", "https://github.com/octocat");

    public static Issue Issue(string title = "Fix it", bool isPullRequest = true, string state = "open", GitHubUser? user = null, bool deletedUser = false)
        => new(
            70,
            7,
            title,
            state,
            1,
            Now.AddDays(-2),
            "https://github.com/octocat/hello/pull/7",
            deletedUser ? null : user ?? Octocat,
            "Please review",
            "<p>Please <strong>review</strong></p>",
            PullRequest: isPullRequest ? new IssuePullRequestRef("https://github.com/octocat/hello/pull/7") : null);

    public static ReactionRollup Reactions(int plusOne = 0, int heart = 0, int rocket = 0)
        => new(plusOne + heart + rocket, plusOne, 0, 0, 0, heart, 0, 0, rocket);

    public static IssueComment Comment(long id, string body = "Looks good", string? association = "MEMBER", bool deletedUser = false, ReactionRollup? reactions = null)
        => new(
            id,
            Now.AddHours(-3),
            "https://github.com/octocat/hello/pull/7#issuecomment-" + id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            deletedUser ? null : Octocat,
            body,
            "<p>" + body + "</p>",
            association,
            reactions);

    public static IssueComment[] Comments(int count, long firstId = 1)
    {
        IssueComment[] comments = new IssueComment[count];
        for (int i = 0; i < count; i++)
        {
            comments[i] = Comment(firstId + i);
        }

        return comments;
    }

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

internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

internal sealed class FakeNavigator : INavigator
{
    public List<(Route Route, string? Argument)> Navigations { get; } = [];

    public int BackCount { get; private set; }

    public bool CanGoBack => Navigations.Count > 1;

    public void Navigate(Route route, string? argument = null) => Navigations.Add((route, argument));

    public void GoBack() => BackCount++;
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

    /// <summary>When set, the next ListAsync waits for this to complete before replying.</summary>
    public TaskCompletionSource? HoldNextList { get; set; }

    public void Enqueue(NotificationThread[] page) => _pages.Enqueue(page);

    public Task<NotificationThread[]> ListAsync(bool all, bool participating, int page, int perPage, CancellationToken cancellationToken)
    {
        ListCalls.Add((all, participating, page, perPage));
        if (TakeFailure() is { } failure)
        {
            return Task.FromException<NotificationThread[]>(failure);
        }

        NotificationThread[] reply = _pages.Count > 0 ? _pages.Dequeue() : [];
        if (HoldNextList is not { } hold)
        {
            return Task.FromResult(reply);
        }

        HoldNextList = null;
        return WaitThenReply(hold.Task, reply);
    }

    private static async Task<NotificationThread[]> WaitThenReply(Task gate, NotificationThread[] reply)
    {
        await gate;
        return reply;
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

/// <summary>Returns one issue and the queued comment pages in order; records what was posted.</summary>
internal sealed class FakeIssuesApi(Issue issue, params IssueComment[][] pages) : IIssuesApi
{
    private readonly Queue<IssueComment[]> _pages = new(pages);

    /// <summary>When set, the next call fails with this status and the value is cleared.</summary>
    public HttpStatusCode? FailNextWith { get; set; }

    public List<(string Owner, string Repo, int Number, int Page)> ListCalls { get; } = [];

    public List<string> Posted { get; } = [];

    public Task<Issue> GetAsync(string owner, string repo, int issueNumber, CancellationToken cancellationToken)
        => TakeFailure() is { } failure ? Task.FromException<Issue>(failure) : Task.FromResult(issue);

    public Task<IssueComment[]> ListCommentsAsync(string owner, string repo, int issueNumber, int page, int perPage, CancellationToken cancellationToken)
    {
        ListCalls.Add((owner, repo, issueNumber, page));
        return TakeFailure() is { } failure
            ? Task.FromException<IssueComment[]>(failure)
            : Task.FromResult(_pages.Count > 0 ? _pages.Dequeue() : []);
    }

    public Task<IssueComment> CreateCommentAsync(string owner, string repo, int issueNumber, CreateCommentRequest request, CancellationToken cancellationToken)
    {
        if (TakeFailure() is { } failure)
        {
            return Task.FromException<IssueComment>(failure);
        }

        Posted.Add(request.Body);
        return Task.FromResult(Sample.Comment(1000 + Posted.Count, request.Body));
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

internal sealed class FakeReactionsApi : IReactionsApi
{
    public HttpStatusCode? FailNextWith { get; set; }

    /// <summary>"issue:7:heart" or "comment:11:+1" for each call.</summary>
    public List<string> Calls { get; } = [];

    public Task CreateForIssueAsync(string owner, string repo, int issueNumber, CreateReactionRequest request, CancellationToken cancellationToken)
        => Record($"issue:{issueNumber}:{request.Content}");

    public Task CreateForIssueCommentAsync(string owner, string repo, long commentId, CreateReactionRequest request, CancellationToken cancellationToken)
        => Record($"comment:{commentId}:{request.Content}");

    private Task Record(string call)
    {
        Calls.Add(call);
        if (FailNextWith is not { } status)
        {
            return Task.CompletedTask;
        }

        FailNextWith = null;
        return Task.FromException(Failure.Status(status));
    }
}
