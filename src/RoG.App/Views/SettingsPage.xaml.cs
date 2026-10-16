using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;

namespace RoG.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = new SettingsViewModel(App.Services);
        Notifications = new NotificationSettingsViewModel(App.Services);
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }

    public NotificationSettingsViewModel Notifications { get; }

    /// <summary>Nur angemeldet (Gäste haben keine Benachrichtigungen).</summary>
    public bool ShowNotificationSettings => Notifications.IsSignedIn;

    private void OnNotificationsExpanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        if (Notifications.Items.Count == 0)
        {
            _ = Notifications.LoadAsync();
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }
}
