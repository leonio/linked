using System.Net;
using Guit.Core.Models;
using Guit.Core.ViewModels;

namespace Guit.Core.Tests;

public sealed class JobsViewModelTests
{
    private static JobsViewModel Create(FakeActionsApi actions, FakeNavigator? navigator = null, FakeLauncher? launcher = null)
        => new(actions, navigator ?? new FakeNavigator(), launcher ?? new FakeLauncher(), new FixedTime(Sample.Now));

    [Test]
    public async Task Load_fills_the_header_and_the_jobs()
    {
        FakeActionsApi actions = new() { Jobs = [Sample.Job(1, "build", seconds: 125), Sample.Job(2, "test", "in_progress", null, null)] };
        JobsViewModel viewModel = Create(actions);

        await viewModel.LoadCommand.ExecuteAsync("octocat/hello@30433012");

        await Assert.That(viewModel.Title).IsEqualTo("Fix it");
        await Assert.That(viewModel.Subtitle).IsEqualTo("octocat/hello · CI #12 · push · main");
        await Assert.That(viewModel.StatusKind).IsEqualTo(RunStatus.Failure);
        await Assert.That(viewModel.Jobs.Count).IsEqualTo(2);
        await Assert.That(viewModel.Jobs[0].Duration).IsEqualTo("2m 05s");
        await Assert.That(viewModel.Jobs[1].StatusKind).IsEqualTo(RunStatus.Running);
        await Assert.That(actions.JobCalls[0]).IsEqualTo(("octocat", "hello", 30433012L));
        await Assert.That(viewModel.IsLoading).IsFalse();
    }

    [Test]
    public async Task A_malformed_argument_is_reported_and_rerun_stays_disabled()
    {
        FakeActionsApi actions = new();
        JobsViewModel viewModel = Create(actions);

        await viewModel.LoadCommand.ExecuteAsync("nope");

        await Assert.That(viewModel.ErrorMessage).IsEqualTo("This link is not valid.");
        await Assert.That(actions.JobCalls.Count).IsEqualTo(0);
        await Assert.That(viewModel.RerunCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task A_failed_load_sets_the_error()
    {
        JobsViewModel viewModel = Create(new FakeActionsApi { FailNextWith = HttpStatusCode.NotFound });

        await viewModel.LoadCommand.ExecuteAsync("octocat/hello@5");

        await Assert.That(viewModel.ErrorMessage).IsNotNull();
        await Assert.That(viewModel.IsLoading).IsFalse();
    }

    [Test]
    public async Task Rerun_asks_GitHub_then_reloads()
    {
        FakeActionsApi actions = new() { Jobs = [Sample.Job(1)] };
        JobsViewModel viewModel = Create(actions);
        await viewModel.LoadCommand.ExecuteAsync("octocat/hello@30433012");

        await viewModel.RerunCommand.ExecuteAsync(null);

        await Assert.That(actions.Reruns).Contains(30433012L);
        await Assert.That(actions.JobCalls.Count).IsEqualTo(2);
        await Assert.That(viewModel.IsRerunning).IsFalse();
        await Assert.That(viewModel.ErrorMessage).IsNull();
    }

    [Test]
    public async Task A_refused_rerun_sets_the_error_and_does_not_reload()
    {
        FakeActionsApi actions = new() { Jobs = [Sample.Job(1)] };
        JobsViewModel viewModel = Create(actions);
        await viewModel.LoadCommand.ExecuteAsync("octocat/hello@30433012");
        actions.FailNextWith = HttpStatusCode.Forbidden;

        await viewModel.RerunCommand.ExecuteAsync(null);

        await Assert.That(viewModel.ErrorMessage).IsEqualTo("GitHub refused this request. The app may need more permissions.");
        await Assert.That(actions.JobCalls.Count).IsEqualTo(1);
        await Assert.That(viewModel.IsRerunning).IsFalse();
    }

    [Test]
    public async Task Jobs_and_the_run_open_on_github()
    {
        FakeLauncher launcher = new();
        FakeNavigator navigator = new();
        JobsViewModel viewModel = Create(new FakeActionsApi { Jobs = [Sample.Job(7)] }, navigator, launcher);
        await viewModel.LoadCommand.ExecuteAsync("octocat/hello@30433012");

        await viewModel.OpenJobCommand.ExecuteAsync(viewModel.Jobs[0]);
        await Assert.That(launcher.Opened).IsEqualTo(new Uri("https://github.com/octocat/hello/actions/runs/30433012/job/7"));

        await viewModel.OpenInBrowserCommand.ExecuteAsync(null);
        await Assert.That(launcher.Opened).IsEqualTo(new Uri("https://github.com/octocat/hello/actions/runs/30433012"));

        viewModel.GoBackCommand.Execute(null);
        await Assert.That(navigator.BackCount).IsEqualTo(1);
    }
}
