using System.Net;
using Guit.Core.ViewModels;

namespace Guit.Core.Tests;

public sealed class ConversationViewModelTests
{
    private static readonly FixedTime Time = new(Sample.Now);

    private static ConversationViewModel Create(FakeIssuesApi api, FakeNavigator? navigator = null, FakeLauncher? launcher = null)
        => new(api, navigator ?? new FakeNavigator(), launcher ?? new FakeLauncher(), Time);

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
}
