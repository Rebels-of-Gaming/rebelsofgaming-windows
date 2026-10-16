using RoG.Core.Api;
using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Stats;

public sealed record StatsGame(string Slug, string Name);

public sealed record LeaderboardEntry(int Rank, string Name, string Slug, int Rounds, int Kills, int Deaths, double? Kd, TimeSpan Playtime, int Headshots);

public sealed record Leaderboard(IReadOnlyList<LeaderboardEntry> Entries, int? MyRank);

public sealed record WeeklyAward(string Award, double Value, IReadOnlyList<string> Members);

public sealed record Awards(string Week, IReadOnlyList<WeeklyAward> Items);

public sealed record OnboardingStep(string Key, string Label, bool Done, Uri? Url);

public sealed record Onboarding(int Progress, bool Complete, IReadOnlyList<OnboardingStep> Steps);

/// <summary>Statistiken (Guide 19): Spiele mit Stats, Ranglisten, Wochen-Awards; dazu die Onboarding-Checkliste.</summary>
public sealed class StatsService(IRoGApiClient client)
{
    /// <summary>Zeitraum: <c>7d</c>, <c>30d</c> (Standard), <c>all</c>.</summary>
    public static readonly IReadOnlyList<string> Periods = ["7d", "30d", "all"];

    /// <summary>Kennzahl der Rangliste (<c>kd</c> erst ab 3 Runden im Zeitraum).</summary>
    public static readonly IReadOnlyList<string> Metrics = ["kills", "kd", "playtime", "rounds", "headshots"];

    public async Task<IReadOnlyList<StatsGame>> GamesAsync(CancellationToken cancellationToken = default) =>
        (await client.V1_stats_gamesAsync(cancellationToken).ConfigureAwait(false)).Data.Select(g => new StatsGame(g.Slug, g.Name)).ToList();

    public async Task<Leaderboard> LeaderboardAsync(string game, string period, string metric, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_stats_leaderboardAsync(game, "25", period, metric, cancellationToken).ConfigureAwait(false);
        return new Leaderboard(
            response.Data.Select(e => new LeaderboardEntry(e.Rank, e.Member.Name, e.Member.Slug, e.Rounds, e.Kills, e.Deaths, e.Kd, TimeSpan.FromSeconds(e.Seconds), e.Headshots))
                .OrderBy(e => e.Rank).ToList(),
            response.Meta?.Me);
    }

    public async Task<Awards> AwardsAsync(string game, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_stats_awardsAsync(game, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new Awards(
            response.Meta?.Week ?? string.Empty,
            response.Data.Select(a => new WeeklyAward(a.Award, a.Value, a.Members?.Select(m => m.Name).ToList() ?? [])).ToList());
    }

    public async Task<Onboarding> OnboardingAsync(CancellationToken cancellationToken = default)
    {
        var d = (await client.V1_me_onboardingAsync(cancellationToken).ConfigureAwait(false)).Data;
        return new Onboarding(
            Math.Clamp(d.Progress, 0, 100),
            d.Complete,
            d.Steps.Select(s => new OnboardingStep(s.Key, s.Label, s.Done, Uri.TryCreate(s.Url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? u : null)).ToList());
    }

    /// <summary>Wert eines Awards lesbar machen: Spielzeit in Stunden, K/D mit zwei Stellen, sonst ganze Zahl.</summary>
    public static string FormatAwardValue(string award, double value, IFormatProvider culture) => award switch
    {
        "most_playtime" => TimeSpan.FromSeconds(value).TotalHours.ToString("0.0", culture) + " h",
        "best_kd" => value.ToString("0.00", culture),
        _ => value.ToString("0", culture),
    };
}
