using System.Globalization;
using System.Text.Json;

namespace RoG.Core.Features.Home;

/// <summary>
/// Eine Kachel des Live-Tickers aus <c>GET /v1/live</c> (online, im Voice, auf Servern, Server-Belegung, nächstes Event …).
/// <see cref="Label"/>, <see cref="Value"/> und <see cref="Hint"/> kommen fertig übersetzt/formatiert von der API;
/// <see cref="Tone"/> wählt die Status-Farbe.
/// </summary>
public sealed record LiveStat(string Key, string Label, string Icon, string Tone, string Value, string? Hint);

/// <summary>
/// Live-Ticker. <c>value</c> ist laut Guide 08 eine Zahl <b>oder</b> fertig formatierter Text (z. B. Datum des nächsten
/// Events); die Spec nennt nur <c>int</c> (Lücke W8) – deshalb wird die Antwort hier selbst gelesen statt über den
/// generierten Client, der an Text-Werten scheitern würde.
/// </summary>
public sealed class LiveTickerService(HttpClient http)
{
    public async Task<IReadOnlyList<LiveStat>> GetAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(new Uri("live", UriKind.Relative), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Parse(doc.RootElement);
    }

    internal static IReadOnlyList<LiveStat> Parse(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<LiveStat>();
        foreach (var item in data.EnumerateArray())
        {
            var key = Text(item, "key");
            var label = Text(item, "label");
            if (key is null || label is null)
            {
                continue; // unvollständige Einträge auslassen (Guide: unbekannte Einträge ignorieren)
            }

            var value = item.TryGetProperty("value", out var v) ? v.ValueKind switch
            {
                JsonValueKind.Number => v.TryGetInt64(out var n) ? n.ToString("N0", CultureInfo.CurrentCulture) : v.GetDouble().ToString("N1", CultureInfo.CurrentCulture),
                JsonValueKind.String => v.GetString() ?? string.Empty,
                _ => string.Empty,
            } : string.Empty;

            result.Add(new LiveStat(key, label, Text(item, "icon") ?? string.Empty, Text(item, "tone") ?? "default", value, Text(item, "hint")));
        }

        return result;
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString()) ? p.GetString() : null;
}
