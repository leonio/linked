using Refit;

namespace Linked.Api.Auth;

/// <summary>OAuth 2.0 token endpoint. Base address: <see cref="LinkedInEndpoints.OAuthBase"/>.</summary>
public interface ILinkedInOAuthApi
{
    [Post("/oauth/v2/accessToken")]
    Task<OAuthToken> ExchangeCodeAsync(
        [Body(BodySerializationMethod.UrlEncoded)] AuthorizationCodeTokenRequest request,
        CancellationToken cancellationToken);
}
