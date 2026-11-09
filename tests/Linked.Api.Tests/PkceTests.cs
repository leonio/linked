using Linked.Api.Auth;

namespace Linked.Api.Tests;

public sealed class PkceTests
{
    [Test]
    public async Task FromVerifier_matches_the_RFC_7636_appendix_B_vector()
    {
        Pkce pkce = Pkce.FromVerifier("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk");

        await Assert.That(pkce.Challenge).IsEqualTo("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
    }

    [Test]
    public async Task Create_returns_a_43_character_verifier_that_differs_each_time()
    {
        Pkce first = Pkce.Create();
        Pkce second = Pkce.Create();

        await Assert.That(first.Verifier.Length).IsEqualTo(43);
        await Assert.That(first.Verifier).IsNotEqualTo(second.Verifier);
    }
}
