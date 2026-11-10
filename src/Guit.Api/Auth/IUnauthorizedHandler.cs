namespace Guit.Api.Auth;

/// <summary>Told when GitHub rejects the access token (HTTP 401). Implemented by Guit.Core's Session.</summary>
public interface IUnauthorizedHandler
{
    /// <summary>Called on a thread-pool thread, possibly several times in a row. Must not block.</summary>
    void OnUnauthorized();
}
