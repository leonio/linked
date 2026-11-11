using System.Net;
using Guit.Core.Models;
using Guit.Core.Services;
using Guit.Core.ViewModels;

namespace Guit.Core.Tests;

public sealed class JobsViewModelTests
{
    private static JobsViewModel Create(FakeActionsApi actions, FakeNavigator? navigator = null, FakeLauncher? launcher = null)
        => new(actions, navigator ?? new FakeNavigator(), launcher ?? new FakeLauncher(), new ImmediateDispatcher(), new FixedTime(Sample.Now));

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
        await Assert.That(viewModel.Notice).IsNotNull();
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
    public async Task A_job_opens_its_log_in_the_app_and_the_run_opens_on_github()
    {
        FakeLauncher launcher = new();
        FakeNavigator navigator = new();
        JobsViewModel viewModel = Create(new FakeActionsApi { Jobs = [Sample.Job(7, "build", steps: [Sample.Step(1, "Checkout")])] }, navigator, launcher);
        await viewModel.LoadCommand.ExecuteAsync("octocat/hello@30433012");

        viewModel.OpenJobCommand.Execute(viewModel.Jobs[1]);
        await Assert.That(navigator.Navigations[0]).IsEqualTo((Route.JobLog, (string?)"octocat/hello@7\tbuild"));

        await viewModel.OpenInBrowserCommand.ExecuteAsync(null);
        await Assert.That(launcher.Opened).IsEqualTo(new Uri("https://github.com/octocat/hello/actions/runs/30433012"));

        viewModel.GoBackCommand.Execute(null);
        await Assert.That(navigator.BackCount).IsEqualTo(1);
    }

    [Test]
    public async Task Steps_are_listed_under_their_job()
    {
        FakeActionsApi actions = new()
        {
            Jobs =
            [
                Sample.Job(1, "build", "in_progress", null, null, [Sample.Step(1, "Checkout", seconds: 3), Sample.Step(2, "Build", "in_progress", null, null), Sample.Step(3, "Test", "queued", null, null)]),
                Sample.Job(2, "lint"),
            ],
        };
        JobsViewModel viewModel = Create(actions);

        await viewModel.LoadCommand.ExecuteAsync("octocat/hello@30433012");

        await Assert.That(string.Join(",", viewModel.Jobs.Select(row => (row.IsStep ? ">" : "") + row.Name + ":" + row.Duration)))
            .IsEqualTo("build:running,>Checkout:3s,>Build:running,>Test:queued,lint:1m 00s");
        await Assert.That(viewModel.Jobs[2].JobId).IsEqualTo(1L);
        await Assert.That(viewModel.Jobs[2].JobName).IsEqualTo("build");
    }

    [Test]
    public async Task Following_a_live_run_picks_up_progress_and_stops_when_it_finishes()
    {
        FakeActionsApi actions = new() { Run = Sample.Run(12, "in_progress", null), Jobs = [Sample.Job(1, "build", "in_progress", null, null)] };
        JobsViewModel viewModel = Create(actions);
        await viewModel.LoadCommand.ExecuteAsync("octocat/hello@30433012");
        await Assert.That(viewModel.IsLive).IsTrue();
        JobItem before = viewModel.Jobs[0];

        // Nothing changed: the same row object stays in the list.
        await viewModel.FollowAsync();
        await Assert.That(ReferenceEquals(viewModel.Jobs[0], before)).IsTrue();

        actions.Run = Sample.Run(12, "completed", "success");
        actions.Jobs = [Sample.Job(1, "build", seconds: 125)];
        await viewModel.FollowAsync();
        await Assert.That(viewModel.IsLive).IsFalse();
        await Assert.That(viewModel.Jobs[0].Duration).IsEqualTo("2m 05s");

        int calls = actions.JobCalls.Count;
        await viewModel.FollowAsync();
        await Assert.That(actions.JobCalls.Count).IsEqualTo(calls);
    }

    [Test]
    public async Task After_a_rerun_the_page_keeps_checking_until_the_run_starts()
    {
        FakeActionsApi actions = new() { Jobs = [Sample.Job(1)] };
        JobsViewModel viewModel = Create(actions);
        await viewModel.LoadCommand.ExecuteAsync("octocat/hello@30433012");

        await viewModel.RerunCommand.ExecuteAsync(null);
        await Assert.That(viewModel.Notice).IsNotNull();
        await Assert.That(viewModel.IsLive).IsFalse();

        // GitHub still reports the old attempt: keep polling.
        int calls = actions.JobCalls.Count;
        await viewModel.FollowAsync();
        await Assert.That(actions.JobCalls.Count).IsEqualTo(calls + 1);

        actions.Run = Sample.Run(12, "queued", null);
        actions.Jobs = [Sample.Job(1, "build", "queued", null, null)];
        await viewModel.FollowAsync();
        await Assert.That(viewModel.IsLive).IsTrue();
        await Assert.That(viewModel.Notice).IsNull();
        await Assert.That(viewModel.Jobs[0].Duration).IsEqualTo("queued");
    }
}
