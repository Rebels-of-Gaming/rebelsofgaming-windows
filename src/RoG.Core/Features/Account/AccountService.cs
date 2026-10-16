using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Core.Auth;

namespace RoG.Core.Features.Account;

/// <summary>Plattform-Stand aus <c>GET /v1</c> (für „Plattform vA.B.C“ in Einstellungen → Über).</summary>
public sealed record PlatformInfo(string Name, string Version, string? Commit);

/// <summary>Konto der App: Profil (<c>/v1/me</c>), Gerät melden (<c>POST /v1/devices</c>), Abmelden.</summary>
public sealed class AccountService(IRoGApiClient client, ITokenStore tokens)
{
    public async Task<UserProfile> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var me = (await client.V1_me_showAsync(cancellationToken).ConfigureAwait(false)).Data;
        return Map(me);
    }

    /// <summary>
    /// Meldet das Gerät des Tokens (bei jedem Start, Guide 35 Abschnitt 2): Plattform <c>windows</c> und App-Version.
    /// Ohne Scope <c>devices:manage</c> (ältere Tokens) passiert nichts – das ist kein Fehler für die Oberfläche.
    /// Kein Name: Den hat der Login gesetzt, umbenennen geht auf der Website (Annahme 4).
    /// </summary>
    public async Task<bool> RegisterDeviceAsync(string appVersion, CancellationToken cancellationToken = default)
    {
        var token = await tokens.GetAsync(cancellationToken).ConfigureAwait(false);
        if (token is null || !ScopeMatcher.Allows(token.Scopes, "devices:manage"))
        {
            return false;
        }

        try
        {
            await client.V1_devices_storeAsync(new ManageDevices { Platform = ManageDevicesPlatform.Windows, App_version = appVersion }, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ApiProblemException ex) when (ex.Problem.Status is 403 or 409 or 422)
        {
            return false;
        }
    }

    /// <summary>
    /// Abmelden: Token serverseitig widerrufen (<c>POST /v1/auth/logout</c>) und lokal löschen. Lokal wird immer gelöscht –
    /// auch offline oder wenn der Token schon ungültig ist (dann ist er auf dem Server ohnehin nicht mehr nutzbar).
    /// </summary>
    /// <returns><c>true</c>, wenn der Server den Widerruf bestätigt hat.</returns>
    public async Task<bool> LogoutAsync(CancellationToken cancellationToken = default)
    {
        var revoked = false;
        try
        {
            if (await tokens.GetAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                await client.V1_auth_logoutAsync(cancellationToken).ConfigureAwait(false);
                revoked = true;
            }
        }
        catch (ApiProblemException)
        {
            // offline oder schon widerrufen – lokal trotzdem abmelden
        }
        catch (GeneratedApiException)
        {
            // unerwartete Antwort – lokal trotzdem abmelden
        }
        finally
        {
            await tokens.ClearAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return revoked;
    }

    public async Task<PlatformInfo> GetPlatformAsync(CancellationToken cancellationToken = default)
    {
        var data = (await client.V1_statusAsync(cancellationToken).ConfigureAwait(false)).Data;
        return new PlatformInfo(data.Name, data.App?.Version ?? data.Version, data.App?.Commit);
    }

    internal static UserProfile Map(MeResource me) => new(
        me.Id,
        me.Slug,
        me.Name,
        me.Email,
        me.Avatar?.Url_small ?? me.Avatar?.Url ?? me.Avatar_url,
        me.Rank?.Label ?? me.Rank?.Name,
        me.Rank?.Color,
        me.Status,
        me.Two_factor_enabled,
        me.Joined_at,
        me.Squads?.Select(s => s.Name).ToList() ?? [],
        (me.Permissions ?? new Dictionary<string, ICollection<string>>())
            .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.ToList(), StringComparer.Ordinal),
        me.Token?.Scopes?.ToList());
}
