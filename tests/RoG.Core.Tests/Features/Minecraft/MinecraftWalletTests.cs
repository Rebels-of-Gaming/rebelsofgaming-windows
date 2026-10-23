using System.Net;
using System.Text.Json.Nodes;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Members;
using RoG.Core.Features.Minecraft;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Minecraft;

public class MinecraftWalletTests
{
    // Form wie in Guide 46 „Eigene Wallet“ (v0.39.0)
    private const string Wallet = """
        {"data":{"balance":3240,
          "daily":{"streak":5,"best_streak":12,"last_claimed_on":"2026-10-21","claimed_today":false,"next_at":"2026-10-22T22:00:00Z"},
          "transactions":[
            {"id":"01JB1","type":"shop_buy","direction":"out","counterparty":"system","amount":120,"reason":"2x DIAMOND",
             "source":null,"period_start":null,"server":"freebuild","created_at":"2026-10-20T13:00:00Z"},
            {"id":"01JB2","type":"earning","direction":"in","counterparty":"system","amount":37,"reason":null,
             "source":"job:miner","period_start":"2026-10-20T12:00:00Z","server":null,"created_at":"2026-10-20T12:59:00Z"},
            {"id":"01JB3","type":"pay","direction":"internal","counterparty":"self","amount":10,"reason":null,
             "source":null,"period_start":null,"server":null,"created_at":null},
            {"type":"pay","direction":"in","amount":5}]}}
        """;

    private static (RoGApi Api, StubHttpHandler Stub) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        return (new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub), stub);
    }

    [Fact]
    public async Task Wallet_is_read_with_daily_and_transactions()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, Wallet));
        using (api)
        {
            var wallet = await new MinecraftService(api.Http).WalletAsync();

            Assert.EndsWith("/v1/me/minecraft/wallet", Assert.Single(stub.Requests).Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal(3240, wallet.Balance);
            Assert.Equal(new MinecraftDaily(5, 12, false, DateTimeOffset.Parse("2026-10-22T22:00:00Z", System.Globalization.CultureInfo.InvariantCulture)), wallet.Daily);
            Assert.Equal(3, wallet.Transactions.Count); // Eintrag ohne id wird übersprungen

            var buy = wallet.Transactions[0];
            Assert.True(buy.IsOutgoing);
            Assert.Equal(-120, buy.SignedAmount);
            Assert.Equal("freebuild", buy.Server);
            Assert.Equal("2x DIAMOND", buy.Reason);

            var earning = wallet.Transactions[1];
            Assert.Equal(37, earning.SignedAmount);
            Assert.Equal("job:miner", earning.Source);
            Assert.NotNull(earning.PeriodStart);

            Assert.Equal(0, wallet.Transactions[2].SignedAmount);
            Assert.Equal("self", wallet.Transactions[2].Counterparty);
        }
    }

    [Fact]
    public void Missing_wallet_means_zero_and_no_transactions()
    {
        var wallet = MinecraftService.ParseWallet(JsonNode.Parse("""{"balance":0,"daily":{"streak":0,"best_streak":0,"claimed_today":false},"transactions":[]}"""));

        Assert.Equal(0, wallet.Balance);
        Assert.Equal(0, wallet.Daily.Streak);
        Assert.Empty(wallet.Transactions);
        Assert.Equal(0, MinecraftService.ParseWallet(null).Balance);
    }

    [Fact]
    public void Admin_transactions_take_the_server_name_from_the_object()
    {
        var tx = MinecraftService.ParseTransaction(JsonNode.Parse("""
            {"id":"01JB4","type":"admin","direction":"in","counterparty":"system","amount":500,"reason":"Bau-Wettbewerb",
             "source":null,"period_start":null,"server":{"id":"01J","name":"RoG Network"},"created_at":"2026-10-22T10:00:00Z"}
            """))!;

        Assert.Equal("RoG Network", tx.Server);
        Assert.True(tx.IsIncoming);
    }

    [Theory]
    [InlineData("""{"visibility":{"coins":"members","bio":"public"}}""", "members")]
    [InlineData("""{"visibility":{"coins":"public"}}""", "public")]
    [InlineData("""{"visibility":{"coins":"everyone"}}""", "private")]
    [InlineData("""{"visibility":{}}""", "private")]
    [InlineData("""{}""", "private")]
    public void Coins_visibility_defaults_to_private(string json, string expected)
    {
        Assert.Equal(expected, MinecraftService.ParseCoinsVisibility(JsonNode.Parse(json)));
    }

    [Fact]
    public async Task Coins_visibility_patch_sends_only_the_coins_field()
    {
        var (api, stub) = Create((r, _) => StubHttpHandler.Json(HttpStatusCode.OK, """{"data":{"visibility":{"coins":"members","bio":"public"}}}"""));
        using (api)
        {
            Assert.Equal("members", await new MinecraftService(api.Http).SetCoinsVisibilityAsync("members"));

            var request = Assert.Single(stub.Requests);
            Assert.Equal("PATCH", request.Method.Method);
            Assert.EndsWith("/v1/me/profile", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            var body = JsonNode.Parse(request.Body!)!.AsObject();
            Assert.Equal(["visibility"], body.Select(p => p.Key));
            Assert.Equal("""{"coins":"members"}""", body["visibility"]!.ToJsonString());
        }
    }

    [Fact]
    public async Task Invalid_coins_visibility_is_not_sent()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{}"));
        using (api)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => new MinecraftService(api.Http).SetCoinsVisibilityAsync("everyone"));
            Assert.Empty(stub.Requests);
        }
    }

    [Theory]
    [InlineData(""","rebel_coins":{"balance":3240,"streak":5,"best_streak":12}""", true)]
    [InlineData(""","rebel_coins":null""", false)]
    [InlineData("", false)]
    public async Task Member_profile_shows_coins_only_with_opt_in(string coins, bool expected)
    {
        var json = """{"data":{"id":"01JA","slug":"wahke","name":"Wahke","avatar_url":null,"status":"offline","profile":{"motto":null""" + coins + "}}}";
        var (api, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, json));
        using (api)
        {
            var profile = await new MemberService(api.Client).GetAsync("wahke");

            Assert.Equal(expected, profile.Coins is not null);
            if (expected)
            {
                Assert.Equal(new MemberCoins(3240, 5, 12), profile.Coins);
            }
        }
    }

    [Fact]
    public async Task Member_profile_has_the_minecraft_summary_only_with_opt_in()
    {
        var json = """
            {"data":{"id":"01JA","slug":"wahke","name":"Wahke","avatar_url":null,"status":"offline","profile":{"motto":null,
            "minecraft":{"playtime_seconds":403200,"sessions":112,"matches_played":135,"matches_won":55,"parkour_courses":1,
            "awards":[{"key":"mc-playtime-100h","name":"Blockveteran","description":"x","icon":"hourglass","color":"#D4AE3C","awarded_at":"2026-10-20T18:02:11Z"}]}}}}
            """;
        var (api, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, json));
        using (api)
        {
            var profile = await new MemberService(api.Client).GetAsync("wahke");

            Assert.Equal(new MemberMinecraft(403200, 112, 135, 55, 1, 1), profile.Minecraft);
        }
    }
}
