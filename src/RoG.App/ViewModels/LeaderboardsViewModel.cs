using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RoG.App.Services;
using RoG.Core.Features.Minecraft;
using RoG.Core.Localization;
using RoG.Core.Navigation;

namespace RoG.App.ViewModels;

/// <summary>
/// Minecraft-Bestenlisten (Website v0.41.0): Spielzeit, je Minispiel, Parkour; Zeitraum gesamt/30 Tage. Es erscheinen
/// nur Mitglieder, die ihre Statistiken freigeben – welche, entscheidet der Server.
/// </summary>
public sealed partial class LeaderboardsViewModel : LoadableViewModel
{
    private readonly MinecraftService _minecraft;
    private bool _suppress;

    public LeaderboardsViewModel(AppServices services)
        : base(services)
    {
        _minecraft = new MinecraftService(services.Api.Http);
        BoardOptions = MinecraftService.Boards.Select(b => new SettingsOption(b, Label("Leaderboard_Board_", b))).ToList();
        PeriodOptions = MinecraftService.Periods.Select(p => new SettingsOption(p, Label("Leaderboard_Period_", p))).ToList();
        _suppress = true;
        SelectedBoard = BoardOptions[0];
        SelectedPeriod = PeriodOptions[0];
        _suppress = false;
    }

    public IReadOnlyList<SettingsOption> BoardOptions { get; }

    public IReadOnlyList<SettingsOption> PeriodOptions { get; }

    public ObservableCollection<SettingsOption> SortOptions { get; } = [];

    public ObservableCollection<SettingsOption> CourseOptions { get; } = [];

    public ObservableCollection<LeaderboardEntryViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial SettingsOption SelectedBoard { get; set; }

    [ObservableProperty]
    public partial SettingsOption SelectedPeriod { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedSort { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedCourse { get; set; }

    [ObservableProperty]
    public partial bool HasSorts { get; set; }

    [ObservableProperty]
    public partial bool HasCourses { get; set; }

    /// <summary>„Siegquote ab 10 Spielen“, „seit 23.09.2026“ – was der Server gewertet hat.</summary>
    [ObservableProperty]
    public partial string? Hint { get; set; }

    [ObservableProperty]
    public partial bool HasHint { get; set; }

    protected override bool HasData => Items.Count > 0;

    partial void OnSelectedBoardChanged(SettingsOption value)
    {
        if (_suppress || value is null)
        {
            return;
        }

        _suppress = true;
        SortOptions.Clear();
        foreach (var sort in MinecraftService.SortsFor(value.Value))
        {
            SortOptions.Add(new SettingsOption(sort, Label("Leaderboard_Sort_", sort.Replace(MinecraftService.ObjectivePrefix, "objective_", StringComparison.Ordinal))));
        }

        HasSorts = SortOptions.Count > 0;
        SelectedSort = SortOptions.FirstOrDefault();
        CourseOptions.Clear();
        SelectedCourse = null;
        HasCourses = false;
        _suppress = false;
        _ = LoadAsync();
    }

    partial void OnSelectedPeriodChanged(SettingsOption value) => Reload();

    partial void OnSelectedSortChanged(SettingsOption? value) => Reload();

    partial void OnSelectedCourseChanged(SettingsOption? value) => Reload();

    private void Reload()
    {
        if (!_suppress)
        {
            _ = LoadAsync();
        }
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var board = await _minecraft.LeaderboardAsync(SelectedBoard.Value, SelectedPeriod.Value, SelectedSort?.Value, SelectedCourse?.Value, cancellationToken);
        var kind = MinecraftService.ValueKind(board.Board, board.Sort);

        _suppress = true;
        if (board.Board == MinecraftService.BoardParkour && board.Courses.Count > 0 && CourseOptions.Count == 0)
        {
            foreach (var (id, label) in board.Courses)
            {
                CourseOptions.Add(new SettingsOption(id, label));
            }

            SelectedCourse = CourseOptions.FirstOrDefault(c => c.Value == board.Course) ?? CourseOptions[0];
        }

        HasCourses = CourseOptions.Count > 0;
        _suppress = false;

        Items.Clear();
        foreach (var entry in board.Entries)
        {
            Items.Add(new LeaderboardEntryViewModel(Services, entry, ValueText(kind, entry.Value)));
        }

        var hints = new List<string>();
        if (board.Sort == MinecraftService.SortWinRate && board.MinMatches is { } min)
        {
            hints.Add(Format(StringKeys.LeaderboardMinMatches, min));
        }

        if (board.From is { } from)
        {
            hints.Add(Format(StringKeys.LeaderboardFrom, from.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)));
        }

        Hint = string.Join(" · ", hints);
        HasHint = hints.Count > 0;
    }

    private string ValueText(LeaderboardValueKind kind, double value) => kind switch
    {
        LeaderboardValueKind.Seconds => Playtime(Services.Localizer, (long)value),
        LeaderboardValueKind.Milliseconds => MinecraftService.FormatDuration((long)value),
        LeaderboardValueKind.Percent => value.ToString("0.#", CultureInfo.CurrentCulture) + " %",
        _ => ((long)value).ToString("N0", CultureInfo.CurrentCulture),
    };

    /// <summary>„112 Std. 5 Min.“ bzw. „5 Min.“.</summary>
    internal static string Playtime(ILocalizer localizer, long seconds)
    {
        var (hours, minutes) = MinecraftService.SplitPlaytime(seconds);
        return hours > 0
            ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.StatsPlaytimeHours), hours.ToString("N0", CultureInfo.CurrentCulture), minutes)
            : string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.StatsPlaytimeMinutes), minutes);
    }

    /// <summary>Übersetzter Name, sonst der Rohwert (unbekannte Spiele/Wertungen der Registry).</summary>
    private string Label(string prefix, string value)
    {
        var key = prefix + value;
        var text = Services.Localizer.GetString(key);
        return text == key ? value : text;
    }
}

public sealed partial class LeaderboardEntryViewModel(AppServices services, LeaderboardEntry entry, string valueText)
{
    public string RankText { get; } = "#" + entry.Rank.ToString(CultureInfo.CurrentCulture);

    public string Name { get; } = entry.Name;

    public string ValueText { get; } = valueText;

    public ImageSource? Avatar { get; } = entry.Avatar is null ? null : new BitmapImage(entry.Avatar);

    /// <summary>Gold für die ersten drei Plätze (Rang/Auszeichnung), sonst gedämpft.</summary>
    public Brush RankBrush { get; } = (Brush)Microsoft.UI.Xaml.Application.Current.Resources[entry.Rank <= 3 ? "RoGGoldBrush" : "RoGMutedBrush"];

    public string? Meta { get; } = entry.Played is { } played
        ? string.Format(
            CultureInfo.CurrentCulture,
            services.Localizer.GetString(StringKeys.LeaderboardMatches),
            played,
            entry.Won ?? 0,
            entry.WinRate is { } rate ? rate.ToString("0.#", CultureInfo.CurrentCulture) : "–")
        : null;

    public bool HasMeta => Meta is not null;

    public string AutomationName => $"{RankText} {Name}, {ValueText}";

    [RelayCommand]
    private void Open() => services.Navigate(new AppRoute(AppSection.Members, entry.Slug));
}
