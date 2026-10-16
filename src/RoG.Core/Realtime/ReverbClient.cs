using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoG.Core.Api;

namespace RoG.Core.Realtime;

public enum RealtimeState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,

    /// <summary>Server hat mit Code 4000–4099 abgelehnt (z. B. falscher App-Key) – kein automatischer Neuversuch.</summary>
    Failed,
}

/// <summary>Fehler aus <c>pusher:error</c>.</summary>
public sealed record PusherError(int? Code, string? Message);

/// <summary>
/// Client für Reverb über das Pusher-Protokoll 7 (<see cref="ClientWebSocket"/>): Verbindung, <c>socket_id</c>,
/// öffentliche/private/Presence-Kanäle (Autorisierung über <c>/v1/broadcasting/auth</c>), Ping/Pong, automatische
/// Neuverbindung mit Backoff und erneutem Abonnieren. Verpasste Ereignisse liefert Reverb nicht nach – nach
/// <see cref="Reconnected"/> gleicht die App per API ab.
/// </summary>
public sealed class ReverbClient : IAsyncDisposable
{
    private static readonly TimeSpan s_connectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan s_maxBackoff = TimeSpan.FromSeconds(30);

    private readonly RealtimeConfig _config;
    private readonly IChannelAuthorizer _authorizer;
    private readonly Func<IWebSocketConnection> _socketFactory;
    private readonly string _clientVersion;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<TimeSpan, CancellationToken, Task> _idleDelay;
    private readonly ConcurrentDictionary<string, ReverbChannel> _channels = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();

    private IWebSocketConnection? _socket;
    private TaskCompletionSource<string>? _established;
    private Task? _loop;
    private int _reconnectAttempt;
    private bool _everConnected;

    public ReverbClient(
        RealtimeConfig config,
        IChannelAuthorizer authorizer,
        Func<IWebSocketConnection> socketFactory,
        string clientVersion,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<TimeSpan, CancellationToken, Task>? idleDelay = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
        _clientVersion = clientVersion;
        _delay = delay ?? Task.Delay;
        _idleDelay = idleDelay ?? Task.Delay;
    }

    public RealtimeState State { get; private set; } = RealtimeState.Disconnected;

    /// <summary>Kennung der Verbindung (für <c>X-Socket-ID</c>, damit eigene Ereignisse nicht zurückkommen).</summary>
    public string? SocketId { get; private set; }

    /// <summary>Sekunden ohne Nachricht, nach denen der Client selbst einen Ping sendet (vom Server vorgegeben).</summary>
    public int ActivityTimeoutSeconds { get; private set; } = 120;

    public event EventHandler<RealtimeState>? StateChanged;

    public event EventHandler<PusherError>? Error;

    /// <summary>Nach einer Neuverbindung: verpasste Ereignisse per API nachladen.</summary>
    public event EventHandler? Reconnected;

    /// <summary>Verbindet und wartet auf <c>pusher:connection_established</c>.</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_lifetime.IsCancellationRequested, this);
        if (State is RealtimeState.Connected or RealtimeState.Connecting)
        {
            return;
        }

        SetState(RealtimeState.Connecting);
        await OpenSocketAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Abonniert einen Kanal (oder liefert den vorhandenen). Ohne Verbindung wird beim Verbinden abonniert.</summary>
    public ReverbChannel Subscribe(string channelName)
    {
        ArgumentException.ThrowIfNullOrEmpty(channelName);
        var created = false;
        var channel = _channels.GetOrAdd(channelName, name =>
        {
            created = true;
            return new ReverbChannel(name);
        });
        if (created && State == RealtimeState.Connected && SocketId is not null)
        {
            _ = SendSubscribeAsync(channel, SocketId, _lifetime.Token);
        }

        return channel;
    }

