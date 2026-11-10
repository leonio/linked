using Guit.Api.Users;
using Refit;

namespace Guit.Api.Search;

/// <summary>OpenAPI tag: search. Limited to 30 requests a minute: never poll it.</summary>
[Headers("Authorization: Bearer")]
public interface ISearchApi
{
    /// <summary>operationId: search/issues-and-pull-requests</summary>
    /// <param name="q">GitHub search syntax, for example "is:open is:pr author:@me".</param>
    /// <param name="sort">"updated", "created" or "comments".</param>
    /// <param name="order">"desc" or "asc".</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="perPage">Page size, at most 100.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [Get("/search/issues")]
    Task<IssueSearchResult> SearchIssuesAsync(
        string q,
        string sort,
        string order,
        int page,
        [AliasAs("per_page")] int perPage,
        CancellationToken cancellationToken);
}

/// <summary>Reply of search/issues-and-pull-requests (inline object).</summary>
public sealed record IssueSearchResult(int TotalCount, bool IncompleteResults, IssueSearchItem[] Items);

/// <summary>Schema: issue-search-result-item. Only the fields the app shows.</summary>
/// <param name="RepositoryUrl">API URL of the repository: https://api.github.com/repos/{owner}/{repo}.</param>
public sealed record IssueSearchItem(
    long Id,
    int Number,
    string Title,
    string State,
    int Comments,
    DateTimeOffset UpdatedAt,
    string HtmlUrl,
    string RepositoryUrl,
    GitHubUser? User = null,
    bool? Draft = null);
