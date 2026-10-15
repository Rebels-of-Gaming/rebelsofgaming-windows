using System.Net;
using System.Net.Http.Headers;
using System.Text;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Api;

public class RoGHttpHandlerTests
{
    private static readonly Uri s_base = new("https://api.example.test/v1/");

    private sealed class Harness
    {
        public required StubHttpHandler Stub { get; init; }

        public required RoGHttpHandler Handler { get; init; }

        public required HttpClient Http { get; init; }

        public required InMemoryTokenStore Tokens { get; init; }

        public required ApiContext Context { get; init; }

        public required List<TimeSpan> Delays { get; init; }
    }

    private static Harness Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond, bool withToken = true, IEtagCache? etags = null)
    {
        var tokens = new InMemoryTokenStore();
        if (withToken)
        {
            tokens.SetAsync(new StoredToken("42|tok", "01U", "Wahke", ["profile:read"], "PC", DateTimeOffset.UtcNow)).Wait();
        }

        var stub = new StubHttpHandler(respond);
        var context = new ApiContext("fr", "0.1.0");
        var delays = new List<TimeSpan>();
        var handler = new RoGHttpHandler(tokens, context, etags, (d, _) =>
        {
            delays.Add(d);
            return Task.CompletedTask;
        })
        { InnerHandler = stub };
        return new Harness
        {
            Stub = stub,
            Handler = handler,
            Http = new HttpClient(handler) { BaseAddress = s_base },
            Tokens = tokens,
            Context = context,
            Delays = delays,
        };
    }

    private static HttpResponseMessage Ok(string json = "{\"data\":[]}") => StubHttpHandler.Json(HttpStatusCode.OK, json);

    [Fact]
    public async Task Adds_bearer_accept_language_accept_and_user_agent()
    {
        var h = Create((_, _) => Ok());
        using var _ = await h.Http.GetAsync(new Uri("me", UriKind.Relative));

        var req = Assert.Single(h.Stub.Requests);
        Assert.Equal("Bearer 42|tok", req.Headers["Authorization"]);
        Assert.Equal("fr", req.Headers["Accept-Language"]);
        Assert.Contains("application/json", req.Headers["Accept"]);
        Assert.StartsWith("RoG-Windows/0.1.0", req.Headers["User-Agent"]);
        Assert.Equal("https://api.example.test/v1/me", req.Uri!.ToString());
    }

    [Fact]
    public async Task Language_change_applies_to_next_request()
    {
        var h = Create((_, _) => Ok());
        using (await h.Http.GetAsync(new Uri("news", UriKind.Relative)))
        {
        }

        h.Context.Language = "en";
        using (await h.Http.GetAsync(new Uri("news", UriKind.Relative)))
        {
        }

        Assert.Equal("en", h.Stub.Requests[1].Headers["Accept-Language"]);
    }

    [Fact]
    public async Task Skip_authorization_and_guest_send_no_token()
    {
        var h = Create((_, _) => Ok());
        using var request = new HttpRequestMessage(HttpMethod.Get, "auth/scopes");
        request.Options.Set(RoGHttpHandler.SkipAuthorization, true);
        using (await h.Http.SendAsync(request))
        {
        }

        var guest = Create((_, _) => Ok(), withToken: false);
        using (await guest.Http.GetAsync(new Uri("news", UriKind.Relative)))
        {
        }

        Assert.False(h.Stub.Requests[0].Headers.ContainsKey("Authorization"));
        Assert.False(guest.Stub.Requests[0].Headers.ContainsKey("Authorization"));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Writes_get_an_idempotency_key(string method)
    {
        var h = Create((_, _) => Ok());
        using var request = new HttpRequestMessage(new HttpMethod(method), "devices") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        using (await h.Http.SendAsync(request))
        {
        }

        Assert.True(Guid.TryParse(h.Stub.Requests[0].Headers[RoGHttpHandler.IdempotencyHeader], out _));
    }

    [Fact]
    public async Task Get_has_no_idempotency_key_and_existing_key_is_kept()
    {
        var h = Create((_, _) => Ok());
        using (await h.Http.GetAsync(new Uri("news", UriKind.Relative)))
        {
        }

        using var post = new HttpRequestMessage(HttpMethod.Post, "conversations/1/messages") { Content = new StringContent("{}") };
        post.Headers.Add(RoGHttpHandler.IdempotencyHeader, "my-action-key");
        using (await h.Http.SendAsync(post))
        {
        }

        Assert.False(h.Stub.Requests[0].Headers.ContainsKey(RoGHttpHandler.IdempotencyHeader));
        Assert.Equal("my-action-key", h.Stub.Requests[1].Headers[RoGHttpHandler.IdempotencyHeader]);
    }

    [Fact]
    public async Task Retry_after_503_resends_same_body_and_same_idempotency_key()
    {
        var h = Create((_, i) =>
        {
            if (i == 0)
            {
                var r = StubHttpHandler.Json(HttpStatusCode.ServiceUnavailable, """{"code":"maintenance","status":503}""", "application/problem+json");
                r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
                return r;
            }

            return Ok("{\"data\":{\"id\":\"1\"}}");
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "forum/threads/1/posts")
        {
            Content = new StringContent("""{"body":"Hallo"}""", Encoding.UTF8, "application/json"),
        };
        using var response = await h.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, h.Stub.Requests.Count);
        Assert.Equal(h.Stub.Requests[0].Headers[RoGHttpHandler.IdempotencyHeader], h.Stub.Requests[1].Headers[RoGHttpHandler.IdempotencyHeader]);
        Assert.Equal("""{"body":"Hallo"}""", h.Stub.Requests[1].Body);
        Assert.Equal([TimeSpan.FromSeconds(2)], h.Delays);
    }

    [Fact]
    public async Task Long_retry_after_is_not_waited_but_reported()
    {
        var h = Create((_, _) =>
        {
            var r = StubHttpHandler.Json(HttpStatusCode.TooManyRequests, """{"code":"rate_limited","status":429}""", "application/problem+json");
            r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            return r;
        });

        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => h.Http.GetAsync(new Uri("search?q=x", UriKind.Relative)));

        Assert.Equal("rate_limited", ex.Problem.Code);
        Assert.Equal(TimeSpan.FromSeconds(60), ex.Problem.RetryAfter);
        Assert.Single(h.Stub.Requests);
    }

    [Fact]
    public async Task Network_errors_are_retried_for_get_then_reported_as_network_error()
    {
        var h = Create((_, _) => throw new HttpRequestException("kaputt"));

        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => h.Http.GetAsync(new Uri("news", UriKind.Relative)));

        Assert.Equal(ApiProblem.Codes.NetworkError, ex.Problem.Code);
        Assert.Equal(3, h.Stub.Requests.Count); // 1 + MaxRetries (2)
    }

    [Fact]
    public async Task Forbidden_throws_problem_without_unauthorized_event()
    {
        var h = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Forbidden, """{"code":"insufficient_scope","status":403}""", "application/problem+json"));
        var fired = false;
        h.Handler.Unauthorized += (_, _) => fired = true;

        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => h.Http.GetAsync(new Uri("admin/dashboard", UriKind.Relative)));

        Assert.True(ex.Problem.IsForbidden);
        Assert.Equal("insufficient_scope", ex.Problem.Code);
        Assert.False(fired);
    }

    [Fact]
    public async Task Unauthorized_with_token_raises_event()
    {
        var h = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Unauthorized, """{"message":"Unauthenticated."}"""));
        ApiProblem? seen = null;
        h.Handler.Unauthorized += (_, p) => seen = p;

        await Assert.ThrowsAsync<ApiProblemException>(() => h.Http.GetAsync(new Uri("me", UriKind.Relative)));

        Assert.NotNull(seen);
        Assert.Equal("unauthenticated", seen!.Code);
    }

    [Fact]
    public async Task Etag_is_stored_and_304_returns_cached_body()
    {
        var etags = new InMemoryEtagCache();
        var h = Create((req, i) =>
        {
            if (i == 0)
            {
                var r = Ok("{\"data\":[1,2,3]}");
                r.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
                return r;
            }

            return new HttpResponseMessage(HttpStatusCode.NotModified);
        }, etags: etags);

        var first = await h.Http.GetStringAsync(new Uri("news", UriKind.Relative));
        using var second = await h.Http.GetAsync(new Uri("news", UriKind.Relative));

        Assert.Equal("{\"data\":[1,2,3]}", first);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("{\"data\":[1,2,3]}", await second.Content.ReadAsStringAsync());
        Assert.False(h.Stub.Requests[0].Headers.ContainsKey("If-None-Match"));
        Assert.Equal("\"v1\"", h.Stub.Requests[1].Headers["If-None-Match"]);
    }

    [Fact]
    public async Task Etag_cache_is_separated_by_language_and_account()
    {
        var etags = new InMemoryEtagCache();
        var h = Create((_, _) =>
        {
            var r = Ok();
            r.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
            return r;
        }, etags: etags);

        using (await h.Http.GetAsync(new Uri("news", UriKind.Relative)))
        {
        }

        h.Context.Language = "de";
        using (await h.Http.GetAsync(new Uri("news", UriKind.Relative)))
        {
        }

        await h.Tokens.SetAsync(new StoredToken("43|other", "02U", "Andere", [], "PC", DateTimeOffset.UtcNow));
        using (await h.Http.GetAsync(new Uri("news", UriKind.Relative)))
        {
        }

        Assert.All(h.Stub.Requests, r => Assert.False(r.Headers.ContainsKey("If-None-Match")));
        Assert.Equal(3, etags.Count);
    }

    [Fact]
    public void Cache_key_never_contains_the_token()
    {
        var handler = new RoGHttpHandler(new InMemoryTokenStore(), new ApiContext());
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.test/v1/me");
        var key = handler.CacheKey(request, new StoredToken("42|supersecret", "01U", "W", [], "PC", DateTimeOffset.UtcNow));
        Assert.DoesNotContain("supersecret", key);
        Assert.DoesNotContain("42|", key);
        handler.Dispose();
    }
}
