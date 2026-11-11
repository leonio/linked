using System.Net;
using Guit.Core.Services;
using Guit.Core.ViewModels;

namespace Guit.Core.Tests;

public sealed class RunsViewModelTests
{
    private static RunsViewModel Create(FakeReposApi repos, FakeActionsApi actions, FakeNavigator? navigator = null)
        => new(repos, actions, navigator ?? new FakeNavigator(), new FixedTime(Sample.Now));

    [Test]
    public async Task Loading_repositories_selects_the_first_and_loads_its_runs()
    {
        FakeActionsApi actions = new(Sample.Runs(2));
        RunsViewModel viewModel = Create(new FakeReposApi("octocat/hello", "octocat/other"), actions);

        await viewModel.LoadRepositoriesCommand.ExecuteAsync(null);

        await Assert.That(viewModel.Repositories.Count).IsEqualTo(2);
        await Assert.That(viewModel.SelectedRepository).IsEqualTo("octocat/hello");
        await Assert.That(actions.RunCalls[0]).IsEqualTo(("octocat", "hello", 1));
        await Assert.That(viewModel.Runs.Count).IsEqualTo(2);
        await Assert.That(viewModel.IsEmpty).IsFalse();
    }

    [Test]
    public async Task Repositories_are_only_fetched_once()
    {
        FakeReposApi repos = new("octocat/hello");
        RunsViewModel viewModel = Create(repos, new FakeActionsApi());

        await viewModel.LoadRepositoriesCommand.ExecuteAsync(null);
        await viewModel.LoadRepositoriesCommand.ExecuteAsync(null);

        await Assert.That(repos.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task Choosing_another_repository_replaces_the_runs()
    {
        FakeActionsApi actions = new(Sample.Runs(3), Sample.Runs(1, firstNumber: 40));
        RunsViewModel viewModel = Create(new FakeReposApi("octocat/hello", "octocat/other"), actions);
        await viewModel.LoadRepositoriesCommand.ExecuteAsync(null);

        viewModel.SelectedRepository = "octocat/other";

        await Assert.That(actions.RunCalls[^1]).IsEqualTo(("octocat", "other", 1));
        await Assert.That(viewModel.Runs.Count).IsEqualTo(1);
        await Assert.That(viewModel.Runs[0].Detail).IsEqualTo("CI #40 · push · main");
    }

    [Test]
    public async Task LoadMore_appends_the_next_page()
    {
        FakeActionsApi actions = new(Sample.Runs(30), Sample.Runs(4, firstNumber: 31));
        RunsViewModel viewModel = Create(new FakeReposApi("octocat/hello"), actions);
        await viewModel.LoadRepositoriesCommand.ExecuteAsync(null);

        await viewModel.LoadMoreCommand.ExecuteAsync(null);
        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        await Assert.That(viewModel.Runs.Count).IsEqualTo(34);
        await Assert.That(actions.RunCalls.Count).IsEqualTo(2);
        await Assert.That(actions.RunCalls[1].Page).IsEqualTo(2);
    }

    [Test]
    public async Task A_failed_run_list_sets_the_error()
    {
        FakeActionsApi actions = new() { FailNextWith = HttpStatusCode.NotFound };
        RunsViewModel viewModel = Create(new FakeReposApi("octocat/hello"), actions);

        await viewModel.LoadRepositoriesCommand.ExecuteAsync(null);

        await Assert.That(viewModel.ErrorMessage).IsNotNull();
        await Assert.That(viewModel.IsLoading).IsFalse();
    }

    [Test]
    public async Task A_failed_repository_list_sets_the_error_and_selects_nothing()
    {
        RunsViewModel viewModel = Create(new FakeReposApi("octocat/hello") { FailNextWith = HttpStatusCode.BadGateway }, new FakeActionsApi());

        await viewModel.LoadRepositoriesCommand.ExecuteAsync(null);

        await Assert.That(viewModel.ErrorMessage).IsNotNull();
        await Assert.That(viewModel.SelectedRepository).IsNull();
    }

    [Test]
    public async Task Open_navigates_to_the_run_detail()
    {
        FakeNavigator navigator = new();
        RunsViewModel viewModel = Create(new FakeReposApi("octocat/hello"), new FakeActionsApi([Sample.Run(12, id: 30433642)]), navigator);
        await viewModel.LoadRepositoriesCommand.ExecuteAsync(null);

        viewModel.OpenCommand.Execute(viewModel.Runs[0]);

        await Assert.That(navigator.Navigations[0]).IsEqualTo((Route.RunDetail, (string?)"octocat/hello@30433642"));
    }
}
