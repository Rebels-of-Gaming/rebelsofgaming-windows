using System.Net;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Voice;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Voice;

public class VoiceServiceTests
{
    [Theory]
    [InlineData("ts.rogclan.de:9987", "Wahke", "ts3server://ts.rogclan.de/?port=9987&nickname=Wahke")]
    [InlineData("ts.rogclan.de", "Rebel Name", "ts3server://ts.rogclan.de/?nickname=Rebel%20Name")]
    [InlineData("1.2.3.4:9988", null, "ts3server://1.2.3.4/?port=9988")]
    public void Builds_teamspeak_links(string address, string? nickname, string expected) =>
        Assert.Equal(expected, VoiceService.TeamSpeakLink(address, nickname)!.AbsoluteUri);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad host:9987")]
    public void No_link_without_valid_host(string? address) => Assert.Null(VoiceService.TeamSpeakLink(address, "x"));

    [Fact]
    public async Task Reads_nested_channels()
    {
        const string json = """
            {"data":{"online":true,"server":{"name":"RoG TS","clients_online":3,"max_clients":64,"address":"ts.rogclan.de:9987"},
             "channels":[{"id":1,"name":"Lobby","spacer":false,"default":true,
               "clients":[{"nickname":"Wahke","away":false,"muted":true,"member":{"slug":"wahke","name":"Wahke"}}],
               "children":[{"id":2,"name":"ET","spacer":false,"default":false,"clients":[{"nickname":"Gast","away":true,"muted":false,"member":null}],"children":[]}]},
              {"id":3,"name":"[spacer]","spacer":true,"default":false,"clients":[],"children":[]}],
             "fetched_at":"2026-10-16T12:00:00Z"}}
            """;
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, json));
        var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);

        var ts = await new VoiceService(api.Client).GetTeamSpeakAsync();

        Assert.True(ts.Online);
        Assert.Equal("ts.rogclan.de:9987", ts.Address);
        var lobby = ts.Channels[0];
        Assert.Equal("Wahke", lobby.Clients[0].MemberName);
        Assert.True(lobby.Clients[0].Muted);
        var et = Assert.Single(lobby.Children);
        Assert.Equal("ET", et.Name);
        Assert.True(et.Clients[0].Away);
        Assert.True(lobby.HasPeople);
        Assert.False(ts.Channels[1].HasPeople);
    }
}
