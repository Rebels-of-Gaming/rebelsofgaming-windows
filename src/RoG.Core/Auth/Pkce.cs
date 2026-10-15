using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RoG.Core.Auth;

/// <summary>PKCE nach RFC 7636 (nur S256) und <c>state</c> für den App-Login.</summary>
public static partial class Pkce
{
    /// <summary>Erzeugt einen <c>code_verifier</c> mit 64 Zeichen aus <c>A–Z a–z 0–9 - _</c> (48 Zufallsbytes, Base64url).</summary>
    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(48));

    /// <summary><c>BASE64URL(SHA256(ASCII(code_verifier)))</c> – 43 Zeichen ohne <c>=</c>.</summary>
    public static string CreateChallenge(string verifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(verifier);
        if (!IsValidVerifier(verifier))
        {
            throw new ArgumentException("code_verifier entspricht nicht RFC 7636 (43–128 Zeichen aus A–Z a–z 0–9 - . _ ~).", nameof(verifier));
        }

        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    /// <summary>Zufälliger <c>state</c> (32 Bytes, Base64url, 43 Zeichen).</summary>
    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>Prüft den <c>code_verifier</c> nach RFC 7636 Abschnitt 4.1.</summary>
    public static bool IsValidVerifier(string? verifier) => verifier is not null && VerifierPattern().IsMatch(verifier);

    /// <summary>Vergleicht <c>state</c> in konstanter Zeit.</summary>
    public static bool StateMatches(string expected, string? actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (actual is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
    }

    internal static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [GeneratedRegex(@"^[A-Za-z0-9\-._~]{43,128}$")]
    private static partial Regex VerifierPattern();
}
