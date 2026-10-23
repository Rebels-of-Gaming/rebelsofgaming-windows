using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RoG.Core.Features.Minecraft;

/// <summary>Im Admin festgelegte Rolle und Plugin-Ordner (maßgeblich für den Deploy); <see cref="Mismatch"/> = Heartbeat meldet anderes.</summary>
public sealed record NetworkDeploy(string? Role, string? Folder, bool Configured, bool Mismatch);

/// <summary>Letzter Heartbeat-Stand eines Minecraft-Servers (nur der aktuelle Stand, kein Verlauf).</summary>
public sealed record NetworkState(
    string? NetworkId,
    string? Role,
    double? Tps,
    double? Mspt,
    int PlayersOnline,
    int? PlayersMax,
    string? MinigameGame,
    string? MinigameState,
    string? Arena,
    string? PluginVersion,
    DateTimeOffset? EmptySince,
    DateTimeOffset? ReportedAt,
    bool Fresh,
    string? DeployFolder);

/// <summary>Minecraft-Server mit Pterodactyl-Verknüpfung (<see cref="Linked"/>), Zuordnung und Heartbeat (<c>null</c> = noch keiner).</summary>
public sealed record NetworkServer(string Id, string Slug, string Name, bool Linked, NetworkDeploy Deploy, NetworkState? State);

public sealed record PoolInstance(
    string Id,
    string Name,
    bool Linked,
    bool Excluded,
    int Failures,
    bool Quarantined,
    bool Backoff,
    string Status,
    string? MinigameState,
    string? Arena,
    int PlayersOnline,
    int? Capacity,
    double? Load,
    bool Joinable,
    DateTimeOffset? EmptySince);

/// <summary>Eintrag im Ereignisprotokoll des Pools (30 Tage).</summary>
public sealed record PoolAction(long Id, string Game, string? ServerName, string Action, string? Reason, bool Ok, string? Error, DateTimeOffset? CreatedAt);

/// <summary>Gruppe (Warteschlange) des Minigame-Pools; ohne Einstellung <see cref="Configured"/> <c>false</c> mit Standardwerten.</summary>
public sealed record PoolGroup(
    string Game,
    bool Configured,
    bool Enabled,
    int MinRunning,
    int StartThreshold,
    int IdleMinutes,
    int Running,
    int Joinable,
    int Players,
    IReadOnlyList<PoolInstance> Instances,
    IReadOnlyList<(string Id, string Name)> Suggestions,
    PoolAction? LastAction);

public sealed record PoolOverview(IReadOnlyList<PoolGroup> Groups, IReadOnlyList<PoolAction> Actions, int CooldownMinutes);

public sealed record ReleaseFile(string Name, long Size);

/// <summary>Geprüfter Stand aus dem GitHub-Release (<c>checking</c> → <c>ready</c>/<c>failed</c>), gilt bis <see cref="ExpiresAt"/>.</summary>
public sealed record PluginRelease(
    string Id,
    string Status,
    string? Error,
    string? Tag,
    DateTimeOffset? PublishedAt,
    string? Sha256,
    long Size,
    bool IsSigned,
    bool SignatureRequired,
    string? SignatureKeyId,
    IReadOnlyList<(string Folder, IReadOnlyList<ReleaseFile> Files)> Folders,
    IReadOnlyList<string> Ignored,
    DateTimeOffset? ExpiresAt)
{
    public bool IsChecking => Status == MinecraftService.ReleaseChecking;

    public bool IsReady => Status == MinecraftService.ReleaseReady;
}

public sealed record DeployTarget(string? ServerId, string ServerName, string? Folder, string Status, string? Message, IReadOnlyList<string> Uploaded, bool Restarted);

/// <summary>Verteil-Lauf (<c>queued</c> → <c>running</c> → <c>done</c>/<c>failed</c>), Fortschritt je Server.</summary>
public sealed record PluginDeploy(
    string Id,
    string Status,
    bool Restart,
    string? Tag,
    bool IsSigned,
    string? Error,
    string? StartedBy,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<DeployTarget> Targets)
{
    public bool IsRunning => Status is MinecraftService.DeployQueued or MinecraftService.DeployRunning;
}

