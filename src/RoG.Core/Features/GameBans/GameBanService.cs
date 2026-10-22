using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RoG.Core.Api;

namespace RoG.Core.Features.GameBans;

/// <summary>Zustellung eines Bans per RCON an einen Server (<c>deliveries</c>): active · failed · dry_run · lifted.</summary>
public sealed record GameBanDelivery(string? ServerName, string Status, string? Error, DateTimeOffset? UpdatedAt);

/// <summary>
/// Zentraler Gameserver-Ban (Guide 43): Person über Kennungstyp + Kennung, Geltung global · game · server, optional ein
/// IP-Netz (nur mit <c>gamebans.manage</c> sichtbar). Status active · expired · lifted.
/// </summary>
public sealed record GameBan(
    string Id,
    string Status,
    string Scope,
    string? GameName,
    string? ServerName,
    string IdentifierType,
    string Identifier,
    string? Name,
    string? Ip,
    string? Reason,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? CreatedAt,
    string? CreatedVia,
    string? CreatedBy,
    string? IssuedBy,
    string? SourceServer,
    string? UserName,
    DateTimeOffset? LiftedAt,
    string? LiftedBy,
    string? LiftReason,
    IReadOnlyList<GameBanDelivery> Deliveries)
{
    public bool IsActive => Status == GameBanService.Active;

    public bool IsPermanent => ExpiresAt is null;

    /// <summary>Anzeigename: Ingame-Name, sonst verknüpftes Mitglied, sonst die Kennung.</summary>
    public string Title => Name ?? UserName ?? Identifier;

    /// <summary>In der öffentlichen Ban-Liste sichtbar (Website v0.34.0; Admins blenden einzelne aus).</summary>
    public bool IsPublic { get; init; } = true;

    /// <summary>Einspruchs-Code aus der Kick-Nachricht – sehen nur die zuständigen Admins.</summary>
    public string? AppealCode { get; init; }

    /// <summary>Öffentliche Ban-Seite (<c>/{sprache}/bans/{id}</c>).</summary>
    public Uri? PublicUrl { get; init; }
}

/// <summary>Neuer Ban aus der App. <see cref="IdentifierType"/> leer = Typ des Spiels (guid/steamid/mc_uuid).</summary>
public sealed record GameBanDraft(string Scope, string? ServerId, string? IdentifierType, string Identifier, string? Name, string? Ip, int Minutes, string Reason);

/// <summary>Plugin-Schlüssel eines Servers – nur Status, nie der Schlüssel selbst (außer einmalig beim Erstellen).</summary>
public sealed record PluginKeyStatus(
    bool Exists,
    bool Active,
    string? Hint,
    bool MayIssue,
    bool MayLift,
    bool MayLiftAny,
    bool MayIp,
    bool MayBanMembers,
    string? MaxScope,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? RotatedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? LastHeartbeatAt,
    string? Plugin,
    string? PluginVersion,
    bool PluginOutdated,
    string? GameVersion,
    int? PlayersOnline,
    bool PluginActive)
{
    /// <summary>Minecraft-Konten verknüpfen (Website v0.36.0): Link-Codes für beliebige UUIDs – nur für den Proxy des Netzwerks.</summary>
    public bool MayLink { get; init; }

    /// <summary>Rebel-Coins buchen (Website v0.39.0): Shops, Handel, Berufe – für Server mit Shop oder Berufen.</summary>
    public bool MayEconomy { get; init; }
}

/// <summary>Rechte des Plugin-Schlüssels (POST/PATCH). Alles über „anlegen, nur dieser Server“ braucht <c>gamebans.manage</c>.</summary>
public sealed record PluginKeyRights(bool MayIssue, bool MayLift, bool MayLiftAny, bool MayIp, bool MayBanMembers, string MaxScope, bool MayLink = false, bool MayEconomy = false)
{
    public static PluginKeyRights Minimal { get; } = new(true, false, false, false, false, GameBanService.ScopeServer);

    public static PluginKeyRights From(PluginKeyStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new(status.MayIssue, status.MayLift, status.MayLiftAny, status.MayIp, status.MayBanMembers, status.MaxScope ?? GameBanService.ScopeServer, status.MayLink, status.MayEconomy);
    }

    /// <summary>Geht über das hinaus, was ohne <c>gamebans.manage</c> vergeben werden darf.</summary>
    public bool NeedsManage => MayLift || MayLiftAny || MayIp || MayBanMembers || MayLink || MayEconomy || MaxScope != GameBanService.ScopeServer;

