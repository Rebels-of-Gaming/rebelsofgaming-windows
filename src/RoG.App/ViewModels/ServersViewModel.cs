using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// Server-Seite: Gameserver mit Live-Stand, „Verbinden“ (Steam, Enemy Territory, Minecraft-Adresse) und „Adresse kopieren“.
/// Lädt beim Öffnen und danach jede Minute still nach (der Query-Worker der Website fragt minütlich ab).
/// </summary>
public sealed partial class ServersViewModel(AppServices services) : ObservableObject
{
    private readonly ServerService _service = new(services.Api.Client);
    private bool _loading;

    public ObservableCollection<ServerItemViewModel> Servers { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(IsEmpty))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsEmpty => !IsLoading && !HasError && Servers.Count == 0;

    /// <summary>„Neuer Gameserver“: Recht <c>servers.create</c> (nur mit 2FA vergeben) und Scope <c>servers:manage</c>.</summary>
    public bool CanProvision => services.CurrentUser is { } me && me.Can(ServerProvisioningService.Permission) && me.HasScope(ServerProvisioningService.Scope);

    [RelayCommand]
    private void Provision() => Views.ServerProvisionWindow.Open(null, () => _ = LoadAsync(silent: true));

    /// <summary>Neu laden; <paramref name="silent"/> = ohne Ladeanzeige (Hintergrund-Aktualisierung, Liste bleibt stehen).</summary>
    public async Task LoadAsync(bool silent = false)
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        IsLoading = !silent || Servers.Count == 0;
        try
        {
            var servers = await _service.GetAllAsync();
            ErrorMessage = null;
            Servers.Clear();
            foreach (var server in servers)
            {
                Servers.Add(new ServerItemViewModel(server, services.Localizer, ConnectAsync, Copy)
                {
                    CanManageProvisioning = server.Provisioning is not null && CanProvision,
                    ManageProvisioning = () => Views.ServerProvisionWindow.Open(server, () => _ = LoadAsync(silent: true)),
                });
            }
        }
        catch (ApiProblemException ex)
        {
            // Bei stiller Aktualisierung die letzte Liste stehen lassen
            if (!silent || Servers.Count == 0)
            {
                ErrorMessage = services.Localizer.GetError(ex.Problem.Code);
            }
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            // Antwort passt nicht zur Spec (z. B. neue Website-Version) – allgemeiner Fehler statt Absturz
            if (!silent || Servers.Count == 0)
            {
                ErrorMessage = services.Localizer.GetError(ApiProblem.Codes.Unknown);
            }
        }
        finally
        {
            _loading = false;
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    private async Task ConnectAsync(ServerItemViewModel item)
    {
        var message = ConnectMessage.Describe(await services.ServerConnector.ConnectAsync(item.Server), item.Name, services.Localizer);
        ShowFeedback(message.Text, ToSeverity(message.Tone));
    }

    internal static InfoBarSeverity ToSeverity(MessageTone tone) => tone switch
    {
        MessageTone.Success => InfoBarSeverity.Success,
        MessageTone.Error => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    };

    private void Copy(ServerItemViewModel item)
    {
        var ok = services.ServerConnector.Copy(item.Address);
        ShowFeedback(Format(ok ? StringKeys.ServersCopied : StringKeys.ServersCopyFailed, item.Address), ok ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private void ShowFeedback(string message, InfoBarSeverity severity)
    {
        // Schließen und wieder öffnen, damit Sprachausgabe/Animation auch bei gleicher Meldung neu auslösen
        IsFeedbackOpen = false;
        FeedbackMessage = message;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }

    private string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, services.Localizer.GetString(key), args);
}

/// <summary>Eine Server-Karte.</summary>
public sealed partial class ServerItemViewModel : ObservableObject
{
    public ServerItemViewModel(GameServer server, ILocalizer localizer, Func<ServerItemViewModel, Task> connect, Action<ServerItemViewModel> copy)
    {
        Server = server;
        var plan = ServerConnector.Plan(server);
        CanConnect = plan.Kind != ConnectKind.None;

        StatusText = localizer.GetString(server.Online switch
        {
            true => StringKeys.ServersOnline,
            false => StringKeys.ServersOffline,
            null => StringKeys.ServersUnknown,
        });
        StatusBrush = (Brush)Application.Current.Resources[server.Online == true ? "RoGStatusOnlineBrush" : "RoGStatusOfflineBrush"];
        PlayersText = string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.ServersPlayers), server.Players, server.MaxPlayers);
        Players = server.PlayerList?.Select(p => p.MemberName is null ? p.Name : $"{p.Name} · {p.MemberName}").ToList() ?? [];
        PlayersHeader = string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.ServersPlayerListHeader), Players.Count);
        ConnectAutomationName = string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.ServersConnectAutomation), server.Name);
        CopyAutomationName = string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.ServersCopyAutomation), server.Name);

        ConnectCommand = new AsyncRelayCommand(() => connect(this));
        CopyCommand = new RelayCommand(() => copy(this));
        ControlCommand = new RelayCommand(() => Views.ServerConsoleWindow.Open(Server));
    }

    public GameServer Server { get; }

    public string Name => Server.Name;

    public string Game => Server.GameName ?? string.Empty;

    public bool HasGame => !string.IsNullOrEmpty(Server.GameName);

    public string Address => Server.Address;

    public string Map => Server.Map ?? string.Empty;

    public bool HasMap => Server.Map is not null;

    public bool CanConnect { get; }

    public string StatusText { get; }

    public Brush StatusBrush { get; }

    public string PlayersText { get; }

    public bool ShowPlayers => Server.Online == true;

    public IReadOnlyList<string> Players { get; }

    public bool HasPlayerList => Players.Count > 0;

    public string PlayersHeader { get; }

    public string ConnectAutomationName { get; }

    public string CopyAutomationName { get; }

    public IAsyncRelayCommand ConnectCommand { get; }

    public IRelayCommand CopyCommand { get; }

    /// <summary>Breite der Verlaufslinie in der Karte (epx).</summary>
    public const double HistoryWidth = 280;

    private bool _historyLoaded;

    /// <summary>Auslastung der letzten 24 h als Linie (Punkte relativ zu <see cref="HistoryWidth"/>×60).</summary>
    [ObservableProperty]
    public partial Microsoft.UI.Xaml.Media.PointCollection? HistoryPoints { get; set; }

    [ObservableProperty]
    public partial string HistoryText { get; set; } = string.Empty;

    /// <summary>Verlauf beim ersten Aufklappen laden (eine Anfrage je Karte).</summary>
    public async Task LoadHistoryAsync()
    {
        if (_historyLoaded)
        {
            return;
        }

        _historyLoaded = true;
        var l = App.Services.Localizer;
        try
        {
            var history = await new ServerService(App.Services.Api.Client).HistoryAsync(Server.Slug);
            var points = new Microsoft.UI.Xaml.Media.PointCollection();
            foreach (var (x, y) in history.Polyline(HistoryWidth, 60))
            {
                points.Add(new Windows.Foundation.Point(x, y));
            }

            HistoryPoints = points;
            HistoryText = history.Points.Count == 0
                ? l.GetString(StringKeys.ServersHistoryEmpty)
                : string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.ServersHistorySummary),
                    history.Peak.ToString("0", CultureInfo.CurrentCulture),
                    history.Uptime is { } up ? up.ToString("0.#", CultureInfo.CurrentCulture) : "–");
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            _historyLoaded = false;
            HistoryText = l.GetString(StringKeys.ServersHistoryEmpty);
        }
    }

    /// <summary>Steuern (Power/Konsole) – nur mit Rechten an diesem Server (<c>control</c> aus der API).</summary>
    public bool CanControl => Server.Control?.Any == true;

    /// <summary>Über die Website angelegt: Installationsstand und Ablauf als Hinweis auf der Karte.</summary>
    public bool HasProvisioning => Server.Provisioning is not null;

    public string ProvisioningText => Server.Provisioning is not { } p ? string.Empty : string.Join(" · ", new[]
    {
        App.Services.Localizer.GetString("Provision_Status_" + p.Status),
        p.ExpiresAt is { } at
            ? string.Format(CultureInfo.CurrentCulture, App.Services.Localizer.GetString(StringKeys.ProvisionExpiresAt), at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public Brush ProvisioningBrush => (Brush)Application.Current.Resources[
        Server.Provisioning is { } p && (p.IsFailed || p.ExpiresSoon(DateTimeOffset.Now)) ? "RoGRedBrush" : Server.Provisioning?.IsInstalling == true ? "RoGGoldBrush" : "RoGMutedBrush"];

    public bool CanManageProvisioning { get; init; }

    public Action? ManageProvisioning { get; init; }

    [RelayCommand]
    private void Manage() => ManageProvisioning?.Invoke();

    public IRelayCommand ControlCommand { get; }
}
