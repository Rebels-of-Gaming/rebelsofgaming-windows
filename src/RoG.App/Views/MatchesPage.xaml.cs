using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;

namespace RoG.App.Views;

/// <summary>Matches (Liste-Detail) und Hall of Fame als zweiter Reiter.</summary>
public sealed partial class MatchesPage : Page
{
    public MatchesPage()
    {
        ViewModel = new MatchesViewModel(App.Services);
        Stats = new StatsViewModel(App.Services);
        Tournaments = new TournamentsViewModel(App.Services);
        InitializeComponent();
    }

    public MatchesViewModel ViewModel { get; }

    public StatsViewModel Stats { get; }

    public TournamentsViewModel Tournaments { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    private void OnDetailClosed(object? sender, EventArgs e) => ViewModel.Selected = null;

    private void OnAddMatchClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => MatchEditorWindow.Open(null, Reload);

    private void OnEditMatchClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (ViewModel.Selected is { } match)
        {
            MatchEditorWindow.Open(match.Id, Reload);
        }
    }

    private void Reload(bool changed)
    {
        if (changed)
        {
            ViewModel.Selected = null;
            _ = ViewModel.LoadAsync();
        }
    }

    private async void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (MatchesPanel is null || HallPanel is null || StatsPanel is null || TournamentsPanel is null)
        {
            return; // feuert schon beim Aufbau
        }

        var index = sender.SelectedItem is null ? 0 : sender.Items.IndexOf(sender.SelectedItem);
        MatchesPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        HallPanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        StatsPanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        TournamentsPanel.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        if (index == 1)
        {
            await ViewModel.Hall.LoadAsync(silent: true);
        }
        else if (index == 2 && Stats.Rows.Count == 0)
        {
            await Stats.LoadAsync();
        }
        else if (index == 3 && Tournaments.Items.Count == 0)
        {
            await Tournaments.LoadAsync();
        }
    }
}
