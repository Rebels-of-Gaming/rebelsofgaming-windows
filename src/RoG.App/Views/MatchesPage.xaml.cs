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
        InitializeComponent();
    }

    public MatchesViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    private void OnDetailClosed(object? sender, EventArgs e) => ViewModel.Selected = null;

    private async void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (MatchesPanel is null || HallPanel is null)
        {
            return; // feuert schon beim Aufbau
        }

        var hall = sender.SelectedItem is not null && sender.Items.IndexOf(sender.SelectedItem) == 1;
        MatchesPanel.Visibility = hall ? Visibility.Collapsed : Visibility.Visible;
        HallPanel.Visibility = hall ? Visibility.Visible : Visibility.Collapsed;
        if (hall)
        {
            await ViewModel.Hall.LoadAsync(silent: true);
        }
    }
}
