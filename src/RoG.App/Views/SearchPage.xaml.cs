using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;

namespace RoG.App.Views;

/// <summary>Ergebnisse der globalen Suche; Parameter = Suchbegriff.</summary>
public sealed partial class SearchPage : Page
{
    public SearchPage()
    {
        ViewModel = new SearchViewModel(App.Services);
        InitializeComponent();
    }

    public SearchViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Query = e.Parameter as string ?? string.Empty;
        await ViewModel.LoadAsync();
    }
}
