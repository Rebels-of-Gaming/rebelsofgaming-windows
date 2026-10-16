using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.App.Services;
using RoG.Core.Features.Stats;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Statistiken: Spiel, Zeitraum und Kennzahl wählen → Rangliste und Wochen-Awards.</summary>
public sealed partial class StatsViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly StatsService _stats = new(services.Api.Client);
    private bool _gamesLoaded;
    private bool _suppress;

    public ObservableCollection<SettingsOption> Games { get; } = [];

    public IReadOnlyList<SettingsOption> Periods { get; } = StatsService.Periods
        .Select(p => new SettingsOption(p, services.Localizer.GetString("Stats_Period_" + p))).ToList();

    public IReadOnlyList<SettingsOption> Metrics { get; } = StatsService.Metrics
        .Select(m => new SettingsOption(m, services.Localizer.GetString("Stats_Metric_" + m))).ToList();

    public ObservableCollection<LeaderboardRowViewModel> Rows { get; } = [];

    public ObservableCollection<AwardRowViewModel> Awards { get; } = [];

    [ObservableProperty]
    public partial SettingsOption? SelectedGame { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedPeriod { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedMetric { get; set; }

    [ObservableProperty]
    public partial string MyRankText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasMyRank { get; set; }

    [ObservableProperty]
    public partial string AwardsTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasAwards { get; set; }

    protected override bool HasData => Rows.Count > 0 || Awards.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!_gamesLoaded)
        {
            var games = await _stats.GamesAsync(cancellationToken);
            _suppress = true;
            Games.Clear();
            foreach (var g in games)
            {
                Games.Add(new SettingsOption(g.Slug, g.Name));
            }

            SelectedGame = Games.FirstOrDefault();
            SelectedPeriod = Periods[1];
            SelectedMetric = Metrics[0];
            _suppress = false;
            _gamesLoaded = true;
        }

        Rows.Clear();
        Awards.Clear();
        HasMyRank = HasAwards = false;
        if (SelectedGame is not { } game)
        {
            return;
        }

        var metric = SelectedMetric?.Value ?? "kills";
        var board = await _stats.LeaderboardAsync(game.Value, SelectedPeriod?.Value ?? "30d", metric, cancellationToken);
        foreach (var e in board.Entries)
        {
            Rows.Add(new LeaderboardRowViewModel(e, metric));
        }

        if (board.MyRank is { } me)
        {
            MyRankText = Format(StringKeys.StatsMyRank, me);
            HasMyRank = true;
        }

        var awards = await _stats.AwardsAsync(game.Value, cancellationToken);
        foreach (var a in awards.Items)
        {
            Awards.Add(new AwardRowViewModel(
                Services.Localizer.GetString("Stats_Award_" + a.Award) is { Length: > 0 } label && !label.StartsWith("Stats_Award_", StringComparison.Ordinal) ? label : a.Award,
                StatsService.FormatAwardValue(a.Award, a.Value, CultureInfo.CurrentCulture),
                string.Join(", ", a.Members)));
        }

        AwardsTitle = Format(StringKeys.StatsAwardsTitle, awards.Week);
        HasAwards = Awards.Count > 0;
    }

    partial void OnSelectedGameChanged(SettingsOption? value) => Reload();

    partial void OnSelectedPeriodChanged(SettingsOption? value) => Reload();

    partial void OnSelectedMetricChanged(SettingsOption? value) => Reload();

    private void Reload()
    {
        if (!_suppress && _gamesLoaded)
        {
            _ = LoadAsync(silent: true);
        }
    }
}

public sealed class LeaderboardRowViewModel(LeaderboardEntry entry, string metric)
{
    public string Rank { get; } = entry.Rank.ToString(CultureInfo.CurrentCulture) + ".";

    public string Name => entry.Name;

    public string Value { get; } = metric switch
    {
        "kd" => entry.Kd?.ToString("0.00", CultureInfo.CurrentCulture) ?? "–",
        "playtime" => entry.Playtime.TotalHours.ToString("0.0", CultureInfo.CurrentCulture) + " h",
        "rounds" => entry.Rounds.ToString(CultureInfo.CurrentCulture),
        "headshots" => entry.Headshots.ToString(CultureInfo.CurrentCulture),
        _ => entry.Kills.ToString(CultureInfo.CurrentCulture),
    };

    public string Details { get; } = string.Format(CultureInfo.CurrentCulture, "{0} K · {1} D · {2} R", entry.Kills, entry.Deaths, entry.Rounds);
}

public sealed record AwardRowViewModel(string Label, string Value, string Members);

/// <summary>Turniere (Matches-Seite): Name, Veranstalter, Zeitraum, Platzierung; Link öffnet die Turnierseite.</summary>
public sealed partial class TournamentsViewModel(AppServices services) : LoadableViewModel(services)
{
    public ObservableCollection<TournamentRowViewModel> Items { get; } = [];

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var list = await new RoG.Core.Features.Squads.SquadExtrasService(Services.Api.Client).TournamentsAsync(cancellationToken: cancellationToken);
        Items.Clear();
        foreach (var t in list)
        {
            Items.Add(new TournamentRowViewModel(t, Services));
        }
    }
}

public sealed partial class TournamentRowViewModel(RoG.Core.Features.Squads.Tournament tournament, AppServices services)
{
    public string Title { get; } = tournament.GameShort is { } g ? $"{tournament.Name} · {g}" : tournament.Name;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        tournament.StartsOn?.ToString("d", CultureInfo.CurrentCulture) + (tournament.EndsOn is { } e && e != tournament.StartsOn ? "–" + e.ToString("d", CultureInfo.CurrentCulture) : string.Empty),
        tournament.Organizer,
        tournament.SquadName,
        tournament.Participants is { } n ? string.Format(CultureInfo.CurrentCulture, services.Localizer.GetString(StringKeys.TournamentParticipants), n) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string Placement { get; } = tournament.Placement is { } p ? p.ToString(CultureInfo.CurrentCulture) + "." : string.Empty;

    public bool HasPlacement => tournament.Placement is not null;

    public bool HasUrl => tournament.Url is not null;

    [RelayCommand]
    private Task OpenAsync() => tournament.Url is { } url ? services.Browser.OpenAsync(url) : Task.CompletedTask;
}
