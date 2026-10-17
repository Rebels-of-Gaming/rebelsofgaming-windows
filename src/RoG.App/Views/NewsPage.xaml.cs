using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;

namespace RoG.App.Views;

public sealed partial class NewsPage : Page
{
    public NewsPage()
    {
        ViewModel = new NewsViewModel(App.Services);
        InitializeComponent();
    }

    public NewsViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string { Length: > 0 } slug)
        {
            await ViewModel.OpenAsync(slug);
        }
        else
        {
            await ViewModel.LoadAsync();
        }
    }

    private void OnDetailClosed(object? sender, EventArgs e)
    {
        ViewModel.Selected = null;
        ViewModel.DetailForced = false;
    }

    private void OnWriteClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => OpenEditor(null);

    private void OnEditClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => OpenEditor(ViewModel.Detail.Slug);

    private void OpenEditor(string? slug) => NewsEditorWindow.Open(ViewModel.Rights, slug, changed =>
    {
        if (changed)
        {
            _ = ViewModel.LoadAsync();
        }
    });

    private async void OnRefreshInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.LoadAsync();
    }
}
