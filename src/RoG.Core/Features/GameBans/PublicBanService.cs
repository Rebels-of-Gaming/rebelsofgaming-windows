using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RoG.Core.Api;

namespace RoG.Core.Features.GameBans;

/// <summary>Letzter Einspruch zu einem öffentlichen Ban (<c>appeal</c>): open · accepted · rejected.</summary>
public sealed record PublicBanAppeal(string Status, DateTimeOffset? CreatedAt, DateTimeOffset? DecidedAt);

/// <summary>
/// Einspruchs-Möglichkeit (<c>appeal_info</c>, nur im Einzelabruf): <see cref="Available"/>, sonst <see cref="Blocked"/>
/// (not_active · unavailable · open · cooldown). <see cref="Proofs"/> (nur mit Token): Nachweise ohne Code – linked ·
/// steam · minecraft. Ohne einen davon braucht der Einspruch den Code aus der Kick-Nachricht.
/// </summary>
public sealed record PublicBanAppealInfo(string? Status, Uri? ThreadUrl, bool Available, string? Blocked, DateTimeOffset? CooldownUntil, IReadOnlyList<string> Proofs)
{
    public bool NeedsCode => Proofs.Count == 0;
}

/// <summary>
/// Eintrag der öffentlichen Ban-Liste (Website v0.34.0, Guide 43, Annahme 488): nur Ingame-Name, gekürzte Kennung,
/// Spiel, Server (nur bei Geltung <c>server</c>), Grund und Zeitraum – nie IP, volle Kennung oder Admins.
/// </summary>
public sealed record PublicBan(
    string Id,
    Uri? Url,
    string? Name,
    string IdentifierType,
    string IdentifierMasked,
    string? GameName,
    string? ServerName,
    string Scope,
    string? ScopeLabel,
    string? Reason,
    string Status,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt,
    bool Permanent,
    DateTimeOffset? EndedAt,
    PublicBanAppeal? Appeal,
    PublicBanAppealInfo? AppealInfo)
{
    public bool IsActive => Status == GameBanService.Active;

    public string Title => Name ?? IdentifierMasked;
}

/// <summary>Eingelegter bzw. entschiedener Einspruch (Antwort von <c>POST /bans/{ban}/appeals</c> und Admin-Liste).</summary>
public sealed record BanAppeal(
    string Id,
    string? BanId,
    string? BanName,
    string? BanStatus,
    Uri? BanUrl,
    string Status,
    string? Proof,
    Uri? ThreadUrl,
    string? UserName,
    string? UserSlug,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    bool CanDecide)
{
    public bool IsOpen => Status == PublicBanService.AppealOpen;
}

/// <summary>Forum-Bereich für Einsprüche (<c>gamebans.appeals_board</c>) und wählbare Bereiche.</summary>
public sealed record AppealSettings(string? BoardId, string? BoardName, bool BoardPublic, IReadOnlyList<(string Id, string Name)> BoardOptions);

/// <summary>
/// Öffentliche Ban-Liste und Einsprüche (Website v0.34.0, Guide 43): Liste/Einzelabruf ohne Login, Einspruch mit Scope
/// <c>gamebans:appeal</c>, Einsprüche entscheiden und Einspruchs-Bereich mit <c>gamebans:manage</c> (Recht prüft der
/// Server). Direkt über <c>/v1/</c> ohne ETag-Cache, damit der Stand nach einer Aktion sofort stimmt.
/// </summary>
public sealed partial class PublicBanService(HttpClient http)
{
    public const string AppealScope = "gamebans:appeal";

    public const string Active = "active";
    public const string Ended = "ended";

    public const string AppealOpen = "open";
    public const string AppealAccepted = "accepted";
    public const string AppealRejected = "rejected";
    public const string AppealAll = "all";

    public const string ProofCode = "code";

    public const int MinStatement = 20;
    public const int MaxStatement = 3000;
    public const int MaxNote = 500;

    public static IReadOnlyList<string> Statuses { get; } = [Active, Ended];

    public static IReadOnlyList<string> AppealStatuses { get; } = [AppealOpen, AppealAccepted, AppealRejected, AppealAll];

    // --- Öffentliche Liste -----------------------------------------------------------------------------------------

