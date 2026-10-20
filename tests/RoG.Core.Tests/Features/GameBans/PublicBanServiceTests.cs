using System.Net;
using System.Text.Json.Nodes;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.GameBans;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.GameBans;

public class PublicBanServiceTests
{
    private const string BanId = "01JB8Z3K6Q9X2M4N7P1R5T8V0W";
    private const string AppealId = "01JBA00000000000000000000A";

    // Form wie in der Spec (v0.34.0): gekürzte Kennung, kein Admin, keine IP
    private const string Ban = """
        {"id":"01JB8Z3K6Q9X2M4N7P1R5T8V0W","url":"https://rebelsofgaming.com/de/bans/01JB8Z3K6Q9X2M4N7P1R5T8V0W","name":"Cheater",
         "identifier_type":"guid","identifier_masked":"0123…CDEF","game":{"name":"Enemy Territory","slug":"et"},"server":null,
         "scope":"game","scope_label":"Alle ET-Server","reason":"Aimbot","status":"active","created_at":"2026-10-20T12:00:00Z",
         "expires_at":null,"permanent":true,"ended_at":null,"appeal":{"status":"rejected","created_at":"2026-10-20T13:00:00Z","decided_at":"2026-10-20T14:00:00Z"}}
        """;

    private const string Appeal = """
        {"id":"01JBA00000000000000000000A","ban":{"id":"01JB8Z3K6Q9X2M4N7P1R5T8V0W","name":"Cheater","status":"active","url":"https://rebelsofgaming.com/de/bans/01JB8Z3K6Q9X2M4N7P1R5T8V0W"},
         "status":"open","proof":"code","thread_url":"https://rebelsofgaming.com/de/forum/t/01JBA/einspruch","user":{"id":"01U","name":"Spieler","slug":"spieler"},
         "created_at":"2026-10-20T12:00:00Z","decided_at":null,"decision_note":null,"can_decide":true}
        """;

