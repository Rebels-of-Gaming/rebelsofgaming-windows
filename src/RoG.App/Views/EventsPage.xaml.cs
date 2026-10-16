using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;

namespace RoG.App.Views;

public sealed partial class EventsPage : Page
{
    public EventsPage()
    {
        ViewModel = new EventsViewModel(App.Services);
        InitializeComponent();
    }

    public EventsViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string { Length: > 0 } id)
        {
            await ViewModel.OpenAsync(id);
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
