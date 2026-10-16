using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Control;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Core.Realtime;

namespace RoG.App.ViewModels;

/// <summary>
/// Server-Steuerung im eigenen Fenster: Zustand und Ressourcen, Power-Knöpfe, Live-Konsole (Wings-WebSocket mit
/// kurzlebigem Token aus der API) und Befehle. Was angezeigt wird, folgt <see cref="ServerAbilities"/>; der Server prüft.
/// </summary>
public sealed partial class ServerConsoleViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaxLines = 2000;

    private readonly AppServices _services;
    private readonly string _slug;
    private readonly ServerControlService _control;
    private readonly DispatcherQueue _dispatcher;
    private ConsoleClient? _console;

    public ServerConsoleViewModel(AppServices services, string slug, string name, ServerAbilities abilities)
    {
        _services = services;
        _slug = slug;
        _control = new ServerControlService(services.Api.Client);
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Name = name;
        Abilities = abilities;
    }

    public string Name { get; }

    public ServerAbilities Abilities { get; }

    public bool CanPower => Abilities.Power;

    public ObservableCollection<string> Lines { get; } = [];

    /// <summary>Befehls-Historie für ↑/↓ in der Eingabe.</summary>
    public CommandHistory History { get; } = new();

    [ObservableProperty]
    public partial string StateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResourcesText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommand))]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommand))]
    public partial ConsoleLevel Level { get; set; }

    /// <summary>Befehle nur mit Recht, ohne Vier-Augen-Sperre und mit passendem Token-Level.</summary>
    public bool CanCommand => IsConnected && Abilities.ConsoleCommand && Abilities.CanCommand && Level >= ConsoleLevel.Command;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Command { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    /// <summary>Neue Zeile in der Konsole – die Ansicht scrollt dann ans Ende.</summary>
    public event EventHandler? LineAdded;

    public async Task StartAsync()
    {
        await RefreshResourcesAsync();
        if (Abilities.Console)
        {
            await ConnectAsync();
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (_console is not null)
        {
            await _console.DisposeAsync();
            _console = null;
        }

        var userAgent = $"RoG-Windows/{AppServices.AppVersion}";
        var console = new ConsoleClient(ct => _control.GetConsoleTicketAsync(_slug, ct), origin => new ClientWebSocketConnection(userAgent, origin));
        console.Output += (_, line) => OnUi(() => AddLine(line));
        console.StatusChanged += (_, state) => OnUi(() => StateText = StateLabel(state));
        console.Stats += (_, json) =>
        {
            if (ServerResources.FromWingsStats(json) is { } r)
            {
                OnUi(() => ShowResources(r));
            }
        };
        console.Disconnected += (_, error) => OnUi(() =>
        {
            IsConnected = false;
            if (error is not null)
            {
                Show(_services.Localizer.GetString(StringKeys.ConsoleDisconnected), InfoBarSeverity.Warning);
            }
        });

        try
        {
            await console.ConnectAsync();
            _console = console;
            Level = console.Level;
            IsConnected = true;
        }
        catch (ApiProblemException ex)
        {
            await console.DisposeAsync();
            Show(_services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or System.Net.WebSockets.WebSocketException or HttpRequestException)
        {
            await console.DisposeAsync();
            Show(_services.Localizer.GetString(StringKeys.ConsoleDisconnected), InfoBarSeverity.Error);
        }
    }

    private bool CanSend() => !string.IsNullOrWhiteSpace(Command);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var command = Command.Trim();
        if (command.Length > 500)
        {
            return;
        }

        Command = string.Empty;
        History.Add(command);
        AddLine("> " + command);
        try
        {
            // Über die API (Audit-Log, Rechte serverseitig) statt direkt über den Socket
            await _control.SendCommandAsync(_slug, command);
        }
        catch (ApiProblemException ex)
        {
            Show(_services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            Show(_services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private Task StartServerAsync() => PowerAsync(PowerSignal.Start);

    [RelayCommand]
    private Task StopServerAsync() => PowerAsync(PowerSignal.Stop);

    [RelayCommand]
    private Task RestartServerAsync() => PowerAsync(PowerSignal.Restart);

    /// <summary>Kill fragt die Seite vorher (Datenverlust möglich).</summary>
    public Task KillServerAsync() => PowerAsync(PowerSignal.Kill);

    [RelayCommand]
    private void ClearLines() => Lines.Clear();

    private async Task PowerAsync(PowerSignal signal)
    {
        try
        {
            await _control.PowerAsync(_slug, signal);
            Show(_services.Localizer.GetString(StringKeys.ConsolePowerSent), InfoBarSeverity.Success);
            await RefreshResourcesAsync();
        }
        catch (ApiProblemException ex)
        {
            Show(_services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            Show(_services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    private async Task RefreshResourcesAsync()
    {
        if (!Abilities.Console)
        {
            return;
        }

        try
        {
            ShowResources(await _control.GetResourcesAsync(_slug));
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // Ressourcen sind Beiwerk – die Konsole liefert sie ohnehin laufend
        }
    }

    private void ShowResources(ServerResources r)
    {
        StateText = StateLabel(r.State);
        var memory = r.MemoryLimit is { } limit ? $"{Mb(r.Memory)} / {Mb(limit)}" : Mb(r.Memory);
        ResourcesText = string.Format(CultureInfo.CurrentCulture, _services.Localizer.GetString(StringKeys.ConsoleResources),
            r.CpuPercent.ToString("0.0", CultureInfo.CurrentCulture), memory, Mb(r.Disk), Uptime(r.Uptime));
    }

    private string StateLabel(string state) => _services.Localizer.GetString(state switch
    {
        "running" => StringKeys.ConsoleStateRunning,
        "starting" => StringKeys.ConsoleStateStarting,
        "stopping" => StringKeys.ConsoleStateStopping,
        "offline" => StringKeys.ConsoleStateOffline,
        _ => StringKeys.ServersUnknown,
    });

    private static string Mb(long bytes) => $"{bytes / 1048576.0:0} MB";

    private static string Uptime(TimeSpan t) => t.TotalDays >= 1 ? $"{(int)t.TotalDays} d {t.Hours} h" : $"{t.Hours} h {t.Minutes} min";

    private void AddLine(string line)
    {
        foreach (var part in line.Split('\n'))
        {
            Lines.Add(part.TrimEnd('\r'));
        }

        while (Lines.Count > MaxLines)
        {
            Lines.RemoveAt(0);
        }

        LineAdded?.Invoke(this, EventArgs.Empty);
    }

    private void Show(string text, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }

    private void OnUi(Action action) => _dispatcher.TryEnqueue(() => action());

    public async ValueTask DisposeAsync()
    {
        if (_console is { } console)
        {
            _console = null;
            await console.DisposeAsync();
        }
    }
}
