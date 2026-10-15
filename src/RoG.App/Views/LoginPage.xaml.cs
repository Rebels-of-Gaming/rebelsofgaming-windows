using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;

namespace RoG.App.Views;

public sealed partial class LoginPage : Page
{
    public LoginPage()
    {
        ViewModel = new LoginViewModel(App.Services);
        InitializeComponent();
    }

    public LoginViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is EventHandler completed)
        {
            ViewModel.Completed += completed;
        }
    }
}