    /// <summary>Aktive bzw. in den letzten 30 Tagen beendete Bans; Suche: Teil des Namens oder die volle Kennung.</summary>
    public async Task<Page<PublicBan>> ListAsync(string status = Active, string? search = null, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { "per_page=50", "filter%5Bstatus%5D=" + (Statuses.Contains(status) ? status : Active) };
        if (Limit(search, 64) is { } s)
        {
            query.Add("filter%5Bsearch%5D=" + Uri.EscapeDataString(s));
        }

        if (!string.IsNullOrEmpty(cursor))
        {
            query.Add("cursor=" + Uri.EscapeDataString(cursor));
        }

        var root = await SendAsync(HttpMethod.Get, "bans?" + string.Join('&', query), null, cancellationToken).ConfigureAwait(false);
        var items = root?["data"] is JsonArray list ? list.Select(Parse).OfType<PublicBan>().ToList() : [];
        return new Page<PublicBan>(items, Str(root?["meta"], "next_cursor"));
    }

    /// <summary>Ein Ban mit <c>appeal_info</c>; ausgeblendete oder länger als 30 Tage beendete → <c>404 not_found</c>.</summary>
    public async Task<PublicBan> GetAsync(string id, CancellationToken cancellationToken = default) =>
        Parse((await SendAsync(HttpMethod.Get, "bans/" + Id(id), null, cancellationToken).ConfigureAwait(false))?["data"])
        ?? throw new InvalidDataException("Ban response without data.");

    /// <summary>Einspruch einlegen: wird ein Forum-Thema im Bereich „Ban-Einsprüche“ (Antwort mit <c>thread_url</c>).</summary>
    public async Task<BanAppeal> AppealAsync(string id, string statement, string? code, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["statement"] = statement.Trim() };
        if (NormalizeCode(code) is { } c)
        {
            body["code"] = c;
        }

