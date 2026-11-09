namespace Linked.Api.Auth;

/// <summary>
/// Supplies the bearer token for authenticated calls. Implemented by the app layer,
/// which owns secure storage. Return an empty string when signed out.
/// </summary>
public interface IAccessTokenProvider
{
    ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}
