using RoG.Core.Api;
using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Squads;

/// <summary>Rekrutierungsstand eines Squads (<c>recruiting_status</c>).</summary>
public enum Recruiting
{
    Unknown,
    Open,
    Waitlist,
    Full,
}

public sealed record SquadSummary(
    string Id,
    string Slug,
    string Name,
    string? Tagline,
    string? GameName,
    string? GameShort,
    Recruiting Recruiting,
    int? MembersCount,
    Uri? Logo,
    Uri? Header,
    string? LeaderName,
    string? ClanTag);

/// <summary>Einladungs-, Ref- oder Store-Link eines Squads.</summary>
public sealed record SquadLink(string Label, Uri Url, string? Code);

public sealed record SquadDetail(
    SquadSummary Summary,
    string? Description,
    string? TrainingTimes,
    IReadOnlyList<SquadLink> Links,
    string? ClanName,
    int? ClanLevel,
    string? ClanRequirements,
    bool Archived);

public sealed record SquadMember(string Id, string Slug, string Name, Uri? Avatar, string? RoleLabel, string? RoleColor, bool Substitute);

/// <summary>Squads (öffentlich): Übersicht, Squad-Seite, Line-up.</summary>
public sealed class SquadService(IRoGApiClient client)
{
    public async Task<IReadOnlyList<SquadSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<SquadSummary>();
        string? cursor = null;
        for (var page = 0; page < 10; page++)
        {
            var response = await client.V1_squads_indexAsync(cursor: cursor, per_page: 50, cancellationToken: cancellationToken).ConfigureAwait(false);
            result.AddRange(response.Data.Select(d => Map(d.Id, d.Slug, d.Name, d.Tagline, d.Game, d.Recruiting_status, d.Members_count, d.Logo, d.Header, d.Leader?.Name, d.Clan?.Tag)));
            cursor = response.Meta?.Next_cursor;
            if (string.IsNullOrEmpty(cursor))
            {
                break;
            }
        }

        return result;
    }

    public async Task<SquadDetail> GetAsync(string slug, CancellationToken cancellationToken = default)
    {
        var d = (await client.V1_squads_showAsync(slug, cancellationToken).ConfigureAwait(false)).Data;
        var summary = Map(d.Id, d.Slug, d.Name, d.Tagline, d.Game, d.Recruiting_status, d.Members_count, d.Logo, d.Header, d.Leader?.Name, d.Clan?.Tag);
        var links = d.Links?
            .Select(l => (l.Label, Url: ApiValues.WebUri(l.Short_url ?? l.Url), l.Code))
            .Where(l => l.Url is not null)
            .Select(l => new SquadLink(l.Label, l.Url!, ApiValues.Text(l.Code)))
            .ToList() ?? [];
        return new SquadDetail(summary, ApiValues.Text(d.Description), ApiValues.Text(d.Training_times), links,
            ApiValues.Text(d.Clan?.Name), d.Clan?.Level, ApiValues.Text(d.Clan?.Requirements), d.Archived_at is not null);
    }

    public async Task<IReadOnlyList<SquadMember>> GetMembersAsync(string slug, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_squads_members_indexAsync(slug, cancellationToken).ConfigureAwait(false);
        return response.Data
            .Select(m => new SquadMember(m.Id, m.Slug, m.Name, ApiValues.WebUri(m.Avatar_url), m.Role?.Label, m.Role?.Color, m.Is_substitute))
            .OrderBy(m => m.Substitute)
            .ToList();
    }

    public static Recruiting ParseRecruiting(string? value) => value switch
    {
        "open" => Recruiting.Open,
        "waitlist" => Recruiting.Waitlist,
        "full" => Recruiting.Full,
        _ => Recruiting.Unknown,
    };

    private static SquadSummary Map(string id, string slug, string name, string? tagline, GameResource? game, string? recruiting, int? members, Logo? logo, Header2? header, string? leader, string? clanTag) => new(
        id, slug, name, ApiValues.Text(tagline), game?.Name, game?.Short, ParseRecruiting(recruiting), members,
        ApiValues.Image(logo?.Url, logo?.Variants?.Select(v => (v.Width, v.Url)), 128),
        ApiValues.Image(header?.Url, header?.Variants?.Select(v => (v.Width, v.Url)), 1280),
        leader, ApiValues.Text(clanTag));
}

/// <summary>Squad-Wertung eines Spielers (nur über die Spieler-ID erkannte Werte, Guide 19).</summary>
public sealed record SquadStatsRow(string Name, bool Substitute, int Rounds, int Kills, int Deaths, double? Kd, TimeSpan Playtime);

public sealed record SquadStats(string? GameName, string Period, IReadOnlyList<SquadStatsRow> Members, IReadOnlyList<(string Weapon, int Kills)> Weapons)
{
    public int TotalKills => Members.Sum(m => m.Kills);

    public int TotalRounds => Members.Sum(m => m.Rounds);
}

/// <summary>Turnier (Guide 14): Veranstalter, Zeitraum, Platzierung.</summary>
public sealed record Tournament(string Name, string? Organizer, string? GameShort, string? SquadName, DateOnly? StartsOn, DateOnly? EndsOn, int? Placement, int? Participants, Uri? Url);

/// <summary>Squad-Statistiken und Turniere (lesend).</summary>
public sealed class SquadExtrasService(IRoGApiClient client)
{
    public async Task<SquadStats> StatsAsync(string squadSlug, string period = "30d", CancellationToken cancellationToken = default)
    {
        var d = (await client.V1_squads_statsAsync(squadSlug, period, cancellationToken).ConfigureAwait(false)).Data;
        return new SquadStats(
            d.Game?.Name,
            d.Period,
            d.Members.Select(m => new SquadStatsRow(m.Member.Name, m.Substitute, m.Rounds, m.Kills, m.Deaths, m.Kd, TimeSpan.FromSeconds(m.Seconds)))
                .OrderByDescending(m => m.Kills).ToList(),
            d.Weapons.Select(w => (w.Weapon, w.Kills)).OrderByDescending(w => w.Kills).Take(5).ToList());
    }

    public async Task<IReadOnlyList<Tournament>> TournamentsAsync(string? squadSlug = null, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_tournaments_indexAsync(squadSlug, cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.Data.Select(t => new Tournament(
                t.Name,
                ApiValues.Text(t.Organizer),
                t.Game?.Short,
                t.Squad?.Name,
                DateOnly.TryParse(t.Starts_on, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : null,
                DateOnly.TryParse(t.Ends_on, System.Globalization.CultureInfo.InvariantCulture, out var e) ? e : null,
                t.Placement,
                t.Participants_count,
                ApiValues.WebUri(t.Url)))
            .OrderByDescending(t => t.StartsOn)
            .ToList();
    }
}
