using System.Net;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Social;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Social;

public class SocialSourceServiceTests
{
    private const string Status = """
        {"data":{"source":"news","source_id":"42","state":"partial","eligible":true,"can_redraft":true,
         "channels":{"usable":2,"any":true},
         "post":{"id":"p1","status":"completed","title":"Patch","scheduled_at":null,"completed_at":"2026-10-18T10:00:00Z","created_at":"2026-10-18T09:00:00Z",
          "targets":[
           {"id":"t1","channel_id":"c1","channel":{"name":"RoG Mastodon","provider":"mastodon","provider_label":"Mastodon","icon":"x","usable":true,"status":"connected"},
            "status":"published","skip_reason":null,"published_at":"2026-10-18T10:00:00Z","remote_url":"https://social.example/@rog/1","error":null,"retryable":false},
           {"id":"t2","channel_id":"c2","channel":null,"status":"failed","skip_reason":null,"published_at":null,"remote_url":null,"error":"Rate limit","retryable":true}]},
         "history":[{"id":"p0","status":"cancelled","state":"cancelled","created_at":null}]}}
        """;

    private static (RoGApi Api, StubHttpHandler Stub) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        return (new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub), stub);
    }

    [Fact]
    public async Task Status_is_read_with_targets_and_actions()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, Status));
        using (api)
        {
            var s = await new SocialSourceService(api.Http).GetAsync(SocialSources.News, "42");

            Assert.EndsWith("/v1/social/sources/news/42", Assert.Single(stub.Requests).Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("partial", s.State);
            Assert.Equal("p1", s.PostId);
            Assert.Equal(2, s.UsableChannels);
            Assert.Equal(1, s.HistoryCount);
            Assert.Collection(s.Targets,
                t =>
                {
                    Assert.Equal("RoG Mastodon", t.ChannelName);
                    Assert.Equal("Mastodon", t.ProviderLabel);
                    Assert.Equal(new Uri("https://social.example/@rog/1"), t.RemoteUrl);
                    Assert.False(t.Retryable);
                },
                t =>
                {
                    Assert.Equal("c2", t.ChannelName);
                    Assert.Equal("Rate limit", t.Error);
                    Assert.True(t.Retryable);
                });
            Assert.True(s.CanRetryFailed);
            Assert.True(s.AlreadyPosted);
            Assert.True(s.CanRedraftMissing);
        }
    }

    [Fact]
    public void Nothing_posted_yet_has_no_actions_but_redraft()
    {
        var s = SocialSourceService.Parse(JsonDocument.Parse("""
            {"source":"event","source_id":"7","state":"none","eligible":true,"can_redraft":true,"channels":{"usable":1,"any":true},"post":null,"history":[]}
            """).RootElement);

        Assert.Null(s.PostId);
        Assert.Empty(s.Targets);
        Assert.False(s.CanRetryFailed);
        Assert.False(s.AlreadyPosted);
        Assert.True(s.CanRedraftMissing);
    }

    [Theory]
    [InlineData(false, "{}")]
    [InlineData(true, "{\"repost\":true}")]
    public async Task Redraft_sends_repost_only_when_asked(bool repost, string expected)
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Created, """{"data":{"id":"p9","status":"draft"}}"""));
        using (api)
        {
            var id = await new SocialSourceService(api.Http).RedraftAsync(SocialSources.Gallery, "album-1", repost);

            Assert.Equal("p9", id);
            var request = Assert.Single(stub.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/v1/social/sources/gallery/album-1/redraft", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal(expected, request.Body);
        }
    }

    [Fact]
    public async Task Retry_failed_reports_unusable_channels()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Accepted,
            """{"data":{"count":1,"channel_ids":["c2"],"unusable":[{"channel_id":"c3","name":"RoG X","reason":"reauth"}]}}"""));
        using (api)
        {
            var result = await new SocialSourceService(api.Http).RetryFailedAsync("p1");

            Assert.EndsWith("/v1/social/posts/p1/retry-failed", Assert.Single(stub.Requests).Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal(1, result.Count);
            Assert.Equal(("RoG X", "reauth"), Assert.Single(result.Unusable));
        }
    }

    [Fact]
    public async Task Retry_target_hits_target_route()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Accepted, """{"data":{"id":"t2","status":"pending"}}"""));
        using (api)
        {
            await new SocialSourceService(api.Http).RetryTargetAsync("p1", "t2");

            Assert.EndsWith("/v1/social/posts/p1/targets/t2/retry", Assert.Single(stub.Requests).Uri!.AbsolutePath, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task States_come_from_the_batch_route_in_chunks_of_100()
    {
        var (api, stub) = Create((req, _) =>
        {
            var ids = req.RequestUri!.Query.TrimStart('?').Split('&').Select(q => Uri.UnescapeDataString(q).Split('=')[1]).ToList();
            var items = ids.Select(i => $$"""{"id":"{{i}}","state":"{{(i == "n7" ? "none" : "published")}}","post_id":null,"published_count":1,"failed_count":0,"can_retry":false,"channels":[]}""");
            return StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":[" + string.Join(",", items) + "]}");
        });
        using (api)
        {
            var ids = Enumerable.Range(0, 150).Select(i => "n" + i).Append("n1").Append(string.Empty);
            var states = await new SocialSourceService(api.Http).StatesAsync("news", ids);

            Assert.Equal(2, stub.Requests.Count);
            Assert.EndsWith("/v1/social/sources/news", stub.Requests[0].Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.StartsWith("?ids%5B%5D=n0&ids%5B%5D=n1&", stub.Requests[0].Uri!.Query, StringComparison.Ordinal);
            Assert.Equal(149, states.Count);
            Assert.False(states.ContainsKey("n7"));
            Assert.Equal("published", states["n149"]);
        }
    }

    [Fact]
    public void Batch_answer_skips_none_and_broken_items()
    {
        var states = SocialSourceService.ParseStates(JsonDocument.Parse("""
            {"data":[
              {"id":"42","state":"partial","post_id":"p1","published_count":1,"failed_count":1,"can_retry":true,"channels":[{"channel_id":"c1","provider":"mastodon","status":"failed"}]},
              {"id":"7","state":"none","post_id":null,"published_count":0,"failed_count":0,"can_retry":false,"channels":[]},
              {"state":"published"}]}
            """).RootElement);

        Assert.Equal("partial", Assert.Single(states).Value);
        Assert.True(states.ContainsKey("42"));
    }

    [Theory]
    [InlineData("published", SocialBadge.Posted)]
    [InlineData("partial", SocialBadge.Partial)]
    [InlineData("failed", SocialBadge.Failed)]
    [InlineData("scheduled", SocialBadge.Pending)]
    [InlineData("publishing", SocialBadge.Pending)]
    [InlineData("draft", SocialBadge.Draft)]
    [InlineData("retracted", SocialBadge.None)]
    [InlineData("cancelled", SocialBadge.None)]
    [InlineData(null, SocialBadge.None)]
    public void Badges_for_lists(string? state, SocialBadge expected) => Assert.Equal(expected, SocialStates.Badge(state));
}
