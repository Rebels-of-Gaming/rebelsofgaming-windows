using System.Net;
using System.Text.Json.Nodes;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Friends;
using RoG.Core.Features.Members;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Friends;

public class FriendsServiceTests
{
    private const string RequestId = "01JBA2M4K6P8R0T2V4X6Z8B0D2";

    // Formen wie in Guide 45 (v0.38.0)
    private const string User = """{"id":"01JB8Y3Q2X5V7N9R4T6W8Z0C2D","slug":"nyx","name":"Nyx","avatar_url":"https://cdn.rogclan.de/avatars/x/128.webp"}""";

    private const string Friends = """
        {"data":[{"user":{"id":"01JB8Y3Q2X5V7N9R4T6W8Z0C2D","slug":"nyx","name":"Nyx","avatar_url":"https://cdn.rogclan.de/avatars/x/128.webp"},"status":"ingame","since":"2026-10-22T18:04:11Z"},
                 {"user":{"id":"01JB8Y3Q2X5V7N9R4T6W8Z0C2E","slug":"hidden","name":"Hidden","avatar_url":null},"status":null,"since":null},
                 {"user":null,"status":"online"}],
         "meta":{"next_cursor":"c2","per_page":25,"total":2,"limit":100}}
        """;

    private static (RoGApi Api, StubHttpHandler Stub) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        return (new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub), stub);
    }

    [Fact]
    public async Task Friends_are_parsed_with_presence_total_and_limit()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, Friends));
        using (api)
        {
            var page = await new FriendsService(api.Http).FriendsAsync("c1");

            var uri = Assert.Single(stub.Requests).Uri!;
            Assert.EndsWith("/v1/me/friends", uri.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("cursor=c1", uri.Query, StringComparison.Ordinal);
            Assert.Equal(2, page.Items.Count);
            Assert.Equal(Presence.Ingame, page.Items[0].Status);
            Assert.Equal("nyx", page.Items[0].User.Slug);
            Assert.NotNull(page.Items[0].User.AvatarUrl);
            Assert.Null(page.Items[1].Status);
            Assert.Equal(2, page.Total);
            Assert.Equal(100, page.Limit);
            Assert.True(page.HasMore);
        }
    }

    [Fact]
    public async Task Requests_filter_by_direction()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK,
            "{\"data\":[{\"id\":\"" + RequestId + "\",\"direction\":\"outgoing\",\"status\":\"pending\",\"user\":" + User + ",\"created_at\":\"2026-10-22T17:58:00Z\"}],\"meta\":{\"next_cursor\":null}}"));
        using (api)
        {
            var service = new FriendsService(api.Http);
            var page = await service.RequestsAsync(FriendsService.Outgoing);
            await service.RequestsAsync("sideways");

            Assert.Contains("filter[direction]=outgoing", Uri.UnescapeDataString(stub.Requests[0].Uri!.Query), StringComparison.Ordinal);
            Assert.DoesNotContain("direction", stub.Requests[1].Uri!.Query, StringComparison.Ordinal);
            var request = Assert.Single(page.Items);
            Assert.False(request.Incoming);
            Assert.False(page.HasMore);
        }
    }

    [Fact]
    public async Task Send_posts_the_slug_and_reports_a_counter_request_as_accepted()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Created,
            "{\"data\":{\"id\":\"" + RequestId + "\",\"direction\":\"incoming\",\"status\":\"accepted\",\"user\":" + User + "}}"));
        using (api)
        {
            var result = await new FriendsService(api.Http).SendAsync(" @nyx ");

            var request = Assert.Single(stub.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/v1/me/friends/requests", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("nyx", (string?)JsonNode.Parse(request.Body!)!["user"]);
            Assert.True(result.IsAccepted);
            Assert.True(result.Incoming);
        }
    }

    [Fact]
    public async Task Accept_decline_cancel_and_remove_use_the_documented_paths()
    {
        var (api, stub) = Create((r, _) => r.RequestUri!.AbsolutePath.EndsWith("/accept", StringComparison.Ordinal)
            ? StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":{\"user\":" + User + ",\"status\":\"online\",\"since\":\"2026-10-22T18:04:11Z\"}}")
            : new HttpResponseMessage(HttpStatusCode.NoContent));
        using (api)
        {
            var service = new FriendsService(api.Http);
            var friend = await service.AcceptAsync(RequestId);
            await service.DeclineAsync(RequestId);
            await service.CancelAsync(RequestId);
            await service.RemoveAsync("nyx");

            Assert.Equal(Presence.Online, friend.Status);
            Assert.Equal((HttpMethod.Post, "/v1/me/friends/requests/" + RequestId + "/accept"), (stub.Requests[0].Method, stub.Requests[0].Uri!.AbsolutePath));
            Assert.Equal((HttpMethod.Post, "/v1/me/friends/requests/" + RequestId + "/decline"), (stub.Requests[1].Method, stub.Requests[1].Uri!.AbsolutePath));
            Assert.Equal((HttpMethod.Delete, "/v1/me/friends/requests/" + RequestId), (stub.Requests[2].Method, stub.Requests[2].Uri!.AbsolutePath));
            Assert.Equal((HttpMethod.Delete, "/v1/me/friends/nyx"), (stub.Requests[3].Method, stub.Requests[3].Uri!.AbsolutePath));
        }
    }

    [Theory]
    [InlineData("nyx", "nyx")]
    [InlineData("  @Nyx_1 ", "Nyx_1")]
    [InlineData("https://rebelsofgaming.com/de/u/nyx", "nyx")]
    [InlineData("01JB8Y3Q2X5V7N9R4T6W8Z0C2D", "01JB8Y3Q2X5V7N9R4T6W8Z0C2D")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("nyx/../admin", null)]
    [InlineData("two words", null)]
    [InlineData("https://rebelsofgaming.com/de/news/x", null)]
    public void User_input_is_normalized(string? input, string? expected) => Assert.Equal(expected, FriendsService.NormalizeUser(input));

    [Fact]
    public async Task Invalid_input_is_not_sent()
    {
        var (api, stub) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        using (api)
        {
            var service = new FriendsService(api.Http);
            await Assert.ThrowsAsync<ArgumentException>(() => service.SendAsync("a b"));
            await Assert.ThrowsAsync<ArgumentException>(() => service.AcceptAsync("../x"));
            await Assert.ThrowsAsync<ArgumentException>(() => service.RemoveAsync(""));
            Assert.Empty(stub.Requests);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "request_cooldown")]
    [InlineData(HttpStatusCode.Forbidden, "blocked")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "friend_limit_reached")]
    public async Task Errors_surface_as_problems(HttpStatusCode status, string code)
    {
        var (api, _) = Create((_, _) => StubHttpHandler.Json(status, $$"""{"status":{{(int)status}},"code":"{{code}}"}""", "application/problem+json"));
        using (api)
        {
            var ex = await Assert.ThrowsAsync<ApiProblemException>(() => new FriendsService(api.Http).SendAsync("nyx"));
            Assert.Equal(code, ex.Problem.Code);
        }
    }
}