/// <summary>
/// Betrieb des Minecraft-Netzwerks (Website v0.41.0, Guide 47, Annahme 496): Heartbeat-Stand und Rollen
/// (<c>minecraft:pool</c>/<c>minecraft:deploy</c>), Minigame-Pool (<c>minecraft:pool</c> + Recht <c>minecraft.pool</c>) und
/// Plugin-Deploy in zwei Stufen (<c>minecraft:deploy</c> + Recht <c>minecraft.deploy</c>). Beides Admin-Rechte mit 2FA.
/// </summary>
public sealed partial class MinecraftService
{
    public const string PoolScope = "minecraft:pool";
    public const string DeployScope = "minecraft:deploy";
    public const string PoolPermission = "minecraft.pool";
    public const string DeployPermission = "minecraft.deploy";

    public const string RoleProxy = "proxy";
    public const string RoleMinigame = "minigame";

    /// <summary>Rollen in der Reihenfolge der Website; nur <c>minigame</c> braucht einen eigenen Ordner.</summary>
    public static IReadOnlyList<string> ServerRoles { get; } = [RoleProxy, "lobby", "freebuild", "plots", "minigame_lobby", RoleMinigame];

    public const string ReleaseChecking = "checking";
    public const string ReleaseReady = "ready";
    public const string DeployQueued = "queued";
    public const string DeployRunning = "running";

    public const int MinRunningMax = 20;
    public const int IdleMinutesMax = 240;

    public async Task<IReadOnlyList<NetworkServer>> NetworkServersAsync(CancellationToken cancellationToken = default)
    {
        var root = await SendAsync(HttpMethod.Get, "minecraft/network/servers", null, cancellationToken).ConfigureAwait(false);
        return root?["data"] is JsonArray list ? list.Select(ParseNetworkServer).OfType<NetworkServer>().ToList() : [];
    }

    /// <summary>Rolle und Ordner festlegen; <paramref name="role"/> <c>null</c> entfernt die Zuordnung.</summary>
    public async Task<NetworkServer?> SetServerRoleAsync(string serverId, string? role, string? folder = null, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject();
        if (role is null)
        {
            body["role"] = null;
        }
        else if (!ServerRoles.Contains(role))
        {
            throw new ArgumentException("Unknown role.", nameof(role));
        }
        else
        {
            body["role"] = role;
            if (role == RoleMinigame)
            {
                body["folder"] = IsValidName(folder?.Trim()) ? folder!.Trim() : throw new ArgumentException("Invalid folder.", nameof(folder));
            }
        }

        var data = (await SendAsync(HttpMethod.Put, $"minecraft/network/servers/{Id(serverId)}/role", body, cancellationToken).ConfigureAwait(false))?["data"];
        return ParseNetworkServer(data);
    }

