using Refit;

namespace Linked.Api.Identity;

/// <summary>OpenID Connect identity endpoints. Base address: <see cref="LinkedInEndpoints.ApiBase"/>.</summary>
[Headers("Authorization: Bearer")]
public interface IIdentityApi
{
    [Get("/v2/userinfo")]
    Task<UserInfo> GetUserInfoAsync(CancellationToken cancellationToken);
}

/// <summary>The signed-in member, as returned by the OIDC userinfo endpoint.</summary>
public sealed record UserInfo(
    string Sub,
    string? Name = null,
    string? GivenName = null,
    string? FamilyName = null,
    string? Picture = null,
    string? Email = null,
    bool? EmailVerified = null);
