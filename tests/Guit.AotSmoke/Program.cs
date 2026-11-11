using System.Net;
using System.Text;
using Guit.Api.Actions;
using Guit.Api.Issues;
using Guit.Api.Notifications;
using Guit.Api.Pulls;
using Guit.Api.Reactions;
using Guit.Api.Repos;
using Guit.Api.Search;
using Guit.Api.Users;
using Guit.Core;
using Guit.Core.Services;
using Guit.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

// Runs the same DI registrations the app uses as a Native AOT binary, with the network replaced
// by canned replies. A plain build cannot prove code survives trimming; this can.
// Add one Check for every new API interface and every new view model. Exit code 0 means pass.

ServiceCollection services = new();
services.AddSingleton<ITokenStore, MemoryTokenStore>();
services.AddSingleton<ILauncher, NullLauncher>();
services.AddSingleton<INavigator, NullNavigator>();
services.AddSingleton<IUiDispatcher, InlineDispatcher>();
services.AddSingleton(new GitHubOptions("smoke-client"));
services.AddGuitCore(static () => new CannedHandler());

await using ServiceProvider provider = services.BuildServiceProvider();

SignInViewModel signIn = provider.GetRequiredService<SignInViewModel>();
await signIn.SignInCommand.ExecuteAsync(null);
Check(provider.GetRequiredService<Session>().IsSignedIn, "device flow sign-in");

GitHubUser user = await provider.GetRequiredService<IUsersApi>().GetAuthenticatedAsync(CancellationToken.None);
Check(user is { Login: "octocat", Id: 583231 }, "users api");

INotificationsApi notifications = provider.GetRequiredService<INotificationsApi>();
NotificationThread[] threads = await notifications.ListAsync(all: false, participating: false, page: 1, perPage: 50, CancellationToken.None);
Check(threads is [{ Id: "1", Subject.Type: "PullRequest", Repository.Owner.Login: "octocat" }], "notifications api: list");
await notifications.MarkAsReadAsync("1", CancellationToken.None);
Check(true, "notifications api: mark as read");

IIssuesApi issues = provider.GetRequiredService<IIssuesApi>();
Issue issue = await issues.GetAsync("octocat", "hello", 7, CancellationToken.None);
Check(issue is { Number: 7, PullRequest: not null, Reactions.PlusOne: 2, User.Login: "octocat" }, "issues api: get");
IssueComment[] comments = await issues.ListCommentsAsync("octocat", "hello", 7, page: 1, perPage: 50, CancellationToken.None);
Check(comments is [{ Id: 11, BodyText: "Looks good", User: null }], "issues api: list comments");
IssueComment posted = await issues.CreateCommentAsync("octocat", "hello", 7, new CreateCommentRequest("Thanks"), CancellationToken.None);
Check(posted is { Id: 12, BodyText: "Thanks" }, "issues api: create comment");

ConversationViewModel conversation = provider.GetRequiredService<ConversationViewModel>();
await conversation.LoadCommand.ExecuteAsync("octocat/hello!7");
Check(conversation is { Title: "Fix it", IsPullRequest: true, HasPullDetails: true, Comments: [{ IsOpeningPost: true, Author: "octocat" }, { Author: "ghost", Body: "Looks good" }] }, "conversation view model");
await provider.GetRequiredService<IReactionsApi>().CreateForIssueCommentAsync("octocat", "hello", 11, new CreateReactionRequest(ReactionContent.Heart), CancellationToken.None);
Check(true, "reactions api: create");
PullRequest pull = await provider.GetRequiredService<IPullsApi>().GetAsync("octocat", "hello", 7, CancellationToken.None);
Check(pull is { Merged: false, Additions: 120, Head.Ref: "feature", Base.Ref: "main" }, "pulls api: get");
IssueSearchResult found = await provider.GetRequiredService<ISearchApi>().SearchIssuesAsync("is:open is:pr author:@me", "updated", "desc", 1, 50, CancellationToken.None);
Check(found is { TotalCount: 1, Items: [{ Number: 7, RepositoryUrl: "https://api.github.com/repos/octocat/hello" }] }, "search api: issues");
PullRequestsViewModel pullRequests = provider.GetRequiredService<PullRequestsViewModel>();
await pullRequests.LoadCommand.ExecuteAsync(null);
Check(pullRequests.Items is [{ Reference: "octocat/hello #7", Argument: "octocat/hello!7", IsDraft: false }], "pull requests view model");

