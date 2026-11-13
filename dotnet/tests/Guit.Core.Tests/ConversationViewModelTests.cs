using System.Net;
using Guit.Api.Reactions;
using Guit.Core.Text;
using Guit.Core.ViewModels;

namespace Guit.Core.Tests;

public sealed class ConversationViewModelTests
{
    private static readonly FixedTime Time = new(Sample.Now);

    private static ConversationViewModel Create(
        FakeIssuesApi api,
        FakeNavigator? navigator = null,
        FakeLauncher? launcher = null,
        FakeReactionsApi? reactions = null,
        FakePullsApi? pulls = null)
        => new(api, pulls ?? new FakePullsApi(Sample.Pull()), reactions ?? new FakeReactionsApi(), navigator ?? new FakeNavigator(), launcher ?? new FakeLauncher(), Time);

    [Test]
    public async Task Load_fills_the_header_and_lists_the_opening_post_first()
    {
        FakeIssuesApi api = new(Sample.Issue(), [Sample.Comment(11, "First reply"), Sample.Comment(12, "Second reply")]);
        ConversationViewModel viewModel = Create(api);

        await viewModel.LoadCommand.ExecuteAsync("octocat/hello!7");

        await Assert.That(viewModel.Title).IsEqualTo("Fix it");
        await Assert.That(viewModel.Subtitle).IsEqualTo("octocat/hello #7");
        await Assert.That(viewModel.State).IsEqualTo("open");
        await Assert.That(viewModel.IsPullRequest).IsTrue();
        await Assert.That(viewModel.Comments.Count).IsEqualTo(3);
        await Assert.That(viewModel.Comments[0].IsOpeningPost).IsTrue();
        await Assert.That(viewModel.Comments[0].Body).IsEqualTo("Please review");
        await Assert.That(viewModel.Comments[0].Content.Blocks[0].Inlines[1]).IsEqualTo(new RichInline("review", RichStyles.Bold));
        await Assert.That(viewModel.Comments[1].Body).IsEqualTo("First reply");
        await Assert.That(viewModel.Comments[1].Association).IsEqualTo("member");
        await Assert.That(viewModel.Comments[1].Posted).IsEqualTo("3h");
        await Assert.That(viewModel.Comments[2].Id).IsEqualTo(12L);
        await Assert.That(api.ListCalls[0]).IsEqualTo(("octocat", "hello", 7, 1));
        await Assert.That(viewModel.IsLoading).IsFalse();
    }

