using Guit.Api.Users;

namespace Guit.Api.Repos;

/// <summary>Schema: minimal-repository / repository. Only the fields the app shows.</summary>
public sealed record RepositorySummary(
    long Id,
    string Name,
    string FullName,
    bool Private,
    string HtmlUrl,
    GitHubUser Owner);
