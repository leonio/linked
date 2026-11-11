using System.Net;
using Guit.Core.Models;
using Guit.Core.ViewModels;

namespace Guit.Core.Tests;

public sealed class JobLogViewModelTests
{
    private static JobLogViewModel Create(FakeActionsApi actions) => new(actions, new FakeNavigator(), new FakeLauncher());

    [Test]
    public async Task Load_shows_the_cleaned_lines()
    {
        FakeActionsApi actions = new() { Log = "2026-01-02T00:00:10.1234567Z ##[group]Run build\n2026-01-02T00:00:11.1234567Z ok\n" };
        JobLogViewModel viewModel = Create(actions);

        await viewModel.LoadCommand.ExecuteAsync(new JobLogLink("octocat", "hello", 7, "build").ToArgument());

        await Assert.That(viewModel.Title).IsEqualTo("build");
        await Assert.That(viewModel.Subtitle).IsEqualTo("octocat/hello");
        await Assert.That(string.Join("|", viewModel.Lines)).IsEqualTo("▸ Run build|ok");
        await Assert.That(actions.LogCalls).Contains(7L);
        await Assert.That(viewModel.Notice).IsNull();
        await Assert.That(viewModel.IsLoading).IsFalse();
    }

    [Test]
    public async Task A_long_log_keeps_its_end_and_says_so()
    {
        FakeActionsApi actions = new() { Log = string.Join("\n", Enumerable.Range(1, JobLogViewModel.MaxLines + 10).Select(i => $"line {i}")) };
        JobLogViewModel viewModel = Create(actions);

        await viewModel.LoadCommand.ExecuteAsync("octocat/hello@7\tbuild");

        await Assert.That(viewModel.Lines.Count).IsEqualTo(JobLogViewModel.MaxLines);
        await Assert.That(viewModel.Lines[^1]).IsEqualTo($"line {JobLogViewModel.MaxLines + 10}");
        await Assert.That(viewModel.Notice).IsNotNull();
    }

    [Test]
    public async Task A_job_that_has_not_finished_explains_why_there_is_no_log()
    {
        JobLogViewModel viewModel = Create(new FakeActionsApi { FailNextWith = HttpStatusCode.NotFound });

        await viewModel.LoadCommand.ExecuteAsync("octocat/hello@7\tbuild");

        await Assert.That(viewModel.ErrorMessage).IsEqualTo("No log yet. GitHub publishes a job's log when the job finishes.");
        await Assert.That(viewModel.Lines.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(null)]
    [Arguments("octocat/hello@7")]
    [Arguments("nope\tbuild")]
    public async Task A_malformed_argument_is_reported(string? argument)
    {
        FakeActionsApi actions = new();
        JobLogViewModel viewModel = Create(actions);

        await viewModel.LoadCommand.ExecuteAsync(argument);

        await Assert.That(viewModel.ErrorMessage).IsEqualTo("This link is not valid.");
        await Assert.That(actions.LogCalls.Count).IsEqualTo(0);
    }
}