    [Test]
    public async Task A_malformed_argument_is_reported_without_calling_the_api()
    {
        FakeIssuesApi api = new(Sample.Issue());
        ConversationViewModel viewModel = Create(api);

        await viewModel.LoadCommand.ExecuteAsync("not-a-link");

        await Assert.That(viewModel.ErrorMessage).IsEqualTo("This link is not valid.");
        await Assert.That(api.ListCalls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task A_failed_load_sets_the_error_and_stops_loading()
    {
        FakeIssuesApi api = new(Sample.Issue()) { FailNextWith = HttpStatusCode.NotFound };
        ConversationViewModel viewModel = Create(api);

        await viewModel.LoadCommand.ExecuteAsync("octocat/hello#7");

        await Assert.That(viewModel.ErrorMessage).IsEqualTo("Not found. It may have been deleted, or you may not have access.");
        await Assert.That(viewModel.Comments.Count).IsEqualTo(0);
        await Assert.That(viewModel.IsLoading).IsFalse();
    }

    [Test]
    public async Task LoadMore_appends_the_next_page_of_comments()
    {
        FakeIssuesApi api = new(Sample.Issue(), Sample.Comments(50), Sample.Comments(2, firstId: 51));
        ConversationViewModel viewModel = Create(api);
        await viewModel.LoadCommand.ExecuteAsync("octocat/hello#7");
        await Assert.That(viewModel.HasMore).IsTrue();

        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        await Assert.That(viewModel.Comments.Count).IsEqualTo(53);
        await Assert.That(viewModel.Comments[^1].Id).IsEqualTo(52L);
        await Assert.That(api.ListCalls[1].Page).IsEqualTo(2);
        await Assert.That(viewModel.HasMore).IsFalse();
    }

    [Test]
    public async Task A_deleted_author_is_shown_as_ghost()
    {
        FakeIssuesApi api = new(Sample.Issue(deletedUser: true), [Sample.Comment(11, association: "NONE", deletedUser: true)]);
        ConversationViewModel viewModel = Create(api);

        await viewModel.LoadCommand.ExecuteAsync("octocat/hello#7");

        await Assert.That(viewModel.Comments[0].Author).IsEqualTo("ghost");
        await Assert.That(viewModel.Comments[1].Author).IsEqualTo("ghost");
        await Assert.That(viewModel.Comments[1].AvatarUrl).IsEqualTo(string.Empty);
        await Assert.That(viewModel.Comments[1].Association).IsNull();
    }

    [Test]
    public async Task GoBack_and_OpenInBrowser_use_the_app_services()
    {
        FakeNavigator navigator = new();
        FakeLauncher launcher = new();
        ConversationViewModel viewModel = Create(new FakeIssuesApi(Sample.Issue()), navigator, launcher);
        await viewModel.LoadCommand.ExecuteAsync("octocat/hello!7");

        viewModel.GoBackCommand.Execute(null);
        await viewModel.OpenInBrowserCommand.ExecuteAsync(null);

        await Assert.That(navigator.BackCount).IsEqualTo(1);
        await Assert.That(launcher.Opened).IsEqualTo(new Uri("https://github.com/octocat/hello/pull/7"));
    }

    [Test]
    public async Task Send_is_only_possible_with_a_loaded_conversation_and_some_text()
    {
        ConversationViewModel viewModel = Create(new FakeIssuesApi(Sample.Issue()));
        viewModel.Draft = "hello";
        await Assert.That(viewModel.SendCommand.CanExecute(null)).IsFalse();

        await viewModel.LoadCommand.ExecuteAsync("octocat/hello#7");
        await Assert.That(viewModel.SendCommand.CanExecute(null)).IsTrue();

        viewModel.Draft = "   ";
        await Assert.That(viewModel.SendCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task Send_appends_the_new_comment_and_clears_the_draft()
    {
        FakeIssuesApi api = new(Sample.Issue());
        ConversationViewModel viewModel = Create(api);
        await viewModel.LoadCommand.ExecuteAsync("octocat/hello#7");
        viewModel.Draft = "  Thanks!  ";

        await viewModel.SendCommand.ExecuteAsync(null);

        await Assert.That(api.Posted).Contains("Thanks!");
        await Assert.That(viewModel.Comments[^1].Body).IsEqualTo("Thanks!");
        await Assert.That(viewModel.Draft).IsEqualTo(string.Empty);
        await Assert.That(viewModel.IsSending).IsFalse();
    }

    [Test]
    public async Task A_failed_send_keeps_the_draft()
    {
        FakeIssuesApi api = new(Sample.Issue());
        ConversationViewModel viewModel = Create(api);
        await viewModel.LoadCommand.ExecuteAsync("octocat/hello#7");
        viewModel.Draft = "Thanks!";
        api.FailNextWith = HttpStatusCode.Forbidden;

        await viewModel.SendCommand.ExecuteAsync(null);

        await Assert.That(viewModel.Draft).IsEqualTo("Thanks!");
        await Assert.That(viewModel.Comments.Count).IsEqualTo(1);
        await Assert.That(viewModel.ErrorMessage).IsNotNull();
        await Assert.That(viewModel.IsSending).IsFalse();
    }

    [Test]
    public async Task Reaction_counts_are_summarised_in_GitHub_order_without_zeroes()
    {
        FakeIssuesApi api = new(Sample.Issue(), [Sample.Comment(11, reactions: Sample.Reactions(plusOne: 3, heart: 1)), Sample.Comment(12)]);
        ConversationViewModel viewModel = Create(api);

        await viewModel.LoadCommand.ExecuteAsync("octocat/hello#7");

        await Assert.That(viewModel.Comments[1].ReactionSummary).IsEqualTo("\U0001F44D 3  \u2764\uFE0F 1");
        await Assert.That(viewModel.Comments[1].HasReactions).IsTrue();
        await Assert.That(viewModel.Comments[2].ReactionSummary).IsEqualTo(string.Empty);
        await Assert.That(viewModel.Comments[2].HasReactions).IsFalse();
    }

    [Test]
    public async Task React_bumps_the_count_and_calls_the_right_endpoint()
    {
        FakeReactionsApi reactions = new();
        FakeIssuesApi api = new(Sample.Issue(), [Sample.Comment(11)]);
        ConversationViewModel viewModel = Create(api, reactions: reactions);
        await viewModel.LoadCommand.ExecuteAsync("octocat/hello#7");

        await viewModel.ReactAsync(viewModel.Comments[1], ReactionContent.Rocket);
        await viewModel.ReactAsync(viewModel.Comments[0], ReactionContent.Heart);

        await Assert.That(viewModel.Comments[1].ReactionSummary).IsEqualTo("\U0001F680 1");
        await Assert.That(reactions.Calls).Contains("comment:11:rocket");
        await Assert.That(reactions.Calls).Contains("issue:7:heart");
    }

    [Test]
    public async Task A_refused_reaction_is_taken_back()
    {
        FakeReactionsApi reactions = new() { FailNextWith = HttpStatusCode.Forbidden };
        FakeIssuesApi api = new(Sample.Issue(), [Sample.Comment(11, reactions: Sample.Reactions(plusOne: 2))]);
        ConversationViewModel viewModel = Create(api, reactions: reactions);
        await viewModel.LoadCommand.ExecuteAsync("octocat/hello#7");

        await viewModel.ReactAsync(viewModel.Comments[1], ReactionContent.PlusOne);

        await Assert.That(viewModel.Comments[1].ReactionSummary).IsEqualTo("\U0001F44D 2");
        await Assert.That(viewModel.ErrorMessage).IsNotNull();
    }

    [Test]
    [Arguments("open", false, false, "open")]
    [Arguments("open", false, true, "draft")]
    [Arguments("closed", false, true, "closed")]
    [Arguments("closed", true, false, "merged")]
    public async Task A_pull_request_refines_the_state(string state, bool merged, bool draft, string expected)
    {
        ConversationViewModel viewModel = Create(new FakeIssuesApi(Sample.Issue(state: state)), pulls: new FakePullsApi(Sample.Pull(state, merged, draft)));

        await viewModel.LoadCommand.ExecuteAsync("octocat/hello!7");

        await Assert.That(viewModel.State).IsEqualTo(expected);
    }

    [Test]
    public async Task A_pull_request_shows_branches_and_diff_size()
    {
        ConversationViewModel viewModel = Create(new FakeIssuesApi(Sample.Issue()));

        await viewModel.LoadCommand.ExecuteAsync("octocat/hello!7");

        await Assert.That(viewModel.Branches).IsEqualTo("feature \u2192 main");
        await Assert.That(viewModel.DiffSummary).IsEqualTo("+120 \u22128 in 5 files");
        await Assert.That(viewModel.HasPullDetails).IsTrue();
    }

    [Test]
    public async Task A_pull_request_from_a_fork_names_the_source_owner()
    {
        ConversationViewModel viewModel = Create(new FakeIssuesApi(Sample.Issue()), pulls: new FakePullsApi(Sample.Pull(headLabel: "contributor:feature")));

        await viewModel.LoadCommand.ExecuteAsync("octocat/hello!7");

        await Assert.That(viewModel.Branches).IsEqualTo("contributor:feature \u2192 main");
    }

    [Test]
    public async Task A_failed_pull_request_lookup_still_shows_the_conversation()
    {
        ConversationViewModel viewModel = Create(new FakeIssuesApi(Sample.Issue(), [Sample.Comment(11)]), pulls: new FakePullsApi(null));

        await viewModel.LoadCommand.ExecuteAsync("octocat/hello!7");

        await Assert.That(viewModel.ErrorMessage).IsNull();
        await Assert.That(viewModel.Comments.Count).IsEqualTo(2);
        await Assert.That(viewModel.State).IsEqualTo("open");
        await Assert.That(viewModel.HasPullDetails).IsFalse();
    }

    [Test]
    public async Task A_plain_issue_does_not_ask_for_pull_request_details()
    {
        FakePullsApi pulls = new(Sample.Pull());
        ConversationViewModel viewModel = Create(new FakeIssuesApi(Sample.Issue(isPullRequest: false)), pulls: pulls);

        await viewModel.LoadCommand.ExecuteAsync("octocat/hello#7");

        await Assert.That(pulls.Calls).IsEqualTo(0);
        await Assert.That(viewModel.IsPullRequest).IsFalse();
    }

    [Test]
    public async Task ShowFiles_opens_the_changed_files_of_a_pull_request_only()
    {
        FakeNavigator navigator = new();
        ConversationViewModel pull = Create(new FakeIssuesApi(Sample.Issue()), navigator);
        await pull.LoadCommand.ExecuteAsync("octocat/hello!7");
        pull.ShowFilesCommand.Execute(null);
        await Assert.That(navigator.Navigations[0]).IsEqualTo((Guit.Core.Services.Route.PullFiles, (string?)"octocat/hello!7"));

        FakeNavigator other = new();
        ConversationViewModel issue = Create(new FakeIssuesApi(Sample.Issue(isPullRequest: false)), other);
        await issue.LoadCommand.ExecuteAsync("octocat/hello#7");
        issue.ShowFilesCommand.Execute(null);
        await Assert.That(other.Navigations.Count).IsEqualTo(0);
    }
}
