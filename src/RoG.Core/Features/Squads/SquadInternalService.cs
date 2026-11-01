using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Core.Features.Events;

namespace RoG.Core.Features.Squads;

/// <summary>Thema im Squad-Forum (Bereich „Intern“).</summary>
public sealed record InternalThread(string Title, string? Author, int Replies, bool Pinned, bool Locked, DateTimeOffset? LastPostAt, Uri? Url);

/// <summary>Squad-Gruppe: letzte Nachricht und Ungelesen-Hinweis (nur für Teilnehmer).</summary>
public sealed record InternalConversation(string Id, bool Unread, string? Author, string? Excerpt, DateTimeOffset? At);

/// <summary>Nächster Squad-Termin mit eigener Antwort.</summary>
public sealed record InternalEvent(string Id, string Title, string TypeLabel, string? Color, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, bool Mandatory, string? Location, int Yes, Rsvp MyRsvp);

/// <summary>Download oder Album des Squads (öffnet die Website).</summary>
public sealed record InternalLink(string Title, string? Meta, Uri? Url);

/// <summary>
/// Squad-Bereich „Intern“ (<c>GET /v1/squads/{squad}/internal</c>): Forum, Squad-Gruppe, Termine, Downloads, Alben.
/// Ein Block ist <c>null</c>, wenn der Token den Lese-Scope des Moduls nicht hat (<see cref="MissingScopes"/>) oder die
/// Person ihn nicht sehen darf (<c>access: false</c>).
/// </summary>
public sealed record SquadInternal(
    IReadOnlyList<InternalThread>? Threads,
    InternalConversation? Conversation,
    IReadOnlyList<InternalEvent>? Events,
    IReadOnlyList<InternalLink>? Downloads,
    IReadOnlyList<InternalLink>? Albums,
    Uri? ForumUrl,
    IReadOnlyList<string> MissingScopes);

/// <summary>
/// Lesender Zugriff auf „Intern“ (auch für die Linux-App), nur Abbildung der
/// generierten Typen. Zugriff: Member des Squads oder <c>squads.manage</c>, sonst <c>403</c>.
/// </summary>
public sealed class SquadInternalService(IRoGApiClient client)
{
    /// <summary>Recht, mit dem auch Nicht-Member den Bereich sehen (Squad-Verwaltung).</summary>
    public const string ManagePermission = "squads.manage";

    public async Task<SquadInternal> GetAsync(string squad, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_squads_internalAsync(squad, cancellationToken).ConfigureAwait(false);
        var d = response.Data;
        var threads = d.Forum is { Access: true } forum
            ? forum.Threads.Select(t => new InternalThread(t.Title, ApiValues.Text(t.Author), t.Replies, t.Pinned, t.Locked, ApiValues.Time(t.Last_post_at), ApiValues.WebUri(t.Url))).ToList()
            : null;
        var conversation = d.Conversation is { Access: true, Id: { Length: > 0 } id } c
            ? new InternalConversation(id, c.Unread, ApiValues.Text(c.Last_message?.Author), ApiValues.Text(c.Last_message?.Excerpt), ApiValues.Time(c.Last_message?.At))
            : null;
        var events = d.Events is { Access: true } ev
            ? ev.Items.Select(e => new InternalEvent(e.Id, e.Title, e.Type_label, ApiValues.Text(e.Color), ApiValues.Time(e.Starts_at), ApiValues.Time(e.Ends_at),
                e.Mandatory, ApiValues.Text(e.Location), e.Yes, EventService.ParseRsvp(e.My_rsvp))).ToList()
            : null;
        var downloads = d.Downloads is { Access: true } dl
            ? dl.Items.Select(x => new InternalLink(x.Title, ApiValues.Text(x.Version), ApiValues.WebUri(x.Url))).ToList()
            : null;
        var albums = d.Albums is { Access: true } al
            ? al.Items.Select(x => new InternalLink(x.Title, x.Items.ToString(System.Globalization.CultureInfo.CurrentCulture), ApiValues.WebUri(x.Url))).ToList()
            : null;
        return new SquadInternal(threads, conversation, events, downloads, albums,
            ApiValues.WebUri(d.Forum?.Url), response.Meta?.Missing_scopes?.ToList() ?? []);
    }
}
