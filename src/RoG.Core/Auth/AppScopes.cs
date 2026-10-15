using System.Text.Json;

namespace RoG.Core.Auth;

/// <summary>
/// Scopes, die die App beim Login anfragt. Die Website schneidet sie pro Konto zu (Verwaltungs-Scopes nur mit Recht und
/// 2FA) – die tatsächlichen Scopes stehen in der Token-Antwort. Quelle: <c>GET /v1/auth/scopes</c> → <c>meta.app_scopes</c>.
/// </summary>
public static class AppScopes
{
    /// <summary>
    /// Scopes, die die Windows-App bewusst nicht anfragt, solange sie die Bereiche nicht anbietet (Guide 35,
    /// Sicherheitshinweis 15.10.2026): eigene API-Tokens, Webhooks, Übersetzungen, Rechtstexte.
    /// </summary>
    public static readonly IReadOnlySet<string> NotRequested = new HashSet<string>(StringComparer.Ordinal)
    {
        "tokens:manage", "webhooks:read", "webhooks:write", "translations:manage", "legal:manage", "bot",
    };

    /// <summary>Rückfall, wenn <c>/v1/auth/scopes</c> nicht erreichbar ist: Mitglieder-Scopes der App.</summary>
    public static readonly IReadOnlyList<string> Fallback =
    [
        "profile:read", "profile:write", "account:write", "members:read", "squads:read", "games:read", "events:read", "events:write",
        "forum:read", "forum:write", "messages:read", "messages:write", "notifications:read", "notifications:write",
        "news:read", "news:comment", "wiki:read", "wiki:write", "servers:read", "stats:read", "matches:read", "gallery:read",
        "gallery:write", "downloads:read", "rules:read", "rules:accept", "halloffame:read", "search:read", "teamspeak:read",
        "absences:read", "absences:write", "uploads:read", "uploads:write", "links:read", "links:write", "cms:read",
        "applications:read", "applications:write", "reports:write", "devices:manage",
    ];

    /// <summary>Liest <c>meta.app_scopes</c> aus der Antwort von <c>GET /v1/auth/scopes</c> und filtert <see cref="NotRequested"/>.</summary>
    public static IReadOnlyList<string> FromScopesResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("meta", out var meta)
                && meta.TryGetProperty("app_scopes", out var list)
                && list.ValueKind == JsonValueKind.Array)
            {
                var scopes = list.EnumerateArray()
                    .Where(s => s.ValueKind == JsonValueKind.String)
                    .Select(s => s.GetString()!)
                    .Where(s => !NotRequested.Contains(s))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (scopes.Count > 0)
                {
                    return scopes;
                }
            }
        }
        catch (JsonException)
        {
            // Rückfall unten
        }

        return Fallback;
    }

    /// <summary>Holt die Scope-Liste von der API; bei jedem Fehler <see cref="Fallback"/>.</summary>
    public static async Task<IReadOnlyList<string>> ResolveAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "auth/scopes");
            request.Options.Set(Api.RoGHttpHandler.SkipAuthorization, true);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return FromScopesResponse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Api.ApiProblemException)
        {
            return Fallback;
        }
        catch (HttpRequestException)
        {
            return Fallback;
        }
    }
}
