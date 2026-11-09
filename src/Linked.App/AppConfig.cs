namespace Linked.App;

internal static class AppConfig
{
    /// <summary>
    /// Client id of the GitHub OAuth App (Settings > Developer settings > OAuth Apps, with
    /// "Enable Device Flow" ticked). A client id is public; there is no client secret in this app.
    /// Set the LINKED_GITHUB_CLIENT_ID environment variable to override it while developing.
    /// </summary>
    public static string GitHubClientId { get; } =
        Environment.GetEnvironmentVariable("LINKED_GITHUB_CLIENT_ID") is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : "REPLACE_WITH_OAUTH_APP_CLIENT_ID";
}
