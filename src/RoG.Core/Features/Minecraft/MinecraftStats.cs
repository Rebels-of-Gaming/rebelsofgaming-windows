using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoG.Core.Api;

namespace RoG.Core.Features.Minecraft;

/// <summary>Ergebnis eines Minispiels (Summe aller verknüpften Accounts); <see cref="WinRate"/> in Prozent, ohne Spiele <c>null</c>.</summary>
public sealed record MinigameStats(string Game, string Label, int Played, int Won, double? WinRate, int Kills, int Deaths, IReadOnlyList<(string Name, long Value)> Objectives);

/// <summary>Bestzeit im Lobby-Parkour (nicht das Jump-Rennen).</summary>
public sealed record ParkourBest(string Course, string Label, long BestMs);

/// <summary>Automatische Auszeichnung (Badge mit Schlüssel <c>mc-…</c>).</summary>
public sealed record MinecraftAward(string Key, string Name, string? Description, string? Color, DateTimeOffset? AwardedAt);

/// <summary>Minecraft-Statistiken eines Mitglieds (<c>GET /v1/members/{member}/minecraft-stats</c>).</summary>
public sealed record MinecraftStats(
    long PlaytimeSeconds,
    int Sessions,
    int Deaths,
    IReadOnlyList<(string Cause, long Count)> DeathsBy,
    int PlayerKills,
    long BlocksPlaced,
    long BlocksBroken,
    int Advancements,
    int PlotsClaimed,
    IReadOnlyList<MinigameStats> Minigames,
    IReadOnlyList<ParkourBest> Parkour,
    IReadOnlyList<MinecraftAward> Awards,
    bool HasStats);

/// <summary>Was <see cref="LeaderboardEntry.Value"/> bedeutet.</summary>
public enum LeaderboardValueKind
{
    Seconds,
    Milliseconds,
    Percent,
    Count,
}

/// <summary>Platz in einer Bestenliste; <see cref="Value"/> je nach Liste Sekunden, Siege, Prozent, Zähler oder Millisekunden.</summary>
public sealed record LeaderboardEntry(int Rank, double Value, int? Played, int? Won, double? WinRate, string MemberId, string Slug, string Name, Uri? Avatar);

/// <summary>Eine Bestenliste mit dem, was der Server tatsächlich gewertet hat (<c>meta</c>).</summary>
public sealed record Leaderboard(
    IReadOnlyList<LeaderboardEntry> Entries,
    string Board,
    string Period,
    string? Sort,
    string? Course,
    IReadOnlyList<(string Id, string Label)> Courses,
    int? MinMatches,
    DateTimeOffset? From);

/// <summary>
/// Minecraft-Statistiken, Bestenlisten und Awards (Website v0.41.0, Guide 44): nur mit Opt-in des Mitglieds
/// (<c>visibility.minecraft_stats</c>, Standard <c>private</c>); beides geht auch im Gastmodus.
/// </summary>
public sealed partial class MinecraftService
{
    public const string BoardPlaytime = "playtime";
    public const string BoardParkour = "parkour";
    public const string PeriodAll = "all";
    public const string Period30Days = "30d";
    public const string SortWins = "wins";
    public const string SortWinRate = "win_rate";
    public const string ObjectivePrefix = "objective:";

    /// <summary>Bekannte Listen der Registry <c>rog.minecraft.games</c>; Spielzeit zuerst, Parkour zuletzt.</summary>
    public static IReadOnlyList<string> Boards { get; } = [BoardPlaytime, "bedwars", "skywars", "tntrun", "buildbattle", "jumprace", "ttt", BoardParkour];

    public static IReadOnlyList<string> Periods { get; } = [PeriodAll, Period30Days];

    /// <summary>Wertungen je Liste: Minispiele <c>wins</c>/<c>win_rate</c> plus freigegebene Ziel-Zähler, sonst keine.</summary>
    public static IReadOnlyList<string> SortsFor(string board) => board switch
    {
        BoardPlaytime or BoardParkour => [],
        "ttt" => [SortWins, SortWinRate, ObjectivePrefix + "correct_kills"],
        "jumprace" => [SortWins, SortWinRate, ObjectivePrefix + "checkpoints"],
        _ => [SortWins, SortWinRate],
    };

    /// <summary>Spielzeit in Sekunden, Parkour in Millisekunden (kleinste zuerst), Siegquote in Prozent, sonst Anzahl.</summary>
    public static LeaderboardValueKind ValueKind(string board, string? sort) => board switch
    {
        BoardPlaytime => LeaderboardValueKind.Seconds,
        BoardParkour => LeaderboardValueKind.Milliseconds,
        _ when sort == SortWinRate => LeaderboardValueKind.Percent,
        _ => LeaderboardValueKind.Count,
    };

    public async Task<MinecraftStats> MemberStatsAsync(string member, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        var data = (await SendAsync(HttpMethod.Get, $"members/{Uri.EscapeDataString(member.Trim())}/minecraft-stats", null, cancellationToken).ConfigureAwait(false))?["data"];
        return ParseStats(data);
    }