Check(conversation.Comments[0].Content.Blocks is [{ Inlines: [{ Text: "Please " }, { Text: "review", Style: Guit.Core.Text.RichStyles.Bold }] }], "rich text from body_html");
RepositorySummary[] mine = await provider.GetRequiredService<IReposApi>().ListMineAsync("pushed", 1, 50, CancellationToken.None);
Check(mine is [{ FullName: "octocat/hello" }], "repos api: list mine");
IActionsApi actions = provider.GetRequiredService<IActionsApi>();
WorkflowRunList runs = await actions.ListRunsAsync("octocat", "hello", 1, 30, CancellationToken.None);
Check(runs is { TotalCount: 1, WorkflowRuns: [{ Id: 30433642, Conclusion: null, Status: "in_progress" }] }, "actions api: list runs");
JobList jobs = await actions.ListJobsAsync("octocat", "hello", 30433642, 1, 100, CancellationToken.None);
Check(jobs is { Jobs: [{ Name: "build", Conclusion: "success", CompletedAt: not null }] }, "actions api: list jobs");
WorkflowRun oneRun = await actions.GetRunAsync("octocat", "hello", 30433642, CancellationToken.None);
Check(oneRun is { RunNumber: 12, Name: "CI" }, "actions api: get run");
await actions.RerunAsync("octocat", "hello", 30433642, CancellationToken.None);
Check(true, "actions api: rerun");
RunsViewModel runsViewModel = provider.GetRequiredService<RunsViewModel>();
await runsViewModel.LoadRepositoriesCommand.ExecuteAsync(null);
Check(runsViewModel is { SelectedRepository: "octocat/hello", Runs: [{ StatusKind: "running", Detail: "CI #12 \u00B7 push \u00B7 main" }] }, "runs view model");
JobsViewModel jobsViewModel = provider.GetRequiredService<JobsViewModel>();
await jobsViewModel.LoadCommand.ExecuteAsync("octocat/hello@30433642");
Check(jobsViewModel is { Title: "Fix it", StatusKind: "running", Jobs: [{ Name: "build", Duration: "2m 05s" }] }, "jobs view model");

InboxViewModel inbox = provider.GetRequiredService<InboxViewModel>();
await inbox.LoadCommand.ExecuteAsync(null);
Check(inbox.Items is [{ Title: "Fix it", Repository: "octocat/hello", IsUnread: true }], "inbox view model");

ShellViewModel shell = provider.GetRequiredService<ShellViewModel>();
await shell.LoadUserCommand.ExecuteAsync(null);
Check(shell is { IsSignedIn: true, Login: "octocat" }, "shell view model");

// Last, because it signs the session out: a 401 from the API must end the session.
try
{
    await notifications.MarkAsDoneAsync("revoked", CancellationToken.None);
}
catch (Refit.ApiException)
{
}

Check(!provider.GetRequiredService<Session>().IsSignedIn && !shell.IsSignedIn, "401 signs out");

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

internal sealed class InlineDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

internal sealed class NullNavigator : INavigator
{
    public bool CanGoBack => false;

    public void Navigate(Route route, string? argument = null)
    {
    }

