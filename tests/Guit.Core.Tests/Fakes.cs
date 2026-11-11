using System.Net;
using Guit.Api.Actions;
using Guit.Api.Auth;
using Guit.Api.Http;
using Guit.Api.Issues;
using Guit.Api.Notifications;
using Guit.Api.Repos;
using Guit.Api.Pulls;
using Guit.Api.Reactions;
using Guit.Api.Search;
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

    public static PullRequest Pull(string state = "open", bool merged = false, bool draft = false, string headLabel = "octocat:feature")
        => new(7, state, merged, 120, 8, 5, new PullBranch("feature", headLabel), new PullBranch("main", "octocat:main"), draft);

    public static IssueSearchItem Found(int number, string title = "Fix it", bool draft = false, bool deletedUser = false)
        => new(
            1000 + number,
            number,
            title,
            "open",
            2,
            Now.AddHours(-3),
            $"https://github.com/octocat/hello/pull/{number}",
            "https://api.github.com/repos/octocat/hello",
            deletedUser ? null : Octocat,
            draft);

    public static IssueSearchItem[] FoundMany(int count, int firstNumber = 1)
    {
        IssueSearchItem[] items = new IssueSearchItem[count];
        for (int i = 0; i < count; i++)
        {
            items[i] = Found(firstNumber + i);
        }

        return items;
    }

    public static WorkflowRun Run(int number, string? status = "completed", string? conclusion = "success", long? id = null)
        => new(
            id ?? 30433000 + number,
            "Fix it",
            number,
            "push",
            Now.AddHours(-4),
            Now.AddHours(-3),
            $"https://github.com/octocat/hello/actions/runs/{id ?? 30433000 + number}",
            "CI",
            status,
            conclusion,
            "main",
            Octocat);

    public static WorkflowRun[] Runs(int count, int firstNumber = 1)
    {
        WorkflowRun[] runs = new WorkflowRun[count];
        for (int i = 0; i < count; i++)
        {
            runs[i] = Run(firstNumber + i);
        }

        return runs;
    }

    public static JobStep Step(int number, string name, string status = "completed", string? conclusion = "success", int? seconds = 5)
        => new(name, status, number, conclusion, status == "queued" ? null : Now.AddMinutes(-10), seconds is { } s ? Now.AddMinutes(-10).AddSeconds(s) : null);

    public static Job Job(long id, string name = "build", string status = "completed", string? conclusion = "success", int? seconds = 60, JobStep[]? steps = null)
        => new(
            id,
            name,
            status,
            Now.AddMinutes(-10),
            $"https://github.com/octocat/hello/actions/runs/30433012/job/{id}",
            conclusion,
            seconds is { } s ? Now.AddMinutes(-10).AddSeconds(s) : null,
            steps);

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

    /// <summary>The If-Modified-Since value of each poll (null for an unconditional one).</summary>
    public List<string?> PollDates { get; } = [];

    /// <summary>When set, polls answer 304 while the caller sends this date.</summary>
    public string? NotModifiedSince { get; set; }

    /// <summary>The Last-Modified value sent with each 200 poll reply.</summary>
    public DateTimeOffset LastModified { get; set; } = Sample.Now;

    /// <summary>The X-Poll-Interval value sent with each poll reply, in seconds.</summary>
    public int PollInterval { get; set; } = 60;

    public Task<ApiResponse<NotificationThread[]>> PollAsync(bool all, bool participating, int page, int perPage, string? ifModifiedSince, CancellationToken cancellationToken)
    {
        ListCalls.Add((all, participating, page, perPage));
        PollDates.Add(ifModifiedSince);

        HttpStatusCode status = HttpStatusCode.OK;
        NotificationThread[]? content = null;
        if (FailNextWith is { } failure)
        {
            FailNextWith = null;
            status = failure;
        }
        else if (NotModifiedSince is not null && ifModifiedSince == NotModifiedSince)
        {
            status = HttpStatusCode.NotModified;
        }
        else
        {
            content = _pages.Count > 0 ? _pages.Dequeue() : [];
        }

        HttpResponseMessage message = new(status) { RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/notifications") };
        message.Headers.TryAddWithoutValidation("X-Poll-Interval", PollInterval.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (content is not null)
        {
            message.Content = new StringContent("[]");
            message.Content.Headers.LastModified = LastModified;
        }

        return Task.FromResult(new ApiResponse<NotificationThread[]>(message, content, GitHubRefit.CreateSettings()));
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

internal sealed class FakePullsApi(PullRequest? pull = null) : IPullsApi
{
    private readonly Queue<PullFile[]> _filePages = new();

    public HttpStatusCode? FailNextWith { get; set; }

    public int Calls { get; private set; }

    public List<(string Owner, string Repo, int Number, int Page)> FileCalls { get; } = [];

    public FakePullsApi WithFiles(params PullFile[][] pages)
    {
        foreach (PullFile[] page in pages)
        {
            _filePages.Enqueue(page);
        }

        return this;
    }

    public Task<PullRequest> GetAsync(string owner, string repo, int pullNumber, CancellationToken cancellationToken)
    {
        Calls++;
        return pull is null
            ? Task.FromException<PullRequest>(Failure.Status(HttpStatusCode.BadGateway))
            : Task.FromResult(pull);
    }

    public Task<PullFile[]> ListFilesAsync(string owner, string repo, int pullNumber, int page, int perPage, CancellationToken cancellationToken)
    {
        FileCalls.Add((owner, repo, pullNumber, page));
        if (FailNextWith is { } status)
        {
            FailNextWith = null;
            return Task.FromException<PullFile[]>(Failure.Status(status));
        }

        return Task.FromResult(_filePages.Count > 0 ? _filePages.Dequeue() : []);
    }
}

/// <summary>Returns the queued result pages in order (an empty page once they run out) and records each query.</summary>
internal sealed class FakeSearchApi(params IssueSearchItem[][] pages) : ISearchApi
{
    private readonly Queue<IssueSearchItem[]> _pages = new(pages);

    public HttpStatusCode? FailNextWith { get; set; }

    public List<(string Q, string Sort, string Order, int Page, int PerPage)> Calls { get; } = [];

    public Task<IssueSearchResult> SearchIssuesAsync(string q, string sort, string order, int page, int perPage, CancellationToken cancellationToken)
    {
        Calls.Add((q, sort, order, page, perPage));
        if (FailNextWith is { } status)
        {
            FailNextWith = null;
            return Task.FromException<IssueSearchResult>(Failure.Status(status));
        }

        IssueSearchItem[] items = _pages.Count > 0 ? _pages.Dequeue() : [];
        return Task.FromResult(new IssueSearchResult(items.Length, false, items));
    }
}

internal sealed class FakeReposApi(params string[] fullNames) : IReposApi
{
    public HttpStatusCode? FailNextWith { get; set; }

    public int Calls { get; private set; }

    public Task<RepositorySummary[]> ListMineAsync(string sort, int page, int perPage, CancellationToken cancellationToken)
    {
        Calls++;
        if (FailNextWith is { } status)
        {
            FailNextWith = null;
            return Task.FromException<RepositorySummary[]>(Failure.Status(status));
        }

        RepositorySummary[] repositories = new RepositorySummary[fullNames.Length];
        for (int i = 0; i < fullNames.Length; i++)
        {
            string name = fullNames[i][(fullNames[i].IndexOf('/', StringComparison.Ordinal) + 1)..];
            repositories[i] = new RepositorySummary(i + 1, name, fullNames[i], false, "https://github.com/" + fullNames[i], Sample.Octocat);
        }

        return Task.FromResult(repositories);
    }
}

/// <summary>Returns queued run pages in order, one run for GetRun, and a fixed job list; records every call.</summary>
internal sealed class FakeActionsApi(params WorkflowRun[][] runPages) : IActionsApi
{
    private readonly Queue<WorkflowRun[]> _runPages = new(runPages);

    public HttpStatusCode? FailNextWith { get; set; }

    public WorkflowRun Run { get; set; } = Sample.Run(12, "completed", "failure");

    public Job[] Jobs { get; set; } = [];

    public List<(string Owner, string Repo, int Page)> RunCalls { get; } = [];

    public List<(string Owner, string Repo, long RunId)> JobCalls { get; } = [];

    public List<long> Reruns { get; } = [];

    public Task<WorkflowRunList> ListRunsAsync(string owner, string repo, int page, int perPage, CancellationToken cancellationToken)
    {
        RunCalls.Add((owner, repo, page));
        if (TakeFailure() is { } failure)
        {
            return Task.FromException<WorkflowRunList>(failure);
        }

        WorkflowRun[] runs = _runPages.Count > 0 ? _runPages.Dequeue() : [];
        return Task.FromResult(new WorkflowRunList(runs.Length, runs));
    }

    public Task<WorkflowRun> GetRunAsync(string owner, string repo, long runId, CancellationToken cancellationToken)
        => TakeFailure() is { } failure ? Task.FromException<WorkflowRun>(failure) : Task.FromResult(Run);

    public Task<JobList> ListJobsAsync(string owner, string repo, long runId, int page, int perPage, CancellationToken cancellationToken)
    {
        JobCalls.Add((owner, repo, runId));
        return TakeFailure() is { } failure
            ? Task.FromException<JobList>(failure)
            : Task.FromResult(new JobList(Jobs.Length, Jobs));
    }

    public string Log { get; set; } = string.Empty;

    public List<long> LogCalls { get; } = [];

    public Task<string> GetJobLogsAsync(string owner, string repo, long jobId, CancellationToken cancellationToken)
    {
        LogCalls.Add(jobId);
        return TakeFailure() is { } failure ? Task.FromException<string>(failure) : Task.FromResult(Log);
    }

    public Task RerunAsync(string owner, string repo, long runId, CancellationToken cancellationToken)
    {
        if (TakeFailure() is { } failure)
        {
            return Task.FromException(failure);
        }

        Reruns.Add(runId);
        return Task.CompletedTask;
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
