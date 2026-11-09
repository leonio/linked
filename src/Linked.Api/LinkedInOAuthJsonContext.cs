using System.Text.Json;
using System.Text.Json.Serialization;
using Linked.Api.Auth;
using Linked.Api.Identity;

namespace Linked.Api;

/// <summary>
/// Source-generated JSON metadata for the OAuth and OIDC endpoints, which use snake_case.
/// Every request and reply type sent through a Refit client must be listed here:
/// a missing type throws <see cref="NotSupportedException"/> at run time, not at build time.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OAuthToken))]
[JsonSerializable(typeof(UserInfo))]
public sealed partial class LinkedInOAuthJsonContext : JsonSerializerContext;
