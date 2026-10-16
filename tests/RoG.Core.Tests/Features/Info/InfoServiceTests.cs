using System.Net;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Info;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Info;

public class InfoServiceTests
{
    private static InfoService Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler((r, _) => respond(r));
        var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);
        return new InfoService(api.Client, new Uri("https://rebelsofgaming.com/"));
    }

    private static HttpResponseMessage Json(string json) => StubHttpHandler.Json(HttpStatusCode.OK, json);

    [Fact]
    public async Task One_failing_area_does_not_hide_the_others()
    {
        var service = Create(r => r.RequestUri!.AbsolutePath switch
        {
            var p when p.EndsWith("/rules", StringComparison.Ordinal) => StubHttpHandler.Json(HttpStatusCode.Forbidden, """{"code":"forbidden","status":403}""", "application/problem+json"),
            var p when p.EndsWith("/pages", StringComparison.Ordinal) => Json("""{"data":[{"id":"1","slug":"about","url":"https://rebelsofgaming.com/de/about","title":"Über uns","status":"published","visibility":"public","show_in_footer":true,"sort":2,"updated_at":null},{"id":"2","slug":"partner","url":"x","title":"Partner","status":"published","visibility":"public","show_in_footer":true,"sort":1,"updated_at":null}]}"""),
            _ => Json("""{"data":[{"type":"privacy","title":"Datenschutz","version":3,"published_at":null,"requires_acceptance":true,"acceptable":true,"url":"https://rebelsofgaming.com/de/privacy"}]}"""),
        });

        var entries = await service.ListAsync();

        Assert.Equal(["Partner", "Über uns", "Datenschutz"], entries.Select(e => e.Title));
        Assert.True(entries[2].NeedsAcceptance);
    }

    [Fact]
    public async Task Rules_sections_are_ordered_and_rendered()
    {
        var service = Create(_ => Json("""
            {"data":{"id":"1","slug":"clan","scope":"clan","game_id":null,"squad_id":null,"title":"Clanregeln","intro":"Kurz und fair.",
             "visibility":"public","requires_acceptance":true,"enforced":true,"wiki_page":null,"version":4,"accepted":false,"updated_at":"2026-10-01T00:00:00Z",
             "sections":[{"number":2,"anchor":"b","title":"Respekt","body":"","html":"<p>Kein Flame.</p>","important":true},
                         {"number":1,"anchor":"a","title":"Fair Play","body":"","html":"<p>Keine Cheats.</p>","important":false}]}}
            """));

        var doc = await service.GetAsync(new InfoEntry(InfoKind.Rules, "clan", "Clanregeln", null, true));

        Assert.Equal(["§ 1 Fair Play", "§ 2 Respekt"], doc.Sections.Select(s => s.Title));
        Assert.True(doc.Sections[1].Important);
        Assert.Equal("Keine Cheats.", doc.Sections[0].Content.ToPlainText());
        Assert.True(doc.CanAccept);
        Assert.Equal("v4", doc.Meta);
    }
}
