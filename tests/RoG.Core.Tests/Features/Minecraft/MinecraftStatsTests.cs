using System.Net;
using System.Text.Json.Nodes;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Minecraft;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Minecraft;

public class MinecraftStatsTests
{
    private const string Stats = """
        {"data":{
          "playtime_seconds":403200,"sessions":112,
          "deaths":{"total":380,"by":{"player":340,"mob":25,"environment":15,"other":0}},
          "kills":{"player":340},
          "blocks":{"placed":{"total":20160,"groups":{"wood":6720}},"broken":{"total":23520,"groups":{"stone":16800}}},
          "advancements":61,"plots":{"claimed":2,"merged":0},
          "minigames":[
            {"game":"bedwars","label":"Bedwars","played":120,"won":48,"win_rate":40,"kills":240,"deaths":120,"objectives":{"beds_broken":96,"final_kills":96}},
            {"game":"jumprace","label":"Jump-Rennen","played":0,"won":0,"win_rate":null,"kills":0,"deaths":0,"objectives":{}},
            {"label":"ohne Spiel-ID"}],
          "parkour":[{"course":"lobby-tower","label":"Lobby Tower","best_ms":61234}],
          "awards":[{"key":"mc-playtime-100h","name":"Blockveteran","description":"100 Stunden","icon":"hourglass","color":"#D4AE3C","awarded_at":"2026-10-20T18:02:11Z"}],
          "has_stats":true}}
        """;

    private const string Board = """
        {"data":[
          {"rank":1,"value":48,"played":120,"won":48,"win_rate":40,"member":{"id":"01J1","slug":"wahke","name":"Wahke","avatar_url":"https://cdn.example.test/a.png"}},
          {"rank":1,"value":48,"played":90,"won":48,"win_rate":53.3,"member":{"id":"01J2","slug":"fjord","name":"Fjord","avatar_url":"javascript:alert(1)"}},
          {"rank":3,"value":9,"played":null,"won":null,"win_rate":null,"member":null}],
         "meta":{"board":"bedwars","period":"30d","sort":"win_rate","course":null,"courses":[],"min_matches":10,"from":"2026-09-23T00:00:00Z"}}
        """;