    public async Task<PoolOverview> PoolAsync(string? game = null, CancellationToken cancellationToken = default)
    {
        var path = "minecraft/pool?limit=50";
        if (IsValidName(game))
        {
            path += "&filter%5Bgame%5D=" + game;
        }

        return ParsePool(await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Einstellung einer Gruppe; <paramref name="servers"/> ersetzt die Zuordnung (nur Minecraft-Server mit Pterodactyl).</summary>
    public async Task<PoolGroup?> SavePoolAsync(string game, bool enabled, int minRunning, int startThreshold, int idleMinutes, IReadOnlyList<string> servers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(servers);
        var body = new JsonObject
        {
            ["enabled"] = enabled,
            ["min_running"] = Math.Clamp(minRunning, 1, MinRunningMax),
            ["start_threshold"] = Math.Clamp(startThreshold, 1, 100),
            ["idle_minutes"] = Math.Clamp(idleMinutes, 1, IdleMinutesMax),
            ["servers"] = new JsonArray(servers.Distinct(StringComparer.Ordinal).Select(s => (JsonNode)Id(s)).ToArray()),
        };
        var data = (await SendAsync(HttpMethod.Put, "minecraft/pool/" + Game(game), body, cancellationToken).ConfigureAwait(false))?["data"];
        return ParsePoolGroup(data);
    }

    public async Task DeletePoolAsync(string game, CancellationToken cancellationToken = default) =>
        await SendAsync(HttpMethod.Delete, "minecraft/pool/" + Game(game), null, cancellationToken).ConfigureAwait(false);

    /// <summary>Wartungsschalter setzen und/oder Fehlstarts/Quarantäne aufheben.</summary>
    public async Task UpdatePoolServerAsync(string game, string serverId, bool? excluded, bool resetFailures, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject();
        if (excluded is { } e)
        {
            body["excluded"] = e;
        }

        if (resetFailures)
        {
            body["reset_failures"] = true;
        }

        if (body.Count == 0)
        {
            return;
        }

        await SendAsync(new HttpMethod("PATCH"), $"minecraft/pool/{Game(game)}/servers/{Id(serverId)}", body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stufe 1: neuestes Release prüfen lassen (läuft im Job; läuft schon eine Prüfung, kommt diese zurück).</summary>
    public async Task<PluginRelease?> CheckReleaseAsync(CancellationToken cancellationToken = default) =>
        ParseRelease((await SendAsync(HttpMethod.Post, "minecraft/plugin-releases", new JsonObject(), cancellationToken).ConfigureAwait(false))?["data"]);

    public async Task<PluginRelease?> ReleaseAsync(string id, CancellationToken cancellationToken = default) =>
        ParseRelease((await SendAsync(HttpMethod.Get, "minecraft/plugin-releases/" + Id(id), null, cancellationToken).ConfigureAwait(false))?["data"]);

    /// <summary>Die letzten 20 Läufe.</summary>
    public async Task<IReadOnlyList<PluginDeploy>> DeploysAsync(CancellationToken cancellationToken = default)
    {
        var root = await SendAsync(HttpMethod.Get, "minecraft/plugin-deploys", null, cancellationToken).ConfigureAwait(false);
        return root?["data"] is JsonArray list ? list.Select(ParseDeploy).OfType<PluginDeploy>().ToList() : [];
    }

    public async Task<PluginDeploy?> DeployAsync(string id, CancellationToken cancellationToken = default) =>
        ParseDeploy((await SendAsync(HttpMethod.Get, "minecraft/plugin-deploys/" + Id(id), null, cancellationToken).ConfigureAwait(false))?["data"]);

    /// <summary>
    /// Stufe 2: geprüften Stand verteilen (<c>confirm: true</c>). Ohne <paramref name="servers"/> alle Server mit festgelegter
    /// Rolle ohne Abweichung – den Proxy nur bei ausdrücklicher Auswahl (entscheidet der Server).
    /// </summary>
    public async Task<PluginDeploy?> StartDeployAsync(string releaseId, IReadOnlyList<string>? servers, bool restart, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["release"] = Id(releaseId), ["confirm"] = true, ["restart"] = restart };
        if (servers is { Count: > 0 })
        {
            body["servers"] = new JsonArray(servers.Distinct(StringComparer.Ordinal).Select(s => (JsonNode)Id(s)).ToArray());
        }

        return ParseDeploy((await SendAsync(HttpMethod.Post, "minecraft/plugin-deploys", body, cancellationToken).ConfigureAwait(false))?["data"]);
    }

    /// <summary>Gruppen- bzw. Ordnername <c>[a-z0-9][a-z0-9-]{0,47}</c>.</summary>
    public static bool IsValidName(string? value) => value is not null && NamePattern().IsMatch(value);

    public static NetworkServer? ParseNetworkServer(JsonNode? n)
    {
        if (Str(n, "id") is not { } id)
        {
            return null;
        }

        var d = n!["deploy"];
        var m = n["minecraft"] as JsonObject;
        return new NetworkServer(
            id,
            Str(n, "slug") ?? id,
            Str(n, "name") ?? Str(n, "slug") ?? id,
            Bool(n, "linked"),
            new NetworkDeploy(Str(d, "role"), Str(d, "folder"), Bool(d, "configured"), Bool(d, "mismatch")),
            m is null
                ? null
                : new NetworkState(
                    Str(m, "network_id"),
                    Str(m, "role"),
                    DoubleOf(m, "tps"),
                    DoubleOf(m, "mspt"),
                    Math.Max(0, IntOf(m, "players_online")),
                    NullableInt(m, "players_max"),
                    Str(m["minigame"], "game"),
                    Str(m["minigame"], "state"),
                    Str(m["minigame"], "arena"),
                    Str(m, "plugin_version"),
                    Time(m, "empty_since"),
                    Time(m, "reported_at"),
                    Bool(m, "fresh"),
                    Str(m, "deploy_folder")));
    }

    public static PoolOverview ParsePool(JsonNode? root) => new(
        root?["data"] is JsonArray list ? list.Select(ParsePoolGroup).OfType<PoolGroup>().ToList() : [],
        root?["meta"]?["actions"] is JsonArray actions ? actions.Select(ParsePoolAction).OfType<PoolAction>().ToList() : [],
        Math.Max(0, IntOf(root?["meta"], "cooldown_minutes")));

    public static PoolGroup? ParsePoolGroup(JsonNode? n) =>
        Str(n, "game") is { } game
            ? new PoolGroup(
                game,
                Bool(n, "configured"),
                Bool(n, "enabled"),
                NullableInt(n, "min_running") ?? 1,
                NullableInt(n, "start_threshold") ?? 60,
                NullableInt(n, "idle_minutes") ?? 10,
                Math.Max(0, IntOf(n, "running")),
                Math.Max(0, IntOf(n, "joinable")),
                Math.Max(0, IntOf(n, "players")),
                n!["instances"] is JsonArray instances ? instances.Select(ParseInstance).OfType<PoolInstance>().ToList() : [],
                n["suggestions"] is JsonArray suggestions
                    ? suggestions.Select(s => Str(s, "id") is { } id ? new[] { (id, Str(s, "name") ?? Str(s, "slug") ?? id) } : []).SelectMany(x => x).ToList()
                    : [],
                ParsePoolAction(n["last_action"]))
            : null;

    public static PoolInstance? ParseInstance(JsonNode? n) =>
        Str(n, "id") is { } id
            ? new PoolInstance(
                id,
                Str(n, "name") ?? Str(n, "slug") ?? id,
                Bool(n, "linked"),
                Bool(n, "excluded"),
                Math.Max(0, IntOf(n, "failures")),
                Bool(n, "quarantined"),
                Bool(n, "backoff"),
                Str(n, "status") ?? "offline",
                Str(n, "minigame_state"),
                Str(n, "arena"),
                Math.Max(0, IntOf(n, "players_online")),
                NullableInt(n, "capacity"),
                DoubleOf(n, "load"),
                Bool(n, "joinable"),
                Time(n, "empty_since"))
            : null;

    public static PoolAction? ParsePoolAction(JsonNode? n) =>
        n is JsonObject o && Str(o, "action") is { } action
            ? new PoolAction(
                o["id"] is JsonValue v && v.TryGetValue<long>(out var id) ? id : 0,
                Str(o, "game") ?? string.Empty,
                Str(o["server"], "name"),
                action,
                Str(o, "reason"),
                Bool(o, "ok"),
                Str(o, "error"),
                Time(o, "created_at"))
            : null;

    public static PluginRelease? ParseRelease(JsonNode? n) =>
        Str(n, "id") is { } id
            ? new PluginRelease(
                id,
                Str(n, "status") ?? ReleaseChecking,
                Str(n, "error"),
                Str(n, "tag"),
                Time(n, "published_at"),
                Str(n, "sha256"),
                Math.Max(0, LongOf(n, "size")),
                Bool(n, "signed"),
                Bool(n, "signature_required"),
                Str(n!["signature"], "key_id"),
                n["folders"] is JsonObject folders
                    ? folders.Select(f => (f.Key, (IReadOnlyList<ReleaseFile>)(f.Value is JsonArray files
                        ? files.Select(x => Str(x, "name") is { } name ? new ReleaseFile(name, Math.Max(0, LongOf(x, "size"))) : null).OfType<ReleaseFile>().ToList()
                        : []))).OrderBy(f => f.Key, StringComparer.Ordinal).ToList()
                    : [],
                n["ignored"] is JsonArray ignored ? ignored.Select(i => i is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null).OfType<string>().ToList() : [],
                Time(n, "expires_at"))
            : null;

    public static PluginDeploy? ParseDeploy(JsonNode? n) =>
        Str(n, "id") is { } id
            ? new PluginDeploy(
                id,
                Str(n, "status") ?? DeployQueued,
                Bool(n, "restart"),
                Str(n!["release"], "tag"),
                Bool(n["release"], "signed"),
                Str(n, "error"),
                Str(n["started_by"], "name"),
                Time(n, "created_at"),
                Time(n, "finished_at"),
                n["targets"] is JsonArray targets
                    ? targets.Select(t => t is JsonObject o
                        ? new DeployTarget(
                            Str(o["server"], "id"),
                            Str(o["server"], "name") ?? "?",
                            Str(o, "folder"),
                            Str(o, "status") ?? "pending",
                            Str(o, "message"),
                            o["uploaded"] is JsonArray up ? up.Select(u => u is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : Str(u, "name")).OfType<string>().ToList() : [],
                            Bool(o, "restarted"))
                        : null).OfType<DeployTarget>().ToList()
                    : [])
            : null;

    private static string Game(string game) =>
        IsValidName(game) ? game : throw new ArgumentException("Invalid game.", nameof(game));

    private static bool Bool(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.GetValueKind() == JsonValueKind.True;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,47}$")]
    private static partial Regex NamePattern();
}
