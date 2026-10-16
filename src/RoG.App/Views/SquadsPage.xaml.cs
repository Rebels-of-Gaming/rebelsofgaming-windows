using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;

namespace RoG.App.Views;

public sealed partial class SquadsPage : Page
{
    public SquadsPage()
    {
        ViewModel = new SquadsViewModel(App.Services);
        InitializeComponent();
    }

    public SquadsViewModel ViewModel { get; }

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

    private void OnStatsExpanding(Expander sender, ExpanderExpandingEventArgs args) => _ = ViewModel.Detail.LoadStatsAsync();

    private void OnGalleryExpanding(Expander sender, ExpanderExpandingEventArgs args) => _ = ViewModel.Detail.LoadGalleryAsync();

    private void OnModeChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        ViewModel.GamesMode = sender.SelectedItem is not null && sender.Items.IndexOf(sender.SelectedItem) == 1;

    private void OnDetailClosed(object? sender, EventArgs e)
    {
        ViewModel.Selected = null;
        ViewModel.DetailForced = false;
    }

    private async void OnApplyClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (ViewModel.Detail.Slug is { } slug && await RoG.App.Controls.ApplicationDialog.ShowAsync(XamlRoot, slug))
        {
            App.Services.Navigate(new RoG.Core.Navigation.AppRoute(RoG.Core.Navigation.AppSection.Account));
        }
    }

    private async void OnRefreshInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.LoadAsync();
    }
}
