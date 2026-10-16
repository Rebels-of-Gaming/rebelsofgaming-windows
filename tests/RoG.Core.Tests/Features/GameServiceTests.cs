using System.Net;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Games;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features;

public class GameServiceTests
{
    [Fact]
    public async Task Active_games_with_safe_links_only()
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """
            {"data":[
              {"id":"1","slug":"et","name":"Enemy Territory","short":"ET","platform":"pc","active":true,"genre":"shooter","color":null,"emoji":"🎯",
               "channel_prefix":"et","header":null,"store_links":null,"stats_source":"none","stats_provider":null,"stats_config":null,"clan_tag":"RoG|",
               "clan_name":null,"id_format":null,"has_servers":true,
               "links":[{"id":"l1","type":"store","label":"ET: Legacy","url":"https://etlegacy.com","short_url":null,"code":null,"squad":null},
                        {"id":"l2","type":"ref","label":"Kaputt","url":"javascript:alert(1)","short_url":null,"code":null,"squad":null}]},
              {"id":"2","slug":"old","name":"Alt","short":"A","platform":"pc","active":false,"genre":"x","color":null,"emoji":"","channel_prefix":"a",
               "header":null,"store_links":null,"stats_source":"none","stats_provider":null,"stats_config":null,"clan_tag":null,"clan_name":null,
               "id_format":null,"has_servers":false,"links":null}]}
            """));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);

        var games = await new GameService(api.Client).ListAsync();

        var et = Assert.Single(games);
        Assert.Equal("RoG|", et.ClanTag);
        Assert.Equal("ET: Legacy", Assert.Single(et.Links).Label);
    }
}
