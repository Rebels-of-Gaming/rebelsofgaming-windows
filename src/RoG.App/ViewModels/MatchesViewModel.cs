using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RoG.App.Services;
using RoG.Core.Features.Matches;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Matches (neueste zuerst, Detail mit Maps und Line-up) und Hall of Fame.</summary>
public sealed partial class MatchesViewModel : LoadableViewModel
{
    private readonly MatchService _matches;
    private string? _cursor;

    public MatchesViewModel(AppServices services)
        : base(services)
    {
        _matches = new MatchService(services.Api.Client);
        Hall = new HallOfFameViewModel(services, _matches);
    }

    public ObservableCollection<MatchRowViewModel> Items { get; } = [];

    public HallOfFameViewModel Hall { get; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint), nameof(HasSelection))]
    public partial MatchRowViewModel? Selected { get; set; }

    public bool IsDetailOpen => Selected is not null;

    public bool ShowSelectHint => Selected is null;

    public bool HasSelection => Selected is not null;

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var page = await _matches.ListAsync(null, cancellationToken);
        Items.Clear();
        foreach (var m in page.Items)
        {
            Items.Add(new MatchRowViewModel(m, Services));
        }

        _cursor = page.NextCursor;
        HasMore = page.HasMore;
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_cursor is null)
        {
            return;
        }

        try
        {
            var page = await _matches.ListAsync(_cursor);
            foreach (var m in page.Items)
            {
                Items.Add(new MatchRowViewModel(m, Services));
            }

            _cursor = page.NextCursor;
            HasMore = page.HasMore;
        }
        catch (Exception ex) when (ex is RoG.Core.Api.ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // erneut versuchen über „Mehr laden“
        }
    }
}

/// <summary>Ein Match: Liste zeigt Datum, Gegner und Ergebnis; das Detail nutzt dieselbe Instanz.</summary>
public sealed partial class MatchRowViewModel(MatchItem match, AppServices services)
{
    public string Title { get; } = $"{match.SquadName ?? "RoG"} vs. {match.Opponent}";

    public string Subtitle { get; } = string.Join(" · ", new[]
    {
        match.PlayedAt?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture),
        match.Game,
        match.Tournament is null ? null : match.Placement is { } p ? $"{match.Tournament} (#{p})" : match.Tournament,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string Score { get; } = match.Own is null && match.OpponentScore is null ? "–" : $"{match.Own?.ToString(CultureInfo.CurrentCulture) ?? "–"} : {match.OpponentScore?.ToString(CultureInfo.CurrentCulture) ?? "–"}";

    public Brush ResultBrush { get; } = BrushOf(match.Result);

    public string ResultText { get; } = services.Localizer.GetString(match.Result switch
    {
        MatchOutcome.Win => StringKeys.MatchesWin,
        MatchOutcome.Loss => StringKeys.MatchesLoss,
        MatchOutcome.Draw => StringKeys.MatchesDraw,
        _ => StringKeys.MatchesOpen,
    });

    public IReadOnlyList<MatchMapRow> Maps { get; } = match.Maps.Select(m => new MatchMapRow(m.Map, $"{m.Own?.ToString(CultureInfo.CurrentCulture) ?? "–"} : {m.Opponent?.ToString(CultureInfo.CurrentCulture) ?? "–"}", BrushOf(m.Result))).ToList();

    public bool HasMaps => match.Maps.Count > 0;

    public string Lineup { get; } = string.Join(", ", match.Lineup);

    public bool HasLineup => match.Lineup.Count > 0;

    public IReadOnlyList<LinkViewModel> Links { get; } = match.Links.Select(l => new LinkViewModel(l.Label, l.Url, null, services)).ToList();

    public bool HasLinks => match.Links.Count > 0;

    public string AutomationName => $"{Title}, {Score}, {ResultText}";

    internal static Brush BrushOf(MatchOutcome outcome) => (Brush)Application.Current.Resources[outcome switch
    {
        MatchOutcome.Win => "RoGGreenBrush",
        MatchOutcome.Loss => "RoGRedBrush",
        MatchOutcome.Draw => "RoGGoldBrush",
        _ => "RoGMutedBrush",
    }];
}

public sealed record MatchMapRow(string Map, string Score, Brush ResultBrush);

/// <summary>Hall of Fame: Trophäen, Meilensteine, Ehrenmitglieder.</summary>
public sealed partial class HallOfFameViewModel(AppServices services, MatchService matches) : LoadableViewModel(services)
{
    public ObservableCollection<HallEntryViewModel> Trophies { get; } = [];

    public ObservableCollection<HallEntryViewModel> Milestones { get; } = [];

    public ObservableCollection<HonoraryViewModel> Honorary { get; } = [];

    [ObservableProperty]
    public partial bool HasTrophies { get; set; }

    [ObservableProperty]
    public partial bool HasMilestones { get; set; }

    [ObservableProperty]
    public partial bool HasHonorary { get; set; }

    protected override bool HasData => HasTrophies || HasMilestones || HasHonorary;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var hall = await matches.GetHallOfFameAsync(cancellationToken);
        Fill(Trophies, hall.Trophies);
        Fill(Milestones, hall.Milestones);
        Honorary.Clear();
        foreach (var h in hall.Honorary)
        {
            Honorary.Add(new HonoraryViewModel(h.Name, h.Avatar, h.Period, h.Tribute));
        }

        HasTrophies = Trophies.Count > 0;
        HasMilestones = Milestones.Count > 0;
        HasHonorary = Honorary.Count > 0;
    }

    private static void Fill(ObservableCollection<HallEntryViewModel> target, IEnumerable<HallEntry> entries)
    {
        target.Clear();
        foreach (var e in entries)
        {
            target.Add(new HallEntryViewModel(e));
        }
    }
}

public sealed class HallEntryViewModel(HallEntry entry)
{
    public string Title => entry.Title;

    public string Description => entry.Description ?? string.Empty;

    public bool HasDescription => entry.Description is not null;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        entry.AchievedOn?.ToLocalTime().ToString("Y", CultureInfo.CurrentCulture),
        entry.Placement is { } p ? $"#{p}" : null,
        entry.Squad,
        entry.Game,
    }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>Medaille: Gold/Silber/Bronze als Farbe, sonst Gold (Auszeichnung).</summary>
    public Brush MedalBrush { get; } = entry.Medal switch
    {
        "silver" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xC0, 0xC4, 0xCC)),
        "bronze" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xCD, 0x7F, 0x32)),
        _ => (Brush)Application.Current.Resources["RoGGoldBrush"],
    };
}

public sealed class HonoraryViewModel(string name, Uri? avatar, string? period, string tribute)
{
    public string Name => name;

    public ImageSource? Avatar { get; } = avatar is null ? null : new BitmapImage(avatar) { DecodePixelWidth = 96 };

    public string Period => period ?? string.Empty;

    public string Tribute => tribute;
}