        return ParseAppeal((await SendAsync(HttpMethod.Post, $"bans/{Id(id)}/appeals", body, cancellationToken).ConfigureAwait(false))?["data"])
            ?? throw new InvalidDataException("Appeal response without data.");
    }

    /// <summary>Prüft einen Einspruch vor dem Senden; Schlüssel der Fehlertexte (<c>Bans_Rule_*</c>) oder leer.</summary>
    public static IReadOnlyList<string> ValidateAppeal(string? statement, string? code, bool needsCode)
    {
        var errors = new List<string>();
        var length = statement?.Trim().Length ?? 0;
        if (length is < MinStatement or > MaxStatement)
        {
            errors.Add("Bans_Rule_Statement");
        }

        var normalized = NormalizeCode(code);
        if ((needsCode && normalized is null) || (normalized is not null && !CodePattern().IsMatch(normalized)))
        {
            errors.Add("Bans_Rule_Code");
        }

        return errors;
    }

    /// <summary>Code aus der Kick-Nachricht: Leerzeichen raus, Groß-/Kleinschreibung und Bindestriche prüft der Server.</summary>
    public static string? NormalizeCode(string? code)
    {
        var c = code is null ? null : WhitespacePattern().Replace(code, string.Empty);
        return string.IsNullOrEmpty(c) ? null : c.ToUpperInvariant();
    }

    // --- Einsprüche verwalten --------------------------------------------------------------------------------------

    public async Task<Page<BanAppeal>> AppealsAsync(string status = AppealOpen, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { "per_page=50", "filter%5Bstatus%5D=" + (AppealStatuses.Contains(status) ? status : AppealOpen) };
        if (!string.IsNullOrEmpty(cursor))
        {
            query.Add("cursor=" + Uri.EscapeDataString(cursor));
        }

        var root = await SendAsync(HttpMethod.Get, "game-ban-appeals?" + string.Join('&', query), null, cancellationToken).ConfigureAwait(false);
        var items = root?["data"] is JsonArray list ? list.Select(ParseAppeal).OfType<BanAppeal>().ToList() : [];
        return new Page<BanAppeal>(items, Str(root?["meta"], "next_cursor"));
    }

    /// <summary>Annehmen hebt den Ban auf; ablehnen lässt ihn bestehen (neuer Einspruch erst nach 30 Tagen, außer <paramref name="reappealAllowed"/>).</summary>
    public async Task<BanAppeal> DecideAsync(string appealId, bool accept, string? note, bool reappealAllowed = false, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["decision"] = accept ? AppealAccepted : AppealRejected };
        if (Limit(note, MaxNote) is { } n)
        {
            body["note"] = n;
        }

        if (!accept && reappealAllowed)
        {
            body["reappeal_allowed"] = true;
        }

        return ParseAppeal((await SendAsync(HttpMethod.Post, $"game-ban-appeals/{Id(appealId)}/decision", body, cancellationToken).ConfigureAwait(false))?["data"])
            ?? throw new InvalidDataException("Decision response without data.");
    }

    public async Task<AppealSettings> SettingsAsync(CancellationToken cancellationToken = default) =>
        ParseSettings((await SendAsync(HttpMethod.Get, "game-bans/settings", null, cancellationToken).ConfigureAwait(false))?["data"]);

    /// <summary>Forum-Bereich wählen (<c>null</c> = keiner, Einsprüche dann aus).</summary>
    public async Task<AppealSettings> SetBoardAsync(string? boardId, CancellationToken cancellationToken = default) =>
        ParseSettings((await SendAsync(HttpMethod.Patch, "game-bans/settings", new JsonObject { ["appeals_board"] = string.IsNullOrEmpty(boardId) ? null : boardId }, cancellationToken).ConfigureAwait(false))?["data"]);

    /// <summary>Bereich „Ban-Einsprüche“ anlegen und wählen.</summary>
    public async Task<AppealSettings> CreateBoardAsync(CancellationToken cancellationToken = default) =>
        ParseSettings((await SendAsync(HttpMethod.Post, "game-bans/settings/appeals-board", new JsonObject(), cancellationToken).ConfigureAwait(false))?["data"]);

    // --- Lesen -----------------------------------------------------------------------------------------------------

    public static PublicBan? Parse(JsonNode? n)
    {
        if (n is not JsonObject || Str(n, "id") is not { } id)
        {
            return null;
        }

        var appeal = n["appeal"] is JsonObject a && Str(a, "status") is { } status
            ? new PublicBanAppeal(status, Time(a, "created_at"), Time(a, "decided_at"))
            : null;
        var info = n["appeal_info"] is JsonObject i
            ? new PublicBanAppealInfo(
                Str(i, "status"),
                Link(i, "thread_url"),
                Bool(i, "available"),
                Str(i, "blocked"),
                Time(i, "cooldown_until"),
                i["proofs"] is JsonArray ps ? ps.OfType<JsonValue>().Where(p => p.GetValueKind() == JsonValueKind.String).Select(p => p.GetValue<string>()).ToList() : [])
            : null;
        return new PublicBan(
            id,
            Link(n, "url"),
            Str(n, "name"),
            Str(n, "identifier_type") ?? "?",
            Str(n, "identifier_masked") ?? "—",
            Str(n["game"], "name"),
            Str(n["server"], "name"),
            Str(n, "scope") ?? GameBanService.ScopeGame,
            Str(n, "scope_label"),
            Str(n, "reason"),
            Str(n, "status") ?? Active,
            Time(n, "created_at"),
            Time(n, "expires_at"),
            Bool(n, "permanent"),
            Time(n, "ended_at"),
            appeal,
            info);
    }

    public static BanAppeal? ParseAppeal(JsonNode? n)
    {
        if (n is not JsonObject || Str(n, "id") is not { } id)
        {
            return null;
        }

        var ban = n["ban"];
        return new BanAppeal(
            id,
            Str(ban, "id") ?? Str(n, "ban_id"),
            Str(ban, "name"),
            Str(ban, "status"),
            Link(ban, "url"),
            Str(n, "status") ?? AppealOpen,
            Str(n, "proof"),
            Link(n, "thread_url"),
            Str(n["user"], "name"),
            Str(n["user"], "slug"),
            Time(n, "created_at"),
            Time(n, "decided_at"),
            Str(n, "decision_note"),
            Bool(n, "can_decide"));
    }

    public static AppealSettings ParseSettings(JsonNode? d)
    {
        var board = d?["appeals_board"];
        var options = d?["board_options"] is JsonArray os
            ? os.Select(o => (Id: Str(o, "id"), Name: Str(o, "name"))).Where(o => o.Id is not null).Select(o => (o.Id!, o.Name ?? o.Id!)).ToList()
            : [];
        return new AppealSettings(Str(board, "id"), Str(board, "name"), Bool(board, "public"), options);
    }

    private static string Id(string id) =>
        UlidPattern().IsMatch(id) ? id : throw new ArgumentException("Invalid id.", nameof(id));

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        }

        request.Options.Set(RoGHttpHandler.SkipEtag, true);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    private static string? Limit(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v[..Math.Min(v.Length, max)];
    }

    private static string? Str(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? ApiValues.Text(v.GetValue<string>()) : null;

    private static bool Bool(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.GetValueKind() == JsonValueKind.True;

    private static DateTimeOffset? Time(JsonNode? n, string name) => ApiValues.Time(Str(n, name));

    /// <summary>Nur https-Adressen (Ban-Seite, Forum-Thema) – alles andere wird nicht verlinkt.</summary>
    private static Uri? Link(JsonNode? n, string name) =>
        Str(n, name) is { } s && Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? u : null;

    [GeneratedRegex("^[A-Z0-9-]{4,16}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex("^[0-9A-HJKMNP-TV-Za-hjkmnp-tv-z]{26}$")]
    private static partial Regex UlidPattern();
}
