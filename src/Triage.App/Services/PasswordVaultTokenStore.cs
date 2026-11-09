using Triage.Core.Services;
using Windows.Security.Credentials;

namespace Triage.App.Services;

/// <summary>Keeps the access token in the Windows Credential Locker, encrypted for the current user.</summary>
internal sealed class PasswordVaultTokenStore : ITokenStore
{
    private const string Resource = "Triage";
    private const string UserName = "github";

    public string? Load()
    {
        PasswordCredential? credential = Find();
        if (credential is null)
        {
            return null;
        }

        credential.RetrievePassword();
        return credential.Password;
    }

    public void Save(string token) => new PasswordVault().Add(new PasswordCredential(Resource, UserName, token));

    public void Clear()
    {
        if (Find() is { } credential)
        {
            new PasswordVault().Remove(credential);
        }
    }

    private static PasswordCredential? Find()
    {
        try
        {
            return new PasswordVault().Retrieve(Resource, UserName);
        }
        catch (Exception)
        {
            // Retrieve throws when the credential does not exist; there is no TryRetrieve.
            return null;
        }
    }
}
