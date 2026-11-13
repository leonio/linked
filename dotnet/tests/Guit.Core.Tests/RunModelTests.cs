using Guit.Api.Actions;
using Guit.Core.Models;

namespace Guit.Core.Tests;

public sealed class RunModelTests
{
    [Test]
    [Arguments("completed", "success", RunStatus.Success)]
    [Arguments("completed", "failure", RunStatus.Failure)]
    [Arguments("completed", "timed_out", RunStatus.Failure)]
    [Arguments("completed", "cancelled", RunStatus.Cancelled)]
    [Arguments("completed", "skipped", RunStatus.Skipped)]
    [Arguments("completed", "neutral", RunStatus.Other)]
    [Arguments("in_progress", null, RunStatus.Running)]
    [Arguments("queued", null, RunStatus.Queued)]
    [Arguments("waiting", null, RunStatus.Queued)]
    [Arguments(null, null, RunStatus.Other)]
    public async Task Kind_combines_status_and_conclusion(string? status, string? conclusion, string expected)
    {
        await Assert.That(RunStatus.Kind(status, conclusion)).IsEqualTo(expected);
    }

    [Test]
    public async Task RunItem_builds_its_display_text()
    {
        RunItem item = RunItem.From(Sample.Run(12, status: "completed", conclusion: "failure"), Sample.Now);

        await Assert.That(item.Title).IsEqualTo("Fix it");
        await Assert.That(item.Detail).IsEqualTo("CI #12 · push · main");
        await Assert.That(item.Actor).IsEqualTo("octocat");
        await Assert.That(item.Updated).IsEqualTo("3h");
        await Assert.That(item.StatusKind).IsEqualTo(RunStatus.Failure);
        await Assert.That(item.StatusGlyph).IsEqualTo("");
    }

    [Test]
    public async Task RunItem_copes_with_missing_name_branch_and_actor()
    {
        WorkflowRun run = new(1, "Title", 3, "schedule", Sample.Now, Sample.Now, "https://github.com/o/r/actions/runs/1", Status: "queued");

        RunItem item = RunItem.From(run, Sample.Now);

        await Assert.That(item.Detail).IsEqualTo("Workflow #3 · schedule");
        await Assert.That(item.Actor).IsEqualTo(string.Empty);
        await Assert.That(item.StatusKind).IsEqualTo(RunStatus.Queued);
    }

    [Test]
    [Arguments(5, "5s")]
    [Arguments(125, "2m 05s")]
    [Arguments(3725, "1h 02m")]
    [Arguments(-4, "0s")]
    public async Task Durations_are_short(int seconds, string expected)
    {
        await Assert.That(JobItem.FormatDuration(TimeSpan.FromSeconds(seconds))).IsEqualTo(expected);
    }

    [Test]
    public async Task JobItem_shows_the_duration_or_the_status_word()
    {
        await Assert.That(JobItem.From(Sample.Job(1, seconds: 125)).Duration).IsEqualTo("2m 05s");
        await Assert.That(JobItem.From(Sample.Job(2, status: "in_progress", conclusion: null, seconds: null)).Duration).IsEqualTo("running");
    }

    [Test]
    public async Task RunLink_round_trips_and_rejects_bad_text()
    {
        RunLink link = new("octocat", "hello.world", 30433642);

        await Assert.That(link.ToArgument()).IsEqualTo("octocat/hello.world@30433642");
        await Assert.That(RunLink.TryParseArgument("octocat/hello.world@30433642", out RunLink parsed)).IsTrue();
        await Assert.That(parsed).IsEqualTo(link);
        await Assert.That(link.ToWebUri()).IsEqualTo(new Uri("https://github.com/octocat/hello.world/actions/runs/30433642"));
        await Assert.That(RunLink.TryCreate("octocat/hello", 5, out RunLink created)).IsTrue();
        await Assert.That(created.Repo).IsEqualTo("hello");
    }

    [Test]
    [Arguments(null)]
    [Arguments("octocat/hello")]
    [Arguments("octocat@5")]
    [Arguments("octocat/@5")]
    [Arguments("octocat/hello@x")]
    [Arguments("octocat/hello@0")]
    public async Task RunLink_rejects_malformed_arguments(string? argument)
    {
        await Assert.That(RunLink.TryParseArgument(argument, out _)).IsFalse();
    }
}
