using System.Net.Http.Json;
using System.Text.Json.Nodes;
using RoG.Core.Api;

namespace RoG.Core.Features.Apps;

/// <summary>Zahlen einer Plattform: Store-Downloads (1–2 Tage verzögert) und angemeldete App-Geräte mit häufigsten Versionen.</summary>
public sealed record AppPlatformStats(
    string Platform,
    string? Store,
    bool StoreConfigured,
    long Downloads,
    long Downloads30d,
    long Redownloads,
    long Updates30d,
    string? LastReport,
    int Devices,
    int ActiveDevices,
    int Accounts,
    IReadOnlyList<(string Version, int Devices)> Versions);

public sealed record AppStats(IReadOnlyList<AppPlatformStats> Platforms, long Downloads, long Downloads30d, int Devices, int Active, IReadOnlyList<(DateOnly Date, long Downloads)> Days, DateTimeOffset? LastImport);

/// <summary>
/// Verwaltung → App-Statistik (Website v0.53.0, <c>GET /v1/apps/stats</c>): nur Summen, keine personenbezogenen Daten.
/// Recht <c>apps.stats</c> + Scope <c>system:read</c>.
/// </summary>
public sealed class AppStatsService(HttpClient api)
{
    public const string Permission = "apps.stats";
    public const string Scope = "system:read";

    public async Task<AppStats> GetAsync(CancellationToken cancellationToken = default) =>
        Parse((await api.GetFromJsonAsync<JsonNode>(new Uri("apps/stats", UriKind.Relative), cancellationToken).ConfigureAwait(false))?["data"]);

    public static AppStats Parse(JsonNode? d)
    {
        var totals = d?["totals"];
        return new AppStats(
            d?["platforms"] is JsonArray list ? list.Select(ParsePlatform).OfType<AppPlatformStats>().ToList() : [],
            Long(totals, "downloads"),
            Long(totals, "downloads_30d"),
            (int)Long(totals, "devices"),
            (int)Long(totals, "active"),
            d?["days"] is JsonArray days
                ? days.Select(x => DateOnly.TryParse(Str(x, "date"), System.Globalization.CultureInfo.InvariantCulture, out var date) ? (date, Long(x, "downloads")) : ((DateOnly, long)?)null)
                    .OfType<(DateOnly, long)>().ToList()
                : [],
            ApiValues.Time(Str(d, "last_import")));
    }

    private static AppPlatformStats? ParsePlatform(JsonNode? n) =>
        Str(n, "platform") is { } platform
            ? new AppPlatformStats(
                platform,
                Str(n, "store"),
                n!["store_configured"] is JsonValue v && v.TryGetValue<bool>(out var c) && c,
                Long(n, "downloads"),
                Long(n, "downloads_30d"),
                Long(n, "redownloads"),
                Long(n, "updates_30d"),
                Str(n, "last_report"),
                (int)Long(n, "devices"),
                (int)Long(n, "active_devices"),
                (int)Long(n, "accounts"),
                n["versions"] is JsonArray versions
                    ? versions.Select(x => Str(x, "version") is { } ver ? (ver, (int)Long(x, "devices")) : ((string, int)?)null).OfType<(string, int)>().ToList()
                    : [])
            : null;

    private static string? Str(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    private static long Long(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.TryGetValue<long>(out var l) ? l : 0;
}
