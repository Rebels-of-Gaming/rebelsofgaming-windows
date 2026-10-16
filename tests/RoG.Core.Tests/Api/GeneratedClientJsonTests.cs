using System.Net;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Api;

public class GeneratedClientJsonTests
{
    [Fact]
    public async Task Patch_sends_only_the_fields_that_are_set()
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """{"data":{}}"""));
        var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);

        try
        {
            await api.Client.V1_me_profile_updateAsync(new Body87 { Motto = "GG", Show_on_map = false });
        }
        catch (GeneratedApiException)
        {
            // Antwortform egal – geprüft wird die Anfrage
        }

        using var body = JsonDocument.Parse(stub.Requests[0].Body!);
        var names = body.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["motto", "show_on_map"], names);
    }
}
