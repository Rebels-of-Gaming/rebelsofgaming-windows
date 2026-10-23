using RoG.Core.Api;
using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Members;

/// <summary>Online-Stand (Presence): grün online · blau Voice · rot auf RoG-Server · gold im Spiel · grau offline.</summary>
public enum Presence
{
    Offline,
    Online,
    Voice,
    Server,
    Ingame,
}

public sealed record MemberSummary(
    string Id,
    string Slug,
    string Name,
    Uri? Avatar,
    string? RankLabel,
    string? RankColor,
    Presence Presence,
    string? Activity,
    IReadOnlyList<string> Squads);

public sealed record MemberBadge(string Name, string? Description, string Color);

public sealed record MemberProfile(
    MemberSummary Summary,
    Uri? Banner,
    string? Motto,
    string? Bio,
    string? Location,
    DateTimeOffset? JoinedAt,
    DateTimeOffset? LastSeenAt,
    IReadOnlyList<MemberBadge> Badges,
    IReadOnlyList<(string Platform, string Handle)> Platforms,
    MemberCoins? Coins = null,
    MemberMinecraft? Minecraft = null);

/// <summary>Rebel-Coins im Profil (<c>profile.rebel_coins</c>) – nur mit Opt-in des Mitglieds, sonst <c>null</c>.</summary>
public sealed record MemberCoins(int Balance, int Streak, int BestStreak);

/// <summary>Kurzfassung der Minecraft-Statistiken (<c>profile.minecraft</c>) – nur mit Opt-in, sonst <c>null</c>.</summary>
public sealed record MemberMinecraft(long PlaytimeSeconds, int Sessions, int MatchesPlayed, int MatchesWon, int ParkourCourses, int Awards);

public sealed record TeamGroup(string Name, string? Description, IReadOnlyList<TeamMember> Members);

public sealed record TeamMember(string Slug, string Name, Uri? Avatar, string RoleLabel, string? RoleColor, string? Responsibility);

/// <summary>Mitglieder (Suche, Online-Filter), Profil, Team-Seite.</summary>
public sealed class MemberService(IRoGApiClient client)
{
    public async Task<Page<MemberSummary>> ListAsync(string? search, bool onlineOnly, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_members_indexAsync(
            cursor: cursor,
            per_page: 40,
            filtersearch: string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            filteronline: onlineOnly ? "1" : null,
            include: "squads",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return new Page<MemberSummary>(response.Data.Select(d => Map(d.Id, d.Slug, d.Name, d.Avatar_url, d.Rank, d.Status, d.Activity, d.Squads)).ToList(), response.Meta?.Next_cursor);
    }

    public async Task<MemberProfile> GetAsync(string slug, CancellationToken cancellationToken = default)
    {
        var d = (await client.V1_members_showAsync(slug, cancellationToken).ConfigureAwait(false)).Data;
        var p = d.Profile;
        var location = string.Join(", ", new[] { p?.City, p?.Country }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new MemberProfile(
            Map(d.Id, d.Slug, d.Name, d.Avatar_url, d.Rank, d.Status, d.Activity, d.Squads),
            ApiValues.WebUri(p?.Banner?.Url_small ?? p?.Banner?.Url),
            ApiValues.Text(p?.Motto),
            ApiValues.Text(p?.Bio),
            location.Length == 0 ? null : location,
            ApiValues.Time(d.Joined_at),
            ApiValues.Time(d.Last_seen_at),
            d.Badges?.Select(b => new MemberBadge(b.Name, ApiValues.Text(b.Description), b.Color)).ToList() ?? [],
            p?.Platform_ids?.Select(x => (x.Name, x.Handle)).ToList() ?? [],
            p?.Rebel_coins is { } c ? new MemberCoins(Math.Max(0, c.Balance), Math.Max(0, c.Streak), Math.Max(0, c.Best_streak)) : null,
            p?.Minecraft is { } m
                ? new MemberMinecraft(Math.Max(0, m.Playtime_seconds), Math.Max(0, m.Sessions), Math.Max(0, m.Matches_played), Math.Max(0, m.Matches_won), Math.Max(0, m.Parkour_courses), m.Awards?.Count ?? 0)
                : null);
    }

    public async Task<IReadOnlyList<TeamGroup>> GetTeamAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.V1_team_indexAsync(cancellationToken).ConfigureAwait(false);
        return response.Data.Select(g => new TeamGroup(
            g.Name,
            ApiValues.Text(g.Description),
            g.Members.Select(m => new TeamMember(m.Slug, m.Name, ApiValues.WebUri(m.Avatar_url), m.Role.Label, m.Role.Color, ApiValues.Text(m.Responsibility))).ToList())).ToList();
    }

    public static Presence ParsePresence(string? value) => value switch
    {
        "online" => Presence.Online,
        "voice" => Presence.Voice,
        "server" => Presence.Server,
        "ingame" => Presence.Ingame,
        _ => Presence.Offline,
    };

    private static MemberSummary Map(string id, string slug, string name, string? avatar, Rank3? rank, string? status, Activity4? activity, ICollection<SquadResource>? squads) => new(
        id, slug, name, ApiValues.WebUri(avatar), rank?.Label, rank?.Color, ParsePresence(status), ApiValues.Text(activity?.Label),
        squads?.Select(s => s.Name).ToList() ?? []);
}
