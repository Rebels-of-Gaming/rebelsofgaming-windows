using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;

namespace RoG.App.Views;

public sealed partial class VotesPage : Page
{
    public VotesPage()
    {
        ViewModel = new VotesViewModel(App.Services);
        InitializeComponent();
    }

    public VotesViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }
}
