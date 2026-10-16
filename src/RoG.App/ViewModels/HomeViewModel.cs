using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Events;
using RoG.Core.Features.Home;
using RoG.Core.Features.News;
using RoG.Core.Navigation;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Startseite: Live-Ticker aus <c>/v1/live</c> (still aktualisiert), Hinweis im Gastmodus.</summary>
public sealed partial class HomeViewModel(AppServices services) : ObservableObject
{
    private readonly LiveTickerService _ticker = new(services.Api.Http);
    private readonly NewsService _news = new(services.Api.Client, services.Environment.WebBaseUrl);
    private readonly EventService _events = new(services.Api.Client);

    public ObservableCollection<LiveStatItem> Stats { get; } = [];

    public ObservableCollection<HomeLinkItem> LatestNews { get; } = [];

    public ObservableCollection<HomeLinkItem> NextEvents { get; } = [];

    [ObservableProperty]
    public partial bool HasNews { get; set; }

    [ObservableProperty]
    public partial bool HasEvents { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(IsEmpty))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsGuest { get; set; }

    [ObservableProperty]
    public partial string Greeting { get; set; } = string.Empty;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsEmpty => !IsLoading && !HasError && Stats.Count == 0;

    public async Task LoadAsync(bool silent = false)
    {
        var token = await services.Tokens.GetAsync();
        IsGuest = token is null;
        var name = services.CurrentUser?.Name ?? token?.UserName;
        Greeting = name is null
            ? services.Localizer.GetString(StringKeys.HomeGreetingGuest)
            : string.Format(CultureInfo.CurrentCulture, services.Localizer.GetString(StringKeys.HomeGreeting), name);

        IsLoading = !silent || Stats.Count == 0;
        try
        {
            var stats = await _ticker.GetAsync();
            ErrorMessage = null;
            Stats.Clear();
            foreach (var stat in stats)
            {
                Stats.Add(new LiveStatItem(stat));
            }
        }
        catch (ApiProblemException ex)
        {
            if (!silent || Stats.Count == 0)
            {
                ErrorMessage = services.Localizer.GetError(ex.Problem.Code);
            }
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or System.Text.Json.JsonException or HttpRequestException)
        {
            if (!silent || Stats.Count == 0)
            {
                ErrorMessage = services.Localizer.GetError(ApiProblem.Codes.Unknown);
            }
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }

        if (!silent || LatestNews.Count == 0)
        {
            await LoadTeasersAsync();
        }
    }

    /// <summary>Neueste News und nächste Termine (je 3) – Beiwerk, Fehler bleiben still.</summary>
    private async Task LoadTeasersAsync()
    {
        try
        {
            var news = await _news.ListAsync();
            LatestNews.Clear();
            foreach (var n in news.Items.Take(3))
            {
                LatestNews.Add(new HomeLinkItem(n.Title, NewsDetailViewModel.MetaOf(n), () => services.Navigate(new AppRoute(AppSection.News, n.Slug))));
            }
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // News-Kachel bleibt leer
        }

        try
        {
            var events = await _events.ListAsync(DateTimeOffset.Now.Date, 30);
            NextEvents.Clear();
            foreach (var e in events.Items.Where(e => !e.Cancelled).Take(3))
            {
                NextEvents.Add(new HomeLinkItem(e.Title, EventDetailViewModel.WhenOf(e), () => services.Navigate(new AppRoute(AppSection.Calendar, e.Id))));
            }
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // Termin-Kachel bleibt leer
        }

        HasNews = LatestNews.Count > 0;
        HasEvents = NextEvents.Count > 0;
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();
}

/// <summary>Kachel des Live-Tickers: Wert, Beschriftung (übersetzt von der API), Symbol und Status-Farbe.</summary>
public sealed class LiveStatItem(LiveStat stat)
{
    public string Label { get; } = stat.Label;

    public string Value => stat.Value;

    public string? Hint { get; } = stat.Hint;

    public bool HasHint => !string.IsNullOrEmpty(stat.Hint);

    /// <summary>Symbol (Segoe Fluent Icons) zum Icon-Namen der Website (Lucide-Namen).</summary>
    public string Glyph { get; } = stat.Icon switch
    {
        "users" or "user" => "",
        "activity" => "",
        "headset" or "headphones" or "mic" => "",
        "server" or "servers" => "",
        "calendar" or "calendar-days" => "",
        "gamepad" or "gamepad-2" => "",
        "twitch" or "video" or "tv" => "",
        "trophy" or "award" => "",
        "message" or "message-circle" => "",
        "download" => "",
        _ => "",
    };

    public Brush ToneBrush { get; } = (Brush)Application.Current.Resources[stat.Tone switch
    {
        "green" => "RoGGreenBrush",
        "gold" => "RoGGoldBrush",
        "red" => "RoGRedBrush",
        "blue" => "RoGBlueBrush",
        _ => "RoGMutedBrush",
    }];

    public string AutomationName { get; } = $"{stat.Label}: {stat.Value}";
}

/// <summary>Anklickbarer Eintrag auf der Startseite (News, Termin).</summary>
public sealed partial class HomeLinkItem(string title, string meta, Func<bool> open)
{
    public string Title => title;

    public string Meta => meta;

    [RelayCommand]
    private void Open() => open();
}
