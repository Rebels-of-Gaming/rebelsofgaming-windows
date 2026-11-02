using System.Text.Json.Nodes;

namespace RoG.Core.Features.Minecraft;

/// <summary>Secret im Fortschritt eines Mitglieds: ungefunden zeigt die App „???“ mit Hinweis.</summary>
public sealed record SecretProgress(string Id, string Name, string? Hint, bool Found, DateTimeOffset? FoundAt);

/// <summary>Fortschritt je Gruppe („Hauptlobby“, „Minigame-Lobby“): <see cref="Found"/> von <see cref="Total"/>.</summary>
public sealed record SecretGroupProgress(string Id, string Label, int Found, int Total, IReadOnlyList<SecretProgress> Secrets);

/// <summary>Secret der Übersicht: Anzahl der Finder (nie wer), eigener Fund nur mit Token.</summary>
public sealed record SecretEntry(string Id, string Name, string? Hint, int Order, int Finders, DateTimeOffset? FoundAt);

/// <summary>Gruppe der Übersicht; <see cref="Found"/> ist ohne Token <c>null</c>.</summary>
public sealed record SecretGroup(string Id, string Label, int Total, int? Found, IReadOnlyList<SecretEntry> Secrets);

/// <summary>
/// Lobby-Secrets (Website v0.55.0): versteckte Secrets in der Lobby, der erste Fund ist ein Erfolg im Profil. Übersicht
/// <c>GET /v1/minecraft/secrets</c> (auch Gastmodus), Fortschritt eines Mitglieds wie die Minecraft-Statistiken nur mit
/// Opt-in (<c>minecraft_stats</c>). Fundorte liefert die API nie.
/// </summary>
public sealed partial class MinecraftService
{
    public async Task<IReadOnlyList<SecretGroup>> SecretsAsync(CancellationToken cancellationToken = default)
    {
        var root = await SendAsync(HttpMethod.Get, "minecraft/secrets", null, cancellationToken).ConfigureAwait(false);
        return ParseSecretGroups(root?["data"]);
    }

    public async Task<IReadOnlyList<SecretGroupProgress>> MemberSecretsAsync(string member, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        var root = await SendAsync(HttpMethod.Get, $"members/{Uri.EscapeDataString(member.Trim())}/minecraft-secrets", null, cancellationToken).ConfigureAwait(false);
        return ParseSecretProgress(root?["data"]);
    }

    public static IReadOnlyList<SecretGroup> ParseSecretGroups(JsonNode? data) =>
        data is JsonArray groups
            ? groups.Select(g => Str(g, "id") is { } id
                ? new SecretGroup(
                    id,
                    Str(g, "label") ?? id,
                    Math.Max(0, IntOf(g, "total")),
                    g!["found"] is JsonValue f && f.TryGetValue<int>(out var found) ? Math.Max(0, found) : null,
                    g["secrets"] is JsonArray secrets
                        ? secrets.Select(s => Str(s, "id") is { } sid
                            ? new SecretEntry(sid, Str(s, "name") ?? sid, Str(s, "hint"), IntOf(s, "order"), Math.Max(0, IntOf(s, "finders")), Time(s, "found_at"))
                            : null).OfType<SecretEntry>().OrderBy(s => s.Order).ToList()
                        : [])
                : null).OfType<SecretGroup>().ToList()
            : [];

    public static IReadOnlyList<SecretGroupProgress> ParseSecretProgress(JsonNode? data) =>
        data is JsonArray groups
            ? groups.Select(g => Str(g, "id") is { } id
                ? new SecretGroupProgress(
                    id,
                    Str(g, "label") ?? id,
                    Math.Max(0, IntOf(g, "found")),
                    Math.Max(0, IntOf(g, "total")),
                    g!["secrets"] is JsonArray secrets
                        ? secrets.Select(s => Str(s, "id") is { } sid
                            ? new SecretProgress(sid, Str(s, "name") ?? sid, Str(s, "hint"), s!["found"] is JsonValue v && v.TryGetValue<bool>(out var b) && b, Time(s, "found_at"))
                            : null).OfType<SecretProgress>().ToList()
                        : [])
                : null).OfType<SecretGroupProgress>().ToList()
            : [];
}
