using System.Net;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Account;
using RoG.Core.Features.Home;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Account;

public class AccountServiceTests
{
    // Form wie MeResource der Website (gekürzt auf die Felder, die die App nutzt)
    private const string Me = """
        {"data":{"id":"01U","slug":"wahke","name":"Wahke","email":"w@example.test","avatar_url":null,"avatar_source":"discord",
         "avatar":{"source":"discord","url":"https://i.example.test/a.png","url_small":"https://i.example.test/a-s.png","sizes":null,"processing":false},
         "banner":{"url":null,"url_small":null,"sizes":null,"width":null,"height":null,"processing":false},
         "locale":"de","timezone":"Europe/Luxembourg","theme":"dark","status":"member",
         "rank":{"id":"01R","name":"leader","label":"Clanleader","color":"#D4AE3C"},
         "two_factor_enabled":true,"joined_at":"2020-05-01","accounts":[],
         "squads":[{"id":"01S","slug":"et","name":"ET Squad","tagline":null,"description":null,"training_times":null,"recruiting_status":"open","extras":[]}],
         "permissions":{"global":["roles.view","servers.power"],"squad:01S":["squad.lineup"]},
         "token":{"name":"Windows-App – PC von Wahke","scopes":["profile:read","devices:manage","servers:*"],"expires_at":null}}}
        """;

    private sealed class Harness
    {
        public required StubHttpHandler Stub { get; init; }

        public required InMemoryTokenStore Tokens { get; init; }

        public required AccountService Service { get; init; }

        public required RoGApi Api { get; init; }
    }

    private static Harness Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond, params string[] scopes)
    {
        var tokens = new InMemoryTokenStore();
        tokens.SetAsync(new StoredToken("42|tok", "01U", "Wahke", scopes.Length == 0 ? ["devices:manage"] : scopes, "PC", DateTimeOffset.UtcNow)).Wait();
        var stub = new StubHttpHandler(respond);
        var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), tokens, new ApiContext(), innerHandler: stub);
        return new Harness { Stub = stub, Tokens = tokens, Api = api, Service = new AccountService(api.Client, tokens) };
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => StubHttpHandler.Json(status, json);

    [Fact]
    public async Task Maps_me_with_permissions_and_scopes()
    {
        var h = Create((_, _) => Json(Me));

        var me = await h.Service.GetMeAsync();

        Assert.Equal("Wahke", me.Name);
        Assert.Equal("Clanleader", me.RankLabel);
        Assert.Equal("https://i.example.test/a-s.png", me.AvatarUrl);
        Assert.Equal(["ET Squad"], me.SquadNames);
        Assert.True(me.Can("servers.power"));
        Assert.False(me.Can("squad.lineup"));
        Assert.True(me.Can("squad.lineup", "squad:01S"));
        Assert.True(me.CanAnywhere("squad.lineup"));
        Assert.True(me.HasScope("servers:power"));
        Assert.False(me.HasScope("roles:manage"));
    }

    [Theory]
    [InlineData(new[] { "*" }, "roles:manage", true)]
    [InlineData(new[] { "roles:*" }, "roles:manage", true)]
    [InlineData(new[] { "roles:*" }, "rolesx:manage", false)]
    [InlineData(new[] { "profile:read" }, "profile:write", false)]
    public void Scope_wildcards(string[] granted, string scope, bool expected) =>
        Assert.Equal(expected, ScopeMatcher.Allows(granted, scope));

    [Fact]
    public async Task Registers_the_device_as_windows_with_lowercase_platform()
    {
        var h = Create((_, _) => Json("""{"data":{"id":"01D","name":"PC","platform":"windows","app_version":"2.0.1","push":false,"push_provider":null,"scopes":[],"current":true,"last_active_at":null,"created_at":null}}""", HttpStatusCode.Created));

        Assert.True(await h.Service.RegisterDeviceAsync("2.0.1"));

        var req = Assert.Single(h.Stub.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/v1/devices", req.Uri!.AbsolutePath, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(req.Body!);
        Assert.Equal("windows", body.RootElement.GetProperty("platform").GetString());
        Assert.Equal("2.0.1", body.RootElement.GetProperty("app_version").GetString());
    }

    [Fact]
    public async Task Skips_device_registration_without_scope()
    {
        var h = Create((_, _) => Json("{}"), "profile:read");

        Assert.False(await h.Service.RegisterDeviceAsync("2.0.1"));
        Assert.Empty(h.Stub.Requests);
    }

    [Fact]
    public async Task Device_registration_errors_do_not_throw()
    {
        var h = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Forbidden, """{"code":"insufficient_scope","status":403}""", "application/problem+json"));

        Assert.False(await h.Service.RegisterDeviceAsync("2.0.1"));
    }

    [Fact]
    public async Task Logout_revokes_and_clears_the_token()
    {
        var h = Create((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));

        Assert.True(await h.Service.LogoutAsync());

        Assert.EndsWith("/v1/auth/logout", Assert.Single(h.Stub.Requests).Uri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Null(await h.Tokens.GetAsync());
    }

    [Fact]
    public async Task Logout_clears_the_token_even_offline()
    {
        var h = Create((_, _) => throw new HttpRequestException("offline"));

        Assert.False(await h.Service.LogoutAsync());
        Assert.Null(await h.Tokens.GetAsync());
    }

    [Fact]
    public async Task Reads_platform_version()
    {
        var h = Create((_, _) => Json("""{"data":{"name":"Rebels of Gaming","version":"v1","app":{"version":"0.29.0","commit":"abc123","deployed_at":null},"time":"2026-10-16T12:00:00Z","realtime":null,"apps":{"app_store_url":null,"play_store_url":null}},"links":{}}"""));

        var platform = await h.Service.GetPlatformAsync();

        Assert.Equal("0.29.0", platform.Version);
        Assert.Equal("abc123", platform.Commit);
    }

    [Fact]
    public async Task Reads_the_live_ticker()
    {
        var h = Create((_, _) => Json("""{"data":[{"key":"online","label":"Online","icon":"activity","tone":"green","value":12,"hint":null},{"key":"server","label":"Auf Servern","icon":"server","tone":"red","value":3,"hint":"ET #1"},{"key":"next_event","label":"Nächstes Event","icon":"calendar","tone":"gold","value":"Sa., 18.10. 20:00","hint":"Clanwar"},{"label":"ohne Schlüssel","value":1}],"meta":{"updated_at":"2026-10-16T12:00:00Z","ttl":30}}"""));

        var stats = await new LiveTickerService(h.Api.Http).GetAsync();

        // Zahl und fertiger Text (Guide 08) – unvollständige Einträge fallen weg
        Assert.Equal(["online", "server", "next_event"], stats.Select(s => s.Key));
        Assert.Equal(12.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), stats[0].Value);
        Assert.Null(stats[0].Hint);
        Assert.Equal("ET #1", stats[1].Hint);
        Assert.Equal("Sa., 18.10. 20:00", stats[2].Value);
    }
}
