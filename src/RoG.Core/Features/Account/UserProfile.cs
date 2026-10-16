namespace RoG.Core.Features.Account;

/// <summary>
/// Angemeldete Person aus <c>GET /v1/me</c>. Rechte kommen nur aus <see cref="Permissions"/> (je Geltungsbereich, z. B.
/// <c>global</c> oder <c>squad:01J…</c>) und den Token-Scopes – nie aus Rollennamen.
/// </summary>
public sealed record UserProfile(
    string Id,
    string Slug,
    string Name,
    string? Email,
    string? AvatarUrl,
    string? RankLabel,
    string? RankColor,
    string Status,
    bool TwoFactorEnabled,
    string? JoinedAt,
    IReadOnlyList<string> SquadNames,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Permissions,
    IReadOnlyList<string>? TokenScopes)
{
    public const string GlobalScope = "global";

    /// <summary>Hat die Person das Recht <paramref name="permission"/> (global oder im angegebenen Bereich)?</summary>
    public bool Can(string permission, string? scope = null)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return Has(GlobalScope, permission) || (scope is not null && Has(scope, permission));
    }

    /// <summary>Hat die Person das Recht in irgendeinem Bereich (global, Squad, Server …)?</summary>
    public bool CanAnywhere(string permission) => Permissions.Values.Any(list => list.Contains(permission, StringComparer.Ordinal));

    /// <summary>
    /// Erlaubt der Token den Scope? <c>null</c> = Anfrage ohne Token-Einschränkung. Platzhalter wie <c>roles:*</c>
    /// oder <c>*</c> decken passende Scopes ab.
    /// </summary>
    public bool HasScope(string scope) => TokenScopes is null || ScopeMatcher.Allows(TokenScopes, scope);

    private bool Has(string scope, string permission) =>
        Permissions.TryGetValue(scope, out var list) && list.Contains(permission, StringComparer.Ordinal);
}

/// <summary>Vergleicht Token-Scopes mit Platzhaltern (<c>*</c>, <c>bereich:*</c>).</summary>
public static class ScopeMatcher
{
    public static bool Allows(IEnumerable<string> granted, string scope)
    {
        ArgumentNullException.ThrowIfNull(granted);
        ArgumentNullException.ThrowIfNull(scope);
        foreach (var g in granted)
        {
            if (g == "*" || string.Equals(g, scope, StringComparison.Ordinal))
            {
                return true;
            }

            if (g.EndsWith(":*", StringComparison.Ordinal) && scope.StartsWith(g[..^1], StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
