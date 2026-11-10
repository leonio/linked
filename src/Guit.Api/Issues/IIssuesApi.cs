using Refit;

namespace Guit.Api.Issues;

/// <summary>
/// OpenAPI tag: issues. Every pull request is also an issue, so these calls serve comments for both.
/// The "full" media type makes GitHub include body_text and body_html next to the Markdown body.
/// </summary>
[Headers("Authorization: Bearer", "Accept: application/vnd.github.full+json")]
public interface IIssuesApi
{
    /// <summary>operationId: issues/get</summary>
    [Get("/repos/{owner}/{repo}/issues/{issueNumber}")]
    Task<Issue> GetAsync(string owner, string repo, int issueNumber, CancellationToken cancellationToken);

    /// <summary>operationId: issues/list-comments. Oldest first.</summary>
    [Get("/repos/{owner}/{repo}/issues/{issueNumber}/comments")]
    Task<IssueComment[]> ListCommentsAsync(
        string owner,
        string repo,
        int issueNumber,
        int page,
        [AliasAs("per_page")] int perPage,
        CancellationToken cancellationToken);
}
