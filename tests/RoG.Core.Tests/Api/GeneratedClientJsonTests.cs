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
            await api.Client.V1_me_profile_updateAsync(new Body98 { Motto = "GG", Show_on_map = false });
        }
        catch (GeneratedApiException)
        {
            // Antwortform egal – geprüft wird die Anfrage
        }

        using var body = JsonDocument.Parse(stub.Requests[0].Body!);
        var names = body.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["motto", "show_on_map"], names);
    }

    [Fact]
    public async Task Profile_with_empty_php_arrays_for_objects_is_read()
    {
        // PHP liefert leere Objekte als [] – so scheiterte jedes Profil ohne verknüpfte Konten (2.4.8)
        const string json = """
            {"data":{"id":"01m4","slug":"test","name":"Test","avatar_url":null,"rank":{"id":"r1","name":"owner","label":"Owner","color":"#D4AE3C"},
            "status":null,"activity":null,"last_seen_at":null,"joined_at":"2026-10-13T12:56:55Z","squads":[],"roles":[],"badges":[],
            "profile":{"bio":"Bio","motto":null,"banner":{"url":null,"url_small":null,"sizes":null,"width":null,"height":null,"processing":false},
            "city":null,"country":null,"birthday":null,"hardware":{},"socials":{},"accounts":[],"game_accounts":[],"platform_ids":[],"stats":null}}}
            """;
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, json));
        var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);

        var profile = await new RoG.Core.Features.Members.MemberService(api.Client).GetAsync("test");

        Assert.Equal("Bio", profile.Bio);
    }
}
