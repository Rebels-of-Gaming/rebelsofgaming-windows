using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;
using RoG.Core.Navigation;

namespace RoG.App.Views;

public sealed partial class MinecraftSecretsPage : Page
{
    public MinecraftSecretsPage()
    {
        ViewModel = new MinecraftSecretsViewModel(App.Services);
        InitializeComponent();
    }

    public MinecraftSecretsViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    private void OnLeaderboardsClick(object sender, RoutedEventArgs e) => App.Services.Navigate(new AppRoute(AppSection.Leaderboards));

    private async void OnRefreshInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.LoadAsync();
    }
}
