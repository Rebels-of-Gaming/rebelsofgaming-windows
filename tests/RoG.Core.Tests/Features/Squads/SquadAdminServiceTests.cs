using System.Net;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Account;
using RoG.Core.Features.Squads;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Squads;

public class SquadAdminServiceTests
{
    private static readonly SquadDetail s_current = new(
        new SquadSummary("01S", "rog-et", "RoG ET", "Old school", "Enemy Territory", "ET", Recruiting.Open, 12, null, null, "Kolibri", "RoG"),
        "Beschreibung",
        "Mi 20 Uhr",
        [],
        null,
        null,
        null,
        Archived: false);

    [Fact]
    public void Patch_contains_only_changed_fields_in_app_language()
    {
        var body = SquadAdminService.BuildPatch(s_current, new SquadEdit(Tagline: "Old school ", Description: "Neu", Recruiting: "full", ClanTag: "RoG", ClanLevel: 12000), "de");

        Assert.Equal(["description", "recruiting_status", "clan_level"], body.Keys);
        Assert.Equal("Neu", ((Dictionary<string, string>)body["description"]!)["de"]);
        Assert.Equal(9999, body["clan_level"]);
        Assert.Empty(SquadAdminService.BuildPatch(s_current, new SquadEdit(), "de"));
    }

    [Fact]
    public async Task Update_without_changes_sends_nothing()
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """{"data":{}}"""));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);
        var service = new SquadAdminService(api.Http);

        Assert.False(await service.UpdateAsync(s_current, new SquadEdit(Tagline: "Old school"), "de"));
        Assert.True(await service.UpdateAsync(s_current, new SquadEdit(Tagline: "Neu"), "en"));

        var request = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Contains("\"tagline\":{\"en\":\"Neu\"}", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Member_role_is_put_with_substitute_flag()
    {
        var stub = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);

        await new SquadAdminService(api.Http).PutMemberAsync("rog-et", "kolibri", "01R", substitute: true);

        var request = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.EndsWith("/v1/squads/rog-et/members/kolibri", request.Uri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("\"is_substitute\":true", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Roles_are_parsed_with_assignable_flag()
    {
        using var doc = JsonDocument.Parse("""{"data":[{"id":"1","name":"squad-leader","label":"Leader","color":"#D4AE3C","assignable":false},{"id":"2","name":"member","label":"Member","color":null,"assignable":true}]}""");

        var roles = SquadAdminService.ParseRoles(doc.RootElement);

        Assert.Equal(["Leader", "Member"], roles.Select(r => r.Label));
        Assert.False(roles[0].Assignable);
        Assert.True(roles[1].Assignable);
    }

    [Fact]
    public void Rights_come_from_squad_scope_and_token_scope()
    {
        UserProfile Me(Dictionary<string, IReadOnlyList<string>> permissions, IReadOnlyList<string>? scopes) =>
            new("u", "u", "U", null, null, null, null, "active", true, null, [], permissions, scopes);

        var leader = Me(new() { ["squad:01S"] = ["squads.manage", "squad.members"] }, ["squads:write"]);
        Assert.Equal(new SquadAdminRights(true, true, false), SquadAdminRights.For(leader, "01S"));
        Assert.False(SquadAdminRights.For(leader, "02X").Any);
        Assert.False(SquadAdminRights.For(Me(new() { ["global"] = ["squads.manage"] }, ["squads:read"]), "01S").Any);
        Assert.True(SquadAdminRights.For(Me(new() { ["global"] = ["squads.manage"] }, null), "01S").Archive);
    }
}
