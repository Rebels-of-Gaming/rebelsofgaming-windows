using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;

namespace RoG.App.Views;

public sealed partial class MembersPage : Page
{
    public MembersPage()
    {
        ViewModel = new MembersViewModel(App.Services);
        InitializeComponent();
    }

    public MembersViewModel ViewModel { get; }

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

    private void OnMapClick(object sender, RoutedEventArgs e) => MemberMapWindow.Open();

    private void OnDetailClosed(object? sender, EventArgs e)
    {
        ViewModel.Selected = null;
        ViewModel.DetailForced = false;
    }

    private void OnModeChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        ViewModel.TeamMode = sender.SelectedItem is not null && sender.Items.IndexOf(sender.SelectedItem) == 1;

    private async void OnRefreshInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.LoadAsync();
    }

    private void OnSearchInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SearchBox.Focus(FocusState.Keyboard);
    }
}
