using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using RoG.Core.Realtime;

namespace RoG.Core.Tests.Realtime;

/// <summary>WebSocket ohne Netzwerk: Der Test schiebt Servernachrichten hinein und liest, was der Client gesendet hat.</summary>
internal sealed class FakeWebSocket : IWebSocketConnection
{
    private readonly Channel<string?> _incoming = Channel.CreateUnbounded<string?>();
    private readonly Channel<string> _sentSignal = Channel.CreateUnbounded<string>();

    public Uri? ConnectedTo { get; private set; }

    public ConcurrentQueue<string> Sent { get; } = new();

    public bool Disposed { get; private set; }

    /// <summary>Was beim Verbinden automatisch vom Server kommt (z. B. connection_established).</summary>
    public string? OnConnect { get; init; }

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        ConnectedTo = uri;
        if (OnConnect is not null)
        {
            ServerSends(OnConnect);
        }

        return Task.CompletedTask;
    }

    public Task SendAsync(string message, CancellationToken cancellationToken)
    {
        Sent.Enqueue(message);
        _sentSignal.Writer.TryWrite(message);
        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken) => await _incoming.Reader.ReadAsync(cancellationToken);

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        _incoming.Writer.TryWrite(null);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    public void ServerSends(string json) => _incoming.Writer.TryWrite(json);

    /// <summary>Server trennt die Verbindung.</summary>
    public void ServerCloses() => _incoming.Writer.TryWrite(null);

    /// <summary>Wartet auf die nächste gesendete Nachricht mit diesem Event.</summary>
    public async Task<JsonElement> NextSentAsync(string eventName, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        while (true)
        {
            var message = await _sentSignal.Reader.ReadAsync(cts.Token);
            using var doc = JsonDocument.Parse(message);
            if (doc.RootElement.GetProperty("event").GetString() == eventName)
            {
                return doc.RootElement.Clone();
            }
        }
    }

    public static string Established(string socketId = "123.456", int activityTimeout = 30) =>
        JsonSerializer.Serialize(new
        {
            @event = "pusher:connection_established",
            data = JsonSerializer.Serialize(new { socket_id = socketId, activity_timeout = activityTimeout }),
        });
}
