using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Realtime;

namespace RoG.Core.Tests.Realtime;

public class ReverbClientTests
{
    private static readonly RealtimeConfig s_config =
        new("app-key", "ws.example.test", 443, "https", new Uri("https://api.example.test/v1/broadcasting/auth"));

    private sealed class FakeAuthorizer : IChannelAuthorizer
    {
        public List<(string SocketId, string Channel)> Calls { get; } = [];

        public Func<string, ChannelAuthorization>? Respond { get; init; }

        public Task<ChannelAuthorization> AuthorizeAsync(string socketId, string channelName, CancellationToken cancellationToken)
        {
            Calls.Add((socketId, channelName));
            return Task.FromResult(Respond?.Invoke(channelName) ?? new ChannelAuthorization("app-key:signature", null));
        }
    }

    private static Task NeverIdle(TimeSpan _, CancellationToken ct) => Task.Delay(Timeout.Infinite, ct);

    private static (ReverbClient Client, List<FakeWebSocket> Sockets) Create(IChannelAuthorizer? authorizer = null, string socketId = "123.456")
    {
        var sockets = new List<FakeWebSocket>();
        var client = new ReverbClient(
            s_config,
            authorizer ?? new FakeAuthorizer(),
            () =>
            {
                var socket = new FakeWebSocket { OnConnect = FakeWebSocket.Established(socketId + (sockets.Count == 0 ? string.Empty : "." + sockets.Count)) };
                sockets.Add(socket);
                return socket;
            },
            "0.1.0",
            delay: (_, _) => Task.CompletedTask,
            idleDelay: NeverIdle);
        return (client, sockets);
    }

    private static string Message(string ev, string channel, object data, bool dataAsString = true) =>
        JsonSerializer.Serialize(new { @event = ev, channel, data = dataAsString ? (object)JsonSerializer.Serialize(data) : data });

    private static async Task<T> WithTimeout<T>(Task<T> task) => await task.WaitAsync(TimeSpan.FromSeconds(5));

    [Fact]
    public void Socket_uri_follows_pusher_protocol_7()
    {
        Assert.Equal("wss://ws.example.test/app/app-key?protocol=7&client=rog-windows&version=0.1.0&flash=false",
            s_config.BuildSocketUri("0.1.0").ToString());
        var local = s_config with { Scheme = "http", Port = 8080, Host = "ws.rog.test" };
        Assert.Equal("ws://ws.rog.test:8080/app/app-key?protocol=7&client=rog-windows&version=0.1.0&flash=false",
            local.BuildSocketUri("0.1.0").ToString());
    }

    [Fact]
    public void Config_is_read_from_get_v1_data_realtime()
    {
        var api = new Uri("https://api.example.test/v1/");
        var config = RealtimeConfig.FromStatusResponse("""
            {"data":{"name":"RoG","version":"v1","realtime":{"key":"k","host":"ws.rebelsofgaming.com","port":443,"scheme":"https",
             "auth_endpoint":"https://api.rebelsofgaming.com/v1/broadcasting/auth"}}}
            """, api);

        Assert.NotNull(config);
        Assert.Equal("k", config!.Key);
        Assert.True(config.UseTls);
        Assert.Equal("https://api.rebelsofgaming.com/v1/broadcasting/auth", config.AuthEndpoint.ToString());
        Assert.Null(RealtimeConfig.FromStatusResponse("""{"data":{"realtime":null}}""", api));
        Assert.Null(RealtimeConfig.FromStatusResponse("kein json", api));
    }

    [Fact]
    public async Task Connect_waits_for_connection_established_and_reads_socket_id()
    {
        var (client, sockets) = Create();
        await using var _ = client;

        await client.ConnectAsync();

        Assert.Equal(RealtimeState.Connected, client.State);
        Assert.Equal("123.456", client.SocketId);
        Assert.Equal(30, client.ActivityTimeoutSeconds);
        Assert.Equal("/app/app-key", sockets[0].ConnectedTo!.AbsolutePath);
    }

