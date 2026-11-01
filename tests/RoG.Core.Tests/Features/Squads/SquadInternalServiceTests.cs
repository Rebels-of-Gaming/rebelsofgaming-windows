using System.Net;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Events;
using RoG.Core.Features.Squads;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Squads;

public class SquadInternalServiceTests
{
    [Fact]
    public async Task Maps_accessible_blocks_and_leaves_the_others_empty()
    {
        const string json = """
            {"data":{
              "squad":{"id":"01S","slug":"rog-et","name":"RoG ET","url":"https://rebelsofgaming.com/squads/rog-et"},
              "forum":{"access":true,"board":{"name":"ET intern","slug":"et-intern"},"can_post":true,"url":"https://rebelsofgaming.com/forum/et-intern","new_url":null,
                "threads":[{"id":"01T","title":"Taktik","url":"https://rebelsofgaming.com/forum/t/1/taktik","author":"Wahke","replies":3,"pinned":true,"locked":false,"pending":false,"last_post_at":"2026-10-31T20:00:00Z","last_poster":"Kolibri"}]},
              "conversation":{"access":true,"id":"01C","url":null,"unread":true,"last_message":{"author":"Kolibri","excerpt":"Heute 20 Uhr?","at":"2026-10-31T19:00:00Z","mine":false}},
              "events":{"access":true,"url":"https://rebelsofgaming.com/events","items":[{"id":"01E","title":"Training","type":"training","type_label":"Training",
                "event_type":{"key":"training","system":true,"name":"Training","emoji":null,"color":"#D4AE3C","icon":null,"id":null},"color":"#D4AE3C",
                "starts_at":"2026-11-02T19:00:00Z","ends_at":"2026-11-02T21:00:00Z","mandatory":true,"location":null,"yes":5,"my_rsvp":"yes","url":"https://rebelsofgaming.com/events/01E",
                "lineup":{"published":false,"starters":0,"substitutes":0,"my_slot":null}}]},
              "downloads":{"access":false,"url":"https://rebelsofgaming.com/downloads","items":[]},
              "albums":{"access":true,"url":"https://rebelsofgaming.com/gallery","items":[{"slug":"lan","title":"LAN 2026","visibility":"squad","items":12,"cover":null,"url":"https://rebelsofgaming.com/gallery/lan"}]}},
             "meta":{"missing_scopes":["downloads:read"]}}
            """;
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, json));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);

        var result = await new SquadInternalService(api.Client).GetAsync("rog-et");

        Assert.EndsWith("/v1/squads/rog-et/intern", Assert.Single(stub.Requests).Uri!.AbsolutePath, StringComparison.Ordinal);
        var thread = Assert.Single(result.Threads!);
        Assert.Equal("Taktik", thread.Title);
        Assert.True(thread.Pinned);
        Assert.True(result.Conversation!.Unread);
        Assert.Equal("Heute 20 Uhr?", result.Conversation.Excerpt);
        var ev = Assert.Single(result.Events!);
        Assert.True(ev.Mandatory);
        Assert.Equal(Rsvp.Yes, ev.MyRsvp);
        Assert.Null(result.Downloads);
        Assert.Equal("LAN 2026", Assert.Single(result.Albums!).Title);
        Assert.Equal(["downloads:read"], result.MissingScopes);
        Assert.NotNull(result.ForumUrl);
    }
}