    /// <summary>Höchstens 50 Plätze; unbekannte Werte schickt die App nicht (der Server würde <c>422 invalid_query</c> melden).</summary>
    public async Task<Leaderboard> LeaderboardAsync(string board, string? period = null, string? sort = null, string? course = null, CancellationToken cancellationToken = default)
    {
        var b = Boards.Contains(board) ? board : BoardPlaytime;
        var query = new List<string> { "board=" + b };
        if (period is not null && Periods.Contains(period))
        {
            query.Add("period=" + period);
        }

        if (sort is not null && SortsFor(b).Contains(sort))
        {
            query.Add("sort=" + Uri.EscapeDataString(sort));
        }

        if (b == BoardParkour && Limit(course, 64) is { } c)
        {
            query.Add("course=" + Uri.EscapeDataString(c));
        }

        var root = await SendAsync(HttpMethod.Get, "minecraft/leaderboards?" + string.Join('&', query), null, cancellationToken).ConfigureAwait(false);
        return ParseLeaderboard(root);
    }

    public static MinecraftStats ParseStats(JsonNode? d)
    {
        var deaths = d?["deaths"];
        var blocks = d?["blocks"];
        return new MinecraftStats(
            Math.Max(0, LongOf(d, "playtime_seconds")),
            Math.Max(0, IntOf(d, "sessions")),
            Math.Max(0, IntOf(deaths, "total")),
            Counters(deaths?["by"]),
            Math.Max(0, IntOf(d?["kills"], "player")),
            Math.Max(0, LongOf(blocks?["placed"], "total")),
            Math.Max(0, LongOf(blocks?["broken"], "total")),
            Math.Max(0, IntOf(d, "advancements")),
            Math.Max(0, IntOf(d?["plots"], "claimed")),
            d?["minigames"] is JsonArray games ? games.Select(ParseMinigame).OfType<MinigameStats>().ToList() : [],
            d?["parkour"] is JsonArray parkour
                ? parkour.Select(p => Str(p, "course") is { } course ? new ParkourBest(course, Str(p, "label") ?? course, Math.Max(0, LongOf(p, "best_ms"))) : null).OfType<ParkourBest>().ToList()
                : [],
            d?["awards"] is JsonArray awards ? awards.Select(ParseAward).OfType<MinecraftAward>().ToList() : [],
            d?["has_stats"] is JsonValue v && v.TryGetValue<bool>(out var has) && has);
    }

    public static MinigameStats? ParseMinigame(JsonNode? n) =>
        Str(n, "game") is { } game
            ? new MinigameStats(
                game,
                Str(n, "label") ?? game,
                Math.Max(0, IntOf(n, "played")),
                Math.Max(0, IntOf(n, "won")),
                DoubleOf(n, "win_rate"),
                Math.Max(0, IntOf(n, "kills")),
                Math.Max(0, IntOf(n, "deaths")),
                Counters(n!["objectives"]))
            : null;

    public static MinecraftAward? ParseAward(JsonNode? n) =>
        Str(n, "key") is { } key && Str(n, "name") is { } name
            ? new MinecraftAward(key, name, Str(n, "description"), Str(n, "color"), Time(n, "awarded_at"))
            : null;

    public static Leaderboard ParseLeaderboard(JsonNode? root)
    {
        var meta = root?["meta"];
        var entries = root?["data"] is JsonArray list
            ? list.Select(e => e?["member"] is { } m && Str(m, "id") is { } id && Str(m, "slug") is { } slug
                ? new LeaderboardEntry(
                    Math.Max(1, IntOf(e, "rank")),
                    DoubleOf(e, "value") ?? 0,
                    NullableInt(e, "played"),
                    NullableInt(e, "won"),
                    DoubleOf(e, "win_rate"),
                    id,
                    slug,
                    Str(m, "name") ?? slug,
                    ApiValues.WebUri(Str(m, "avatar_url")))
                : null).OfType<LeaderboardEntry>().ToList()
            : [];
        return new Leaderboard(
            entries,
            Str(meta, "board") ?? BoardPlaytime,
            Str(meta, "period") ?? PeriodAll,
            Str(meta, "sort"),
            Str(meta, "course"),
            meta?["courses"] is JsonArray courses
                ? courses.Select(c => Str(c, "id") is { } id ? new[] { (id, Str(c, "label") ?? id) } : []).SelectMany(x => x).ToList()
                : [],
            NullableInt(meta, "min_matches"),
            Time(meta, "from"));
    }

    /// <summary>Spielzeit kurz: „3 Std. 12 Min.“-Bausteine als Stunden und Minuten (Text macht die App).</summary>
    public static (long Hours, int Minutes) SplitPlaytime(long seconds)
    {
        var s = Math.Max(0, seconds);
        return (s / 3600, (int)(s % 3600 / 60));
    }

    /// <summary>Parkour-Zeit als <c>m:ss.fff</c> (bzw. <c>h:mm:ss.fff</c>).</summary>
    public static string FormatDuration(long milliseconds)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return t.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}")
            : string.Create(CultureInfo.InvariantCulture, $"{t.Minutes}:{t.Seconds:00}.{t.Milliseconds:000}");
    }

    private static IReadOnlyList<(string, long)> Counters(JsonNode? n) =>
        n is JsonObject o
            ? o.Where(p => p.Value is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<long>(out _))
                .Select(p => (p.Key, Math.Max(0, p.Value!.GetValue<long>())))
                .ToList()
            : [];

    private static long LongOf(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<long>(out var l) ? l : 0;

    private static int? NullableInt(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.GetValueKind() == JsonValueKind.Number ? IntOf(n, name) : null;

    private static double? DoubleOf(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<double>(out var d) && double.IsFinite(d) ? d : null;
}