    internal JsonObject ToJson() => new()
    {
        ["may_issue"] = MayIssue,
        ["may_lift"] = MayLift,
        ["may_lift_any"] = MayLiftAny,
        ["may_ip"] = MayIp,
        ["may_ban_members"] = MayBanMembers,
        ["may_link"] = MayLink,
        ["may_economy"] = MayEconomy,
        ["max_scope"] = MaxScope,
    };
}

/// <summary>Neu erstellter bzw. rotierter Schlüssel: <see cref="Token"/> nur hier, nie speichern oder loggen.</summary>
public sealed record PluginKeyCreated(PluginKeyStatus Status, string Token);

/// <summary>
/// Zentrale Gameserver-Bans (Website v0.33.0, Guide 43) und Plugin-Schlüssel je Server. Direkt über <c>/v1/</c> ohne
/// ETag-Cache (Stand soll nach einer Aktion sofort stimmen, der Schlüssel darf in keinen Cache).
/// Bans: Scope <c>gamebans:manage</c> plus Recht <c>servers.bans</c> (Server) bzw. <c>gamebans.manage</c>;
/// Schlüssel: Scope <c>servers:manage</c> plus Recht <c>servers.manage</c>. Den Rest entscheidet der Server.
/// </summary>
public sealed partial class GameBanService(HttpClient http)
{
    public const string BanScope = "gamebans:manage";
    public const string KeyScope = "servers:manage";
    public const string ManagePermission = "gamebans.manage";

    public const string Active = "active";
    public const string Expired = "expired";
    public const string Lifted = "lifted";
    public const string All = "all";

    public const string ScopeServer = "server";
    public const string ScopeGame = "game";
    public const string ScopeGlobal = "global";

    public const int MaxReason = 200;

    public static IReadOnlyList<string> Statuses { get; } = [Active, Expired, Lifted, All];

    public static IReadOnlyList<string> Scopes { get; } = [ScopeServer, ScopeGame, ScopeGlobal];

    /// <summary>Kennungstypen der Website; eigene Typen für künftige Spiele sind erlaubt.</summary>
    public static IReadOnlyList<string> KnownTypes { get; } = ["guid", "steamid", "mc_uuid"];

    // --- Bans ------------------------------------------------------------------------------------------------------

    /// <summary>Bans, die auf einem Server gelten (<c>filter[server]</c>: global, Spiel des Servers, dieser Server).</summary>
    public async Task<Page<GameBan>> ListAsync(string? server, string status = Active, string? search = null, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { "per_page=50", "filter%5Bstatus%5D=" + Uri.EscapeDataString(Statuses.Contains(status) ? status : Active) };
        if (!string.IsNullOrWhiteSpace(server))
        {
            query.Add("filter%5Bserver%5D=" + Uri.EscapeDataString(server));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add("filter%5Bsearch%5D=" + Uri.EscapeDataString(search.Trim()[..Math.Min(search.Trim().Length, 64)]));
        }

        if (!string.IsNullOrEmpty(cursor))
        {
            query.Add("cursor=" + Uri.EscapeDataString(cursor));
        }

        var root = await SendAsync(HttpMethod.Get, "game-bans?" + string.Join('&', query), null, cancellationToken).ConfigureAwait(false);
        var items = root?["data"] is JsonArray list ? list.Select(Parse).OfType<GameBan>().ToList() : [];
        return new Page<GameBan>(items, Str(root?["meta"], "next_cursor"));
    }

    public async Task<GameBan> GetAsync(string id, CancellationToken cancellationToken = default) =>
        ParseData(await SendAsync(HttpMethod.Get, "game-bans/" + Id(id), null, cancellationToken).ConfigureAwait(false));

    public async Task<GameBan> CreateAsync(GameBanDraft draft, CancellationToken cancellationToken = default) =>
        ParseData(await SendAsync(HttpMethod.Post, "game-bans", Body(draft), cancellationToken).ConfigureAwait(false));

