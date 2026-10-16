using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;
using RoG.Core.Features.Notifications;

namespace RoG.App.Views;

public sealed partial class NotificationsPage : Page
{
    public NotificationsPage()
    {
        ViewModel = new NotificationsViewModel(App.Services);
        InitializeComponent();
    }

    public NotificationsViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.Services.Realtime.NotificationReceived += OnLiveNotification;
        await ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        App.Services.Realtime.NotificationReceived -= OnLiveNotification;
        base.OnNavigatedFrom(e);
    }

    private void OnLiveNotification(object? sender, NotificationItem item) => ViewModel.Prepend(item);

    private async void OnRefreshInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.LoadAsync();
    }
}
