using Refit;

namespace Guit.Api.Users;

/// <summary>OpenAPI tag: users.</summary>
[Headers("Authorization: Bearer")]
public interface IUsersApi
{
    /// <summary>operationId: users/get-authenticated</summary>
    [Get("/user")]
    Task<GitHubUser> GetAuthenticatedAsync(CancellationToken cancellationToken);
}

/// <summary>Schema: private-user / public-user / simple-user. Only the fields the app shows.</summary>
public sealed record GitHubUser(
    long Id,
    string Login,
    string AvatarUrl,
    string HtmlUrl,
    string? Name = null);
