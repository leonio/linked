using System.Text.Json.Serialization;
using Refit;

namespace Linked.Api.Auth;

/// <summary>Form body for <c>POST /login/device/code</c>.</summary>
public sealed class DeviceCodeRequest
{
    [AliasAs("client_id")]
    public required string ClientId { get; init; }

    [AliasAs("scope")]
    public required string Scope { get; init; }
}

/// <summary>The code the member types at <see cref="VerificationUri"/>, and how to poll for the result.</summary>
public sealed record DeviceCode(
    [property: JsonPropertyName("device_code")] string Code,
    string UserCode,
    string VerificationUri,
    int ExpiresIn,
    int Interval);

/// <summary>Form body for <c>POST /login/oauth/access_token</c> while polling.</summary>
public sealed class DeviceTokenRequest
{
    [AliasAs("client_id")]
    public required string ClientId { get; init; }

    [AliasAs("device_code")]
    public required string DeviceCode { get; init; }

    [AliasAs("grant_type")]
    public string GrantType { get; init; } = "urn:ietf:params:oauth:grant-type:device_code";
}

/// <summary>
/// Poll reply. GitHub answers 200 in both cases: either <see cref="AccessToken"/> is set,
/// or <see cref="Error"/> is (<c>authorization_pending</c>, <c>slow_down</c>, <c>expired_token</c>, <c>access_denied</c>).
/// </summary>
public sealed record DeviceTokenResponse(
    string? AccessToken = null,
    string? TokenType = null,
    string? Scope = null,
    string? Error = null,
    string? ErrorDescription = null,
    int? Interval = null);