    /// <summary>Nur aktive Bans: neue Dauer ab jetzt (0 = dauerhaft) und/oder Grund. Geltung, Kennung und IP bleiben.</summary>
    public async Task<GameBan> UpdateAsync(string id, int? minutes, string? reason, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject();
        if (minutes is { } m)
        {
            body["minutes"] = Math.Max(0, m);
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            body["reason"] = reason.Trim();
        }

        return ParseData(await SendAsync(HttpMethod.Patch, "game-bans/" + Id(id), body, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>In der öffentlichen Ban-Liste zeigen bzw. ausblenden – geht auch bei beendeten Bans, ändert für Plugins nichts.</summary>
    public async Task<GameBan> SetPublicAsync(string id, bool isPublic, CancellationToken cancellationToken = default) =>
        ParseData(await SendAsync(HttpMethod.Patch, "game-bans/" + Id(id), new JsonObject { ["is_public"] = isPublic }, cancellationToken).ConfigureAwait(false));

    public async Task<GameBan> LiftAsync(string id, string? reason, CancellationToken cancellationToken = default) =>
        ParseData(await SendAsync(HttpMethod.Post, $"game-bans/{Id(id)}/lift", new JsonObject { ["reason"] = Trimmed(reason) }, cancellationToken).ConfigureAwait(false));

    /// <summary>Vorfall: alle aktiven Bans aufheben, die über das Plugin dieses Servers kamen (nur <c>gamebans.manage</c>).</summary>
    public async Task<int> LiftPluginBansAsync(string server, string? reason, DateTimeOffset? since, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["reason"] = Trimmed(reason),
            ["since"] = since?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        };
        var root = await SendAsync(HttpMethod.Post, $"servers/{Uri.EscapeDataString(server)}/game-bans/lift", body, cancellationToken).ConfigureAwait(false);
        return Int(root?["data"], "lifted") ?? 0;
    }

    /// <summary>Prüft einen neuen Ban vor dem Senden; Schlüssel der Fehlertexte (<c>GameBans_Rule_*</c>) oder leer.</summary>
    public static IReadOnlyList<string> Validate(GameBanDraft draft, bool mayIp)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(draft.Identifier) || !IdentifierPattern().IsMatch(draft.Identifier.Trim()))
        {
            errors.Add("GameBans_Rule_Identifier");
        }

        if (!string.IsNullOrWhiteSpace(draft.IdentifierType) && !TypePattern().IsMatch(draft.IdentifierType.Trim()))
        {
            errors.Add("GameBans_Rule_Type");
        }

        if (string.IsNullOrWhiteSpace(draft.Reason) || draft.Reason.Trim().Length > MaxReason)
        {
            errors.Add("GameBans_Rule_Reason");
        }

        if (!Scopes.Contains(draft.Scope) || (draft.Scope != ScopeGlobal && string.IsNullOrWhiteSpace(draft.ServerId)))
        {
            errors.Add("GameBans_Rule_Scope");
        }

        if (!string.IsNullOrWhiteSpace(draft.Ip) && (!mayIp || !IpPattern().IsMatch(draft.Ip.Trim())))
        {
            errors.Add("GameBans_Rule_Ip");
        }

        if (draft.Minutes < 0)
        {
            errors.Add("GameBans_Rule_Minutes");
        }

        return errors;
    }

    internal static JsonObject Body(GameBanDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var body = new JsonObject
        {
            ["scope"] = draft.Scope,
            ["identifier"] = draft.Identifier.Trim(),
            ["minutes"] = Math.Max(0, draft.Minutes),
            ["reason"] = draft.Reason.Trim(),
        };

        // Geltung game: der Server bestimmt das Spiel; global gilt überall
        if (draft.Scope != ScopeGlobal && !string.IsNullOrWhiteSpace(draft.ServerId))
        {
            body["server_id"] = draft.ServerId;
        }

        if (Trimmed(draft.IdentifierType) is { } type)
        {
            body["identifier_type"] = type;
        }

        if (Trimmed(draft.Name) is { } name)
        {
            body["name"] = name;
        }

        if (Trimmed(draft.Ip) is { } ip)
        {
            body["ip"] = ip;
        }

        return body;
    }

    public static GameBan? Parse(JsonNode? n)
    {
        if (n is not JsonObject || Str(n, "id") is not { } id)
        {
            return null;
        }

        var deliveries = n["deliveries"] is JsonArray ds
            ? ds.OfType<JsonObject>().Select(d => new GameBanDelivery(Str(d["server"], "name"), Str(d, "status") ?? "active", Str(d, "error"), Time(d, "updated_at"))).ToList()
            : [];
        return new GameBan(
            id,
            Str(n, "status") ?? Active,
            Str(n, "scope") ?? ScopeGame,
            Str(n["game"], "name"),
            Str(n["server"], "name"),
            Str(n, "identifier_type") ?? "?",
            Str(n, "identifier") ?? string.Empty,
            Str(n, "name"),
            Str(n, "ip"),
            Str(n, "reason"),
            Time(n, "expires_at"),
            Time(n, "created_at"),
            Str(n, "created_via"),
            Str(n["created_by"], "name"),
            Str(n["issued_by"], "name"),
            Str(n["source_server"], "name"),
            Str(n["user"], "name"),
            Time(n, "lifted_at"),
            Str(n["lifted_by"], "name"),
            Str(n, "lift_reason"),
            deliveries)
        {
            IsPublic = n["is_public"] is not JsonValue p || p.GetValueKind() != JsonValueKind.False,
            AppealCode = Str(n, "appeal_code"),
            PublicUrl = Str(n, "public_url") is { } url && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? u : null,
        };
    }

