using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;

namespace RoG.App.Views;

public sealed partial class ToolsPage : Page
{
    public ToolsPage()
    {
        ViewModel = new ToolsViewModel(App.Services);
        InitializeComponent();
    }

    public ToolsViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
        if (e.Parameter is string { Length: > 0 } slug)
        {
            ViewModel.Open(slug);
        }
    }
}
