using System.Globalization;
using System.Net;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Stats;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features;

public class StatsServiceTests
{
    private static StatsService Create(string json, out StubHttpHandler stub)
    {
        stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, json));
        var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);
        return new StatsService(api.Client);
    }

    [Fact]
    public async Task Leaderboard_is_ordered_and_requests_period_and_metric()
    {
        var service = Create("""
            {"data":[
              {"rounds":5,"kills":40,"deaths":20,"suicides":0,"teamkills":0,"seconds":3600,"assists":0,"headshots":4,"objectives":0,"kd":2.0,"hs_rate":0.1,"rank":2,"member":{"id":"b","slug":"b","name":"Bravo"}},
              {"rounds":9,"kills":90,"deaths":30,"suicides":0,"teamkills":0,"seconds":7200,"assists":0,"headshots":9,"objectives":0,"kd":3.0,"hs_rate":0.1,"rank":1,"member":{"id":"a","slug":"a","name":"Alpha"}}],
             "meta":{"game":{"id":"g","slug":"et","name":"ET"},"period":"7d","metric":"kd","from":"2026-10-09","me":4}}
            """, out var stub);

        var board = await service.LeaderboardAsync("et", "7d", "kd");

        Assert.Equal(["Alpha", "Bravo"], board.Entries.Select(e => e.Name));
        Assert.Equal(TimeSpan.FromHours(2), board.Entries[0].Playtime);
        Assert.Equal(4, board.MyRank);
        var query = stub.Requests[0].Uri!.Query;
        Assert.Contains("period=7d", query, StringComparison.Ordinal);
        Assert.Contains("metric=kd", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Onboarding_drops_non_https_links()
    {
        var service = Create("""
            {"data":{"progress":50,"complete":false,"steps":[
              {"key":"discord","label":"Discord verknüpfen","done":true,"url":"https://rebelsofgaming.com/de/account"},
              {"key":"steam","label":"Steam verknüpfen","done":false,"url":"javascript:alert(1)"}]}}
            """, out _);

        var onboarding = await service.OnboardingAsync();

        Assert.Equal(50, onboarding.Progress);
        Assert.NotNull(onboarding.Steps[0].Url);
        Assert.Null(onboarding.Steps[1].Url);
    }

    [Theory]
    [InlineData("most_playtime", 5400, "1.5 h")]
    [InlineData("best_kd", 2.345, "2.35")]
    [InlineData("most_kills", 148, "148")]
    public void Award_values_are_formatted(string award, double value, string expected) =>
        Assert.Equal(expected, StatsService.FormatAwardValue(award, value, CultureInfo.InvariantCulture));
}
