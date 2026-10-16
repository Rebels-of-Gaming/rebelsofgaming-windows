using System.Text.Json;
using RoG.Core.Api;

namespace RoG.Core.Features.Admin;

public sealed record Kpi(string Key, string Label, string Value, Uri? Url);

public sealed record SystemCheck(string Key, string Label, bool? Ok, string? Value, bool Critical, string? Help);

public sealed record SystemStatus(string Status, DateTimeOffset? CheckedAt, string? Version, IReadOnlyList<SystemCheck> Checks);

public sealed record AuditEntry(string Id, string Action, string? Actor, string? Subject, DateTimeOffset? CreatedAt);

/// <summary>
/// Verwaltung – Übersicht (Guide 31): Kennzahlen <c>/v1/admin/dashboard</c>, Systemstatus <c>/v1/system/status</c>
/// (in der Spec ohne Schema) und Audit-Log. Alles roh gelesen; jede Quelle darf fehlen (403, null).
/// </summary>
public sealed class AdminOverviewService(HttpClient http)
{
    public async Task<IReadOnlyList<Kpi>> KpisAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await GetAsync("admin/dashboard", cancellationToken).ConfigureAwait(false);
        return ParseKpis(doc.RootElement);
    }

    public async Task<SystemStatus?> SystemAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await GetAsync("system/status", cancellationToken).ConfigureAwait(false);
        return ParseSystem(doc.RootElement);
    }

    public async Task<Page<AuditEntry>> AuditAsync(string? search, string? cursor, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { "per_page=50" };
        if (ApiValues.Text(search) is { } s)
        {
            query.Add("filter%5Bsearch%5D=" + Uri.EscapeDataString(s));
        }

        if (cursor is not null)
        {
            query.Add("cursor=" + Uri.EscapeDataString(cursor));
        }

        using var doc = await GetAsync("audit-log?" + string.Join('&', query), cancellationToken).ConfigureAwait(false);
        return ParseAudit(doc.RootElement);
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string? S(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) ? p.ValueKind switch
    {
        JsonValueKind.String => p.GetString(),
        JsonValueKind.Number => p.GetRawText(),
        _ => null,
    } : null;

    private static JsonElement O(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) ? p : default;

    private static IEnumerable<JsonElement> A(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray() : [];

    internal static IReadOnlyList<Kpi> ParseKpis(JsonElement root) =>
        A(O(root, "data"), "kpis").Select(k => new Kpi(
            S(k, "key") ?? string.Empty,
            S(k, "label") ?? S(k, "key") ?? string.Empty,
            S(k, "value") ?? "–",
            Uri.TryCreate(S(k, "url"), UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? u : null)).ToList();

    internal static SystemStatus? ParseSystem(JsonElement root)
    {
        var d = O(root, "data");
        if (d.ValueKind != JsonValueKind.Object)
        {
            return null; // Überwachung lief noch nie
        }

        return new SystemStatus(
            S(d, "status") ?? "unknown",
            ApiValues.Time(S(d, "checked_at")),
            S(O(d, "app"), "version"),
            A(d, "checks").Select(c => new SystemCheck(
                S(c, "key") ?? string.Empty,
                S(c, "label") ?? string.Empty,
                O(c, "ok").ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null },
                S(c, "value"),
                O(c, "critical").ValueKind == JsonValueKind.True,
                S(c, "help"))).ToList());
    }

    internal static Page<AuditEntry> ParseAudit(JsonElement root) => new(
        A(root, "data").Select(e => new AuditEntry(
            S(e, "id") ?? string.Empty,
            S(e, "action") ?? string.Empty,
            S(O(e, "actor"), "name"),
            S(O(e, "subject"), "label") ?? S(O(e, "subject"), "type"),
            ApiValues.Time(S(e, "created_at")))).ToList(),
        S(O(root, "meta"), "next_cursor"));
}
