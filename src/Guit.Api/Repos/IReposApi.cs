using Refit;

namespace Guit.Api.Repos;

/// <summary>OpenAPI tag: repos.</summary>
[Headers("Authorization: Bearer")]
public interface IReposApi
{
    /// <summary>operationId: repos/list-for-authenticated-user</summary>
    /// <param name="sort">"pushed", "updated", "created" or "full_name".</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="perPage">Page size, at most 100.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [Get("/user/repos")]
    Task<RepositorySummary[]> ListMineAsync(string sort, int page, [AliasAs("per_page")] int perPage, CancellationToken cancellationToken);
}
