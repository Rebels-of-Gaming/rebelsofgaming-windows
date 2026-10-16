using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using RoG.Core.Api;
using RoG.Core.Features.Notifications;
using RoG.Core.Realtime;

namespace RoG.App.Services;

/// <summary>
/// Echtzeit der App: startet den <see cref="RealtimeHub"/> (nach Login/Abmelden neu), hält die Zähler für Glocke und
/// Nachrichten und reicht alle Ereignisse im UI-Thread weiter. Neue Benachrichtigungen werden als Windows-Toast
/// gezeigt (Einstellung) und setzen die Zahl am Taskleisten-Symbol.
/// </summary>
public sealed partial class RealtimeService : ObservableObject, IAsyncDisposable
{
    private readonly AppServices _services;
    private readonly RealtimeHub _hub;
    private readonly NotificationService _notifications;
    private DispatcherQueue? _dispatcher;

    public RealtimeService(AppServices services)
    {
        _services = services;
        _notifications = new NotificationService(services.Api.Client);
        var userAgent = $"RoG-Windows/{AppServices.AppVersion}";
        _hub = new RealtimeHub(
            ct => RealtimeConfig.FetchAsync(services.Api.Http, ct),
            config => new ReverbClient(config, new HttpChannelAuthorizer(services.Api.Http, config.AuthEndpoint), () => new ClientWebSocketConnection(userAgent), AppServices.AppVersion));

        _hub.NotificationReceived += (_, n) => OnUi(() => OnNotification(n));
        _hub.UnreadChanged += (_, n) => OnUi(() => UnreadNotifications = n);
        _hub.MessagesUnreadChanged += (_, n) => OnUi(() => UnreadMessages = n);
        _hub.LiveChanged += (_, _) => OnUi(() => LiveChanged?.Invoke(this, EventArgs.Empty));
        _hub.ServerStatusChanged += (_, s) => OnUi(() => ServerStatusChanged?.Invoke(this, s));
        _hub.Reconnected += (_, _) => OnUi(() => _ = RefreshCountersAsync());
        _hub.StateChanged += (_, s) => OnUi(() => State = s);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badge))]
    public partial int UnreadNotifications { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badge))]
    public partial int UnreadMessages { get; set; }

    [ObservableProperty]
    public partial RealtimeState State { get; set; }

    /// <summary>Zahl für Taskleiste und Tray: ungelesene Benachrichtigungen + Unterhaltungen.</summary>
    public int Badge => UnreadNotifications + UnreadMessages;

    /// <summary>Neue Benachrichtigung (nach dem Toast) – die offene Glocke-Seite stellt sie vorn an.</summary>
    public event EventHandler<NotificationItem>? NotificationReceived;

    public event EventHandler? LiveChanged;

    public event EventHandler<ServerStatusUpdate>? ServerStatusChanged;

    /// <summary>Im UI-Thread aufrufen (merkt sich die DispatcherQueue). Gast: nur öffentliche Kanäle.</summary>
    public async Task StartAsync(string? userId)
    {
        _dispatcher ??= DispatcherQueue.GetForCurrentThread();
        try
        {
            await _hub.StartAsync(userId);
        }
        catch (Exception ex) when (ex is System.Net.WebSockets.WebSocketException or HttpRequestException or ApiProblemException or OperationCanceledException or InvalidOperationException)
        {
            // Reverb nicht erreichbar – die App läuft ohne Live-Aktualisierung (Neuverbindung macht der Client selbst)
        }

        if (userId is null)
        {
            UnreadNotifications = 0;
            UnreadMessages = 0;
        }
        else
        {
            await RefreshCountersAsync();
        }
    }

    public Task StopAsync() => _hub.StopAsync();

    /// <summary>Zähler per API (Start, nach Neuverbindung, nach „alle gelesen“).</summary>
    public async Task RefreshCountersAsync()
    {
        if (await _services.Tokens.GetAsync() is null)
        {
            return;
        }

        try
        {
            UnreadNotifications = await _notifications.UnreadCountAsync();
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // Zähler bleibt beim letzten Stand
        }
    }

    partial void OnUnreadNotificationsChanged(int value) => BadgeService.Set(Badge);

    partial void OnUnreadMessagesChanged(int value) => BadgeService.Set(Badge);

    private void OnNotification(NotificationItem item)
    {
        UnreadNotifications++;
        if (_services.Settings.ToastNotifications)
        {
            ToastService.Show(item);
        }

        NotificationReceived?.Invoke(this, item);
    }

    private void OnUi(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcher.TryEnqueue(() => action());
        }
    }

    public async ValueTask DisposeAsync() => await _hub.DisposeAsync();
}
