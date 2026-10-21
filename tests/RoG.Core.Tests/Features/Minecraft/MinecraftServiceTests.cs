using System.Net;
using System.Text.Json.Nodes;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Minecraft;
using RoG.Core.Navigation;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Minecraft;

public class MinecraftServiceTests
{
    private const string AccountId = "01JB8Z3K6Q9X2M4N7P1R5T8V0W";
    private const string ServerId = "01JBA00000000000000000000A";

    // Form wie in Guide 44 (v0.36.0)
    private const string Account = """
        {"id":"01JB8Z3K6Q9X2M4N7P1R5T8V0W","uuid":"069a79f4-44e9-4726-a5be-fca90e38aaf5","xuid":null,"name":"Notch","edition":"java",
         "platform":"java","linked_at":"2026-10-21T12:03:00Z","last_seen_at":"2026-10-21T12:00:00Z"}
        """;

    private const string AdminAccount = """
        {"id":"01JB8Z3K6Q9X2M4N7P1R5T8V0W","uuid":"00000000-0000-0000-0009-01f2a3b4c5d6","xuid":"2535411234567890","name":".Steve","edition":"bedrock",
         "platform":"xbox","linked_at":"2026-10-21T12:03:00Z","last_seen_at":null,"user":{"id":"01J","name":"Wahke","slug":"wahke"},
         "linked_via":{"server":{"id":"01JBA00000000000000000000A","name":"RoG Network","slug":"rog-network"},"plugin_key_id":"01K"}}
        """;

    private const string Preview = """
        {"data":{"preview":true,"name":"Notch","edition":"java","platform":"java","server":"RoG Network",
         "requested_at":"2026-10-21T12:00:00Z","minutes_ago":2,"expires_at":"2026-10-21T12:10:00Z"}}
        """;

