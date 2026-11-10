using System.Net;
using Guit.Core.Models;
using Guit.Core.Services;
using Guit.Core.ViewModels;

namespace Guit.Core.Tests;

public sealed class PullRequestsViewModelTests
{
    private static PullRequestsViewModel Create(FakeSearchApi api, FakeNavigator? navigator = null, FakeLauncher? launcher = null)
        => new(api, navigator ?? new FakeNavigator(), launcher ?? new FakeLauncher(), new FixedTime(Sample.Now));

    [Test]
    public async Task Load_maps_the_rows()
    {
        FakeSearchApi api = new([Sample.Found(7), Sample.Found(8, "Draft work", draft: true, deletedUser: true)]);
        PullRequestsViewModel viewModel = Create(api);

        await viewModel.LoadCommand.ExecuteAsync(null);

        await Assert.That(viewModel.Items.Count).IsEqualTo(2);
        await Assert.That(viewModel.Items[0].Title).IsEqualTo("Fix it");
        await Assert.That(viewModel.Items[0].Reference).IsEqualTo("octocat/hello #7");
        await Assert.That(viewModel.Items[0].Author).IsEqualTo("octocat");
        await Assert.That(viewModel.Items[0].Updated).IsEqualTo("3h");
        await Assert.That(viewModel.Items[0].Argument).IsEqualTo("octocat/hello!7");
        await Assert.That(viewModel.Items[1].IsDraft).IsTrue();
        await Assert.That(viewModel.Items[1].Author).IsEqualTo("ghost");
        await Assert.That(viewModel.IsEmpty).IsFalse();
    }

    [Test]
    [Arguments(PullRequestScope.ReviewRequested, "is:open is:pr review-requested:@me archived:false")]
    [Arguments(PullRequestScope.Created, "is:open is:pr author:@me archived:false")]
    [Arguments(PullRequestScope.Assigned, "is:open is:pr assignee:@me archived:false")]
    public async Task Each_scope_sends_its_query_sorted_by_update(PullRequestScope scope, string query)
    {
        FakeSearchApi api = new();
        PullRequestsViewModel viewModel = Create(api);

        if (scope == PullRequestScope.ReviewRequested)
        {
            await viewModel.LoadCommand.ExecuteAsync(null);
        }
        else
        {
            viewModel.Scope = scope;
        }

        await Assert.That(api.Calls[^1]).IsEqualTo((query, "updated", "desc", 1, 50));
    }

    [Test]
    public async Task LoadMore_appends_and_stops_after_a_short_page()
    {
        FakeSearchApi api = new(Sample.FoundMany(50), Sample.FoundMany(2, firstNumber: 51));
        PullRequestsViewModel viewModel = Create(api);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.LoadMoreCommand.ExecuteAsync(null);
        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        await Assert.That(viewModel.Items.Count).IsEqualTo(52);
        await Assert.That(api.Calls.Count).IsEqualTo(2);
        await Assert.That(api.Calls[1].Page).IsEqualTo(2);
    }

    [Test]
    public async Task A_failed_load_sets_the_error()
    {
        FakeSearchApi api = new() { FailNextWith = HttpStatusCode.Forbidden };
        PullRequestsViewModel viewModel = Create(api);

        await viewModel.LoadCommand.ExecuteAsync(null);

        await Assert.That(viewModel.ErrorMessage).IsNotNull();
        await Assert.That(viewModel.IsLoading).IsFalse();
        await Assert.That(viewModel.IsEmpty).IsFalse();
    }

    [Test]
    public async Task Open_navigates_to_the_conversation()
    {
        FakeNavigator navigator = new();
        PullRequestsViewModel viewModel = Create(new FakeSearchApi([Sample.Found(7)]), navigator);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.OpenCommand.ExecuteAsync(viewModel.Items[0]);

        await Assert.That(navigator.Navigations[0]).IsEqualTo((Route.Conversation, (string?)"octocat/hello!7"));
    }
}
