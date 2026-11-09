namespace Triage.Api.Auth;

/// <summary>
/// Supplies the bearer token for authenticated calls. Implemented by Triage.Core's Session.
/// Return an empty string when signed out.
/// </summary>
public interface IAccessTokenProvider
{
    ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}
