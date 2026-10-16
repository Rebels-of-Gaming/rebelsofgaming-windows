using System.Net;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Servers;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Servers;

public class ServerServiceTests
{
    // Form wie ServerResource der Website (app/Modules/Servers/Http/Api/V1/Resources/ServerResource.php)
    private const string Et = """
        {"id":"01HET","slug":"et-main","name":"RoG ET #1","description":null,
         "game":{"id":"01G","name":"Enemy Territory","short":"ET"},"squad":null,
         "address":"et.rogclan.de:27960","query_type":"quake3","connect_url":"et://et.rogclan.de:27960",
         "visibility":"public","last_online_at":"2026-10-16T10:00:00+00:00",
         "live":{"online":true,"name":"^1RoG","map":"supply","players":12,"max_players":24,"version":"2.82",
                 "passworded":false,"latency":21,"checked_at":"2026-10-16T10:00:00+00:00",
                 "player_list":[{"name":"Wahke","score":40,"ping":20,"time":600,"member":{"name":"Wahke","slug":"wahke","verified":true}},
                                {"name":"Gast","score":null,"ping":null,"time":null,"member":null}]},
         "pterodactyl_linked":false,"control":null}
        """;

    private const string Minecraft = """
        {"id":"01HMC","slug":"mc","name":"RoG Minecraft","description":null,"game":null,"squad":null,
         "address":"mc.rogclan.de:25565","query_type":"minecraft","connect_url":null,"visibility":"public",
         "last_online_at":null,
         "live":{"online":null,"name":null,"map":"","players":0,"max_players":0,"version":null,"passworded":false,
                 "latency":null,"checked_at":null,"player_list":null},
         "pterodactyl_linked":false,"control":null}
        """;

    private static (ServerService Service, StubHttpHandler Stub) Create(params string[] pages)
    {
        var stub = new StubHttpHandler((_, i) => StubHttpHandler.Json(HttpStatusCode.OK, pages[i]));
        var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);
        return (new ServerService(api.Client), stub);
    }

    private static string Page(string? nextCursor, params string[] servers) =>
        "{\"data\":[" + string.Join(',', servers) + "],"
        + "\"links\":{\"first\":null,\"last\":null,\"prev\":null,\"next\":null},"
        + "\"meta\":{\"path\":\"x\",\"per_page\":50,\"next_cursor\":" + (nextCursor is null ? "null" : $"\"{nextCursor}\"") + ",\"prev_cursor\":null}}";

    [Fact]
    public async Task Maps_servers_with_live_state_and_players()
    {
        var (service, stub) = Create(Page(null, Et, Minecraft));

        var servers = await service.GetAllAsync();

        Assert.Equal(2, servers.Count);
        var et = servers[0];
        Assert.Equal("RoG ET #1", et.Name);
        Assert.Equal("ET", et.GameShort);
        Assert.Equal(ServerQueryType.Quake3, et.QueryType);
        Assert.Equal("et://et.rogclan.de:27960", et.ConnectUrl);
        Assert.True(et.Online);
        Assert.Equal("supply", et.Map);
        Assert.Equal((12, 24), (et.Players, et.MaxPlayers));
        Assert.Equal(["Wahke", null], et.PlayerList!.Select(p => p.MemberName));

        var mc = servers[1];
        Assert.Equal(ServerQueryType.Minecraft, mc.QueryType);
        Assert.Null(mc.ConnectUrl);
        Assert.Null(mc.Online);
        Assert.Null(mc.Map);
        Assert.Null(mc.PlayerList);

        Assert.Contains("servers", stub.Requests[0].Uri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("per_page=50", stub.Requests[0].Uri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Follows_the_cursor_until_the_last_page()
    {
        var (service, stub) = Create(Page("abc", Et), Page(null, Minecraft));

        var servers = await service.GetAllAsync();

        Assert.Equal(["et-main", "mc"], servers.Select(s => s.Slug));
        Assert.Equal(2, stub.Requests.Count);
        Assert.Contains("cursor=abc", stub.Requests[1].Uri!.Query, StringComparison.Ordinal);
    }
}

public class ServerControlMappingTests
{
    [Fact]
    public async Task Control_abilities_are_mapped_and_null_without_rights()
    {
        const string withControl = """
            {"id":"01HET","slug":"et-main","name":"RoG ET #1","description":null,"game":null,"squad":null,
             "address":"et.rogclan.de:27960","query_type":"quake3","connect_url":null,"visibility":"public","last_online_at":null,
             "live":null,"pterodactyl_linked":true,
             "control":{"config_approval":true,"can_command":false,
               "abilities":{"power":true,"console":true,"console_command":true,"files_read":false,"files_write":false,
                            "files_approve":false,"backups":false,"rcon":false,"bans":false}}}
            """;
        var page = "{\"data\":[" + withControl + "],\"links\":{\"first\":null,\"last\":null,\"prev\":null,\"next\":null},"
            + "\"meta\":{\"path\":\"x\",\"per_page\":50,\"next_cursor\":null,\"prev_cursor\":null}}";
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, page));
        var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);

        var server = Assert.Single(await new ServerService(api.Client).GetAllAsync());

        Assert.Equal(new ServerAbilities(Power: true, Console: true, ConsoleCommand: true, CanCommand: false, ConfigApproval: true), server.Control);
        Assert.True(server.Control!.Any);
    }
}

public class ServerHistoryTests
{
    [Fact]
    public void Polyline_scales_to_slots_and_width()
    {
        var t = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        var history = new ServerHistory("24h", 30, 99.5,
        [
            new ServerLoadPoint(t, 0, 20),
            new ServerLoadPoint(t.AddMinutes(30), 10, 20),
            new ServerLoadPoint(t.AddMinutes(60), 20, 20),
        ]);

        var line = history.Polyline(200, 50);

        Assert.Equal([(0d, 50d), (100d, 25d), (200d, 0d)], line);
        Assert.Equal(20, history.Peak);
        Assert.Empty(new ServerHistory("24h", 30, null, []).Polyline(200, 50));
    }
}
