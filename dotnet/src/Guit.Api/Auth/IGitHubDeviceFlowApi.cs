using Refit;

namespace Guit.Api.Auth;

/// <summary>
/// OAuth device flow (no client secret, so it is safe in a desktop app).
/// Base address: <see cref="GitHubEndpoints.WebBase"/>. These endpoints are not in the OpenAPI description;
/// see https://docs.github.com/apps/oauth-apps/building-oauth-apps/authorizing-oauth-apps#device-flow
/// </summary>
[Headers("Accept: application/json")]
public interface IGitHubDeviceFlowApi
{
    [Post("/login/device/code")]
    Task<DeviceCode> RequestCodeAsync(
        [Body(BodySerializationMethod.UrlEncoded)] DeviceCodeRequest request,
        CancellationToken cancellationToken);

    [Post("/login/oauth/access_token")]
    Task<DeviceTokenResponse> PollTokenAsync(
        [Body(BodySerializationMethod.UrlEncoded)] DeviceTokenRequest request,
        CancellationToken cancellationToken);
}
