using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Events;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Termine: die nächsten 60 Tage nach Tagen gruppiert, Detail mit Zusage und „In Kalender übernehmen“.</summary>
public sealed partial class EventsViewModel : LoadableViewModel
{
    private readonly EventService _events;

    public EventsViewModel(AppServices services)
        : base(services)
    {
        _events = new EventService(services.Api.Client);
        Detail = new EventDetailViewModel(services, _events);
        Detail.Deleted += (_, id) =>
        {
            if (Items.FirstOrDefault(i => i.Id == id) is { } row)
            {
                Items.Remove(row);
                Selected = null;
            }
        };
        Detail.Changed += (_, item) =>
        {
            var row = Items.FirstOrDefault(i => i.Id == item.Id);
            if (row is not null)
            {
                Items[Items.IndexOf(row)] = new EventRowViewModel(item, row.ShowDayHeader, services.Localizer);
            }
        };
    }

    public ObservableCollection<EventRowViewModel> Items { get; } = [];

    public EventDetailViewModel Detail { get; }

    /// <summary>Termine anlegen: Recht <c>events.manage</c> (global = clanweit, im Squad = Squad-Termine) + Scope.</summary>
    public bool CanManage => Services.CurrentUser is { } me && me.CanAnywhere("events.manage") && me.HasScope("events:write");

    /// <summary>Auswahl für den Dialog „Neuer Termin“: Typen und die Bereiche, in denen man Termine anlegen darf.</summary>
    public async Task<(IReadOnlyList<SettingsOption> Types, IReadOnlyList<SettingsOption> Scopes)> CreateOptionsAsync()
    {
        var admin = new EventAdminService(Services.Api.Client, Services.Api.Http);
        var types = (await admin.TypesAsync()).Select(t => new SettingsOption(t.Key, t.Label)).ToList();
        var me = Services.CurrentUser;
        var scopes = new List<SettingsOption>();
        if (me?.Can("events.manage") == true)
        {
            scopes.Add(new SettingsOption(string.Empty, Services.Localizer.GetString(StringKeys.EventsScopeClan)));
        }

        var squadIds = me?.Permissions.Where(p => p.Key.StartsWith("squad:", StringComparison.Ordinal) && p.Value.Contains("events.manage"))
            .Select(p => p.Key["squad:".Length..]).ToHashSet() ?? [];
        if (squadIds.Count > 0)
        {
            foreach (var squad in await new RoG.Core.Features.Squads.SquadService(Services.Api.Client).ListAsync())
            {
                if (squadIds.Contains(squad.Id))
                {
                    scopes.Add(new SettingsOption(squad.Id, squad.Name));
                }
            }
        }

        return (types, scopes);
    }

    /// <summary>Anlegen, Liste neu laden und den neuen Termin öffnen; Rückgabe = Fehlermeldung oder <c>null</c>.</summary>
    public async Task<string?> CreateAsync(EventDraft draft)
    {
        try
        {
            var id = await new EventAdminService(Services.Api.Client, Services.Api.Http).CreateAsync(draft);
            await OpenAsync(id);
            return null;
        }
        catch (ApiProblemException ex)
        {
            return ex.Problem.Code == "validation_failed" && ex.Problem.Detail is { } d ? d : Services.Localizer.GetError(ex.Problem.Code);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or ArgumentException)
        {
            return Services.Localizer.GetError(ApiProblem.Codes.Unknown);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial EventRowViewModel? Selected { get; set; }

    public bool IsDetailOpen => Selected is not null;

    public bool ShowSelectHint => Selected is null;

    protected override bool HasData => Items.Count > 0;

    /// <summary>Liste laden und den Termin <paramref name="id"/> öffnen (Startseite, Links).</summary>
    public async Task OpenAsync(string id)
    {
        await LoadAsync();
        Selected = Items.FirstOrDefault(i => i.Id == id);
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var page = await _events.ListAsync(DateTimeOffset.Now.Date, 60, null, cancellationToken);
        var selected = Selected?.Id;
        Items.Clear();
        DateTime? day = null;
        foreach (var e in page.Items)
        {
            var thisDay = e.StartsAt?.ToLocalTime().Date;
            Items.Add(new EventRowViewModel(e, thisDay != day, Services.Localizer));
            day = thisDay;
        }

        if (selected is not null)
        {
            Selected = Items.FirstOrDefault(i => i.Id == selected);
        }
    }

    partial void OnSelectedChanged(EventRowViewModel? value)
    {
        if (value is not null)
        {
            Detail.Show(value.Item);
        }
    }
}

/// <summary>Zeile: Tagesüberschrift (beim ersten Termin des Tages), Uhrzeit, Titel, Typ-Farbe, eigene Antwort.</summary>
public sealed class EventRowViewModel(EventItem item, bool showDayHeader, ILocalizer localizer)
{
    public EventItem Item => item;

    public string Id => item.Id;

    public bool ShowDayHeader => showDayHeader;

    public string DayHeader { get; } = item.StartsAt?.ToLocalTime().ToString("dddd, d. MMMM", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture) ?? string.Empty;

    public string Time { get; } = item.StartsAt?.ToLocalTime().ToString("t", CultureInfo.CurrentCulture) ?? string.Empty;

    public string Title => item.Title;

    public string Subtitle { get; } = string.Join(" · ", new[] { item.TypeLabel, item.SquadName, item.LocationLabel }.Where(s => !string.IsNullOrEmpty(s)));

    public Brush TypeBrush { get; } = EventDetailViewModel.BrushOf(item.Color);

    public string RsvpText { get; } = EventDetailViewModel.RsvpLabel(item.MyRsvp, localizer);

    public bool HasRsvp => item.MyRsvp != Rsvp.None;

    public bool Cancelled => item.Cancelled;

    public double Opacity => item.Cancelled ? 0.5 : 1;
}

public sealed partial class EventDetailViewModel(AppServices services, EventService events) : LoadableViewModel(services)
{
    private EventItem? _item;

    /// <summary>Termin hat sich geändert (Zusage) – die Liste aktualisiert ihre Zeile.</summary>
    public event EventHandler<EventItem>? Changed;

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    public partial string? TypeLabel { get; set; }

    [ObservableProperty]
    public partial Brush? TypeBrush { get; set; }

    [ObservableProperty]
    public partial string? When { get; set; }

    [ObservableProperty]
    public partial string? Where { get; set; }

    [ObservableProperty]
    public partial string? Counts { get; set; }

    [ObservableProperty]
    public partial string? Flags { get; set; }

    [ObservableProperty]
    public partial string? Description { get; set; }

    [ObservableProperty]
    public partial ServerItemViewModel? Server { get; set; }

    [ObservableProperty]
    public partial bool HasServer { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsYes), nameof(IsMaybe), nameof(IsNo))]
    public partial Rsvp MyRsvp { get; set; }

