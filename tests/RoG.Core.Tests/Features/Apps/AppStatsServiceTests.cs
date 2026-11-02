using System.Text.Json.Nodes;
using RoG.Core.Features.Apps;

namespace RoG.Core.Tests.Features.Apps;

public class AppStatsServiceTests
{
    [Fact]
    public void Parses_platforms_totals_and_days()
    {
        var stats = AppStatsService.Parse(JsonNode.Parse("""
            {"platforms":[{"platform":"windows","store":"microsoft_store","store_configured":false,"downloads":0,"redownloads":0,"downloads_30d":0,"updates_30d":0,
               "last_report":null,"devices":12,"active_devices":9,"accounts":8,"versions":[{"version":"2.5.26","devices":7},{"version":"2.5.19","devices":5}]},
              {"platform":"linux","store":null,"store_configured":false,"downloads":0,"redownloads":0,"downloads_30d":0,"updates_30d":0,"last_report":null,"devices":1,"active_devices":1,"accounts":1,"versions":[]}],
             "totals":{"downloads":340,"downloads_30d":41,"devices":13,"active":10},
             "days":[{"date":"2026-11-01","downloads":3}],"last_import":"2026-11-02T03:00:00Z"}
            """));

        Assert.Equal(2, stats.Platforms.Count);
        Assert.Equal([("2.5.26", 7), ("2.5.19", 5)], stats.Platforms[0].Versions);
        Assert.Null(stats.Platforms[1].Store);
        Assert.Equal(340, stats.Downloads);
        Assert.Equal((new DateOnly(2026, 11, 1), 3L), Assert.Single(stats.Days));
        Assert.NotNull(stats.LastImport);
    }
}
