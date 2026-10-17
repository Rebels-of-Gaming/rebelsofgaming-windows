using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RoG.Core.Api;

namespace RoG.Core.Features.Events;

/// <summary>Person mit Antwort auf einen Termin (<see cref="Absent"/> = im Zeitraum abwesend gemeldet, Grund nie).</summary>
public sealed record EventParticipant(string Id, string Slug, string Name, bool Absent, string? Comment);

/// <summary>Teilnehmer nach Antwort (Guide 13 „Teilnehmer“): ja, vielleicht, nein, offen (eingeladen, ohne Antwort).</summary>
public sealed record EventParticipants(
    IReadOnlyList<EventParticipant> Yes,
    IReadOnlyList<EventParticipant> Maybe,
    IReadOnlyList<EventParticipant> No,
    IReadOnlyList<EventParticipant> Open);

public sealed record LineupPlayer(string UserId, string Name, string Slot, string? Position)
{
    public bool IsSubstitute => Slot == "substitute";
}

/// <summary>Aufstellung eines Squad-Termins; <see cref="CanEdit"/> aus <c>meta.can_edit</c>.</summary>
public sealed record EventLineup(IReadOnlyList<LineupPlayer> Players, string? Note, DateTimeOffset? PublishedAt, bool CanEdit)
{
    public bool IsPublished => PublishedAt is not null;

    public IEnumerable<LineupPlayer> Starters => Players.Where(p => !p.IsSubstitute);

    public IEnumerable<LineupPlayer> Substitutes => Players.Where(p => p.IsSubstitute);
}

/// <summary>
/// Teilnehmer und Aufstellung (Guide 13): lesen, Vorschlag aus den Antworten, speichern, veröffentlichen/zurückziehen.
/// Recht <c>squad.lineup</c> im Squad oder <c>events.manage</c>, Scope <c>events:write</c> – der Server meldet es in
/// <c>meta.can_edit</c>.
/// </summary>
public sealed class EventLineupService(HttpClient http)
{
    public async Task<EventParticipants> ParticipantsAsync(string eventId, CancellationToken cancellationToken = default)
    {
        using var doc = await GetAsync($"events/{Uri.EscapeDataString(eventId)}/participants", cancellationToken).ConfigureAwait(false);
        return ParseParticipants(doc!.RootElement);
    }

    internal static EventParticipants ParseParticipants(JsonElement root)
    {
        var d = root.TryGetProperty("data", out var data) ? data : root;
        IReadOnlyList<EventParticipant> List(string name) => d.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(p => new EventParticipant(S(p, "id") ?? string.Empty, S(p, "slug") ?? string.Empty, S(p, "name") ?? string.Empty,
                p.TryGetProperty("absent", out var ab) && ab.ValueKind == JsonValueKind.True, ApiValues.Text(S(p, "comment")))).Where(p => p.Id.Length > 0).ToList()
            : [];
        return new EventParticipants(List("yes"), List("maybe"), List("no"), List("open"));
    }

    /// <summary>Aufstellung oder <c>null</c> (noch nicht veröffentlicht und keine Leitung → <c>404</c>, oder kein Squad-Termin).</summary>
    public async Task<EventLineup?> GetLineupAsync(string eventId, CancellationToken cancellationToken = default)
    {
        using var doc = await GetAsync($"events/{Uri.EscapeDataString(eventId)}/lineup", cancellationToken, allowMissing: true).ConfigureAwait(false);
        return doc is null ? null : ParseLineup(doc.RootElement);
    }

    internal static EventLineup ParseLineup(JsonElement root)
    {
        var d = root.TryGetProperty("data", out var data) ? data : root;
        var players = d.TryGetProperty("players", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(p =>
            {
                var user = p.TryGetProperty("user", out var u) ? u : default;
                return new LineupPlayer(S(user, "id") ?? string.Empty, S(user, "name") ?? string.Empty, S(p, "slot") ?? "starter", ApiValues.Text(S(p, "position")));
            }).Where(p => p.UserId.Length > 0).ToList()
            : [];
        var canEdit = root.TryGetProperty("meta", out var meta) && meta.TryGetProperty("can_edit", out var ce) && ce.ValueKind == JsonValueKind.True;
        return new EventLineup(players, ApiValues.Text(S(d, "note")), ApiValues.Time(S(d, "published_at")), canEdit);
    }

    /// <summary>Vorschlag: Zusagen = Stammspieler, Vielleicht = Ersatz – mit Namen aus den Teilnehmern.</summary>
    public async Task<IReadOnlyList<LineupPlayer>> SuggestionAsync(string eventId, EventParticipants participants, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(participants);
        using var doc = await GetAsync($"events/{Uri.EscapeDataString(eventId)}/lineup/suggestion", cancellationToken).ConfigureAwait(false);
        var names = participants.Yes.Concat(participants.Maybe).Concat(participants.Open).Concat(participants.No)
            .GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        var d = doc!.RootElement.TryGetProperty("data", out var data) ? data : default;
        return d.ValueKind == JsonValueKind.Object && d.TryGetProperty("players", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(p => S(p, "user_id") is { } id
                    ? new LineupPlayer(id, names.GetValueOrDefault(id, id), S(p, "slot") ?? "starter", ApiValues.Text(S(p, "position")))
                    : null)
                .OfType<LineupPlayer>().ToList()
            : [];
    }

    public async Task SaveAsync(string eventId, IEnumerable<LineupPlayer> players, string? note, CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync(Lineup(eventId), Body(players, note), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Speichert und veröffentlicht in einem Schritt (Squad wird benachrichtigt; leer → <c>422 lineup_empty</c>).</summary>
    public async Task PublishAsync(string eventId, IEnumerable<LineupPlayer> players, string? note, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(new Uri($"events/{Uri.EscapeDataString(eventId)}/lineup/publish", UriKind.Relative), Body(players, note), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task UnpublishAsync(string eventId, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync(new Uri($"events/{Uri.EscapeDataString(eventId)}/lineup/publish", UriKind.Relative), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public static Dictionary<string, object?> Body(IEnumerable<LineupPlayer> players, string? note) => new()
    {
        ["players"] = players.DistinctBy(p => p.UserId)
            .Select(p => new Dictionary<string, object?> { ["user_id"] = p.UserId, ["slot"] = p.IsSubstitute ? "substitute" : "starter", ["position"] = ApiValues.Text(p.Position) })
            .ToList(),
        ["note"] = ApiValues.Text(note),
    };

    private static Uri Lineup(string eventId) => new($"events/{Uri.EscapeDataString(eventId)}/lineup", UriKind.Relative);

    private async Task<JsonDocument?> GetAsync(string path, CancellationToken cancellationToken, bool allowMissing = false)
    {
        try
        {
            using var response = await http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (ApiProblemException ex) when (allowMissing && (ex.Problem.Status is (int)HttpStatusCode.NotFound or (int)HttpStatusCode.UnprocessableEntity))
        {
            return null;
        }
    }

    private static string? S(JsonElement o, string n) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
