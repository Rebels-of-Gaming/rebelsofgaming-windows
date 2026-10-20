using System.Globalization;
using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Core.Features.Servers;

namespace RoG.Core.Features.Events;

/// <summary>Eigene Antwort auf einen Termin.</summary>
public enum Rsvp
{
    None,
    Yes,
    Maybe,
    No,
}

public sealed record EventItem(
    string Id,
    string Title,
    string TypeLabel,
    string? Color,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? SquadName,
    bool Mandatory,
    bool Cancelled,
    string? LocationLabel,
    GameServer? Server,
    int Yes,
    int Maybe,
    int? MaxParticipants,
    Rsvp MyRsvp,
    string? Description,
    Uri? WebUrl,
    Uri? IcsUrl);

/// <summary>Termine: kommende Termine, Detail, Zusage/Vielleicht/Absage.</summary>
public sealed class EventService(IRoGApiClient client)
{
    /// <summary>Termine ab <paramref name="from"/> (Standard: heute) für <paramref name="days"/> Tage, chronologisch.</summary>
    public async Task<Page<EventItem>> ListAsync(DateTimeOffset from, int days = 60, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_events_indexAsync(
            cursor: cursor,
            per_page: 50,
            filterfrom: from.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            filterto: from.AddDays(days).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var items = response.Data.Select(Map).OrderBy(e => e.StartsAt ?? DateTimeOffset.MaxValue).ToList();
        return new Page<EventItem>(items, response.Meta?.Next_cursor);
    }

    public async Task<EventItem> GetAsync(string id, CancellationToken cancellationToken = default) =>
        Map((await client.V1_events_showAsync(id, cancellationToken).ConfigureAwait(false)).Data);

    /// <summary>Zusage/Vielleicht/Absage; Rückgabe = neuer Stand laut API.</summary>
    public async Task<Rsvp> RespondAsync(string id, Rsvp answer, string? comment = null, CancellationToken cancellationToken = default)
    {
        var status = answer switch
        {
            Rsvp.Yes => RsvpStatus.Yes,
            Rsvp.Maybe => RsvpStatus.Maybe,
            Rsvp.No => RsvpStatus.No,
            _ => throw new ArgumentOutOfRangeException(nameof(answer)),
        };
        var response = await client.V1_events_rsvpAsync(id, new Body89 { Status = status, Comment = comment }, cancellationToken).ConfigureAwait(false);
        return ParseRsvp(response.Data?.Status) is var parsed && parsed != Rsvp.None ? parsed : answer;
    }

    public static Rsvp ParseRsvp(string? value) => value switch
    {
        "yes" => Rsvp.Yes,
        "maybe" => Rsvp.Maybe,
        "no" => Rsvp.No,
        _ => Rsvp.None,
    };

    internal static EventItem Map(EventResource e)
    {
        GameServer? server = null;
        if (e.Location?.Server is { } s)
        {
            server = new GameServer(s.Id, s.Slug, s.Name, s.Game, null, s.Address, ServerQueryType.Unknown,
                ApiValues.Text(s.Connect_url), s.Online, ApiValues.Text(s.Map), s.Players, s.Max_players, false, null);
        }

        var location = ApiValues.Text(e.Location?.Label) ?? ApiValues.Text(e.Location?.Value) ?? server?.Name;
        return new EventItem(
            e.Id,
            e.Title,
            e.Type_label,
            ApiValues.Text(e.Color),
            ApiValues.Time(e.Starts_at),
            ApiValues.Time(e.Ends_at),
            e.Squad?.Name,
            e.Mandatory,
            e.Cancelled,
            location,
            server,
            e.Rsvp_counts?.Yes ?? 0,
            e.Rsvp_counts?.Maybe ?? 0,
            e.Max_participants,
            ParseRsvp(e.My_rsvp?.Status),
            ApiValues.Text(e.Description),
            ApiValues.WebUri(e.Url),
            ApiValues.WebUri(e.Ics_url));
    }
}
