using Linked.Api.Auth;

namespace Linked.Core.Services;

/// <summary>
/// Owns the signed-in state. The app layer implements this on top of secure storage
/// and the sign-in flow; view models only see this interface.
/// </summary>
public interface ISessionService : IAccessTokenProvider
{
    bool IsSignedIn { get; }

    /// <summary>Runs the interactive sign-in flow. Returns false if the member cancelled.</summary>
    Task<bool> SignInAsync(CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);
}