    private static (RoGApi Api, StubHttpHandler Stub) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        return (new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub), stub);
    }

    [Fact]
    public void Public_ban_is_mapped_without_full_identifier()
    {
        var ban = PublicBanService.Parse(JsonNode.Parse(Ban))!;

        Assert.True(ban.IsActive);
        Assert.True(ban.Permanent);
        Assert.Equal("Cheater", ban.Title);
        Assert.Equal("0123…CDEF", ban.IdentifierMasked);
        Assert.Equal("Enemy Territory", ban.GameName);
        Assert.Equal("Alle ET-Server", ban.ScopeLabel);
        Assert.Equal("rejected", ban.Appeal!.Status);
        Assert.Null(ban.AppealInfo);
        Assert.Equal("https", ban.Url!.Scheme);
    }

    [Fact]
    public void Title_falls_back_to_the_masked_identifier()
    {
        var ban = PublicBanService.Parse(JsonNode.Parse("""{"id":"x","name":null,"identifier_masked":"7656…1234"}"""))!;
        Assert.Equal("7656…1234", ban.Title);
    }

    [Fact]
    public void Non_https_links_are_dropped()
    {
        var ban = PublicBanService.Parse(JsonNode.Parse("""{"id":"x","url":"javascript:alert(1)"}"""))!;
        Assert.Null(ban.Url);
    }

    [Fact]
    public async Task List_filters_by_status_search_and_cursor()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":[" + Ban + "],\"meta\":{\"next_cursor\":\"c2\"}}"));
        using (api)
        {
            var page = await new PublicBanService(api.Http).ListAsync(PublicBanService.Ended, " Cheat ", "c1");

            var uri = Assert.Single(stub.Requests).Uri!;
            Assert.EndsWith("/v1/bans", uri.AbsolutePath, StringComparison.Ordinal);
            var query = Uri.UnescapeDataString(uri.Query);
            Assert.Contains("filter[status]=ended", query, StringComparison.Ordinal);
            Assert.Contains("filter[search]=Cheat", query, StringComparison.Ordinal);
            Assert.Contains("cursor=c1", query, StringComparison.Ordinal);
            Assert.Single(page.Items);
            Assert.True(page.HasMore);
        }
    }

    [Fact]
    public async Task Unknown_status_falls_back_to_active()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":[],\"meta\":{\"next_cursor\":null}}"));
        using (api)
        {
            await new PublicBanService(api.Http).ListAsync("lifted");
            Assert.Contains("filter[status]=active", Uri.UnescapeDataString(Assert.Single(stub.Requests).Uri!.Query), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Detail_carries_appeal_info_and_proofs()
    {
        const string Info = """
            ,"appeal_info":{"status":"rejected","thread_url":"https://rebelsofgaming.com/de/forum/t/01JBA/einspruch","available":false,
            "blocked":"cooldown","cooldown_until":"2026-11-19T14:00:00Z","proofs":["steam"]}}
            """;
        var detail = Ban.TrimEnd()[..^1] + Info;
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":" + detail + "}"));
        using (api)
        {
            var ban = await new PublicBanService(api.Http).GetAsync(BanId);

            Assert.EndsWith("/v1/bans/" + BanId, Assert.Single(stub.Requests).Uri!.AbsolutePath, StringComparison.Ordinal);
            var info = ban.AppealInfo!;
            Assert.False(info.Available);
            Assert.Equal("cooldown", info.Blocked);
            Assert.NotNull(info.CooldownUntil);
            Assert.Equal(["steam"], info.Proofs);
            Assert.False(info.NeedsCode);
            Assert.NotNull(info.ThreadUrl);
        }
    }

    [Fact]
    public async Task Invalid_ids_never_reach_the_server()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{}"));
        using (api)
        {
            var service = new PublicBanService(api.Http);
            await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync("../game-bans"));
            await Assert.ThrowsAsync<ArgumentException>(() => service.DecideAsync("x/y", true, null));
            Assert.Empty(stub.Requests);
        }
    }

    [Fact]
    public async Task Appeal_sends_statement_and_normalized_code()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Created, "{\"data\":{\"id\":\"01JBA\",\"ban_id\":\"" + BanId + "\",\"status\":\"open\",\"proof\":\"code\",\"thread_url\":\"https://rebelsofgaming.com/de/forum/t/01JBA/x\"}}"));
        using (api)
        {
            var appeal = await new PublicBanService(api.Http).AppealAsync(BanId, "  Ich habe nicht gecheatet, das war mein Bruder.  ", " k7qx-4m2p ");

            var request = Assert.Single(stub.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith($"/v1/bans/{BanId}/appeals", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            var body = JsonNode.Parse(request.Body!)!;
            Assert.Equal("Ich habe nicht gecheatet, das war mein Bruder.", (string?)body["statement"]);
            Assert.Equal("K7QX-4M2P", (string?)body["code"]);
            Assert.True(appeal.IsOpen);
            Assert.Equal(BanId, appeal.BanId);
            Assert.NotNull(appeal.ThreadUrl);
        }
    }

    [Fact]
    public async Task Appeal_without_code_sends_no_code()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Created, "{\"data\":{\"id\":\"01JBA\",\"status\":\"open\",\"proof\":\"steam\"}}"));
        using (api)
        {
            await new PublicBanService(api.Http).AppealAsync(BanId, new string('x', 30), "  ");
            Assert.Null(JsonNode.Parse(Assert.Single(stub.Requests).Body!)!["code"]);
        }
    }

    [Theory]
    [InlineData("zu kurz", null, false, "Bans_Rule_Statement")]
    [InlineData("Das ist eine ausreichend lange Stellungnahme.", null, true, "Bans_Rule_Code")]
    [InlineData("Das ist eine ausreichend lange Stellungnahme.", "!!", false, "Bans_Rule_Code")]
    public void Appeal_rules(string statement, string? code, bool needsCode, string expected) =>
        Assert.Contains(expected, PublicBanService.ValidateAppeal(statement, code, needsCode));

    [Fact]
    public void Valid_appeal_has_no_errors()
    {
        Assert.Empty(PublicBanService.ValidateAppeal("Das ist eine ausreichend lange Stellungnahme.", "K7QX4M2P", needsCode: true));
        Assert.Empty(PublicBanService.ValidateAppeal("Das ist eine ausreichend lange Stellungnahme.", null, needsCode: false));
        Assert.NotEmpty(PublicBanService.ValidateAppeal(new string('x', PublicBanService.MaxStatement + 1), null, needsCode: false));
    }

    [Fact]
    public void Admin_appeal_is_mapped()
    {
        var appeal = PublicBanService.ParseAppeal(JsonNode.Parse(Appeal))!;

        Assert.True(appeal.IsOpen);
        Assert.True(appeal.CanDecide);
        Assert.Equal(("Cheater", "active"), (appeal.BanName, appeal.BanStatus));
        Assert.Equal(("Spieler", "spieler"), (appeal.UserName, appeal.UserSlug));
        Assert.Equal("code", appeal.Proof);
    }

    [Fact]
    public async Task Appeals_list_filters_by_status()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":[" + Appeal + "],\"meta\":{\"next_cursor\":null}}"));
        using (api)
        {
            var page = await new PublicBanService(api.Http).AppealsAsync(PublicBanService.AppealAll);

            var uri = Assert.Single(stub.Requests).Uri!;
            Assert.EndsWith("/v1/game-ban-appeals", uri.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("filter[status]=all", Uri.UnescapeDataString(uri.Query), StringComparison.Ordinal);
            Assert.Single(page.Items);
            Assert.False(page.HasMore);
        }
    }

    [Fact]
    public async Task Reject_with_reappeal_sends_flag_and_note()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":" + Appeal + "}"));
        using (api)
        {
            await new PublicBanService(api.Http).DecideAsync(AppealId, accept: false, " Beweise eindeutig ", reappealAllowed: true);

            var request = Assert.Single(stub.Requests);
            Assert.EndsWith($"/v1/game-ban-appeals/{AppealId}/decision", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            var body = JsonNode.Parse(request.Body!)!;
            Assert.Equal("rejected", (string?)body["decision"]);
            Assert.Equal("Beweise eindeutig", (string?)body["note"]);
            Assert.True((bool?)body["reappeal_allowed"]);
        }
    }

    [Fact]
    public async Task Accept_never_sends_reappeal()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":" + Appeal + "}"));
        using (api)
        {
            await new PublicBanService(api.Http).DecideAsync(AppealId, accept: true, null, reappealAllowed: true);

            var body = JsonNode.Parse(Assert.Single(stub.Requests).Body!)!;
            Assert.Equal("accepted", (string?)body["decision"]);
            Assert.Null(body["reappeal_allowed"]);
            Assert.Null(body["note"]);
        }
    }

    [Fact]
    public async Task Appeal_errors_surface_as_problems()
    {
        var (api, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Conflict, """{"status":409,"code":"appeal_cooldown"}""", "application/problem+json"));
        using (api)
        {
            var ex = await Assert.ThrowsAsync<ApiProblemException>(() => new PublicBanService(api.Http).AppealAsync(BanId, new string('x', 30), null));
            Assert.Equal("appeal_cooldown", ex.Problem.Code);
        }
    }

    [Fact]
    public void Settings_are_mapped()
    {
        var settings = PublicBanService.ParseSettings(JsonNode.Parse("""
            {"appeals_board":{"id":"7","name":"Ban-Einsprüche","public":true},"board_options":[{"id":"7","name":"Ban-Einsprüche"},{"id":"9","name":"Allgemein"}]}
            """));

        Assert.Equal(("7", "Ban-Einsprüche", true), (settings.BoardId, settings.BoardName, settings.BoardPublic));
        Assert.Equal(2, settings.BoardOptions.Count);
        Assert.Null(PublicBanService.ParseSettings(JsonNode.Parse("""{"appeals_board":null,"board_options":[]}""")).BoardId);
    }

    [Fact]
    public async Task Clearing_the_board_sends_null()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":{\"appeals_board\":null,\"board_options\":[]}}"));
        using (api)
        {
            await new PublicBanService(api.Http).SetBoardAsync(null);

            var request = Assert.Single(stub.Requests);
            Assert.Equal(HttpMethod.Patch, request.Method);
            var body = JsonNode.Parse(request.Body!)!.AsObject();
            Assert.True(body.ContainsKey("appeals_board"));
            Assert.Null(body["appeals_board"]);
        }
    }

    [Fact]
    public void Admin_ban_carries_public_flag_and_appeal_code()
    {
        var ban = GameBanService.Parse(JsonNode.Parse("""
            {"id":"01JB8Z3K6Q9X2M4N7P1R5T8V0W","status":"active","identifier":"x","is_public":false,"appeal_code":"K7QX4M2P",
             "public_url":"https://rebelsofgaming.com/de/bans/01JB8Z3K6Q9X2M4N7P1R5T8V0W"}
            """))!;
        Assert.False(ban.IsPublic);
        Assert.Equal("K7QX4M2P", ban.AppealCode);
        Assert.NotNull(ban.PublicUrl);

        // ältere Antworten ohne Feld: sichtbar
        Assert.True(GameBanService.Parse(JsonNode.Parse("""{"id":"x","identifier":"y"}"""))!.IsPublic);
    }

    [Fact]
    public async Task Hiding_sends_only_the_flag()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":{\"id\":\"" + BanId + "\",\"identifier\":\"x\",\"is_public\":false}}"));
        using (api)
        {
            var ban = await new GameBanService(api.Http).SetPublicAsync(BanId, isPublic: false);

            var request = Assert.Single(stub.Requests);
            Assert.Equal(HttpMethod.Patch, request.Method);
            var body = JsonNode.Parse(request.Body!)!.AsObject();
            Assert.Single(body);
            Assert.False((bool?)body["is_public"]);
            Assert.False(ban.IsPublic);
        }
    }
}
