using System.Net;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Offline;
using RoG.Core.Security;
using RoG.Core.Tests.Features;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Offline;

public sealed class OutboxTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rog-outbox-" + Guid.NewGuid().ToString("N"));

    private string File => Path.Combine(_folder, "outbox.dat");

    private (Outbox Outbox, StubHttpHandler Stub, RoGApi Api) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond, IPayloadProtector? protector = null)
    {
        var stub = new StubHttpHandler(respond);
        var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);
        return (new Outbox(api.Http, File, protector ?? NoPayloadProtection.Instance), stub, api);
    }

    [Fact]
    public async Task Network_error_keeps_item_and_retry_reuses_idempotency_key()
    {
        var calls = 0;
        var (outbox, stub, api) = Create((_, _) => ++calls <= 3
            ? throw new HttpRequestException("offline")
            : StubHttpHandler.Json(HttpStatusCode.Created, """{"data":{}}"""));
        using (api)
        using (outbox)
        {
            await outbox.EnqueueAsync(OutboxKind.ForumReply, "t1", new { body = "Hallo" }, "Forum");

            Assert.Equal(0, await outbox.FlushAsync());
            Assert.Equal(1, outbox.Count);
            Assert.Equal(1, await outbox.FlushAsync());
            Assert.Equal(0, outbox.Count);

            var keys = stub.Requests.Select(r => r.Headers[RoGHttpHandler.IdempotencyHeader]).Distinct().ToList();
            Assert.Single(keys);
            Assert.EndsWith("/v1/forum/threads/t1/posts", stub.Requests[^1].Uri!.AbsolutePath, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Rejected_item_is_dropped_and_reported()
    {
        var (outbox, _, api) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Forbidden, """{"status":403,"code":"moderation_muted"}""", "application/problem+json"));
        using (api)
        using (outbox)
        {
            OutboxFailure? failure = null;
            outbox.Failed += (_, f) => failure = f;
            await outbox.EnqueueAsync(OutboxKind.EventRsvp, "e1", new { status = "yes" }, "Termin");

            await outbox.FlushAsync();

            Assert.Equal(0, outbox.Count);
            Assert.Equal("moderation_muted", failure?.Problem.Code);
        }
    }

    [Fact]
    public async Task Direct_messages_are_never_written_to_disk_others_are_encrypted()
    {
        var (outbox, _, api) = Create((_, _) => throw new HttpRequestException("offline"), new XorProtector());
        using (api)
        using (outbox)
        {
            await outbox.EnqueueAsync(OutboxKind.DirectMessage, "c1", new { body = "geheim" }, "DM");
            Assert.False(System.IO.File.Exists(File));

            await outbox.EnqueueAsync(OutboxKind.ForumReply, "t1", new { body = "öffentlich" }, "Forum");
            Assert.True(System.IO.File.Exists(File));
            Assert.DoesNotContain("öffentlich", System.IO.File.ReadAllText(File), StringComparison.Ordinal);
        }

        var (reloaded, _, api2) = Create((_, _) => throw new HttpRequestException("offline"), new XorProtector());
        using (api2)
        using (reloaded)
        {
            await reloaded.LoadAsync();
            Assert.Equal([OutboxKind.ForumReply], reloaded.Items.Select(i => i.Kind));
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
