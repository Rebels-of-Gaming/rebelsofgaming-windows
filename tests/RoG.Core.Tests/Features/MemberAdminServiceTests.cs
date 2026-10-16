using System.Net;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Admin;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features;

public class MemberAdminServiceTests
{
    [Fact]
    public async Task Assign_in_squad_sends_scope_and_list_requests_squads()
    {
        var stub = new StubHttpHandler((request, _) => request.Method == HttpMethod.Get
            ? StubHttpHandler.Json(HttpStatusCode.OK, """
                {"data":[{"id":"u1","slug":"nyx","name":"Nyx","avatar_url":null,"rank":{"id":"r","name":"member","label":"Member","color":"#fff"},
                  "status":null,"activity":null,"last_seen_at":null,"joined_at":null,"squads":[],"roles":null}],
                 "links":{"first":null,"last":null,"prev":null,"next":null},"meta":{"path":"x","per_page":50,"next_cursor":null,"prev_cursor":null}}
                """)
            : StubHttpHandler.Json(HttpStatusCode.Created, """{"data":{}}"""));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);
        var service = new MemberAdminService(api.Client);

        var page = await service.ListAsync("ny", "active", null);
        await service.AssignAsync("nyx", "role1", "squad1", null);

        Assert.Equal("Member", Assert.Single(page.Items).Rank);
        Assert.Contains("include=squads", stub.Requests[0].Uri!.Query, StringComparison.Ordinal);
        Assert.Contains("\"scope_type\":\"squad\"", stub.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("\"scope_id\":\"squad1\"", stub.Requests[1].Body, StringComparison.Ordinal);
    }
}
