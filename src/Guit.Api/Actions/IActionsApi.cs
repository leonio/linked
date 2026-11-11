using Guit.Api.Users;
using Refit;

namespace Guit.Api.Actions;

/// <summary>OpenAPI tag: actions.</summary>
[Headers("Authorization: Bearer")]
public interface IActionsApi
{
    /// <summary>operationId: actions/list-workflow-runs-for-repo. Newest first.</summary>
    [Get("/repos/{owner}/{repo}/actions/runs")]
    Task<WorkflowRunList> ListRunsAsync(string owner, string repo, int page, [AliasAs("per_page")] int perPage, CancellationToken cancellationToken);

    /// <summary>operationId: actions/get-workflow-run</summary>
    [Get("/repos/{owner}/{repo}/actions/runs/{runId}")]
    Task<WorkflowRun> GetRunAsync(string owner, string repo, long runId, CancellationToken cancellationToken);

    /// <summary>operationId: actions/list-jobs-for-workflow-run. Jobs of the latest attempt.</summary>
    [Get("/repos/{owner}/{repo}/actions/runs/{runId}/jobs")]
    Task<JobList> ListJobsAsync(string owner, string repo, long runId, int page, [AliasAs("per_page")] int perPage, CancellationToken cancellationToken);

    /// <summary>
    /// operationId: actions/download-job-logs-for-workflow-run. GitHub answers with a redirect to a short-lived
    /// plain-text download; the HTTP handler follows it, so this returns the log text itself. Logs can be megabytes.
    /// </summary>
    [Get("/repos/{owner}/{repo}/actions/jobs/{jobId}/logs")]
    Task<string> GetJobLogsAsync(string owner, string repo, long jobId, CancellationToken cancellationToken);

    /// <summary>operationId: actions/re-run-workflow</summary>
    [Post("/repos/{owner}/{repo}/actions/runs/{runId}/rerun")]
    Task RerunAsync(string owner, string repo, long runId, CancellationToken cancellationToken);
}

/// <summary>Reply of actions/list-workflow-runs-for-repo (inline object).</summary>
public sealed record WorkflowRunList(int TotalCount, WorkflowRun[] WorkflowRuns);

/// <summary>Schema: workflow-run. Only the fields the app shows.</summary>
/// <param name="Status">"queued", "in_progress", "completed", "waiting", "requested" or "pending".</param>
/// <param name="Conclusion">Null until completed; then "success", "failure", "cancelled", "skipped", "timed_out", "neutral", "action_required"...</param>
public sealed record WorkflowRun(
    long Id,
    string DisplayTitle,
    int RunNumber,
    string Event,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string HtmlUrl,
    string? Name = null,
    string? Status = null,
    string? Conclusion = null,
    string? HeadBranch = null,
    GitHubUser? Actor = null);

/// <summary>Reply of actions/list-jobs-for-workflow-run (inline object).</summary>
public sealed record JobList(int TotalCount, Job[] Jobs);

/// <summary>Schema: job.</summary>
public sealed record Job(
    long Id,
    string Name,
    string Status,
    DateTimeOffset StartedAt,
    string HtmlUrl,
    string? Conclusion = null,
    DateTimeOffset? CompletedAt = null,
    JobStep[]? Steps = null);

/// <summary>Schema: job.steps[] (inline object).</summary>
/// <param name="Name">The step name as shown on github.com.</param>
/// <param name="Status">"queued", "in_progress" or "completed".</param>
/// <param name="Number">1-based position within the job.</param>
/// <param name="Conclusion">Null until completed.</param>
public sealed record JobStep(
    string Name,
    string Status,
    int Number,
    string? Conclusion = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null);
