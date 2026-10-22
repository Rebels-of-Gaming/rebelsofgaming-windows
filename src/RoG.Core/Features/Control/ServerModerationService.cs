using System.Net.Http.Json;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Control;

public sealed record ServerBan(string Id, string Player, string? Reason, string Status, string? IssuedBy, DateTimeOffset? CreatedAt, DateTimeOffset? EndsAt)
{
    public bool IsActive => Status == "active";
}

/// <summary>Config-Antrag (Vier-Augen-Prinzip) mit Diff gegen den aktuellen Stand.</summary>
public sealed record ConfigChange(string Id, string Path, string? Author, string? Note, string After, DateTimeOffset? CreatedAt, string Diff, bool Outdated);

/// <summary>
/// RCON, Sperren und Config-Freigaben (Guide 18). <c>POST …/bans</c> hat in der Spec keinen Body (Lücke W10) und
/// <c>config-changes?include=diff</c> liefert den Diff als Liste – beides liest/sendet die App selbst.
/// </summary>
public sealed class ServerModerationService(IRoGApiClient client, HttpClient http)
{
    public async Task<string> RconAsync(string server, string command, CancellationToken cancellationToken = default)
    {
        var line = command.Trim();
        if (line.Length == 0 || line.Length > 500 || line.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Eine Zeile, höchstens 500 Zeichen");
        }

        return (await client.V1_servers_rconAsync(server, new Body66 { Command = line }, cancellationToken).ConfigureAwait(false)).Data.Output;
    }

    public async Task<IReadOnlyList<ServerBan>> BansAsync(string server, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_servers_bans_indexAsync(server, "active", cancellationToken).ConfigureAwait(false);
        return response.Data.Select(b => new ServerBan(
            b.Id,
            b.User?.Name ?? ApiValues.Text(b.Player_name) ?? ApiValues.Text(b.Player_id) ?? "—",
            ApiValues.Text(b.Reason),
            b.Status,
            b.Issued_by?.Name,
            ApiValues.Time(b.Created_at),
            ApiValues.Time(b.Ends_at))).ToList();
    }

    /// <summary>Gast sperren über Spieler-ID oder Ingame-Name; <paramref name="minutes"/> 0 = dauerhaft.</summary>
    public async Task BanAsync(string server, string? playerId, string? playerName, int minutes, string? reason, CancellationToken cancellationToken = default)
    {
        var id = ApiValues.Text(playerId);
        var name = ApiValues.Text(playerName);
        if (id is null && name is null)
        {
            throw new ArgumentException("Spieler-ID oder Name nötig", nameof(playerId));
        }

        var body = new Dictionary<string, object?> { ["minutes"] = Math.Max(0, minutes) };
        if (id is not null)
        {
            body["player_id"] = id;
        }

        if (name is not null)
        {
            body["player_name"] = name;
        }

        if (ApiValues.Text(reason) is { } r)
        {
            body["reason"] = r;
        }

        using var response = await http.PostAsJsonAsync(new Uri($"servers/{Uri.EscapeDataString(server)}/bans", UriKind.Relative), body, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task LiftAsync(string server, string banId, CancellationToken cancellationToken = default) =>
        await client.V1_servers_bans_destroyAsync(server, banId, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<ConfigChange>> PendingChangesAsync(string server, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(
            new Uri($"servers/{Uri.EscapeDataString(server)}/config-changes?filter%5Bstatus%5D=pending&include=diff", UriKind.Relative), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseChanges(doc.RootElement);
    }

    public async Task ApproveAsync(string server, string changeId, string? note, CancellationToken cancellationToken = default) =>
        await client.V1_servers_configChanges_approveAsync(server, changeId, new Body64 { Note = ApiValues.Text(note) }, cancellationToken).ConfigureAwait(false);

    public async Task RejectAsync(string server, string changeId, string? note, CancellationToken cancellationToken = default) =>
        await client.V1_servers_configChanges_rejectAsync(server, changeId, new Body65 { Note = ApiValues.Text(note) }, cancellationToken).ConfigureAwait(false);

    internal static IReadOnlyList<ConfigChange> ParseChanges(JsonElement root)
    {
        static string? S(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        static JsonElement O(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) ? p : default;

        static string Diff(JsonElement d) => d.ValueKind switch
        {
            JsonValueKind.String => d.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join("\n", d.EnumerateArray().Select(l => new DiffLine(S(l, "type") ?? "context", S(l, "text") ?? string.Empty, null, null).Display)),
            _ => string.Empty,
        };

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return data.EnumerateArray().Select(c => new ConfigChange(
            S(c, "id") ?? string.Empty,
            S(O(c, "file"), "path") ?? string.Empty,
            S(O(c, "author"), "name"),
            ApiValues.Text(S(c, "note")),
            S(c, "after") ?? "save",
            ApiValues.Time(S(c, "created_at")),
            Diff(O(c, "diff")),
            O(c, "outdated").ValueKind == JsonValueKind.True)).Where(c => c.Id.Length > 0).ToList();
    }
}
