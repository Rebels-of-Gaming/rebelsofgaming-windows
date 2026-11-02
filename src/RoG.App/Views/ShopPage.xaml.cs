using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;

namespace RoG.App.Views;

public sealed partial class ShopPage : Page
{
    public ShopPage()
    {
        ViewModel = new ShopViewModel(App.Services);
        InitializeComponent();
    }

    public ShopViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    private async void OnRefreshInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.LoadAsync();
    }
}