    private static (RoGApi Api, StubHttpHandler Stub) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        return (new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub), stub);
    }

    [Fact]
    public async Task Member_stats_are_read_completely()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, Stats));
        using (api)
        {
            var stats = await new MinecraftService(api.Http).MemberStatsAsync("wahke");

            Assert.EndsWith("/v1/members/wahke/minecraft-stats", Assert.Single(stub.Requests).Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal(403200, stats.PlaytimeSeconds);
            Assert.Equal(380, stats.Deaths);
            Assert.Contains(("mob", 25L), stats.DeathsBy);
            Assert.Equal(340, stats.PlayerKills);
            Assert.Equal(20160, stats.BlocksPlaced);
            Assert.Equal(23520, stats.BlocksBroken);
            Assert.Equal(2, stats.PlotsClaimed);
            Assert.Equal(2, stats.Minigames.Count);
            Assert.Equal(40, stats.Minigames[0].WinRate);
            Assert.Contains(("beds_broken", 96L), stats.Minigames[0].Objectives);
            Assert.Null(stats.Minigames[1].WinRate);
            Assert.Equal(new ParkourBest("lobby-tower", "Lobby Tower", 61234), Assert.Single(stats.Parkour));
            Assert.Equal("Blockveteran", Assert.Single(stats.Awards).Name);
            Assert.True(stats.HasStats);
        }
    }

    [Fact]
    public async Task Hidden_stats_are_reported_as_problem()
    {
        var (api, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Forbidden, """{"type":"about:blank","title":"Forbidden","status":403,"code":"minecraft_stats_hidden"}"""));
        using (api)
        {
            var ex = await Assert.ThrowsAsync<ApiProblemException>(() => new MinecraftService(api.Http).MemberStatsAsync("fjord"));

            Assert.Equal("minecraft_stats_hidden", ex.Problem.Code);
        }
    }

    [Fact]
    public async Task Leaderboard_is_requested_and_parsed()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, Board));
        using (api)
        {
            var board = await new MinecraftService(api.Http).LeaderboardAsync("bedwars", "30d", "win_rate");

            var query = Uri.UnescapeDataString(Assert.Single(stub.Requests).Uri!.Query);
            Assert.Equal("?board=bedwars&period=30d&sort=win_rate", query);
            Assert.Equal(2, board.Entries.Count);
            Assert.Equal(1, board.Entries[1].Rank);
            Assert.Equal(53.3, board.Entries[1].WinRate);
            Assert.NotNull(board.Entries[0].Avatar);
            Assert.Null(board.Entries[1].Avatar);
            Assert.Equal(10, board.MinMatches);
            Assert.NotNull(board.From);
            Assert.Equal(LeaderboardValueKind.Percent, MinecraftService.ValueKind(board.Board, board.Sort));
        }
    }

    [Theory]
    [InlineData("unknown", "ever", "kills", null, "?board=playtime")]
    [InlineData("playtime", "all", "wins", null, "?board=playtime&period=all")]
    [InlineData("ttt", null, "objective:correct_kills", null, "?board=ttt&sort=objective:correct_kills")]
    [InlineData("bedwars", null, "objective:correct_kills", null, "?board=bedwars")]
    [InlineData("parkour", null, null, "lobby-tower", "?board=parkour&course=lobby-tower")]
    [InlineData("bedwars", null, null, "lobby-tower", "?board=bedwars")]
    public async Task Only_known_values_are_sent(string board, string? period, string? sort, string? course, string expected)
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """{"data":[],"meta":{"board":"playtime","period":"all","sort":null,"course":null,"courses":[],"min_matches":null,"from":null}}"""));
        using (api)
        {
            await new MinecraftService(api.Http).LeaderboardAsync(board, period, sort, course);

            Assert.Equal(expected, Uri.UnescapeDataString(Assert.Single(stub.Requests).Uri!.Query));
        }
    }

    [Fact]
    public void Parkour_courses_come_from_meta()
    {
        var board = MinecraftService.ParseLeaderboard(JsonNode.Parse("""
            {"data":[{"rank":1,"value":61234,"played":null,"won":null,"win_rate":null,"member":{"id":"01J1","slug":"wahke","name":"Wahke","avatar_url":null}}],
             "meta":{"board":"parkour","period":"all","sort":null,"course":"lobby-tower","courses":[{"id":"lobby-tower","label":"Lobby Tower"},{"label":"ohne ID"}],"min_matches":null,"from":null}}
            """));

        Assert.Equal([("lobby-tower", "Lobby Tower")], board.Courses);
        Assert.Equal(LeaderboardValueKind.Milliseconds, MinecraftService.ValueKind(board.Board, board.Sort));
        Assert.Equal("1:01.234", MinecraftService.FormatDuration((long)board.Entries[0].Value));
    }

    [Theory]
    [InlineData("playtime", null, LeaderboardValueKind.Seconds)]
    [InlineData("bedwars", "wins", LeaderboardValueKind.Count)]
    [InlineData("ttt", "objective:correct_kills", LeaderboardValueKind.Count)]
    [InlineData("skywars", "win_rate", LeaderboardValueKind.Percent)]
    public void Value_kind_follows_board_and_sort(string board, string? sort, LeaderboardValueKind expected) =>
        Assert.Equal(expected, MinecraftService.ValueKind(board, sort));

    [Theory]
    [InlineData(0, 0L, 0)]
    [InlineData(3599, 0L, 59)]
    [InlineData(403200, 112L, 0)]
    public void Playtime_is_split_into_hours_and_minutes(long seconds, long hours, int minutes) =>
        Assert.Equal((hours, minutes), MinecraftService.SplitPlaytime(seconds));

    [Fact]
    public void Long_parkour_times_show_hours() =>
        Assert.Equal("1:00:00.005", MinecraftService.FormatDuration(3_600_005));

    [Fact]
    public async Task Both_profile_visibilities_are_read_and_stats_are_patched_alone()
    {
        var (api, stub) = Create((_, n) => StubHttpHandler.Json(HttpStatusCode.OK, n == 0
            ? """{"data":{"visibility":{"coins":"members","minecraft_stats":"public","email":"private"}}}"""
            : """{"data":{"visibility":{"coins":"members","minecraft_stats":"members"}}}"""));
        using (api)
        {
            var service = new MinecraftService(api.Http);
            Assert.Equal(("members", "public"), await service.ProfileVisibilityAsync());

            Assert.Equal("members", await service.SetStatsVisibilityAsync("members"));
            Assert.Equal("PATCH", stub.Requests[1].Method.Method);
            Assert.Equal("""{"visibility":{"minecraft_stats":"members"}}""", stub.Requests[1].Body);
            await Assert.ThrowsAsync<ArgumentException>(() => service.SetStatsVisibilityAsync("everyone"));
        }
    }

    [Fact]
    public void Missing_stats_visibility_defaults_to_private() =>
        Assert.Equal(MinecraftService.VisibilityPrivate, MinecraftService.ParseVisibility(JsonNode.Parse("""{"visibility":{"coins":"public"}}"""), MinecraftService.StatsField));
}
