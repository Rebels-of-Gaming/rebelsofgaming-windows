using System.Net;
using System.Text.Json.Nodes;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.GameBans;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.GameBans;

public class GameBanServiceTests
{
    private const string BanId = "01JB0000000000000000000000";

    // Form wie im Beispiel aus Guide 43
    private const string Ban = """
        {"id":"01JB0000000000000000000000","status":"active","scope":"game","game":{"id":"01G","name":"Enemy Territory","slug":"et"},
         "server":null,"identifier_type":"guid","identifier":"0123456789ABCDEF0123456789ABCDEF","name":"Cheater","ip":null,
         "reason":"Aimbot","expires_at":"2026-10-21T12:00:00Z","created_at":"2026-10-20T12:00:00Z","created_via":"api",
         "created_by":{"id":"01U","name":"Wahke","slug":"wahke"},"issued_by":null,"source_server":{"id":"01S","name":"RoG ET #1","slug":"rog-et-1"},
         "user":null,"lifted_at":null,"lifted_by":null,"lifted_via":null,"lift_reason":null,
         "deliveries":[{"server":{"id":"01S","name":"RoG ET #1","slug":"rog-et-1"},"status":"failed","error":"timeout","updated_at":"2026-10-20T12:01:00Z"}]}
        """;

    private const string Key = """
        {"exists":true,"active":true,"hint":"rogsrv_…a1b2","may_issue":true,"may_lift":false,"may_lift_any":false,"may_ip":false,
         "may_ban_members":false,"max_scope":"server","created_at":"2026-10-20T10:00:00Z","rotated_at":null,"revoked_at":null,
         "last_used_at":"2026-10-20T11:59:00Z","last_heartbeat_at":"2026-10-20T11:59:00Z","plugin":"rog-etl","plugin_version":"1.0.0",
         "plugin_outdated":false,"game_version":"ET:Legacy 2.83","players_online":7,"plugin_active":true
        """;

