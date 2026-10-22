using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RoG.Core.Features.Minecraft;

/// <summary>Zuordnung RoG-Rolle → LuckPerms-Gruppe (höhere Priorität zuerst).</summary>
public sealed record MinecraftGroupMapping(string RoleId, string RoleLabel, string Group, int Priority, DateTimeOffset? UpdatedAt);

/// <summary>Aus dem Mapping entfernte Gruppe, die der Proxy weiter verwaltet, bis der Owner sie freigibt.</summary>
public sealed record MinecraftRetiredGroup(string Group, DateTimeOffset? RetiredAt, DateTimeOffset? ReleasableAt)
{
    /// <summary>Freigabe frühestens ab <see cref="ReleasableAt"/> – davor antwortet der Server <c>409 group_release_too_early</c>.</summary>
    public bool IsReleasable(DateTimeOffset now) => ReleasableAt is null || ReleasableAt <= now;
}

/// <summary>Antwort von <c>GET /v1/minecraft/group-mappings</c>.</summary>
public sealed record MinecraftGroupOverview(
    IReadOnlyList<MinecraftGroupMapping> Mappings,
    IReadOnlyList<string> ManagedGroups,
    IReadOnlyList<MinecraftRetiredGroup> RetiredGroups);

/// <summary>
/// Gruppen-Mapping (Website v0.37.0, Guide 44 „Gruppen (LuckPerms)“): Scope <c>minecraft:manage</c> und Recht
/// <c>minecraft.manage</c>. Nur Ränge und Extra-Rollen; Rangschutz und geschützte Gruppen prüft der Server
/// (<c>403 forbidden</c>, <c>422 role_not_mappable</c>), Freigeben entfernter Gruppen nur der Owner.
/// </summary>
public sealed partial class MinecraftService
{
    public const int MaxPriority = 1000;

    public async Task<MinecraftGroupOverview> GroupMappingsAsync(CancellationToken cancellationToken = default)
    {
        var root = await SendAsync(HttpMethod.Get, "minecraft/group-mappings", null, cancellationToken).ConfigureAwait(false);
        return ParseGroupOverview(root);
    }

    /// <summary>Setzt bzw. ändert die Gruppe der Rolle; Antwort ist das gespeicherte Mapping.</summary>
    public async Task<MinecraftGroupMapping?> SetGroupAsync(string roleId, string group, int priority, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["group"] = NormalizeGroup(group) is { } g && IsValidGroup(g) ? g : throw new ArgumentException("Invalid group name.", nameof(group)),
            ["priority"] = Math.Clamp(priority, 0, MaxPriority),
        };
        var data = (await SendAsync(HttpMethod.Put, "minecraft/group-mappings/" + Id(roleId), body, cancellationToken).ConfigureAwait(false))?["data"];
        return ParseMapping(data);
    }

    /// <summary>Entfernt das Mapping; die Gruppe bleibt als entfernte Gruppe verwaltet.</summary>
    public async Task RemoveGroupAsync(string roleId, CancellationToken cancellationToken = default) =>
        await SendAsync(HttpMethod.Delete, "minecraft/group-mappings/" + Id(roleId), null, cancellationToken).ConfigureAwait(false);

    /// <summary>„Nicht mehr verwalten“ – nur Owner, frühestens ab <c>releasable_at</c>.</summary>
    public async Task ReleaseGroupAsync(string group, CancellationToken cancellationToken = default)
    {
        var g = NormalizeGroup(group) is { } n && IsValidGroup(n) ? n : throw new ArgumentException("Invalid group name.", nameof(group));
        await SendAsync(HttpMethod.Delete, "minecraft/retired-groups/" + g, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Wie der Server erwartet: klein, ohne Leerzeichen außen.</summary>
    public static string? NormalizeGroup(string? group)
    {
        var g = group?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(g) ? null : g;
    }

    /// <summary>LuckPerms-Gruppenname <c>[a-z0-9_-]{1,36}</c>.</summary>
    public static bool IsValidGroup(string? group) => NormalizeGroup(group) is { } g && GroupPattern().IsMatch(g);

    public static MinecraftGroupOverview ParseGroupOverview(JsonNode? root)
    {
        var mappings = root?["data"] is JsonArray list
            ? list.Select(ParseMapping).OfType<MinecraftGroupMapping>().OrderByDescending(m => m.Priority).ToList()
            : [];
        var meta = root?["meta"];
        var managed = meta?["managed_groups"] is JsonArray groups
            ? groups.Select(g => g is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>().ToList()
            : [];
        var retired = meta?["retired_groups"] is JsonArray r
            ? r.Select(n => Str(n, "group") is { } g ? new MinecraftRetiredGroup(g, Time(n, "retired_at"), Time(n, "releasable_at")) : null)
                .OfType<MinecraftRetiredGroup>().ToList()
            : [];
        return new MinecraftGroupOverview(mappings, managed, retired);
    }

    public static MinecraftGroupMapping? ParseMapping(JsonNode? n)
    {
        if (n is not JsonObject || Str(n["role"], "id") is not { } roleId || Str(n, "group") is not { } group)
        {
            return null;
        }

        var priority = n["priority"] is JsonValue v && v.TryGetValue<int>(out var p) ? p : 0;
        return new MinecraftGroupMapping(roleId, Str(n["role"], "label") ?? Str(n["role"], "name") ?? roleId, group, priority, Time(n, "updated_at"));
    }

    [GeneratedRegex("^[a-z0-9_-]{1,36}$")]
    private static partial Regex GroupPattern();
}
