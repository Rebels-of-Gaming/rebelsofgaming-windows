using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Home;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Startseite: Live-Ticker aus <c>/v1/live</c> (still aktualisiert), Hinweis im Gastmodus.</summary>
public sealed partial class HomeViewModel(AppServices services) : ObservableObject
{
    private readonly LiveTickerService _ticker = new(services.Api.Client);

    public ObservableCollection<LiveStatItem> Stats { get; } = [];

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
        catch (RoG.Core.Api.Generated.GeneratedApiException)
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
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();
}

/// <summary>Kachel des Live-Tickers: Wert, Beschriftung (übersetzt von der API), Symbol und Status-Farbe.</summary>
public sealed class LiveStatItem(LiveStat stat)
{
    public string Label { get; } = stat.Label;

    public string Value { get; } = stat.Value.ToString("N0", CultureInfo.CurrentCulture);

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

    public string AutomationName { get; } = $"{stat.Label}: {stat.Value.ToString(CultureInfo.CurrentCulture)}";
}
