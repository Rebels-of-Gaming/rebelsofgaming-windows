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
        InitializeComponent();
    }

    public MatchesViewModel ViewModel { get; }

    public StatsViewModel Stats { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    private void OnDetailClosed(object? sender, EventArgs e) => ViewModel.Selected = null;

    private async void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (MatchesPanel is null || HallPanel is null || StatsPanel is null)
        {
            return; // feuert schon beim Aufbau
        }

        var index = sender.SelectedItem is null ? 0 : sender.Items.IndexOf(sender.SelectedItem);
        MatchesPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        HallPanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        StatsPanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (index == 1)
        {
            await ViewModel.Hall.LoadAsync(silent: true);
        }
        else if (index == 2 && Stats.Rows.Count == 0)
        {
            await Stats.LoadAsync();
        }
    }
}