    [Fact]
    public async Task Public_channel_subscribes_without_auth_and_receives_events()
    {
        var (client, sockets) = Create();
        await using var _ = client;
        await client.ConnectAsync();

        var channel = client.Subscribe("servers");
        var subscribe = await sockets[0].NextSentAsync("pusher:subscribe");
        Assert.Equal("servers", subscribe.GetProperty("data").GetProperty("channel").GetString());
        Assert.False(subscribe.GetProperty("data").TryGetProperty("auth", out var _));

        var received = new TaskCompletionSource<ReverbEvent>();
        channel.EventReceived += (_, e) => received.TrySetResult(e);
        sockets[0].ServerSends(Message("pusher_internal:subscription_succeeded", "servers", new { }));
        sockets[0].ServerSends(Message(".server.status", "servers", new { id = "01S", online = true, players = 12 }));

        var ev = await WithTimeout(received.Task);
        Assert.Equal(".server.status", ev.Name);
        Assert.True(channel.IsSubscribed);
        using var data = JsonDocument.Parse(ev.DataJson);
        Assert.Equal(12, data.RootElement.GetProperty("players").GetInt32());
    }

    [Fact]
    public async Task Event_data_as_object_is_supported_too()
    {
        var (client, sockets) = Create();
        await using var _ = client;
        await client.ConnectAsync();
        var channel = client.Subscribe("live");
        var received = new TaskCompletionSource<ReverbEvent>();
        channel.EventReceived += (_, e) => received.TrySetResult(e);

        sockets[0].ServerSends(Message(".live.updated", "live", new { online = 5 }, dataAsString: false));

        var ev = await WithTimeout(received.Task);
        Assert.Equal(5, ev.Deserialize<JsonElement>().GetProperty("online").GetInt32());
    }

    [Fact]
    public async Task Private_channel_is_authorized_with_socket_id()
    {
        var authorizer = new FakeAuthorizer();
        var (client, sockets) = Create(authorizer);
        await using var _ = client;
        await client.ConnectAsync();

        client.Subscribe("private-App.Models.User.01U");
        var subscribe = await sockets[0].NextSentAsync("pusher:subscribe");

        Assert.Equal(("123.456", "private-App.Models.User.01U"), Assert.Single(authorizer.Calls));
        Assert.Equal("app-key:signature", subscribe.GetProperty("data").GetProperty("auth").GetString());
    }

    [Fact]
    public async Task Channels_subscribed_before_connect_are_sent_after_connection_established()
    {
        var (client, sockets) = Create();
        await using var _ = client;
        client.Subscribe("servers");

        await client.ConnectAsync();
        var subscribe = await sockets[0].NextSentAsync("pusher:subscribe");
        Assert.Equal("servers", subscribe.GetProperty("data").GetProperty("channel").GetString());
    }

    [Fact]
    public async Task Forbidden_private_channel_reports_subscription_failed()
    {
        var (client, _) = Create(new ThrowingAuthorizer());
        await using var __ = client;
        var channel = client.Subscribe("presence-squad.01SQ");
        var failed = new TaskCompletionSource<int?>();
        channel.SubscriptionFailed += (_, status) => failed.TrySetResult(status);

        await client.ConnectAsync();

        Assert.Equal(403, await WithTimeout(failed.Task));
        Assert.False(channel.IsSubscribed);
    }

    private sealed class ThrowingAuthorizer : IChannelAuthorizer
    {
        public Task<ChannelAuthorization> AuthorizeAsync(string socketId, string channelName, CancellationToken cancellationToken) =>
            throw new ApiProblemException(new ApiProblem { Code = "forbidden", Status = 403 });
    }

    [Fact]
    public async Task Presence_channel_tracks_members()
    {
        var authorizer = new FakeAuthorizer { Respond = _ => new ChannelAuthorization("app-key:sig", """{"user_id":"01U","user_info":{"name":"Wahke"}}""") };
        var (client, sockets) = Create(authorizer);
        await using var _ = client;
        await client.ConnectAsync();

        var channel = client.Subscribe("presence-squad.01SQ");
        var subscribe = await sockets[0].NextSentAsync("pusher:subscribe");
        Assert.Contains("01U", subscribe.GetProperty("data").GetProperty("channel_data").GetString());

        var subscribed = new TaskCompletionSource();
        var removed = new TaskCompletionSource<PresenceMember>();
        channel.Subscribed += (_, _) => subscribed.TrySetResult();
        channel.MemberRemoved += (_, m) => removed.TrySetResult(m);

        sockets[0].ServerSends(Message("pusher_internal:subscription_succeeded", "presence-squad.01SQ", new
        {
            presence = new
            {
                ids = new[] { "01U", "02U" },
                hash = new Dictionary<string, object> { ["01U"] = new { name = "Wahke" }, ["02U"] = new { name = "Rebel" } },
                count = 2,
            },
        }));
        await subscribed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, channel.Members.Count);

