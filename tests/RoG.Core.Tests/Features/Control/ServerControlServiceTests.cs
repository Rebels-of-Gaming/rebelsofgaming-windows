using System.Net;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Control;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Control;

public class ServerControlServiceTests
{
    [Theory]
    [InlineData("read", ConsoleLevel.Read)]
    [InlineData("command", ConsoleLevel.Command)]
    [InlineData("power", ConsoleLevel.Power)]
    public async Task Console_ticket_maps_level(string level, ConsoleLevel expected)
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK,
            $$$"""{"data":{"token":"jwt","socket":"wss://node.example/api/servers/abc/ws","level":"{{{level}}}","demo":false,"origin":"https://rebelsofgaming.com"}}"""));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);

        var ticket = await new ServerControlService(api.Client).GetConsoleTicketAsync("sven");

        Assert.Equal(expected, ticket.Level);
        Assert.Equal("jwt", ticket.Token);
        Assert.Equal("wss", ticket.Socket.Scheme);
        Assert.Equal("https://rebelsofgaming.com", ticket.Origin);
        Assert.False(ticket.Demo);
    }
}
