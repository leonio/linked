using Refit;

namespace Guit.Api.Pulls;

/// <summary>OpenAPI tag: pulls.</summary>
[Headers("Authorization: Bearer")]
public interface IPullsApi
{
    /// <summary>operationId: pulls/get</summary>
    [Get("/repos/{owner}/{repo}/pulls/{pullNumber}")]
    Task<PullRequest> GetAsync(string owner, string repo, int pullNumber, CancellationToken cancellationToken);
}

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
