using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Events;

public sealed record EventTypeOption(string Key, string Label);

/// <summary>Neuer Termin (Guide 13): Ortszeit + Zeitzone, optional wöchentliche Serie.</summary>
public sealed record EventDraft(
    string Type,
    string Title,
    DateTime StartsLocal,
    int DurationMinutes,
    string? SquadId = null,
    string? Description = null,
    string? LocationType = null,
    string? Location = null,
    bool Mandatory = false,
    IReadOnlyList<int>? WeeklyDays = null,
    int? Count = null,
    string Timezone = "Europe/Luxembourg");

/// <summary>
/// Termine verwalten (Recht <c>events.manage</c>, Scope <c>events:write</c>): Typen, anlegen (auch Serien), absagen,
/// löschen. Anlegen geht als JSON wie im Guide (die Spec beschreibt <c>title</c>/<c>description</c> abweichend).
/// </summary>
public sealed class EventAdminService(IRoGApiClient client, HttpClient http)
{
    public static readonly IReadOnlyList<string> LocationTypes = ["teamspeak", "discord", "server", "online", "address"];

    public async Task<IReadOnlyList<EventTypeOption>> TypesAsync(CancellationToken cancellationToken = default) =>
        (await client.V1_events_typesAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Data
            .Select(t => new EventTypeOption(t.Key, t.Label)).ToList();

    /// <summary>Anlegen; Rückgabe = ID des (ersten) Termins.</summary>
    public async Task<string> CreateAsync(EventDraft draft, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(new Uri("events", UriKind.Relative), Body(draft), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return doc.RootElement.TryGetProperty("data", out var d) && d.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty;
    }

    /// <summary>Absagen (benachrichtigt Zusagende); <paramref name="series"/> = alle folgenden einer Serie.</summary>
    public async Task CancelAsync(string id, bool series, CancellationToken cancellationToken = default) =>
        await client.V1_events_cancelAsync(id, new Body85 { Series = series }, cancellationToken).ConfigureAwait(false);

    public async Task DeleteAsync(string id, bool series, CancellationToken cancellationToken = default) =>
        await client.V1_events_destroyAsync(id, series, cancellationToken).ConfigureAwait(false);

    internal static Dictionary<string, object?> Body(EventDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Title);
        var body = new Dictionary<string, object?>
        {
            ["type"] = draft.Type,
            ["title"] = new Dictionary<string, string> { ["de"] = draft.Title.Trim() },
            ["starts_at"] = draft.StartsLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            ["timezone"] = draft.Timezone,
            ["duration_minutes"] = Math.Clamp(draft.DurationMinutes, 15, 24 * 60),
            ["mandatory"] = draft.Mandatory,
        };
        if (draft.SquadId is { Length: > 0 } squad)
        {
            body["squad_id"] = squad;
        }

        if (ApiValues.Text(draft.Description) is { } description)
        {
            body["description"] = new Dictionary<string, string> { ["de"] = description };
        }

        if (draft.LocationType is { Length: > 0 } type)
        {
            body["location_type"] = type;
            body["location"] = ApiValues.Text(draft.Location);
        }

        if (draft.WeeklyDays is { Count: > 0 } days)
        {
            var recurrence = new Dictionary<string, object?>
            {
                ["frequency"] = "weekly",
                ["interval"] = 1,
                ["weekdays"] = days.Where(d => d is >= 1 and <= 7).Distinct().Order().ToList(),
                ["ends"] = draft.Count is > 0 ? "count" : "never",
            };
            if (draft.Count is > 0 and var count)
            {
                recurrence["count"] = Math.Min(count, 366);
            }

            body["recurrence"] = recurrence;
        }

        return body;
    }
}
