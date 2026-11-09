using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Linked.Api.Auth;

/// <summary>Proof Key for Code Exchange (RFC 7636) values for one authorization request.</summary>
public readonly record struct Pkce(string Verifier, string Challenge)
{
    public const string ChallengeMethod = "S256";

    /// <summary>Creates a fresh verifier (43 URL-safe characters) and its S256 challenge.</summary>
    public static Pkce Create()
    {
        Span<byte> entropy = stackalloc byte[32];
        RandomNumberGenerator.Fill(entropy);
        return FromVerifier(Base64Url.EncodeToString(entropy));
    }

    /// <summary>Derives the challenge for a known verifier.</summary>
    public static Pkce FromVerifier(string verifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(verifier);

        Span<byte> ascii = stackalloc byte[128];
        int written = Encoding.ASCII.GetBytes(verifier, ascii);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(ascii[..written], hash);
        return new Pkce(verifier, Base64Url.EncodeToString(hash));
    }
}
