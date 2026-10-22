using System.Net;
using System.Text.Json.Nodes;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.GameBans;
using RoG.Core.Features.Minecraft;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Minecraft;

public class MinecraftEconomyTests
{
    private const string WalletId = "01JB8Z3K6Q9X2M4N7P1R5T8V0W";

    private const string Economy = """
        {"data":{"daily_base":50,"daily_milestones":[{"day":30,"bonus":500},{"day":7,"bonus":100}],"system_daily_limit":20000,
          "key_system_daily_limit":250000,"key_earnings_hourly_limit":50000,"pay_daily_limit":100000,"admin_daily_limit":200000}}
        """;

    private static (RoGApi Api, StubHttpHandler Stub) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        return (new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub), stub);
    }

    [Fact]
    public async Task Economy_settings_are_read_and_saved_completely()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, Economy));
        using (api)
        {
            var service = new MinecraftService(api.Http);
            var settings = await service.EconomyAsync();

            Assert.Equal(50, settings.DailyBase);
            Assert.Equal([new DailyMilestone(7, 100), new DailyMilestone(30, 500)], settings.Milestones);
            Assert.Equal(200000, settings.AdminDailyLimit);

            await service.SaveEconomyAsync(settings with { DailyBase = 2_000_000 });
            Assert.Equal("PUT", stub.Requests[1].Method.Method);
            var body = JsonNode.Parse(stub.Requests[1].Body!)!;
            Assert.Equal(MinecraftService.MaxAmount, (int)body["daily_base"]!);
            Assert.Equal(2, body["daily_milestones"]!.AsArray().Count);
            Assert.Equal(250000, (int)body["key_system_daily_limit"]!);
        }
    }

    [Theory]
    [InlineData("7: 100, 30: 500", "7:100|30:500")]
    [InlineData("30=500\n7 : 100", "7:100|30:500")]
    [InlineData("", "")]
    public void Milestones_are_parsed_and_sorted(string text, string expected)
    {
        var parsed = MinecraftService.ParseMilestones(text)!;

        Assert.Equal(expected, string.Join('|', parsed.Select(m => $"{m.Day}:{m.Bonus}")));
    }

    [Theory]
    [InlineData("7: 100, 7: 200")]
    [InlineData("0: 100")]
    [InlineData("3651: 1")]
    [InlineData("7: -1")]
    [InlineData("seven: 100")]
    [InlineData("7: 1000001")]
    public void Invalid_milestones_are_rejected(string text)
    {
        Assert.Null(MinecraftService.ParseMilestones(text));
    }

    [Fact]
    public void Milestones_round_trip()
    {
        var list = new[] { new DailyMilestone(7, 100), new DailyMilestone(30, 500) };

        Assert.Equal(list, MinecraftService.ParseMilestones(MinecraftService.FormatMilestones(list)));
    }

    [Fact]
    public async Task Wallets_are_listed_with_filters_and_parsed()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """
            {"data":[
              {"id":"01JB1","kind":"account","uuid":null,"name":null,"user":{"id":"01J","name":"Wahke","slug":"wahke"},
               "accounts":[{"uuid":"069a79f4-44e9-4726-a5be-fca90e38aaf5","name":"Notch","edition":"java"}],"balance":3240,
               "daily":{"streak":5,"best_streak":12,"last_claimed_on":"2026-10-21","claimed_today":true,"next_at":"2026-10-22T22:00:00Z"},
               "last_activity_at":"2026-10-22T10:00:00Z","created_at":"2026-10-01T10:00:00Z"},
              {"id":"01JB2","kind":"guest","uuid":"853c80ef-3c37-49fd-aa49-938b674adae6","name":"jeb_","user":null,"accounts":[],"balance":10,
               "daily":{"streak":0,"best_streak":1,"last_claimed_on":null,"claimed_today":false,"next_at":"2026-10-22T22:00:00Z"},
               "last_activity_at":null,"created_at":null},
              {"kind":"guest"}],
             "links":{"next":null,"prev":null},"meta":{"per_page":50,"next_cursor":"c2"}}
            """));
        using (api)
        {
            var page = await new MinecraftService(api.Http).WalletsAsync(" jeb ", MinecraftService.KindGuest, "c1");

            var query = Uri.UnescapeDataString(Assert.Single(stub.Requests).Uri!.Query);
            Assert.Contains("filter[search]=jeb", query, StringComparison.Ordinal);
            Assert.Contains("filter[kind]=guest", query, StringComparison.Ordinal);
            Assert.Contains("cursor=c1", query, StringComparison.Ordinal);
            Assert.Equal(2, page.Items.Count);
            Assert.True(page.HasMore);

            var account = page.Items[0];
            Assert.False(account.IsGuest);
            Assert.Equal("Wahke", account.UserName);
            Assert.Equal("Notch", Assert.Single(account.Accounts).Name);
            Assert.True(account.Daily.ClaimedToday);

            var guest = page.Items[1];
            Assert.True(guest.IsGuest);
            Assert.Equal("853c80ef-3c37-49fd-aa49-938b674adae6", guest.Uuid);
            Assert.Null(guest.UserSlug);
        }
    }

    [Fact]
    public async Task Unknown_kind_is_not_sent()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """{"data":[],"meta":{"next_cursor":null}}"""));
        using (api)
        {
            await new MinecraftService(api.Http).WalletsAsync(kind: "bots");

            Assert.DoesNotContain("kind", Uri.UnescapeDataString(Assert.Single(stub.Requests).Uri!.Query), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Adjustment_is_sent_and_returns_the_new_balance()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Created, """
            {"data":{"transaction":{"id":"01JB9","type":"admin","direction":"in","counterparty":"system","amount":500},"balance":3740}}
            """));
        using (api)
        {
            var balance = await new MinecraftService(api.Http).AdjustAsync(WalletId, MinecraftService.Credit, 500, "  Bau-Wettbewerb  ");

            Assert.Equal(3740, balance);
            var request = Assert.Single(stub.Requests);
            Assert.EndsWith($"/v1/minecraft/wallets/{WalletId}/adjustments", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("""{"direction":"credit","amount":500,"reason":"Bau-Wettbewerb"}""", request.Body);
        }
    }

    [Theory]
    [InlineData("gift", 500, "Bau-Wettbewerb")]
    [InlineData("credit", 0, "Bau-Wettbewerb")]
    [InlineData("credit", 1_000_001, "Bau-Wettbewerb")]
    [InlineData("debit", 5, "  x ")]
    public async Task Invalid_adjustments_are_not_sent(string direction, int amount, string reason)
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Created, "{}"));
        using (api)
        {
            await Assert.ThrowsAnyAsync<ArgumentException>(() => new MinecraftService(api.Http).AdjustAsync(WalletId, direction, amount, reason));
            Assert.Empty(stub.Requests);
        }
    }

    [Fact]
    public async Task Insufficient_funds_is_reported_as_problem()
    {
        var (api, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Conflict, """{"type":"about:blank","title":"Conflict","status":409,"code":"insufficient_funds"}"""));
        using (api)
        {
            var ex = await Assert.ThrowsAsync<ApiProblemException>(() => new MinecraftService(api.Http).AdjustAsync(WalletId, MinecraftService.Debit, 5, "Korrektur"));

            Assert.Equal("insufficient_funds", ex.Problem.Code);
        }
    }

    [Fact]
    public async Task Wallet_transactions_page_through_the_wallet()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """
            {"data":[{"id":"01JB4","type":"admin","direction":"in","counterparty":"system","amount":500,"reason":"Bau",
              "server":null,"transaction_id":"01JB4","from_wallet_id":null,"to_wallet_id":"01JB1","meta":null}],
             "meta":{"next_cursor":null}}
            """));
        using (api)
        {
            var page = await new MinecraftService(api.Http).WalletTransactionsAsync(WalletId, "c1");

            Assert.Contains("cursor=c1", Assert.Single(stub.Requests).Uri!.Query, StringComparison.Ordinal);
            Assert.Equal("Bau", Assert.Single(page.Items).Reason);
            Assert.False(page.HasMore);
        }
    }

    [Theory]
    [InlineData("rog-proxy 0.6.0\nrog-core=0.6", "rog-proxy:0.6.0|rog-core:0.6")]
    [InlineData("ROG-Proxy v1", "rog-proxy:1")]
    [InlineData("", "")]
    public void Plugin_versions_are_parsed(string text, string expected)
    {
        var parsed = PublicBanService.ParsePluginVersions(text)!;

        Assert.Equal(expected, string.Join('|', parsed.Select(v => v.Plugin + ":" + v.Version)));
    }

    [Theory]
    [InlineData("rog-proxy")]
    [InlineData("rog-proxy 0.6.0.1")]
    [InlineData("-proxy 1.0")]
    [InlineData("rog-proxy 1.0\nrog-proxy 2.0")]
    public void Invalid_plugin_versions_are_rejected(string text)
    {
        Assert.Null(PublicBanService.ParsePluginVersions(text));
    }

    [Fact]
    public async Task Plugin_versions_are_read_and_replace_all_admin_entries()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """
            {"data":{"appeals_board":null,"board_options":[],"min_plugin_version":"1.0.0",
              "min_plugin_versions_env":[{"plugin":"rog-core","version":"0.5.0"}],
              "min_plugin_versions":[{"plugin":"rog-proxy","version":"0.6.0"}]}}
            """));
        using (api)
        {
            var settings = await new PublicBanService(api.Http).SetPluginVersionsAsync([new PluginMinVersion("rog-proxy", "0.6.0")]);

            Assert.Equal("1.0.0", settings.MinPluginVersion);
            Assert.Equal(new PluginMinVersion("rog-core", "0.5.0"), Assert.Single(settings.EnvPluginVersions));
            Assert.Equal(new PluginMinVersion("rog-proxy", "0.6.0"), Assert.Single(settings.PluginVersions));
            var request = Assert.Single(stub.Requests);
            Assert.Equal("PATCH", request.Method.Method);
            Assert.Equal("""{"min_plugin_versions":[{"plugin":"rog-proxy","version":"0.6.0"}]}""", request.Body);
        }
    }
}
