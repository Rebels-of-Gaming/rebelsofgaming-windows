using System.Net;
using System.Text.Json.Nodes;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Minecraft;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Minecraft;

public class MinecraftGroupsTests
{
    private const string RoleId = "01JB8Z3K6Q9X2M4N7P1R5T8V0W";

    // Form wie in Guide 44 (v0.37.0)
    private const string Overview = """
        {"data":[{"role":{"id":"01JB8Z3K6Q9X2M4N7P1R5T8V0W","name":"member","label":"Member"},"group":"rog-member","priority":10,"updated_at":"2026-10-21T12:00:00Z"},
                 {"role":{"id":"01JB8Z3K6Q9X2M4N7P1R5T8V0X","name":"vip","label":"VIP"},"group":"rog-vip","priority":50,"updated_at":null},
                 {"role":{"id":"01JB8Z3K6Q9X2M4N7P1R5T8V0Y"},"group":null}],
         "meta":{"managed_groups":["rog-vip","rog-member","rog-alt"],
                 "retired_groups":[{"group":"rog-alt","retired_at":"2026-10-21T12:00:00Z","releasable_at":"2026-11-20T12:00:00Z"}]}}
        """;

    private static (RoGApi Api, StubHttpHandler Stub) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        return (new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub), stub);
    }

    [Fact]
    public void Overview_is_parsed_highest_priority_first()
    {
        var overview = MinecraftService.ParseGroupOverview(JsonNode.Parse(Overview));

        Assert.Equal(["rog-vip", "rog-member"], overview.Mappings.Select(m => m.Group));
        Assert.Equal("VIP", overview.Mappings[0].RoleLabel);
        Assert.Equal(10, overview.Mappings[1].Priority);
        Assert.Equal(["rog-vip", "rog-member", "rog-alt"], overview.ManagedGroups);
        var retired = Assert.Single(overview.RetiredGroups);
        Assert.Equal("rog-alt", retired.Group);
        Assert.False(retired.IsReleasable(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.True(retired.IsReleasable(new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Empty_or_broken_answers_give_an_empty_overview()
    {
        var overview = MinecraftService.ParseGroupOverview(JsonNode.Parse("""{"data":{},"meta":{"managed_groups":[1,"x"]}}"""));

        Assert.Empty(overview.Mappings);
        Assert.Equal(["x"], overview.ManagedGroups);
        Assert.Empty(overview.RetiredGroups);
    }

    [Theory]
    [InlineData("rog-member", true)]
    [InlineData(" ROG_Member ", true)]
    [InlineData("a", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("rog member", false)]
    [InlineData("rog.member", false)]
    [InlineData("abcdefghijabcdefghijabcdefghijabcdefg", false)]
    public void Group_names_follow_luckperms(string? group, bool valid) => Assert.Equal(valid, MinecraftService.IsValidGroup(group));

    [Fact]
    public async Task Set_group_puts_normalized_name_and_clamped_priority()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK,
            """{"data":{"role":{"id":"01JB8Z3K6Q9X2M4N7P1R5T8V0W","label":"Member"},"group":"rog-member","priority":1000}}"""));
        using (api)
        {
            var mapping = await new MinecraftService(api.Http).SetGroupAsync(RoleId, " Rog-Member ", 5000);

            var request = Assert.Single(stub.Requests);
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.EndsWith("/v1/minecraft/group-mappings/" + RoleId, request.Uri!.AbsolutePath, StringComparison.Ordinal);
            var body = JsonNode.Parse(request.Body!)!;
            Assert.Equal("rog-member", (string?)body["group"]);
            Assert.Equal(1000, (int)body["priority"]!);
            Assert.Equal("Member", mapping!.RoleLabel);
        }
    }

    [Fact]
    public async Task Invalid_input_is_not_sent()
    {
        var (api, stub) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        using (api)
        {
            var service = new MinecraftService(api.Http);
            await Assert.ThrowsAsync<ArgumentException>(() => service.SetGroupAsync(RoleId, "rog member", 0));
            await Assert.ThrowsAsync<ArgumentException>(() => service.SetGroupAsync("../x", "rog", 0));
            await Assert.ThrowsAsync<ArgumentException>(() => service.ReleaseGroupAsync("../admin"));
            Assert.Empty(stub.Requests);
        }
    }

    [Fact]
    public async Task Remove_and_release_use_delete()
    {
        var (api, stub) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        using (api)
        {
            var service = new MinecraftService(api.Http);
            await service.RemoveGroupAsync(RoleId);
            await service.ReleaseGroupAsync("Rog-Alt");

            Assert.All(stub.Requests, r => Assert.Equal(HttpMethod.Delete, r.Method));
            Assert.EndsWith("/v1/minecraft/group-mappings/" + RoleId, stub.Requests[0].Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.EndsWith("/v1/minecraft/retired-groups/rog-alt", stub.Requests[1].Uri!.AbsolutePath, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Forbidden_is_reported_as_api_problem()
    {
        var (api, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Conflict,
            """{"type":"about:blank","title":"Conflict","status":409,"code":"group_release_too_early"}""", "application/problem+json"));
        using (api)
        {
            var ex = await Assert.ThrowsAsync<ApiProblemException>(() => new MinecraftService(api.Http).ReleaseGroupAsync("rog-alt"));
            Assert.Equal("group_release_too_early", ex.Problem.Code);
        }
    }
}