    // --- Plugin-Schlüssel ------------------------------------------------------------------------------------------

    public async Task<PluginKeyStatus> KeyAsync(string server, CancellationToken cancellationToken = default) =>
        ParseKey((await SendAsync(HttpMethod.Get, KeyPath(server), null, cancellationToken).ConfigureAwait(false))?["data"]);

    /// <summary>Erstellen bzw. rotieren – der alte Schlüssel gilt danach nicht mehr.</summary>
    public async Task<PluginKeyCreated> CreateKeyAsync(string server, PluginKeyRights rights, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rights);
        var data = (await SendAsync(HttpMethod.Post, KeyPath(server), rights.ToJson(), cancellationToken).ConfigureAwait(false))?["data"];
        var token = Str(data, "token") ?? throw new InvalidDataException("Plugin key response without token.");
        return new PluginKeyCreated(ParseKey(data), token);
    }

    public async Task<PluginKeyStatus> UpdateKeyAsync(string server, PluginKeyRights rights, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rights);
        return ParseKey((await SendAsync(HttpMethod.Patch, KeyPath(server), rights.ToJson(), cancellationToken).ConfigureAwait(false))?["data"]);
    }

    public async Task RevokeKeyAsync(string server, CancellationToken cancellationToken = default) =>
        await SendAsync(HttpMethod.Delete, KeyPath(server), null, cancellationToken).ConfigureAwait(false);

    public static PluginKeyStatus ParseKey(JsonNode? d) => new(
        Bool(d, "exists"),
        Bool(d, "active"),
        Str(d, "hint"),
        Bool(d, "may_issue"),
        Bool(d, "may_lift"),
        Bool(d, "may_lift_any"),
        Bool(d, "may_ip"),
        Bool(d, "may_ban_members"),
        Str(d, "max_scope"),
        Time(d, "created_at"),
        Time(d, "rotated_at"),
        Time(d, "revoked_at"),
        Time(d, "last_used_at"),
        Time(d, "last_heartbeat_at"),
        Str(d, "plugin"),
        Str(d, "plugin_version"),
        Bool(d, "plugin_outdated"),
        Str(d, "game_version"),
        Int(d, "players_online"),
        Bool(d, "plugin_active"))
    {
        MayLink = Bool(d, "may_link"),
        MayEconomy = Bool(d, "may_economy"),
    };

    private static string KeyPath(string server) => $"servers/{Uri.EscapeDataString(server)}/plugin-key";

    private static string Id(string id) =>
        UlidPattern().IsMatch(id) ? id : throw new ArgumentException("Invalid ban id.", nameof(id));

    private static GameBan ParseData(JsonNode? root) =>
        Parse(root?["data"]) ?? throw new InvalidDataException("Game ban response without data.");

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

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Str(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? ApiValues.Text(v.GetValue<string>()) : null;

    private static int? Int(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var i) ? i : null;

    private static bool Bool(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.GetValueKind() == JsonValueKind.True;

    private static DateTimeOffset? Time(JsonNode? n, string name) => ApiValues.Time(Str(n, name));

    // Muster wie auf der Website (Guide 43)
    [GeneratedRegex("^[A-Za-z0-9:_\\-.\\[\\]]{1,64}$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[a-z][a-z0-9_]{1,15}$")]
    private static partial Regex TypePattern();

    // IPv4 (einzeln oder /24) bzw. IPv6 (/64) – grob, die Website prüft genau
    [GeneratedRegex("^(\\d{1,3}(\\.\\d{1,3}){3}(/24)?|[0-9A-Fa-f:]{2,39}(/64)?)$")]
    private static partial Regex IpPattern();

    [GeneratedRegex("^[0-9A-HJKMNP-TV-Za-hjkmnp-tv-z]{26}$")]
    private static partial Regex UlidPattern();
}
