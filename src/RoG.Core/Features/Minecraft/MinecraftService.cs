using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RoG.Core.Api;

namespace RoG.Core.Features.Minecraft;

/// <summary>
/// Verknüpfter Minecraft-Account (Java oder Bedrock). <see cref="UserName"/>/<see cref="UserSlug"/> und
/// <see cref="ServerName"/> nur in der Verwaltungsliste (<c>user</c>, <c>linked_via.server</c>).
/// </summary>
public sealed record MinecraftAccount(
    string Id,
    string Uuid,
    string? Xuid,
    string Name,
    string Edition,
    string? Platform,
    DateTimeOffset? LinkedAt,
    DateTimeOffset? LastSeenAt,
    string? UserName = null,
    string? UserSlug = null,
    string? ServerName = null);

/// <summary>Vorschau eines Link-Codes („Code für Notch (Java), angefordert vor 2 Minuten – bist das du?“).</summary>
public sealed record MinecraftLinkPreview(
    string Name,
    string Edition,
    string? Platform,
    string? Server,
    DateTimeOffset? RequestedAt,
    int? MinutesAgo,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// Minecraft-Kontoverknüpfung (Website v0.36.0, Guide 44): eigene Accounts (<c>minecraft:read</c>), Code einlösen und
/// trennen (<c>minecraft:write</c>), Verwaltung aller Accounts (<c>minecraft:manage</c> + Recht <c>minecraft.manage</c>).
/// Einlösen ist immer zweistufig – erst Vorschau, dann Bestätigen; ein Code aus einem Link wird nie automatisch
/// eingelöst. Direkt über <c>/v1/</c> ohne ETag-Cache, damit der Stand nach einer Aktion sofort stimmt.
/// </summary>
public sealed partial class MinecraftService(HttpClient http)
{
    public const string ReadScope = "minecraft:read";
    public const string WriteScope = "minecraft:write";
    public const string ManageScope = "minecraft:manage";
    public const string ManagePermission = "minecraft.manage";

    public const string Java = "java";
    public const string Bedrock = "bedrock";

    /// <summary>Länge eines Link-Codes (A–Z und 2–9 ohne 0/O/1/I).</summary>
    public const int CodeLength = 6;

    public static IReadOnlyList<string> Editions { get; } = [Java, Bedrock];

    // --- Eigene Accounts -------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<MinecraftAccount>> MineAsync(CancellationToken cancellationToken = default)
    {
        var root = await SendAsync(HttpMethod.Get, "me/minecraft/accounts", null, cancellationToken).ConfigureAwait(false);
        return root?["data"] is JsonArray list ? list.Select(Parse).OfType<MinecraftAccount>().ToList() : [];
    }

    /// <summary>Schritt 1: zeigt, zu welchem Account der Code gehört – verknüpft nichts.</summary>
    public async Task<MinecraftLinkPreview> PreviewAsync(string code, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["code"] = RequireCode(code) };
        var data = (await SendAsync(HttpMethod.Post, "me/minecraft/link", body, cancellationToken).ConfigureAwait(false))?["data"];
        return ParsePreview(data) ?? throw new InvalidDataException("Link preview without data.");
    }

    /// <summary>Schritt 2: verknüpft den Account (<c>confirm: true</c>), Antwort ist der neue Account.</summary>
    public async Task<MinecraftAccount> ConfirmAsync(string code, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["code"] = RequireCode(code), ["confirm"] = true };
        var data = (await SendAsync(HttpMethod.Post, "me/minecraft/link", body, cancellationToken).ConfigureAwait(false))?["data"];
        return Parse(data) ?? throw new InvalidDataException("Link response without data.");
    }

    public async Task UnlinkAsync(string id, CancellationToken cancellationToken = default) =>
        await SendAsync(HttpMethod.Delete, "me/minecraft/accounts/" + Id(id), null, cancellationToken).ConfigureAwait(false);

    // --- Einstellungen (Website v0.38.0) ---------------------------------------------------------------------------

    /// <summary>Direktnachrichten von Freunden aus Website/App auch im Spiel anzeigen (Standard aus).</summary>
    public async Task<bool> ShowWebsiteMessagesAsync(CancellationToken cancellationToken = default)
    {
        var data = (await SendAsync(HttpMethod.Get, "me/minecraft/settings", null, cancellationToken).ConfigureAwait(false))?["data"];
        return ParseShowWebsiteMessages(data);
    }

    public async Task<bool> SetShowWebsiteMessagesAsync(bool value, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["show_website_messages"] = value };
        var data = (await SendAsync(new HttpMethod("PATCH"), "me/minecraft/settings", body, cancellationToken).ConfigureAwait(false))?["data"];
        return ParseShowWebsiteMessages(data);
    }

    public static bool ParseShowWebsiteMessages(JsonNode? data) =>
        data?["show_website_messages"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    // --- Verwaltung ------------------------------------------------------------------------------------------------

    /// <summary>Alle verknüpften Accounts, neueste zuerst; Suche nach Name oder UUID, optional Edition bzw. Server (ID).</summary>
    public async Task<Page<MinecraftAccount>> ListAsync(string? search = null, string? edition = null, string? serverId = null, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { "per_page=50" };
        if (Limit(search, 64) is { } s)
        {
            query.Add("filter%5Bsearch%5D=" + Uri.EscapeDataString(s));
        }

        if (edition is not null && Editions.Contains(edition))
        {
            query.Add("filter%5Bedition%5D=" + edition);
        }

        if (!string.IsNullOrEmpty(serverId))
        {
            query.Add("filter%5Bserver%5D=" + Id(serverId));
        }

        if (!string.IsNullOrEmpty(cursor))
        {
            query.Add("cursor=" + Uri.EscapeDataString(cursor));
        }

        var root = await SendAsync(HttpMethod.Get, "minecraft/accounts?" + string.Join('&', query), null, cancellationToken).ConfigureAwait(false);
        var items = root?["data"] is JsonArray list ? list.Select(Parse).OfType<MinecraftAccount>().ToList() : [];
        return new Page<MinecraftAccount>(items, Str(root?["meta"], "next_cursor"));
    }

    /// <summary>Trennt einen fremden Account (Konten auf gleicher oder höherer Position → <c>403 forbidden</c>).</summary>
    public async Task AdminUnlinkAsync(string id, CancellationToken cancellationToken = default) =>
        await SendAsync(HttpMethod.Delete, "minecraft/accounts/" + Id(id), null, cancellationToken).ConfigureAwait(false);

    // --- Codes -----------------------------------------------------------------------------------------------------

    /// <summary>Wie der Server: Groß-/Kleinschreibung, Leerzeichen und Bindestriche sind egal.</summary>
    public static string? NormalizeCode(string? code)
    {
        var c = code is null ? null : SeparatorPattern().Replace(code, string.Empty).ToUpperInvariant();
        return string.IsNullOrEmpty(c) ? null : c;
    }

    /// <summary>6 Zeichen aus A–Z und 2–9 ohne 0/O/1/I – sonst gar nicht erst senden (zählt sonst als Fehlversuch).</summary>
    public static bool IsValidCode(string? code) => NormalizeCode(code) is { } c && CodePattern().IsMatch(c);

    /// <summary>Code aus einem Link (<c>?code=…</c>) – nur übernehmen, wenn er gültig aussieht; eingelöst wird er nie automatisch.</summary>
    public static string? CodeFromQuery(string? query)
    {
        foreach (var pair in (query ?? string.Empty).TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0].Equals("code", StringComparison.OrdinalIgnoreCase))
            {
                var value = NormalizeCode(Uri.UnescapeDataString(parts[1].Replace('+', ' ')));
                return value is not null && CodePattern().IsMatch(value) ? value : null;
            }
        }

        return null;
    }

    private static string RequireCode(string code) =>
        NormalizeCode(code) is { } c && CodePattern().IsMatch(c) ? c : throw new ArgumentException("Invalid link code.", nameof(code));

    // --- Lesen -----------------------------------------------------------------------------------------------------

    public static MinecraftAccount? Parse(JsonNode? n)
    {
        if (n is not JsonObject || Str(n, "id") is not { } id || Str(n, "uuid") is not { } uuid)
        {
            return null;
        }

        return new MinecraftAccount(
            id,
            uuid,
            Str(n, "xuid"),
            Str(n, "name") ?? uuid,
            Str(n, "edition") ?? Java,
            Str(n, "platform"),
            Time(n, "linked_at"),
            Time(n, "last_seen_at"),
            Str(n["user"], "name"),
            Str(n["user"], "slug"),
            Str(n["linked_via"]?["server"], "name"));
    }

    public static MinecraftLinkPreview? ParsePreview(JsonNode? n)
    {
        if (n is not JsonObject || Str(n, "name") is not { } name)
        {
            return null;
        }

        return new MinecraftLinkPreview(
            name,
            Str(n, "edition") ?? Java,
            Str(n, "platform"),
            Str(n, "server"),
            Time(n, "requested_at"),
            n["minutes_ago"] is JsonValue v && v.TryGetValue<int>(out var minutes) ? Math.Max(0, minutes) : null,
            Time(n, "expires_at"));
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

    private static DateTimeOffset? Time(JsonNode? n, string name) => ApiValues.Time(Str(n, name));

    [GeneratedRegex("^[A-HJ-NP-Z2-9]{6}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("[\\s\\-]+")]
    private static partial Regex SeparatorPattern();

    [GeneratedRegex("^[0-9A-HJKMNP-TV-Za-hjkmnp-tv-z]{26}$")]
    private static partial Regex UlidPattern();
}