    public async Task UnsubscribeAsync(string channelName, CancellationToken cancellationToken = default)
    {
        if (_channels.TryRemove(channelName, out var channel))
        {
            channel.MarkUnsubscribed();
            if (State == RealtimeState.Connected)
            {
                await SendAsync("pusher:unsubscribe", new JsonObject { ["channel"] = channelName }, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        var socket = _socket;
        _socket = null;
        if (socket is not null)
        {
            try
            {
                await socket.CloseAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
            }
            catch (OperationCanceledException)
            {
            }

            await socket.DisposeAsync().ConfigureAwait(false);
        }

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        foreach (var channel in _channels.Values)
        {
            channel.MarkUnsubscribed();
        }

        SetState(RealtimeState.Disconnected);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_lifetime.IsCancellationRequested)
        {
            await DisconnectAsync().ConfigureAwait(false);
        }

        _lifetime.Dispose();
    }

    private async Task OpenSocketAsync(CancellationToken cancellationToken)
    {
        var socket = _socketFactory();
        _established = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        timeout.CancelAfter(s_connectTimeout);

        await socket.ConnectAsync(_config.BuildSocketUri(_clientVersion), timeout.Token).ConfigureAwait(false);
        _socket = socket;
        _loop = Task.Run(() => ReceiveLoopAsync(socket), CancellationToken.None);

        await using (timeout.Token.Register(() => _established.TrySetCanceled()))
        {
            await _established.Task.ConfigureAwait(false);
        }
    }

    private async Task ReceiveLoopAsync(IWebSocketConnection socket)
    {
        var token = _lifetime.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                string? message;
                using (var activity = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    // Pusher: nach activity_timeout ohne Nachricht selbst pingen, Antwort binnen 30 s erwarten.
                    var pingTask = PingWhenIdleAsync(activity.Token);
                    try
                    {
                        message = await socket.ReceiveAsync(token).ConfigureAwait(false);
                    }
                    finally
                    {
                        await activity.CancelAsync().ConfigureAwait(false);
                        await pingTask.ConfigureAwait(false);
                    }
                }

                if (message is null)
                {
                    break; // Gegenseite hat geschlossen
                }

                if (message.Length > 0)
                {
                    try
                    {
                        await HandleMessageAsync(message, token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
                    {
                        // Unerwartetes Nachrichtenformat – Nachricht verwerfen, Verbindung behalten.
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (WebSocketException)
        {
            // Verbindung weg – unten neu verbinden
        }
        catch (IOException)
        {
        }

        _established?.TrySetException(new WebSocketException(WebSocketError.ConnectionClosedPrematurely));
        if (!token.IsCancellationRequested && State != RealtimeState.Failed)
        {
            await ReconnectAsync(token).ConfigureAwait(false);
        }
    }

    private async Task PingWhenIdleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _idleDelay(TimeSpan.FromSeconds(ActivityTimeoutSeconds), cancellationToken).ConfigureAwait(false);
            await SendAsync("pusher:ping", new JsonObject(), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
    }

    private async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        foreach (var channel in _channels.Values)
        {
            channel.MarkUnsubscribed();
        }

        SocketId = null;
        SetState(RealtimeState.Reconnecting);
        var old = _socket;
        _socket = null;
        if (old is not null)
        {
            await old.DisposeAsync().ConfigureAwait(false);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var wait = Backoff(_reconnectAttempt++);
            try
            {
                await _delay(wait, cancellationToken).ConfigureAwait(false);
                await OpenSocketAsync(cancellationToken).ConfigureAwait(false);
                return; // neue Empfangsschleife läuft, Kanäle werden in connection_established neu abonniert
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException or HttpRequestException)
            {
                // nächster Versuch
            }
        }
    }

    internal static TimeSpan Backoff(int attempt)
    {
        var seconds = Math.Min(s_maxBackoff.TotalSeconds, Math.Pow(2, Math.Min(attempt, 10)));
        return TimeSpan.FromSeconds(seconds);
    }

    private async Task HandleMessageAsync(string message, CancellationToken cancellationToken)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(message);
        }
        catch (JsonException)
        {
            return;
        }

        var eventName = root?["event"]?.GetValue<string>();
        if (eventName is null)
        {
            return;
        }

        var channelName = root!["channel"] is JsonValue c && c.TryGetValue<string>(out var cn) ? cn : null;
        var data = UnwrapData(root["data"]);

        switch (eventName)
        {
            case "pusher:connection_established":
                await OnEstablishedAsync(data, cancellationToken).ConfigureAwait(false);
                break;
            case "pusher:ping":
                await SendAsync("pusher:pong", new JsonObject(), cancellationToken).ConfigureAwait(false);
                break;
            case "pusher:pong":
                break;
            case "pusher:error":
                await OnErrorAsync(data).ConfigureAwait(false);
                break;
            case "pusher_internal:subscription_succeeded":
                if (channelName is not null && _channels.TryGetValue(channelName, out var ok))
                {
                    ok.OnSubscriptionSucceeded(data);
                }

                break;
            case "pusher:subscription_error":
                if (channelName is not null && _channels.TryGetValue(channelName, out var failed))
                {
                    failed.OnSubscriptionFailed(ReadInt(data, "status"));
                }

                break;
            case "pusher_internal:member_added":
                if (channelName is not null && _channels.TryGetValue(channelName, out var added))
                {
                    added.OnMemberAdded(data);
                }

                break;
            case "pusher_internal:member_removed":
                if (channelName is not null && _channels.TryGetValue(channelName, out var removed))
                {
                    removed.OnMemberRemoved(data);
                }

                break;
            default:
                if (channelName is not null && _channels.TryGetValue(channelName, out var channel))
                {
                    channel.OnEvent(eventName, data);
                }

                break;
        }
    }

    private async Task OnEstablishedAsync(string data, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(data);
        var socketId = doc.RootElement.GetProperty("socket_id").GetString()!;
        if (doc.RootElement.TryGetProperty("activity_timeout", out var at) && at.ValueKind == JsonValueKind.Number && at.TryGetInt32(out var seconds) && seconds > 0)
        {
            ActivityTimeoutSeconds = seconds;
        }

        SocketId = socketId;
        var wasReconnect = _everConnected;
        _everConnected = true;
        _reconnectAttempt = 0;
        SetState(RealtimeState.Connected);
        _established?.TrySetResult(socketId);

        foreach (var channel in _channels.Values)
        {
            await SendSubscribeAsync(channel, socketId, cancellationToken).ConfigureAwait(false);
        }

        if (wasReconnect)
        {
            Reconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task OnErrorAsync(string data)
    {
        var code = ReadInt(data, "code");
        var message = ReadString(data, "message");
        Error?.Invoke(this, new PusherError(code, message));
        if (code is >= 4000 and <= 4099)
        {
            // Nicht neu verbinden (falscher Key, SSL nötig, App gesperrt …)
            SetState(RealtimeState.Failed);
            _established?.TrySetException(new InvalidOperationException($"pusher:error {code}: {message}"));
            await _lifetime.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task SendSubscribeAsync(ReverbChannel channel, string socketId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject { ["channel"] = channel.Name };
        if (channel.IsPrivate)
        {
            try
            {
                var auth = await _authorizer.AuthorizeAsync(socketId, channel.Name, cancellationToken).ConfigureAwait(false);
                payload["auth"] = auth.Auth;
                if (auth.ChannelData is not null)
                {
                    payload["channel_data"] = auth.ChannelData;
                }
            }
            catch (ApiProblemException ex)
            {
                channel.OnSubscriptionFailed(ex.Problem.Status);
                return;
            }
        }

        try
        {
            await SendAsync("pusher:subscribe", payload, cancellationToken).ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            // Verbindung weg – nach der Neuverbindung wird erneut abonniert
        }
    }

    private Task SendAsync(string eventName, JsonObject data, CancellationToken cancellationToken)
    {
        var socket = _socket ?? throw new WebSocketException(WebSocketError.InvalidState, "Nicht verbunden.");
        var message = new JsonObject { ["event"] = eventName, ["data"] = data };
        return socket.SendAsync(message.ToJsonString(), cancellationToken);
    }

    private void SetState(RealtimeState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(this, state);
    }

    /// <summary>Pusher sendet <c>data</c> meist als JSON-String – hier immer als JSON-Text zurückgeben.</summary>
    internal static string UnwrapData(JsonNode? data)
    {
        if (data is null)
        {
            return "{}";
        }

        if (data is JsonValue value && value.TryGetValue<string>(out var text))
        {
            try
            {
                using var _ = JsonDocument.Parse(text);
                return text;
            }
            catch (JsonException)
            {
                return JsonValue.Create(text).ToJsonString(); // reiner Text → als JSON-String
            }
        }

        return data.ToJsonString();
    }

    private static int? ReadInt(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
