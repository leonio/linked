using Guit.Api.Actions;
using Guit.Api.Repos;

namespace Guit.Api.Tests;

public sealed class ActionsApiTests
{
    [Test]
    public async Task ListMine_sends_sort_and_paging_and_maps_repositories()
    {
        using RecordingHandler http = new("""[{"id":9,"name":"hello","full_name":"octocat/hello","private":true,"fork":false,"html_url":"https://github.com/octocat/hello","owner":{"login":"octocat","id":1,"avatar_url":"https://avatars/1","html_url":"https://github.com/octocat"}}]""");

        RepositorySummary[] repositories = await Stub.Client<IReposApi>(http).ListMineAsync("pushed", page: 1, perPage: 50, CancellationToken.None);

        await Assert.That(http.Path).IsEqualTo("/user/repos?sort=pushed&page=1&per_page=50");
        await Assert.That(repositories[0].FullName).IsEqualTo("octocat/hello");
        await Assert.That(repositories[0].Private).IsTrue();
    }

    [Test]
    public async Task ListRuns_maps_a_finished_and_a_running_run()
    {
        using RecordingHandler http = new("""
            { "total_count": 2, "workflow_runs": [
              { "id": 30433642, "name": "CI", "display_title": "Fix it", "run_number": 12, "event": "push", "status": "completed", "conclusion": "failure",
                "head_branch": "main", "head_sha": "abc", "created_at": "2026-01-02T00:00:00Z", "updated_at": "2026-01-02T00:05:00Z",
                "html_url": "https://github.com/o/r/actions/runs/30433642",
                "actor": { "login": "octocat", "id": 1, "avatar_url": "https://avatars/1", "html_url": "https://github.com/octocat" } },
              { "id": 30433643, "name": null, "display_title": "Next", "run_number": 13, "event": "pull_request", "status": "in_progress", "conclusion": null,
                "head_branch": null, "created_at": "2026-01-03T00:00:00Z", "updated_at": "2026-01-03T00:00:30Z",
                "html_url": "https://github.com/o/r/actions/runs/30433643" } ] }
            """);

        WorkflowRunList list = await Stub.Client<IActionsApi>(http).ListRunsAsync("o", "r", page: 2, perPage: 30, CancellationToken.None);

        await Assert.That(http.Path).IsEqualTo("/repos/o/r/actions/runs?page=2&per_page=30");
        await Assert.That(list.TotalCount).IsEqualTo(2);
        await Assert.That(list.WorkflowRuns[0].DisplayTitle).IsEqualTo("Fix it");
        await Assert.That(list.WorkflowRuns[0].Conclusion).IsEqualTo("failure");
        await Assert.That(list.WorkflowRuns[0].Actor!.Login).IsEqualTo("octocat");
        await Assert.That(list.WorkflowRuns[1].Conclusion).IsNull();
        await Assert.That(list.WorkflowRuns[1].Name).IsNull();
        await Assert.That(list.WorkflowRuns[1].HeadBranch).IsNull();
        await Assert.That(list.WorkflowRuns[1].Actor).IsNull();
    }

    [Test]
    public async Task ListJobs_maps_times_and_a_job_still_running()
    {
        using RecordingHandler http = new("""
            { "total_count": 2, "jobs": [
              { "id": 1, "run_id": 30433642, "name": "build", "status": "completed", "conclusion": "success",
                "started_at": "2026-01-02T00:00:10Z", "completed_at": "2026-01-02T00:02:15Z", "html_url": "https://github.com/o/r/actions/runs/30433642/job/1" },
              { "id": 2, "run_id": 30433642, "name": "test", "status": "in_progress", "conclusion": null,
                "started_at": "2026-01-02T00:02:20Z", "completed_at": null, "html_url": "https://github.com/o/r/actions/runs/30433642/job/2" } ] }
            """);

        JobList list = await Stub.Client<IActionsApi>(http).ListJobsAsync("o", "r", 30433642, page: 1, perPage: 100, CancellationToken.None);

        await Assert.That(http.Path).IsEqualTo("/repos/o/r/actions/runs/30433642/jobs?page=1&per_page=100");
        await Assert.That(list.Jobs[0].CompletedAt).IsEqualTo(new DateTimeOffset(2026, 1, 2, 0, 2, 15, TimeSpan.Zero));
        await Assert.That(list.Jobs[1].CompletedAt).IsNull();
        await Assert.That(list.Jobs[1].Conclusion).IsNull();
    }

    [Test]
    public async Task Rerun_posts_to_the_run_without_a_body()
    {
        using RecordingHandler http = new("{}");

        await Stub.Client<IActionsApi>(http).RerunAsync("o", "r", 30433642, CancellationToken.None);

        await Assert.That(http.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(http.Path).IsEqualTo("/repos/o/r/actions/runs/30433642/rerun");
        await Assert.That(string.IsNullOrEmpty(http.Body)).IsTrue();
    }
}
