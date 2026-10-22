using RoG.Core.Api;
using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Matches;

public enum MatchOutcome
{
    None,
    Win,
    Draw,
    Loss,
}

public sealed record MatchMap(string Map, int? Own, int? Opponent, MatchOutcome Result);

public sealed record MatchItem(
    string Id,
    string Slug,
    string Type,
    string? SquadName,
    string? Game,
    string Opponent,
    string? OpponentTag,
    DateTimeOffset? PlayedAt,
    int? Own,
    int? OpponentScore,
    MatchOutcome Result,
    string? Tournament,
    int? Placement,
    IReadOnlyList<MatchMap> Maps,
    IReadOnlyList<string> Lineup,
    IReadOnlyList<(string Label, Uri Url)> Links);

public sealed record HallEntry(string Kind, string Title, string? Description, DateTimeOffset? AchievedOn, int? Placement, string? Medal, string? Squad, string? Game);

public sealed record HonoraryMember(string Slug, string Name, Uri? Avatar, string? Period, string Tribute, bool Featured);

public sealed record HallOfFame(IReadOnlyList<HallEntry> Trophies, IReadOnlyList<HallEntry> Milestones, IReadOnlyList<HonoraryMember> Honorary);

/// <summary>Matches (neueste zuerst) und Hall of Fame (Trophäen, Meilensteine, Ehrenmitglieder).</summary>
public sealed class MatchService(IRoGApiClient client)
{
    public async Task<Page<MatchItem>> ListAsync(string? cursor = null, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_matches_indexAsync(cursor: cursor, per_page: 30, sort: Sort4.Minusplayed_at, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new Page<MatchItem>(response.Data.Select(Map).ToList(), response.Meta?.Next_cursor);
    }

    public async Task<HallOfFame> GetHallOfFameAsync(CancellationToken cancellationToken = default)
    {
        var d = (await client.V1_hallOfFame_indexAsync(cancellationToken).ConfigureAwait(false)).Data;
        return new HallOfFame(
            d.Trophies.OrderBy(t => t.Sort).Select(t => new HallEntry(t.Kind, t.Title, ApiValues.Text(t.Description), ApiValues.Time(t.Achieved_on), t.Placement, ApiValues.Text(t.Medal), t.Squad?.Name, t.Game?.Short)).ToList(),
            d.Milestones.OrderBy(t => t.Sort).Select(t => new HallEntry(t.Kind, t.Title, ApiValues.Text(t.Description), ApiValues.Time(t.Achieved_on), t.Placement, ApiValues.Text(t.Medal), t.Squad?.Name, t.Game?.Short)).ToList(),
            d.Honorary.Select(h => new HonoraryMember(h.Slug, h.Name, ApiValues.WebUri(h.Avatar_url), ApiValues.Text(h.Period), h.Tribute, h.Featured)).ToList());
    }

    public static MatchOutcome ParseOutcome(string? value) => value switch
    {
        "win" => MatchOutcome.Win,
        "draw" => MatchOutcome.Draw,
        "loss" => MatchOutcome.Loss,
        _ => MatchOutcome.None,
    };

    private static MatchItem Map(Data273 m) => new(
        m.Id,
        m.Slug,
        m.Type,
        m.Squad?.Name,
        ApiValues.Text(m.Squad?.Game),
        m.Opponent.Name,
        ApiValues.Text(m.Opponent.Tag),
        ApiValues.Time(m.Played_at),
        m.Score.Own,
        m.Score.Opponent,
        ParseOutcome(m.Result),
        m.Tournament?.Name,
        m.Tournament?.Placement,
        m.Maps?.OrderBy(x => x.Position).Select(x => new MatchMap(x.Map, x.Score.Own, x.Score.Opponent, ParseOutcome(x.Result))).ToList() ?? [],
        m.Lineup?.Select(l => l.Member.Name).ToList() ?? [],
        m.Links?.Select(l => (Label: l.Label ?? l.Type, Url: ApiValues.WebUri(l.Url))).Where(l => l.Url is not null).Select(l => (l.Label, l.Url!)).ToList() ?? []);
}
