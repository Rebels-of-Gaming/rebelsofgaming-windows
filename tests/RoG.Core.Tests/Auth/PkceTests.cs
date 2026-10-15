using RoG.Core.Auth;

namespace RoG.Core.Tests.Auth;

public class PkceTests
{
    [Fact]
    public void Challenge_matches_rfc7636_appendix_b()
    {
        // RFC 7636 Anhang B
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.CreateChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void Verifier_is_valid_and_random()
    {
        var a = Pkce.CreateVerifier();
        var b = Pkce.CreateVerifier();
        Assert.Equal(64, a.Length);
        Assert.True(Pkce.IsValidVerifier(a));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Challenge_is_43_chars_base64url_without_padding()
    {
        var challenge = Pkce.CreateChallenge(Pkce.CreateVerifier());
        Assert.Equal(43, challenge.Length);
        Assert.DoesNotContain('=', challenge);
        Assert.DoesNotContain('+', challenge);
        Assert.DoesNotContain('/', challenge);
    }

    [Theory]
    [InlineData("too-short")]
    [InlineData("contains spaces contains spaces contains spaces contains")]
    [InlineData("ungültigeszeichenungültigeszeichenungültigeszeichen")]
    public void Invalid_verifier_is_rejected(string verifier)
    {
        Assert.False(Pkce.IsValidVerifier(verifier));
        Assert.Throws<ArgumentException>(() => Pkce.CreateChallenge(verifier));
    }

    [Fact]
    public void Verifier_with_128_chars_is_valid_but_129_is_not()
    {
        Assert.True(Pkce.IsValidVerifier(new string('a', 128)));
        Assert.False(Pkce.IsValidVerifier(new string('a', 129)));
    }

    [Fact]
    public void State_is_random_and_compared_exactly()
    {
        var state = Pkce.CreateState();
        Assert.Equal(43, state.Length);
        Assert.NotEqual(state, Pkce.CreateState());
        Assert.True(Pkce.StateMatches(state, state));
        Assert.False(Pkce.StateMatches(state, state + "x"));
        Assert.False(Pkce.StateMatches(state, state.ToUpperInvariant()));
        Assert.False(Pkce.StateMatches(state, null));
        Assert.False(Pkce.StateMatches(state, string.Empty));
    }
}
