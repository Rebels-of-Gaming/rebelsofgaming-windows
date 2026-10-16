using System.Net;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Tests.Features;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Api;

public sealed class OfflineCacheTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rog-cache-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Get_without_network_returns_last_response_marked_stale()
    {
        var online = true;
        var stub = new StubHttpHandler((_, _) => online
            ? StubHttpHandler.Json(HttpStatusCode.OK, """{"data":[1,2,3]}""")
            : throw new HttpRequestException("offline"));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);
        Uri? staleUri = null;
        api.Handler.ServedStale += (_, uri) => staleUri = uri;

        using (var first = await api.Http.GetAsync(new Uri("news", UriKind.Relative)))
        {
            Assert.False(first.Headers.Contains(RoGHttpHandler.CacheHeader));
        }

        online = false;
        using var second = await api.Http.GetAsync(new Uri("news", UriKind.Relative));

        Assert.Equal("""{"data":[1,2,3]}""", await second.Content.ReadAsStringAsync());
        Assert.Equal("stale", second.Headers.GetValues(RoGHttpHandler.CacheHeader).Single());
        Assert.EndsWith("/v1/news", staleUri?.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Server_error_also_falls_back_but_unknown_requests_still_fail()
    {
        var calls = 0;
        var stub = new StubHttpHandler((_, _) => ++calls == 1
            ? StubHttpHandler.Json(HttpStatusCode.OK, """{"data":"x"}""")
            : StubHttpHandler.Json(HttpStatusCode.BadGateway, """{"status":502}""", "application/problem+json"));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);

        (await api.Http.GetAsync(new Uri("squads", UriKind.Relative))).Dispose();
        using var cached = await api.Http.GetAsync(new Uri("squads", UriKind.Relative));

        Assert.Equal("stale", cached.Headers.GetValues(RoGHttpHandler.CacheHeader).Single());
        await Assert.ThrowsAsync<ApiProblemException>(() => api.Http.GetAsync(new Uri("matches", UriKind.Relative)));
    }

    [Fact]
    public async Task Persistent_cache_survives_restart_encrypted_and_never_stores_messages()
    {
        using (var cache = new PersistentEtagCache(_folder, new XorProtector()))
        {
            cache.Store("guest|de|https://api.rebelsofgaming.com/v1/news", new EtagEntry("\"1\"", "{\"n\":\"Nachricht\"}"u8.ToArray(), "application/json", DateTimeOffset.UtcNow));
            cache.Store("abc|de|https://api.rebelsofgaming.com/v1/conversations/1/messages", new EtagEntry("", "geheim"u8.ToArray(), null, DateTimeOffset.UtcNow));
            await WaitForFilesAsync(1);
        }

        Assert.Single(Directory.GetFiles(_folder, "*.dat"));
        Assert.DoesNotContain("Nachricht", File.ReadAllText(Directory.GetFiles(_folder, "*.dat")[0]), StringComparison.Ordinal);

        using var reloaded = new PersistentEtagCache(_folder, new XorProtector());
        await reloaded.LoadAsync();
        Assert.True(reloaded.TryGet("guest|de|https://api.rebelsofgaming.com/v1/news", out var entry));
        Assert.Equal("\"1\"", entry.ETag);
        Assert.False(reloaded.TryGet("abc|de|https://api.rebelsofgaming.com/v1/conversations/1/messages", out _));
    }

    [Theory]
    [InlineData("u|de|https://api.rebelsofgaming.com/v1/news?page=2", true)]
    [InlineData("u|de|https://api.rebelsofgaming.com/v1/conversations", false)]
    [InlineData("u|de|https://api.rebelsofgaming.com/v1/me/exports", false)]
    [InlineData("u|de|https://api.rebelsofgaming.com/v1/servers/et/console-token", false)]
    public void Sensitive_paths_are_not_persisted(string key, bool expected) => Assert.Equal(expected, PersistentEtagCache.Persistable(key));

    private async Task WaitForFilesAsync(int count)
    {
        for (var i = 0; i < 50 && (!Directory.Exists(_folder) || Directory.GetFiles(_folder, "*.dat").Length < count); i++)
        {
            await Task.Delay(20);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }
}