        sockets[0].ServerSends(Message("pusher_internal:member_added", "presence-squad.01SQ", new { user_id = "03U", user_info = new { name = "Neu" } }));
        sockets[0].ServerSends(Message("pusher_internal:member_removed", "presence-squad.01SQ", new { user_id = "02U" }));

        var gone = await WithTimeout(removed.Task);
        Assert.Equal("02U", gone.Id);
        Assert.Equal(["01U", "03U"], channel.Members.Select(m => m.Id).Order());
    }

    [Fact]
    public async Task Answers_server_ping_with_pong()
    {
        var (client, sockets) = Create();
        await using var _ = client;
        await client.ConnectAsync();

        sockets[0].ServerSends("""{"event":"pusher:ping","data":{}}""");

        var pong = await sockets[0].NextSentAsync("pusher:pong");
        Assert.Equal("pusher:pong", pong.GetProperty("event").GetString());
    }

    [Fact]
    public async Task Sends_ping_after_activity_timeout()
    {
        var sockets = new List<FakeWebSocket>();
        var client = new ReverbClient(s_config, new FakeAuthorizer(), () =>
        {
            var s = new FakeWebSocket { OnConnect = FakeWebSocket.Established() };
            sockets.Add(s);
            return s;
        }, "0.1.0", delay: (_, _) => Task.CompletedTask, idleDelay: (_, ct) => Task.Delay(50, ct));
        await using var _ = client;
        await client.ConnectAsync();

        var ping = await sockets[0].NextSentAsync("pusher:ping");
        Assert.Equal("pusher:ping", ping.GetProperty("event").GetString());
    }

    [Fact]
    public async Task Fatal_pusher_error_stops_without_reconnect()
    {
        var (client, sockets) = Create();
        await using var _ = client;
        await client.ConnectAsync();
        var error = new TaskCompletionSource<PusherError>();
        client.Error += (_, e) => error.TrySetResult(e);

        sockets[0].ServerSends("""{"event":"pusher:error","data":{"code":4001,"message":"Application does not exist"}}""");

        var e = await WithTimeout(error.Task);
        Assert.Equal(4001, e.Code);
        await Task.Delay(100);
        Assert.Equal(RealtimeState.Failed, client.State);
        Assert.Single(sockets);
    }

    [Fact]
    public async Task Reconnects_after_close_and_resubscribes_channels()
    {
        var (client, sockets) = Create();
        await using var _ = client;
        await client.ConnectAsync();
        client.Subscribe("servers");
        await sockets[0].NextSentAsync("pusher:subscribe");

        var reconnected = new TaskCompletionSource();
        client.Reconnected += (_, _) => reconnected.TrySetResult();
        sockets[0].ServerCloses();

        await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, sockets.Count);
        Assert.True(sockets[0].Disposed);
        Assert.Equal(RealtimeState.Connected, client.State);
        Assert.Equal("123.456.1", client.SocketId);
        var resubscribe = await sockets[1].NextSentAsync("pusher:subscribe");
        Assert.Equal("servers", resubscribe.GetProperty("data").GetProperty("channel").GetString());
    }

    [Fact]
    public async Task Unsubscribe_sends_pusher_unsubscribe()
    {
        var (client, sockets) = Create();
        await using var _ = client;
        await client.ConnectAsync();
        client.Subscribe("servers");
        await sockets[0].NextSentAsync("pusher:subscribe");

        await client.UnsubscribeAsync("servers");

        var unsubscribe = await sockets[0].NextSentAsync("pusher:unsubscribe");
        Assert.Equal("servers", unsubscribe.GetProperty("data").GetProperty("channel").GetString());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(4, 16)]
    [InlineData(5, 30)]
    [InlineData(50, 30)]
    public void Backoff_doubles_up_to_30_seconds(int attempt, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), ReverbClient.Backoff(attempt));

    [Fact]
    public void Data_strings_are_unwrapped()
    {
        Assert.Equal("""{"a":1}""", ReverbClient.UnwrapData(System.Text.Json.Nodes.JsonValue.Create("""{"a":1}""")));
        Assert.Equal("\"Hallo\"", ReverbClient.UnwrapData(System.Text.Json.Nodes.JsonValue.Create("Hallo")));
        Assert.Equal("{}", ReverbClient.UnwrapData(null));
    }
}
