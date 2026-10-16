using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;
using RoG.Core.Localization;

namespace RoG.App.Views;

public sealed partial class AccountPage : Page
{
    public AccountPage()
    {
        ViewModel = new AccountViewModel(App.Services);
        InitializeComponent();
    }

    public AccountViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    private async void OnSignOutClick(object sender, RoutedEventArgs e)
    {
        var localizer = App.Services.Localizer;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = localizer.GetString(StringKeys.AccountSignOutTitle),
            Content = localizer.GetString(StringKeys.AccountSignOutText),
            PrimaryButtonText = localizer.GetString(StringKeys.AccountSignOutConfirm),
            CloseButtonText = localizer.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.SignOutAsync();
        }
    }
}