    [ObservableProperty]
    public partial bool CanRespond { get; set; }

    [ObservableProperty]
    public partial bool IsResponding { get; set; }

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    public bool IsYes => MyRsvp == Rsvp.Yes;

    public bool IsMaybe => MyRsvp == Rsvp.Maybe;

    public bool IsNo => MyRsvp == Rsvp.No;

    protected override bool HasData => _item is not null;

    public void Show(EventItem item)
    {
        Apply(item);
        _ = RefreshFromServerAsync(item.Id);
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_item is not null)
        {
            Apply(await events.GetAsync(_item.Id, cancellationToken));
        }
    }

    private async Task RefreshFromServerAsync(string id)
    {
        // Liste liefert schon alles; das Detail holt nur still den neuesten Stand (Zusagen anderer)
        try
        {
            var fresh = await events.GetAsync(id);
            if (_item?.Id == id)
            {
                Apply(fresh);
            }
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // Stand aus der Liste bleibt
        }
    }

    private async void Apply(EventItem item)
    {
        _item = item;
        Title = item.Title;
        TypeLabel = item.TypeLabel.ToUpper(CultureInfo.CurrentCulture);
        TypeBrush = BrushOf(item.Color);
        When = WhenOf(item);
        Where = item.LocationLabel;
        Counts = item.MaxParticipants is { } max
            ? Format(StringKeys.EventsCountsMax, item.Yes, item.Maybe, max)
            : Format(StringKeys.EventsCounts, item.Yes, item.Maybe);
        Flags = string.Join(" · ", new[]
        {
            item.Cancelled ? Services.Localizer.GetString(StringKeys.EventsCancelled) : null,
            item.Mandatory ? Services.Localizer.GetString(StringKeys.EventsMandatory) : null,
            item.SquadName,
        }.Where(s => !string.IsNullOrEmpty(s)));
        Description = item.Description;
        Server = item.Server is null ? null : new ServerItemViewModel(item.Server, Services.Localizer, ConnectAsync, Copy);
        HasServer = Server is not null;
        MyRsvp = item.MyRsvp;
        CanRespond = !item.Cancelled && await Services.Tokens.GetAsync() is not null;
        DataChanged();
    }

    [RelayCommand]
    private Task RespondYesAsync() => RespondAsync(Rsvp.Yes);

    [RelayCommand]
    private Task RespondMaybeAsync() => RespondAsync(Rsvp.Maybe);

    [RelayCommand]
    private Task RespondNoAsync() => RespondAsync(Rsvp.No);

    private async Task RespondAsync(Rsvp answer)
    {
        if (_item is null || IsResponding)
        {
            return;
        }

        IsResponding = true;
        var item = _item;
        try
        {
            var result = await events.RespondAsync(item.Id, answer);
            var updated = await events.GetAsync(item.Id);
            Apply(updated);
            Changed?.Invoke(this, updated);
            Show(Format(StringKeys.EventsRsvpSaved, RsvpLabel(result, Services.Localizer)), InfoBarSeverity.Success);
        }
        catch (ApiProblemException ex) when (ex.Problem.IsNetworkError)
        {
            var status = answer switch { Rsvp.Yes => "yes", Rsvp.Maybe => "maybe", _ => "no" };
            await Services.Outbox.EnqueueAsync(RoG.Core.Offline.OutboxKind.EventRsvp, item.Id, new { status }, Services.Localizer.GetString(StringKeys.OutboxLabelRsvp));
            Show(Services.Localizer.GetString(StringKeys.OutboxQueued), InfoBarSeverity.Informational);
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
        finally
        {
            IsResponding = false;
        }
    }

    [RelayCommand]
    private Task OpenInBrowserAsync() => _item?.WebUrl is { } url ? Services.Browser.OpenAsync(url) : Task.CompletedTask;

    /// <summary>„In Kalender übernehmen“: .ics der API in den Temp-Ordner laden und mit der Kalender-App öffnen.</summary>
    [RelayCommand]
    private async Task AddToCalendarAsync()
    {
        if (_item is null)
        {
            return;
        }

        try
        {
            using var file = await Services.Api.Client.V1_events_icsAsync(_item.Id);
            var folder = Windows.Storage.ApplicationData.Current.TemporaryFolder;
            var target = await folder.CreateFileAsync($"rog-{_item.Id}.ics", Windows.Storage.CreationCollisionOption.ReplaceExisting);
            await using (var output = await target.OpenStreamForWriteAsync())
            {
                await file.Stream.CopyToAsync(output);
            }

            if (!await Windows.System.Launcher.LaunchFileAsync(target))
            {
                Show(Services.Localizer.GetString(StringKeys.EventsNoCalendarApp), InfoBarSeverity.Warning);
            }
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or IOException or UnauthorizedAccessException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    private async Task ConnectAsync(ServerItemViewModel item)
    {
        var message = ConnectMessage.Describe(await Services.ServerConnector.ConnectAsync(item.Server), item.Name, Services.Localizer);
        Show(message.Text, ServersViewModel.ToSeverity(message.Tone));
    }

    private void Copy(ServerItemViewModel item)
    {
        var ok = Services.ServerConnector.Copy(item.Address);
        Show(Format(ok ? StringKeys.ServersCopied : StringKeys.ServersCopyFailed, item.Address), ok ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private void Show(string text, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }

    /// <summary>Absagen/Löschen nur für Verwalter (der Server prüft den Bereich des Termins).</summary>
    public bool CanManage => Services.CurrentUser is { } me && me.CanAnywhere("events.manage") && me.HasScope("events:write");

    /// <summary>Rückfrage (Titel, Text, „ganze Serie“-Option) – setzt die Seite; Rückgabe (ok, Serie).</summary>
    public Func<string, string, Task<(bool Ok, bool Series)>>? ConfirmSeries { get; set; }

    [RelayCommand]
    private async Task CancelEventAsync()
    {
        if (_item is not { } item || ConfirmSeries is null)
        {
            return;
        }

        var (ok, series) = await ConfirmSeries(Services.Localizer.GetString(StringKeys.EventsCancelTitle), item.Title);
        if (!ok)
        {
            return;
        }

        try
        {
            await new EventAdminService(Services.Api.Client, Services.Api.Http).CancelAsync(item.Id, series);
            Show(Services.Localizer.GetString(StringKeys.EventsCancelDone), InfoBarSeverity.Success);
            var updated = await events.GetAsync(item.Id);
            Apply(updated);
            Changed?.Invoke(this, updated);
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteEventAsync()
    {
        if (_item is not { } item || ConfirmSeries is null)
        {
            return;
        }

        var (ok, series) = await ConfirmSeries(Services.Localizer.GetString(StringKeys.EventsDeleteTitle), item.Title);
        if (!ok)
        {
            return;
        }

        try
        {
            await new EventAdminService(Services.Api.Client, Services.Api.Http).DeleteAsync(item.Id, series);
            Show(Services.Localizer.GetString(StringKeys.EventsDeleted), InfoBarSeverity.Success);
            Deleted?.Invoke(this, item.Id);
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    /// <summary>Termin gelöscht – die Liste entfernt ihn.</summary>
    public event EventHandler<string>? Deleted;

    /// <summary>„Fr., 17. Oktober 2026 · 20:00–22:00“.</summary>
    internal static string WhenOf(EventItem item)
    {
        if (item.StartsAt is not { } start)
        {
            return string.Empty;
        }

        var s = start.ToLocalTime();
        var text = s.ToString("ddd, d. MMMM yyyy · t", CultureInfo.CurrentCulture);
        if (item.EndsAt is { } end)
        {
            var e = end.ToLocalTime();
            text += "–" + (e.Date == s.Date ? e.ToString("t", CultureInfo.CurrentCulture) : e.ToString("g", CultureInfo.CurrentCulture));
        }

        return text;
    }

    internal static Brush BrushOf(string? hex) =>
        AccountViewModel.ColorFromHex(hex) is { } c ? new SolidColorBrush(c) : (Brush)Application.Current.Resources["RoGRedBrush"];

    internal static string RsvpLabel(Rsvp rsvp, ILocalizer localizer) => rsvp switch
    {
        Rsvp.Yes => localizer.GetString(StringKeys.EventsRsvpYes),
        Rsvp.Maybe => localizer.GetString(StringKeys.EventsRsvpMaybe),
        Rsvp.No => localizer.GetString(StringKeys.EventsRsvpNo),
        _ => string.Empty,
    };
}
