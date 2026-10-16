using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;

namespace RoG.App.Views;

public sealed partial class InfoPage : Page
{
    public InfoPage()
    {
        ViewModel = new InfoViewModel(App.Services);
        InitializeComponent();
    }

    public InfoViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string { Length: > 0 } key)
        {
            await ViewModel.OpenAsync(key);
        }
        else
        {
            await ViewModel.LoadAsync();
        }
    }

    private void OnDetailClosed(object? sender, EventArgs e) => ViewModel.Selected = null;

    private async void OnRefreshInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.LoadAsync();
    }
}
