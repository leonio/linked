namespace Linked.Api;

public static class GitHubEndpoints
{
    /// <summary>REST API root. Every interface except the device flow uses this base address.</summary>
    public static readonly Uri ApiBase = new("https://api.github.com");

    /// <summary>Web root. Only the OAuth device flow lives here.</summary>
    public static readonly Uri WebBase = new("https://github.com");

    /// <summary>The REST API version the DTOs in this project were written against.</summary>
    public const string ApiVersion = "2026-03-10";
}
