using Linked.Core.Services;

namespace Linked.App.Services;

/// <summary>
/// Placeholder until the login work lands (docs/tasks, phase 1): holds no token and
/// reports signed-out. The real implementation owns the sign-in flow and secure storage.
/// </summary>
internal sealed partial class SessionService : ISessionService
{
    public bool IsSignedIn => false;

    public ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken) => new(string.Empty);

    public Task<bool> SignInAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public Task SignOutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
