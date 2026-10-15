using System.Buffers;
using System.Net.WebSockets;
using System.Text;

namespace RoG.Core.Realtime;

/// <summary>Textbasierte WebSocket-Verbindung – Abstraktion, damit das Pusher-Protokoll ohne Netzwerk testbar ist.</summary>
public interface IWebSocketConnection : IAsyncDisposable
{
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    Task SendAsync(string message, CancellationToken cancellationToken);

    /// <summary>Nächste Textnachricht; <c>null</c>, wenn die Gegenseite die Verbindung geschlossen hat.</summary>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);

    Task CloseAsync(CancellationToken cancellationToken);
}

/// <summary>Echte Verbindung über <see cref="ClientWebSocket"/>.</summary>
public sealed class ClientWebSocketConnection : IWebSocketConnection
{
    private const int MaxMessageBytes = 1024 * 1024;

    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public ClientWebSocketConnection(string userAgent)
    {
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        _socket.Options.SetRequestHeader("User-Agent", userAgent);
    }

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) => _socket.ConnectAsync(uri, cancellationToken);

    public async Task SendAsync(string message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(8 * 1024);
        try
        {
            using var message = new MemoryStream();
            while (true)
            {
                var result = await _socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                message.Write(buffer, 0, result.Count);
                if (message.Length > MaxMessageBytes)
                {
                    throw new WebSocketException(WebSocketError.Faulted, "Nachricht zu groß.");
                }

                if (result.EndOfMessage)
                {
                    return result.MessageType == WebSocketMessageType.Text ? Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length) : string.Empty;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        _sendLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