    private static (RoGApi Api, StubHttpHandler Stub) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        return (new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub), stub);
    }

    [Fact]
    public void Ban_is_mapped_with_deliveries()
    {
        var ban = GameBanService.Parse(JsonNode.Parse(Ban))!;

        Assert.True(ban.IsActive);
        Assert.Equal("game", ban.Scope);
        Assert.Equal("Enemy Territory", ban.GameName);
        Assert.Null(ban.ServerName);
        Assert.Equal(("guid", "0123456789ABCDEF0123456789ABCDEF"), (ban.IdentifierType, ban.Identifier));
        Assert.Equal("Cheater", ban.Title);
        Assert.Equal("Wahke", ban.CreatedBy);
        Assert.Equal("RoG ET #1", ban.SourceServer);
        Assert.False(ban.IsPermanent);
        var delivery = Assert.Single(ban.Deliveries);
        Assert.Equal(("RoG ET #1", "failed", "timeout"), (delivery.ServerName, delivery.Status, delivery.Error));
    }

    [Fact]
    public async Task List_filters_by_server_status_search_and_cursor()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":[" + Ban + ",{\"status\":\"active\"}],\"meta\":{\"next_cursor\":\"c2\"}}"));
        using (api)
        {
            var page = await new GameBanService(api.Http).ListAsync("rog-et-1", GameBanService.Lifted, " cheat ", "c1");

            var uri = Assert.Single(stub.Requests).Uri!;
            Assert.EndsWith("/v1/game-bans", uri.AbsolutePath, StringComparison.Ordinal);
            var query = Uri.UnescapeDataString(uri.Query);
            Assert.Contains("filter[status]=lifted", query, StringComparison.Ordinal);
            Assert.Contains("filter[server]=rog-et-1", query, StringComparison.Ordinal);
            Assert.Contains("filter[search]=cheat", query, StringComparison.Ordinal);
            Assert.Contains("cursor=c1", query, StringComparison.Ordinal);
            Assert.Single(page.Items);
            Assert.Equal("c2", page.NextCursor);
        }
    }

    [Fact]
    public async Task Unknown_status_falls_back_to_active()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":[],\"meta\":{\"next_cursor\":null}}"));
        using (api)
        {
            var page = await new GameBanService(api.Http).ListAsync(null, "whatever");

            Assert.Contains("filter[status]=active", Uri.UnescapeDataString(Assert.Single(stub.Requests).Uri!.Query), StringComparison.Ordinal);
            Assert.DoesNotContain("filter[server]", Uri.UnescapeDataString(stub.Requests[0].Uri!.Query), StringComparison.Ordinal);
            Assert.False(page.HasMore);
        }
    }

    [Fact]
    public async Task Create_sends_the_draft_with_idempotency_key()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Created, "{\"data\":" + Ban + "}"));
        using (api)
        {
            var ban = await new GameBanService(api.Http).CreateAsync(new GameBanDraft("game", "01S", null, " 0123456789abcdef0123456789abcdef ", "Cheater", null, 1440, " Aimbot "));

            var request = Assert.Single(stub.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.True(request.Headers.ContainsKey("Idempotency-Key"));
            var body = JsonNode.Parse(request.Body!)!;
            Assert.Equal("game", (string?)body["scope"]);
            Assert.Equal("01S", (string?)body["server_id"]);
            Assert.Equal("0123456789abcdef0123456789abcdef", (string?)body["identifier"]);
            Assert.Equal(1440, (int?)body["minutes"]);
            Assert.Equal("Aimbot", (string?)body["reason"]);
            Assert.Null(body["identifier_type"]);
            Assert.Null(body["ip"]);
            Assert.Equal(BanId, ban.Id);
        }
    }

    [Fact]
    public void Global_ban_sends_no_server()
    {
        var body = GameBanService.Body(new GameBanDraft("global", "01S", "steamid", "76561198000000000", null, "203.0.113.0/24", 0, "Cheat"));

        Assert.Null(body["server_id"]);
        Assert.Equal("steamid", (string?)body["identifier_type"]);
        Assert.Equal("203.0.113.0/24", (string?)body["ip"]);
        Assert.Equal(0, (int?)body["minutes"]);
    }

    [Theory]
    [InlineData("server", "01S", null, "0123456789abcdef0123456789abcdef", null, 60, "Aimbot", false, "")]
    [InlineData("server", "01S", "steamid", "[U:1:12345]", null, 0, "Cheat", false, "")]
    [InlineData("server", "01S", null, "", null, 60, "Aimbot", false, "GameBans_Rule_Identifier")]
    [InlineData("server", "01S", null, "abc def", null, 60, "Aimbot", false, "GameBans_Rule_Identifier")]
    [InlineData("server", "01S", "Steam ID", "abc", null, 60, "Aimbot", false, "GameBans_Rule_Type")]
    [InlineData("server", "01S", null, "abc", null, 60, " ", false, "GameBans_Rule_Reason")]
    [InlineData("server", null, null, "abc", null, 60, "x", false, "GameBans_Rule_Scope")]
    [InlineData("planet", "01S", null, "abc", null, 60, "x", false, "GameBans_Rule_Scope")]
    [InlineData("global", null, null, "abc", "203.0.113.7", 60, "x", true, "")]
    [InlineData("global", null, null, "abc", "203.0.113.7", 60, "x", false, "GameBans_Rule_Ip")]
    [InlineData("global", null, null, "abc", "nope", 60, "x", true, "GameBans_Rule_Ip")]
    [InlineData("server", "01S", null, "abc", null, -1, "x", false, "GameBans_Rule_Minutes")]
    public void Drafts_are_checked_before_sending(string scope, string? server, string? type, string identifier, string? ip, int minutes, string reason, bool mayIp, string expected)
    {
        var errors = GameBanService.Validate(new GameBanDraft(scope, server, type, identifier, null, ip, minutes, reason), mayIp);

        Assert.Equal(expected, string.Join(",", errors));
    }

    [Fact]
    public void Reason_longer_than_200_is_rejected() =>
        Assert.Contains("GameBans_Rule_Reason", GameBanService.Validate(new GameBanDraft("server", "01S", null, "abc", null, null, 0, new string('x', 201)), false));

    [Fact]
    public async Task Update_and_lift_hit_their_routes()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":" + Ban + "}"));
        using (api)
        {
            var service = new GameBanService(api.Http);
            await service.UpdateAsync(BanId, 0, null);
            await service.LiftAsync(BanId, "  ");

            Assert.Equal(HttpMethod.Patch, stub.Requests[0].Method);
            Assert.EndsWith($"/v1/game-bans/{BanId}", stub.Requests[0].Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("{\"minutes\":0}", stub.Requests[0].Body);
            Assert.EndsWith($"/v1/game-bans/{BanId}/lift", stub.Requests[1].Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("{\"reason\":null}", stub.Requests[1].Body);
        }
    }

    [Fact]
    public async Task Ban_ids_must_be_ulids()
    {
        using var api = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{}")).Api;

        await Assert.ThrowsAsync<ArgumentException>(() => new GameBanService(api.Http).LiftAsync("../servers", null));
    }

    [Fact]
    public async Task Not_active_ban_reports_the_problem_code()
    {
        var (api, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Conflict,
            """{"type":"about:blank","title":"Conflict","status":409,"code":"ban_not_active"}""", "application/problem+json"));
        using (api)
        {
            var ex = await Assert.ThrowsAsync<ApiProblemException>(() => new GameBanService(api.Http).LiftAsync(BanId, null));
            Assert.Equal("ban_not_active", ex.Problem.Code);
        }
    }

    [Fact]
    public async Task Incident_lifts_plugin_bans_since_a_time()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """{"data":{"lifted":4}}"""));
        using (api)
        {
            var lifted = await new GameBanService(api.Http).LiftPluginBansAsync("rog-et-1", "Schlüssel abgeflossen", new DateTimeOffset(2026, 10, 20, 14, 0, 0, TimeSpan.FromHours(2)));

            Assert.Equal(4, lifted);
            var request = Assert.Single(stub.Requests);
            Assert.EndsWith("/v1/servers/rog-et-1/game-bans/lift", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("2026-10-20T12:00:00Z", (string?)JsonNode.Parse(request.Body!)!["since"]);
        }
    }

    [Fact]
    public async Task Plugin_key_status_and_rotation_return_the_token_once()
    {
        var (api, stub) = Create((req, _) => req.Method == HttpMethod.Post
            ? StubHttpHandler.Json(HttpStatusCode.Created, "{\"data\":" + Key + ",\"token\":\"rogsrv_secret\"}}")
            : StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":" + Key + "}}"));
        using (api)
        {
            var service = new GameBanService(api.Http);
            var status = await service.KeyAsync("rog-et-1");
            var created = await service.CreateKeyAsync("rog-et-1", PluginKeyRights.Minimal);

            Assert.True(status.PluginActive);
            Assert.Equal("rogsrv_…a1b2", status.Hint);
            Assert.Equal(7, status.PlayersOnline);
            Assert.Equal("rog-etl", status.Plugin);
            Assert.Equal("rogsrv_secret", created.Token);
            Assert.EndsWith("/v1/servers/rog-et-1/plugin-key", stub.Requests[1].Uri!.AbsolutePath, StringComparison.Ordinal);
            var body = JsonNode.Parse(stub.Requests[1].Body!)!;
            Assert.True((bool?)body["may_issue"]);
            Assert.False((bool?)body["may_lift"]);
            Assert.Equal("server", (string?)body["max_scope"]);
        }
    }

    [Fact]
    public async Task Revoke_deletes_the_key()
    {
        var (api, stub) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        using (api)
        {
            await new GameBanService(api.Http).RevokeKeyAsync("rog-et-1");

            Assert.Equal(HttpMethod.Delete, Assert.Single(stub.Requests).Method);
        }
    }

    [Fact]
    public void Missing_key_parses_as_not_existing()
    {
        var status = GameBanService.ParseKey(JsonNode.Parse("""{"exists":false,"active":false,"hint":null,"plugin_active":false}"""));

        Assert.False(status.Exists);
        Assert.Null(status.MaxScope);
        Assert.Equal(PluginKeyRights.Minimal, PluginKeyRights.From(status) with { MayIssue = true });
    }

    [Theory]
    [InlineData(true, false, false, false, false, "server", false)]
    [InlineData(true, true, false, false, false, "server", true)]
    [InlineData(true, false, false, false, false, "game", true)]
    [InlineData(false, false, false, true, false, "server", true)]
    public void Rights_beyond_issue_on_this_server_need_manage(bool issue, bool lift, bool liftAny, bool ip, bool members, string max, bool expected) =>
        Assert.Equal(expected, new PluginKeyRights(issue, lift, liftAny, ip, members, max).NeedsManage);

    [Fact]
    public void Plugin_key_response_keeps_the_token_out_of_the_status()
    {
        var status = GameBanService.ParseKey(JsonNode.Parse("{" + Key.Trim().TrimStart('{') + ",\"token\":\"rogsrv_secret\"}"));

        Assert.DoesNotContain("rogsrv_secret", status.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void May_link_is_read_kept_and_needs_manage()
    {
        var status = GameBanService.ParseKey(JsonNode.Parse("""{"exists":true,"active":true,"may_issue":true,"may_link":true,"max_scope":"server"}"""));
        Assert.True(status.MayLink);
        Assert.False(GameBanService.ParseKey(JsonNode.Parse(Key + "}")).MayLink);

        var rights = PluginKeyRights.From(status);
        Assert.True(rights.MayLink);
        Assert.True(rights.NeedsManage);
        Assert.False(PluginKeyRights.Minimal.MayLink);
    }

    [Fact]
    public void May_economy_is_read_sent_and_needs_manage()
    {
        var status = GameBanService.ParseKey(JsonNode.Parse("""{"exists":true,"active":true,"may_issue":true,"may_economy":true,"max_scope":"server"}"""));
        Assert.True(status.MayEconomy);
        Assert.False(status.MayLink);

        var rights = PluginKeyRights.From(status);
        Assert.True(rights.NeedsManage);
        Assert.Contains("\"may_economy\":true", rights.ToJson().ToJsonString(), StringComparison.Ordinal);
        Assert.False(PluginKeyRights.Minimal.MayEconomy);
    }
}
