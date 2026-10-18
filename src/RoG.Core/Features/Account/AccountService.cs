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

/// <summary>Angemeldetes Gerät (Konto → Apps &amp; Geräte).</summary>
public sealed record DeviceInfo(string Id, string Name, string Platform, string? AppVersion, bool Current, bool Push, DateTimeOffset? LastActiveAt);

/// <summary>Datenexport (Art. 15/20 DSGVO): Status, Größe, Download bis zum Ablauf.</summary>
public sealed record DataExport(string Id, string Status, long? Size, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt)
{
    public bool IsReady => Status == "ready";
}

/// <summary>Konto-Sicherheit und Daten: Geräte abmelden, Datenexport, Konto löschen (Guide 10/35).</summary>
public sealed class AccountDataService(IRoGApiClient client, ITokenStore tokens)
{
    public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.V1_devices_indexAsync(cancellationToken).ConfigureAwait(false);
        return response.Data
            .Select(d => new DeviceInfo(d.Id, d.Name, d.Platform, d.App_version, d.Current, d.Push, ApiValues.Time(d.Last_active_at)))
            .OrderByDescending(d => d.Current).ThenByDescending(d => d.LastActiveAt)
            .ToList();
    }

    /// <summary>Gerät abmelden (Token widerrufen). Ist es das eigene, wird auch lokal abgemeldet.</summary>
    public async Task RemoveDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        await client.V1_devices_destroyAsync(device.Id, cancellationToken).ConfigureAwait(false);
        if (device.Current)
        {
            await tokens.ClearAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<DataExport>> GetExportsAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.V1_me_exports_indexAsync(cancellationToken).ConfigureAwait(false);
        return response.Data.Select(e => new DataExport(e.Id, e.Status, e.Size, ApiValues.Time(e.Created_at) ?? DateTimeOffset.UtcNow, ApiValues.Time(e.Expires_at))).ToList();
    }

    /// <summary>Neuen Export anstoßen (läuft im Hintergrund; fertig → Benachrichtigung, dann herunterladen).</summary>
    public async Task RequestExportAsync(CancellationToken cancellationToken = default) =>
        await client.V1_me_exports_storeAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>Fertigen Export als ZIP in <paramref name="folder"/> speichern; Rückgabe = Pfad.</summary>
    public async Task<string> DownloadExportAsync(string exportId, string folder, CancellationToken cancellationToken = default)
    {
        using var file = await client.V1_me_exports_downloadAsync(exportId, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"rog-datenexport-{DateTime.Now:yyyy-MM-dd-HHmm}.zip");
        await using var target = System.IO.File.Create(path);
        await file.Stream.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        return path;
    }

    /// <summary>
    /// Konto endgültig löschen (<c>DELETE /v1/me</c>): Bestätigung = Passwort bzw. Anzeigename bei Konten ohne Passwort.
    /// Danach ist auch das Gerät abgemeldet.
    /// </summary>
    public async Task DeleteAccountAsync(string confirmation, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmation);
        await client.V1_me_destroyAsync(new Body80 { Confirmation = confirmation }, cancellationToken).ConfigureAwait(false);
        await tokens.ClearAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Aus dem Clan austreten (<c>POST /v1/me/leave-clan</c>, v0.30.1) – Voraussetzung, damit aktive Mitglieder ihr Konto
    /// löschen können. Bestätigung wie beim Löschen. Fehler kommen als <see cref="Api.ApiProblemException"/>;
    /// <see cref="AccountDeletionFlow"/> wertet sie aus.
    /// </summary>
    public async Task<bool> LeaveClanAsync(string confirmation, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmation);
        var response = await client.V1_me_leaveClanAsync(new Body87 { Confirmation = confirmation }, cancellationToken).ConfigureAwait(false);
        return response.Data.Is_member;
    }
}

public enum AccountDeletionStage
{
    /// <summary>Bestätigung eingeben und löschen.</summary>
    Confirm,

    /// <summary>409 <c>leave_clan_first</c>: erst austreten (gleiche Bestätigung), dann löschen.</summary>
    LeaveRequired,

    /// <summary>Austritt geht in der App nicht (ältere Website: 404/405) – an die Clanleitung wenden.</summary>
    LeaveUnavailable,

    /// <summary>Ausgetreten (oder war schon kein Mitglied) – Löschen ist frei.</summary>
    Left,

    /// <summary>Owner: kann weder austreten noch löschen.</summary>
    Blocked,
}

/// <summary>
/// Zustände des Dialogs „Konto löschen“ (wie Mobile-App): Löschen → bei <c>leave_clan_first</c> Austritt anbieten →
/// danach löschen. <see cref="ErrorCode"/>/<see cref="FieldError"/> beschreiben den letzten Fehler (422 Feld
/// <c>confirmation</c>, 429 mit <see cref="RetryAfter"/>), ohne den Zustand zu wechseln.
/// </summary>
public sealed record AccountDeletionFlow(AccountDeletionStage Stage, string? ErrorCode = null, string? FieldError = null, TimeSpan? RetryAfter = null)
{
    public static AccountDeletionFlow Start { get; } = new(AccountDeletionStage.Confirm);

    public bool CanDelete => Stage is AccountDeletionStage.Confirm or AccountDeletionStage.Left;

    public bool CanLeave => Stage == AccountDeletionStage.LeaveRequired;

    public bool HasError => ErrorCode is not null;

    public AccountDeletionFlow AfterDeleteFailed(Api.ApiProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return problem.Code switch
        {
            "leave_clan_first" when Stage != AccountDeletionStage.LeaveUnavailable => new(AccountDeletionStage.LeaveRequired),
            "owner_cannot_delete" => new(AccountDeletionStage.Blocked, problem.Code),
            _ => Failed(problem),
        };
    }

    public static AccountDeletionFlow AfterLeft() => new(AccountDeletionStage.Left);

    public AccountDeletionFlow AfterLeaveFailed(Api.ApiProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return problem switch
        {
            // schon kein Mitglied (z. B. parallel ausgetragen) → wie ausgetreten
            { Code: "not_a_member" } => new(AccountDeletionStage.Left),
            { Code: "owner_cannot_leave" } => new(AccountDeletionStage.Blocked, problem.Code),
            { Status: 404 or 405 } => new(AccountDeletionStage.LeaveUnavailable),
            _ => Failed(problem),
        };
    }

    private AccountDeletionFlow Failed(Api.ApiProblem problem) =>
        this with
        {
            ErrorCode = problem.Code,
            FieldError = problem.IsValidation && problem.Errors.TryGetValue("confirmation", out var errors) && errors.Count > 0 ? errors[0] : null,
            RetryAfter = problem.IsRateLimited ? problem.RetryAfter : null,
        };
}