    private static (RoGApi Api, StubHttpHandler Stub) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        return (new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub), stub);
    }

    [Theory]
    [InlineData("K7M2QX", "K7M2QX")]
    [InlineData(" k7m-2qx ", "K7M2QX")]
    [InlineData("k7 m2 qx", "K7M2QX")]
    public void Codes_are_normalized_like_the_server(string input, string expected)
    {
        Assert.Equal(expected, MinecraftService.NormalizeCode(input));
        Assert.True(MinecraftService.IsValidCode(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("K7M2Q")]
    [InlineData("K7M2QXX")]
    [InlineData("K7M2Q0")]
    [InlineData("K7M2QO")]
    [InlineData("K7M2Q1")]
    [InlineData("K7M2QI")]
    public void Invalid_codes_are_rejected_before_sending(string? input) => Assert.False(MinecraftService.IsValidCode(input));

    [Fact]
    public void Own_account_is_mapped()
    {
        var a = MinecraftService.Parse(JsonNode.Parse(Account))!;
        Assert.Equal("Notch", a.Name);
        Assert.Equal(MinecraftService.Java, a.Edition);
        Assert.Equal("069a79f4-44e9-4726-a5be-fca90e38aaf5", a.Uuid);
        Assert.NotNull(a.LinkedAt);
        Assert.Null(a.UserName);
    }

    [Fact]
    public void Admin_account_carries_user_and_server()
    {
        var a = MinecraftService.Parse(JsonNode.Parse(AdminAccount))!;
        Assert.Equal(MinecraftService.Bedrock, a.Edition);
        Assert.Equal("xbox", a.Platform);
        Assert.Equal("Wahke", a.UserName);
        Assert.Equal("wahke", a.UserSlug);
        Assert.Equal("RoG Network", a.ServerName);
        Assert.Null(a.LastSeenAt);
    }

    [Fact]
    public void Accounts_without_id_or_uuid_are_skipped()
    {
        Assert.Null(MinecraftService.Parse(JsonNode.Parse("""{"id":"x"}""")));
        Assert.Null(MinecraftService.Parse(JsonNode.Parse("""{"uuid":"x"}""")));
    }

    [Fact]
    public async Task Preview_sends_the_normalized_code_without_confirm()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, Preview));
        using (api)
        {
            var preview = await new MinecraftService(api.Http).PreviewAsync(" k7m-2qx ");

            var request = Assert.Single(stub.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/v1/me/minecraft/link", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            var body = JsonNode.Parse(request.Body!)!.AsObject();
            Assert.Equal("K7M2QX", (string?)body["code"]);
            Assert.False(body.ContainsKey("confirm"));
            Assert.Equal("Notch", preview.Name);
            Assert.Equal(2, preview.MinutesAgo);
            Assert.Equal("RoG Network", preview.Server);
        }
    }

    [Fact]
    public async Task Confirm_links_with_confirm_true_and_a_new_idempotency_key()
    {
        var (api, stub) = Create((_, n) => n == 0
            ? StubHttpHandler.Json(HttpStatusCode.OK, Preview)
            : StubHttpHandler.Json(HttpStatusCode.Created, "{\"data\":" + Account + "}"));
        using (api)
        {
            var service = new MinecraftService(api.Http);
            await service.PreviewAsync("K7M2QX");
            var account = await service.ConfirmAsync("K7M2QX");

            Assert.Equal(2, stub.Requests.Count);
            Assert.True((bool)JsonNode.Parse(stub.Requests[1].Body!)!["confirm"]!);
            Assert.NotEqual(stub.Requests[0].Headers["Idempotency-Key"], stub.Requests[1].Headers["Idempotency-Key"]);
            Assert.Equal(AccountId, account.Id);
        }
    }

    [Fact]
    public async Task Invalid_code_is_never_sent()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, Preview));
        using (api)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => new MinecraftService(api.Http).PreviewAsync("K7M2Q0"));
            Assert.Empty(stub.Requests);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "minecraft_link_code_invalid")]
    [InlineData(HttpStatusCode.Conflict, "minecraft_edition_taken")]
    [InlineData(HttpStatusCode.Conflict, "minecraft_already_linked")]
    public async Task Link_errors_surface_as_problems(HttpStatusCode status, string code)
    {
        var (api, _) = Create((_, _) => StubHttpHandler.Json(status, $$"""{"status":{{(int)status}},"code":"{{code}}"}""", "application/problem+json"));
        using (api)
        {
            var ex = await Assert.ThrowsAsync<ApiProblemException>(() => new MinecraftService(api.Http).ConfirmAsync("K7M2QX"));
            Assert.Equal(code, ex.Problem.Code);
        }
    }

    [Fact]
    public async Task Mine_and_unlink_use_the_own_routes()
    {
        var (api, stub) = Create((r, _) => r.Method == HttpMethod.Get
            ? StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":[" + Account + "]}")
            : new HttpResponseMessage(HttpStatusCode.NoContent));
        using (api)
        {
            var service = new MinecraftService(api.Http);
            Assert.Single(await service.MineAsync());
            await service.UnlinkAsync(AccountId);

            Assert.EndsWith("/v1/me/minecraft/accounts", stub.Requests[0].Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal(HttpMethod.Delete, stub.Requests[1].Method);
            Assert.EndsWith("/v1/me/minecraft/accounts/" + AccountId, stub.Requests[1].Uri!.AbsolutePath, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Admin_list_filters_by_search_edition_server_and_cursor()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":[" + AdminAccount + "],\"meta\":{\"next_cursor\":\"c2\"}}"));
        using (api)
        {
            var page = await new MinecraftService(api.Http).ListAsync(" Steve ", MinecraftService.Bedrock, ServerId, "c1");

            var uri = Assert.Single(stub.Requests).Uri!;
            Assert.EndsWith("/v1/minecraft/accounts", uri.AbsolutePath, StringComparison.Ordinal);
            var query = Uri.UnescapeDataString(uri.Query);
            Assert.Contains("filter[search]=Steve", query, StringComparison.Ordinal);
            Assert.Contains("filter[edition]=bedrock", query, StringComparison.Ordinal);
            Assert.Contains("filter[server]=" + ServerId, query, StringComparison.Ordinal);
            Assert.Contains("cursor=c1", query, StringComparison.Ordinal);
            Assert.True(page.HasMore);
        }
    }

    [Fact]
    public async Task Admin_list_ignores_unknown_editions_and_admin_unlink_deletes()
    {
        var (api, stub) = Create((r, _) => r.Method == HttpMethod.Get
            ? StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":[]}")
            : new HttpResponseMessage(HttpStatusCode.NoContent));
        using (api)
        {
            var service = new MinecraftService(api.Http);
            await service.ListAsync(edition: "pocket");
            await service.AdminUnlinkAsync(AccountId);

            Assert.DoesNotContain("edition", stub.Requests[0].Uri!.Query, StringComparison.Ordinal);
            Assert.EndsWith("/v1/minecraft/accounts/" + AccountId, stub.Requests[1].Uri!.AbsolutePath, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("https://rebelsofgaming.com/account/minecraft?code=k7m2qx", "minecraft/K7M2QX", "K7M2QX")]
    [InlineData("https://rebelsofgaming.com/de/account/minecraft", "minecraft", null)]
    [InlineData("https://rebelsofgaming.com/account/minecraft?code=K7M2Q0", "minecraft", null)]
    [InlineData("https://rebelsofgaming.com/account", null, null)]
    public void Web_links_open_the_minecraft_section(string url, string? key, string? code)
    {
        var route = AppRoute.FromWebUrl(new Uri(url), RoGEnvironment.Get(RoGEnvironment.Production))!;
        Assert.Equal(AppSection.Account, route.Section);
        Assert.Equal(key, route.Key);
        Assert.Equal(code, route.MinecraftCode);
    }

    [Fact]
    public void App_links_open_the_minecraft_section()
    {
        var route = AppRoute.FromAppUri(new Uri("rog://account/minecraft?code=K7M2QX"), RoGEnvironment.Get(RoGEnvironment.Production))!;
        Assert.Equal(AppSection.Account, route.Section);
        Assert.Equal("K7M2QX", route.MinecraftCode);
    }
}
