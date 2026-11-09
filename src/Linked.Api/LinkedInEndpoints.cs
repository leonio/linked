namespace Linked.Api;

public static class LinkedInEndpoints
{
    public static readonly Uri OAuthBase = new("https://www.linkedin.com");
    public static readonly Uri ApiBase = new("https://api.linkedin.com");

    /// <summary>Authorization endpoint for native clients using PKCE and a loopback redirect.</summary>
    public const string NativePkceAuthorization = "https://www.linkedin.com/oauth/native-pkce/authorization";
}
