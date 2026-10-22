using System.Text.Json;
using RoG.Core.Features.Notifications;

namespace RoG.Core.Realtime;

/// <summary>Statusänderung eines öffentlichen Gameservers (Kanal <c>servers</c>, ohne Spielernamen).</summary>
public sealed record ServerStatusUpdate(string Id, string Slug, bool? Online, string? Map, int Players, int MaxPlayers);

/// <summary>
/// Echtzeit für die ganze App: verbindet Reverb mit den Daten aus <c>GET /v1</c>, abonniert die öffentlichen Kanäle
/// <c>live</c> und <c>servers</c> sowie – angemeldet – den eigenen Kanal <c>private-App.Models.User.{id}</c>, und
/// übersetzt Ereignisse in typisierte .NET-Ereignisse (die App wechselt selbst in den UI-Thread). Fällt Reverb aus,
/// läuft die App ohne Live-Aktualisierung weiter; nach einer Neuverbindung meldet <see cref="Reconnected"/>, dass
/// verpasste Daten per API nachgeladen werden sollen.
/// </summary>
public sealed class RealtimeHub : IAsyncDisposable
{
    /// <summary>Laravel sendet Datenbank-/Broadcast-Benachrichtigungen unter dem Klassennamen (Echo: <c>notification()</c>).</summary>
    public const string NotificationEvent = @"Illuminate\Notifications\Events\BroadcastNotificationCreated";

    private readonly Func<RealtimeConfig, ReverbClient> _clientFactory;
    private readonly Func<CancellationToken, Task<RealtimeConfig?>> _configSource;
    private readonly Func<DateTimeOffset> _now;
    private ReverbClient? _client;
    private string? _userChannel;

    public RealtimeHub(Func<CancellationToken, Task<RealtimeConfig?>> configSource, Func<RealtimeConfig, ReverbClient> clientFactory, Func<DateTimeOffset>? now = null)
    {
        _configSource = configSource ?? throw new ArgumentNullException(nameof(configSource));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public event EventHandler<NotificationItem>? NotificationReceived;

    /// <summary>Ungelesen-Zähler der Glocke (<c>.inbox.changed</c>).</summary>
    public event EventHandler<int>? UnreadChanged;

    /// <summary>Zahl ungelesener Unterhaltungen (<c>.messages.unread</c>).</summary>
    public event EventHandler<int>? MessagesUnreadChanged;

    /// <summary>Live-Ticker, TeamSpeak-Viewer usw. haben sich geändert (<c>.live.updated</c>, ohne Inhalt).</summary>
    public event EventHandler? LiveChanged;

    /// <summary>Eigene Freunde oder Anfragen haben sich geändert (<c>.friends.changed</c>, ohne Inhalt – neu laden).</summary>
    public event EventHandler? FriendsChanged;

    public event EventHandler<ServerStatusUpdate>? ServerStatusChanged;

    /// <summary>Alle übrigen Ereignisse des eigenen Kanals (z. B. <c>.media.processed</c>) für spätere Module.</summary>
    public event EventHandler<ReverbEvent>? UserEvent;

    public event EventHandler? Reconnected;

    public event EventHandler<RealtimeState>? StateChanged;

    public RealtimeState State => _client?.State ?? RealtimeState.Disconnected;

    /// <summary>Verbinden (bzw. neu verbinden, z. B. nach Login/Abmelden). Ohne Echtzeit-Konfiguration: <c>false</c>.</summary>
    public async Task<bool> StartAsync(string? userId, CancellationToken cancellationToken = default)
    {
        await StopAsync().ConfigureAwait(false);
        var config = await _configSource(cancellationToken).ConfigureAwait(false);
        if (config is null)
        {
            return false;
        }

        var client = _client = _clientFactory(config);
        client.StateChanged += (_, s) => StateChanged?.Invoke(this, s);
        client.Reconnected += (_, _) => Reconnected?.Invoke(this, EventArgs.Empty);

        client.Subscribe("live").EventReceived += (_, e) => Dispatch(e);
        client.Subscribe("servers").EventReceived += (_, e) => Dispatch(e);
        if (!string.IsNullOrEmpty(userId))
        {
            _userChannel = $"private-App.Models.User.{userId}";
            client.Subscribe(_userChannel).EventReceived += (_, e) => Dispatch(e);
        }

        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Weiteren Kanal abonnieren (z. B. <c>private-conversations.{id}</c> eines offenen Chats); ohne Verbindung <c>null</c>.</summary>
    public ReverbChannel? Subscribe(string channelName) => _client?.Subscribe(channelName);

    public Task UnsubscribeAsync(string channelName) => _client?.UnsubscribeAsync(channelName) ?? Task.CompletedTask;

    public async Task StopAsync()
    {
        var client = _client;
        _client = null;
        _userChannel = null;
        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Ein Ereignis zuordnen (öffentlich für Tests; die App ruft es nicht selbst auf).</summary>
    internal void Dispatch(ReverbEvent e)
    {
        var name = e.Name.TrimStart('.');
        switch (name)
        {
            case "live.updated":
                LiveChanged?.Invoke(this, EventArgs.Empty);
                return;
            case "friends.changed":
                FriendsChanged?.Invoke(this, EventArgs.Empty);
                return;
            case "server.status":
                if (ParseServer(e.DataJson) is { } status)
                {
                    ServerStatusChanged?.Invoke(this, status);
                }

                return;
            case "inbox.changed":
                if (Count(e.DataJson) is { } unread)
                {
                    UnreadChanged?.Invoke(this, unread);
                }

                return;
            case "messages.unread":
                if (Count(e.DataJson) is { } conversations)
                {
                    MessagesUnreadChanged?.Invoke(this, conversations);
                }

                return;
            case NotificationEvent or "notification":
                if (NotificationItem.FromRealtime(e.DataJson, _now()) is { } item)
                {
                    NotificationReceived?.Invoke(this, item);
                }

                return;
        }

        if (e.Channel == _userChannel)
        {
            UserEvent?.Invoke(this, e);
        }
    }

    private static int? Count(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("unread", out var u) && u.ValueKind == JsonValueKind.Number && u.TryGetInt32(out var n) ? n : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ServerStatusUpdate? ParseServer(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var e = doc.RootElement;
            if (!e.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            static int Int(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v) ? v : 0;
            bool? online = e.TryGetProperty("online", out var o) && o.ValueKind is JsonValueKind.True or JsonValueKind.False ? o.GetBoolean() : null;
            var map = e.TryGetProperty("map", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            var slug = e.TryGetProperty("slug", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : string.Empty;
            return new ServerStatusUpdate(id.GetString()!, slug, online, map, Int(e, "players"), Int(e, "max_players"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
