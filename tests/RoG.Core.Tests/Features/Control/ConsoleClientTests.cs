using RoG.Core.Features.Control;
using RoG.Core.Tests.Realtime;

namespace RoG.Core.Tests.Features.Control;

public class ConsoleClientTests
{
    private static ConsoleTicket Ticket(string token, ConsoleLevel level = ConsoleLevel.Command) =>
        new(token, new Uri("wss://node.example/api/servers/x/ws"), "https://admin.example", level, Demo: false);

    [Fact]
    public async Task Authenticates_with_origin_and_streams_output()
    {
        var socket = new FakeWebSocket();
        string? origin = null;
        await using var client = new ConsoleClient(_ => Task.FromResult(Ticket("t1")), o => { origin = o; return socket; });
        var lines = new List<string>();
        var got = new TaskCompletionSource();
        client.Output += (_, line) => { lines.Add(line); got.TrySetResult(); };

        await client.ConnectAsync();
        var auth = await socket.NextSentAsync("auth");
        socket.ServerSends("""{"event":"auth success"}""");
        await socket.NextSentAsync("send logs");
        socket.ServerSends("{\"event\":\"console output\",\"args\":[\"\\u001b[33mServer started\\u001b[0m\"]}");
        await got.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("t1", auth.GetProperty("args")[0].GetString());
        Assert.Equal("https://admin.example", origin);
        Assert.Equal(new Uri("wss://node.example/api/servers/x/ws"), socket.ConnectedTo);
        Assert.Equal(["Server started"], lines);
        Assert.Equal(ConsoleLevel.Command, client.Level);
    }

    [Fact]
    public async Task Renews_token_when_expiring()
    {
        var socket = new FakeWebSocket();
        var count = 0;
        await using var client = new ConsoleClient(_ => Task.FromResult(Ticket("t" + ++count, count > 1 ? ConsoleLevel.Read : ConsoleLevel.Command)), _ => socket);

        await client.ConnectAsync();
        await socket.NextSentAsync("auth");
        socket.ServerSends("""{"event":"token expiring"}""");
        var renewed = await socket.NextSentAsync("auth");

        Assert.Equal("t2", renewed.GetProperty("args")[0].GetString());
        Assert.Equal(ConsoleLevel.Read, client.Level);
    }

    [Fact]
    public async Task Commands_are_sent_as_send_command()
    {
        var socket = new FakeWebSocket();
        await using var client = new ConsoleClient(_ => Task.FromResult(Ticket("t")), _ => socket);
        await client.ConnectAsync();

        await client.SendCommandAsync("  say Hallo ");
        var sent = await socket.NextSentAsync("send command");

        Assert.Equal("say Hallo", sent.GetProperty("args")[0].GetString());
    }

    [Fact]
    public async Task Server_close_reports_disconnect()
    {
        var socket = new FakeWebSocket();
        await using var client = new ConsoleClient(_ => Task.FromResult(Ticket("t")), _ => socket);
        var closed = new TaskCompletionSource<Exception?>();
        client.Disconnected += (_, e) => closed.TrySetResult(e);

        await client.ConnectAsync();
        socket.ServerCloses();

        Assert.Null(await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Auth_success_requests_logs_and_stats_and_reports_it()
    {
        var socket = new FakeWebSocket();
        await using var client = new ConsoleClient(_ => Task.FromResult(Ticket("t")), _ => socket);
        var authenticated = new TaskCompletionSource();
        client.Authenticated += (_, _) => authenticated.TrySetResult();

        await client.ConnectAsync();
        await socket.NextSentAsync("auth");
        socket.ServerSends("""{"event":"auth success"}""");
        var logs = await socket.NextSentAsync("send logs");
        await socket.NextSentAsync("send stats");
        await authenticated.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(System.Text.Json.JsonValueKind.Null, logs.GetProperty("args")[0].ValueKind);
    }

    [Fact]
    public async Task Daemon_error_and_repeated_jwt_error_are_reported()
    {
        var socket = new FakeWebSocket();
        await using var client = new ConsoleClient(_ => Task.FromResult(Ticket("t")), _ => socket);
        var errors = new List<string>();
        var third = new TaskCompletionSource();
        client.Error += (_, e) => { errors.Add(e); if (errors.Count == 2) { third.TrySetResult(); } };

        await client.ConnectAsync();
        await socket.NextSentAsync("auth");
        socket.ServerSends("""{"event":"daemon error","args":["Server is offline"]}""");
        socket.ServerSends("""{"event":"jwt error","args":["bad"]}""");
        await socket.NextSentAsync("auth"); // erster jwt error: neues Token
        socket.ServerSends("""{"event":"jwt error","args":["bad"]}""");
        await socket.NextSentAsync("auth");
        socket.ServerSends("""{"event":"jwt error","args":["bad"]}""");
        await third.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["Server is offline", "bad"], errors);
    }

    [Fact]
    public async Task Demo_ticket_does_not_open_a_socket()
    {
        var opened = false;
        await using var client = new ConsoleClient(
            _ => Task.FromResult(new ConsoleTicket("t", new Uri("wss://demo.invalid/ws"), null, ConsoleLevel.Read, Demo: true)),
            _ => { opened = true; return new FakeWebSocket(); });

        await client.ConnectAsync();

        Assert.True(client.IsDemo);
        Assert.False(opened);
    }

    [Theory]
    [InlineData("\u001b[1;31mFehler\u001b[0m", "Fehler")]
    [InlineData("plain", "plain")]
    public void Ansi_is_removed(string input, string expected) => Assert.Equal(expected, ConsoleClient.StripAnsi(input));
}

public class WingsStatsTests
{
    [Fact]
    public void Stats_are_parsed()
    {
        var r = ServerResources.FromWingsStats("""
            {"memory_bytes":536870912,"memory_limit_bytes":1073741824,"cpu_absolute":12.5,"network":{"rx_bytes":10,"tx_bytes":20},"state":"running","disk_bytes":2048,"uptime":61000}
            """);

        Assert.NotNull(r);
        Assert.Equal("running", r.State);
        Assert.Equal(12.5, r.CpuPercent);
        Assert.Equal(1073741824, r.MemoryLimit);
        Assert.Equal(20, r.NetworkTx);
        Assert.Equal(TimeSpan.FromSeconds(61), r.Uptime);
        Assert.Null(ServerResources.FromWingsStats("kaputt"));
    }
}

public class CommandHistoryTests
{
    [Fact]
    public void Up_and_down_walk_the_history()
    {
        var history = new CommandHistory();
        history.Add("status");
        history.Add("say hi");
        history.Add("say hi");

        Assert.Equal(["status", "say hi"], history.Items);
        Assert.Equal("say hi", history.Previous());
        Assert.Equal("status", history.Previous());
        Assert.Equal("status", history.Previous());
        Assert.Equal("say hi", history.Next());
        Assert.Equal(string.Empty, history.Next());
    }

    [Fact]
    public void Capacity_drops_oldest()
    {
        var history = new CommandHistory(capacity: 2);
        history.Add("a");
        history.Add("b");
        history.Add("c");

        Assert.Equal(["b", "c"], history.Items);
        Assert.Null(new CommandHistory().Previous());
    }
}
