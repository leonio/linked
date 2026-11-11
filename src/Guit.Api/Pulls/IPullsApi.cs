using Refit;

namespace Guit.Api.Pulls;

/// <summary>OpenAPI tag: pulls.</summary>
[Headers("Authorization: Bearer")]
public interface IPullsApi
{
    /// <summary>operationId: pulls/get</summary>
    [Get("/repos/{owner}/{repo}/pulls/{pullNumber}")]
    Task<PullRequest> GetAsync(string owner, string repo, int pullNumber, CancellationToken cancellationToken);

    /// <summary>operationId: pulls/list-files. At most 100 per page; GitHub lists at most 3,000 files.</summary>
    [Get("/repos/{owner}/{repo}/pulls/{pullNumber}/files")]
    Task<PullFile[]> ListFilesAsync(
        string owner,
        string repo,
        int pullNumber,
        int page,
        [AliasAs("per_page")] int perPage,
        CancellationToken cancellationToken);
}

/// <summary>Schema: diff-entry. One changed file of a pull request.</summary>
/// <param name="Filename">Path of the file after the change.</param>
/// <param name="Status">"added", "removed", "modified", "renamed", "copied", "changed" or "unchanged".</param>
/// <param name="Additions">Lines added.</param>
/// <param name="Deletions">Lines removed.</param>
/// <param name="Patch">The unified diff of the file. Missing for binary files and very large diffs.</param>
/// <param name="PreviousFilename">The old path, for a renamed file.</param>
public sealed record PullFile(
    string Filename,
    string Status,
    int Additions,
    int Deletions,
    string? Patch = null,
    string? PreviousFilename = null);

/// <summary>Schema: pull-request. Only the fields the conversation header shows.</summary>
/// <param name="State">"open" or "closed". A merged pull request is "closed" with <paramref name="Merged"/> true.</param>
public sealed record PullRequest(
    int Number,
    string State,
    bool Merged,
    int Additions,
    int Deletions,
    int ChangedFiles,
    PullBranch Head,
    PullBranch Base,
    bool? Draft = null,
    bool? Mergeable = null);

/// <summary>Schema: pull-request.head / pull-request.base (inline objects).</summary>
/// <param name="Ref">The branch name.</param>
/// <param name="Label">"owner:branch".</param>
public sealed record PullBranch(string Ref, string Label);
