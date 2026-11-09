using Triage.Api.Auth;

namespace Triage.Core.Services;

/// <summary>Where the access token is kept between runs. Implemented by the app on Windows secure storage.</summary>
public interface ITokenStore
{
    string? Load();

    void Save(string token);

    void Clear();
}

/// <summary>Things only the app layer can do for a view model.</summary>
public interface ILauncher
{
    /// <summary>Opens a URI in the default browser.</summary>
    Task OpenAsync(Uri uri);

    void CopyToClipboard(string text);
}

/// <summary>App-level settings supplied by the app at start-up.</summary>
/// <param name="ClientId">The OAuth App client id. Public, not a secret.</param>
/// <param name="Scopes">Space-separated OAuth scopes.</param>
public sealed record GitHubOptions(string ClientId, string Scopes = "notifications repo read:user");

/// <summary>The signed-in state. Singleton. Raises <see cref="Changed"/> on the thread that called SignIn/SignOut.</summary>
public sealed class Session(ITokenStore store) : IAccessTokenProvider
{
    private string? _token = store.Load();

    public event EventHandler? Changed;

    public bool IsSignedIn => !string.IsNullOrEmpty(_token);

    public ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken) => new(_token ?? string.Empty);

    public void SignIn(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        store.Save(token);
        _token = token;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        store.Clear();
        _token = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
