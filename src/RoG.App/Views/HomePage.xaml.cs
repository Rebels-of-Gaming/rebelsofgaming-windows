using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace RoG.App.Views;

public sealed partial class HomePage : Page
{
    public HomePage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        GuestHint.IsOpen = await App.Services.Tokens.GetAsync() is null;
    }
}
