using Refit;

namespace Linked.Api.Auth;

/// <summary>Form body for <c>POST /oauth/v2/accessToken</c> (authorization code + PKCE).</summary>
public sealed class AuthorizationCodeTokenRequest
{
    [AliasAs("grant_type")]
    public string GrantType { get; init; } = "authorization_code";

    [AliasAs("code")]
    public required string Code { get; init; }

    [AliasAs("redirect_uri")]
    public required string RedirectUri { get; init; }

    [AliasAs("client_id")]
    public required string ClientId { get; init; }

    [AliasAs("code_verifier")]
    public required string CodeVerifier { get; init; }
}

/// <summary>Reply from the token endpoint. Property names map from snake_case via the JSON context.</summary>
public sealed record OAuthToken(
    string AccessToken,
    int ExpiresIn,
    string? Scope = null,
    string? TokenType = null,
    string? IdToken = null,
    string? RefreshToken = null,
    int? RefreshTokenExpiresIn = null);