    public void GoBack()
    {
    }
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
    private static string? ReadBody(HttpRequestMessage request)
        => request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        bool authorized = request.Headers.Authorization is { Scheme: "Bearer", Parameter: "gho_smoke" };
        string? json = request.RequestUri?.AbsolutePath switch
        {
            "/login/device/code" => """{"device_code":"dev","user_code":"AAAA-BBBB","verification_uri":"https://github.com/login/device","expires_in":900,"interval":0}""",
            "/login/oauth/access_token" => """{"access_token":"gho_smoke","token_type":"bearer","scope":"repo"}""",
            "/user" when authorized => """{"login":"octocat","id":583231,"avatar_url":"https://avatars/1","html_url":"https://github.com/octocat"}""",
            "/notifications" when authorized => """[{"id":"1","unread":true,"reason":"review_requested","updated_at":"2026-01-02T03:04:05Z","last_read_at":null,"subject":{"title":"Fix it","type":"PullRequest","url":"https://api.github.com/repos/octocat/hello/pulls/7","latest_comment_url":null},"repository":{"id":9,"name":"hello","full_name":"octocat/hello","private":false,"html_url":"https://github.com/octocat/hello","owner":{"login":"octocat","id":583231,"avatar_url":"https://avatars/1","html_url":"https://github.com/octocat"}}}]""",
            "/repos/octocat/hello/issues/7" when authorized => """{"id":70,"number":7,"title":"Fix it","state":"open","comments":1,"created_at":"2026-01-01T00:00:00Z","html_url":"https://github.com/octocat/hello/pull/7","user":{"login":"octocat","id":583231,"avatar_url":"https://avatars/1","html_url":"https://github.com/octocat"},"body_text":"Please review","body_html":"<p>Please <strong>review</strong></p>","pull_request":{"html_url":"https://github.com/octocat/hello/pull/7"},"reactions":{"total_count":2,"+1":2,"-1":0,"laugh":0,"confused":0,"heart":0,"hooray":0,"eyes":0,"rocket":0}}""",
            "/repos/octocat/hello/issues/7/comments" when authorized && request.Method == HttpMethod.Get => """[{"id":11,"created_at":"2026-01-02T00:00:00Z","html_url":"https://github.com/octocat/hello/pull/7#issuecomment-11","user":null,"body_text":"Looks good","body_html":"<p>Looks good</p>","author_association":"MEMBER"}]""",
            "/repos/octocat/hello/issues/7/comments" when authorized && request.Method == HttpMethod.Post && ReadBody(request) == """{"body":"Thanks"}"""
                => """{"id":12,"created_at":"2026-01-03T00:00:00Z","html_url":"https://github.com/octocat/hello/pull/7#issuecomment-12","user":{"login":"octocat","id":583231,"avatar_url":"https://avatars/1","html_url":"https://github.com/octocat"},"body_text":"Thanks","body_html":"<p>Thanks</p>"}""",
            "/repos/octocat/hello/issues/comments/11/reactions" when authorized && ReadBody(request) == """{"content":"heart"}""" => """{"id":1,"content":"heart"}""",
            "/repos/octocat/hello/pulls/7" when authorized => """{"number":7,"state":"open","merged":false,"draft":false,"mergeable":null,"additions":120,"deletions":8,"changed_files":5,"head":{"ref":"feature","label":"octocat:feature"},"base":{"ref":"main","label":"octocat:main"}}""",
            "/search/issues" when authorized => """{"total_count":1,"incomplete_results":false,"items":[{"id":70,"number":7,"title":"Fix it","state":"open","comments":2,"updated_at":"2026-01-02T00:00:00Z","html_url":"https://github.com/octocat/hello/pull/7","repository_url":"https://api.github.com/repos/octocat/hello","user":{"login":"octocat","id":583231,"avatar_url":"https://avatars/1","html_url":"https://github.com/octocat"},"draft":false}]}""",
            "/user/repos" when authorized => """[{"id":9,"name":"hello","full_name":"octocat/hello","private":false,"html_url":"https://github.com/octocat/hello","owner":{"login":"octocat","id":583231,"avatar_url":"https://avatars/1","html_url":"https://github.com/octocat"}}]""",
            "/repos/octocat/hello/actions/runs" when authorized => """{"total_count":1,"workflow_runs":[{"id":30433642,"name":"CI","display_title":"Fix it","run_number":12,"event":"push","status":"in_progress","conclusion":null,"head_branch":"main","created_at":"2026-01-02T00:00:00Z","updated_at":"2026-01-02T00:01:00Z","html_url":"https://github.com/octocat/hello/actions/runs/30433642","actor":{"login":"octocat","id":583231,"avatar_url":"https://avatars/1","html_url":"https://github.com/octocat"}}]}""",
            "/repos/octocat/hello/actions/runs/30433642" when authorized => """{"id":30433642,"name":"CI","display_title":"Fix it","run_number":12,"event":"push","status":"in_progress","conclusion":null,"head_branch":"main","created_at":"2026-01-02T00:00:00Z","updated_at":"2026-01-02T00:01:00Z","html_url":"https://github.com/octocat/hello/actions/runs/30433642"}""",
            "/repos/octocat/hello/actions/runs/30433642/jobs" when authorized => """{"total_count":1,"jobs":[{"id":399444496,"name":"build","status":"completed","conclusion":"success","started_at":"2026-01-02T00:00:10Z","completed_at":"2026-01-02T00:02:15Z","html_url":"https://github.com/octocat/hello/actions/runs/30433642/job/399444496"}]}""",
            "/repos/octocat/hello/actions/runs/30433642/rerun" when authorized && request.Method == HttpMethod.Post => "{}",
            "/notifications/threads/1" when authorized => string.Empty,
            _ => null,
        };

        if (request.RequestUri?.AbsolutePath == "/notifications/threads/revoked")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { RequestMessage = request });
        }

        HttpResponseMessage response = json is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : json.Length == 0
            ? new HttpResponseMessage(HttpStatusCode.ResetContent)
            : new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(json, Encoding.UTF8, "application/json") };
        return Task.FromResult(response);
    }
}
