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

    [Theory]
    [InlineData("draft", "", "draft")]
    [InlineData("cancelled", "", "cancelled")]
    [InlineData("completed", "published=2", "published")]
    [InlineData("completed", "published=1,failed=1", "partial")]
    [InlineData("completed", "failed=1,skipped=1", "failed")]
    [InlineData("completed", "retracted=2", "retracted")]
    [InlineData("completed", "", "failed")]
    [InlineData("weird", "", "none")]
    public void List_state_follows_the_website(string status, string summary, string expected)
    {
        var map = summary.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('='))
            .ToDictionary(p => p[0], p => int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expected, SocialSourceService.StateOf(status, map));
    }

    [Fact]
    public void List_takes_newest_post_per_source()
    {
        var states = SocialSourceService.StatesFromPosts(JsonDocument.Parse("""
            {"data":[
              {"id":"p3","source":"news","source_id":"42","status":"completed","target_summary":{"failed":1}},
              {"id":"p2","source":"news","source_id":"42","status":"completed","target_summary":{"published":1}},
              {"id":"p1","source":"news","source_id":"7","status":"scheduled","target_summary":{}}],
             "meta":{"next_cursor":null,"prev_cursor":null}}
            """).RootElement);

        Assert.Equal("failed", states["42"]);
        Assert.Equal("scheduled", states["7"]);
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
