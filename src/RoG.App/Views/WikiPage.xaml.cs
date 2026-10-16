using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;

namespace RoG.App.Views;

public sealed partial class WikiPage : Page
{
    public WikiPage()
    {
        ViewModel = new WikiViewModel(App.Services);
        InitializeComponent();
    }

    public WikiViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string { Length: > 0 } path)
        {
            await ViewModel.OpenPathAsync(path);
        }
        else
        {
            await ViewModel.LoadAsync();
        }
    }

    private void OnDetailClosed(object? sender, EventArgs e) => ViewModel.CloseDetail();

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) => ViewModel.SearchCommand.Execute(null);

    private void OnRevisionsExpanding(Expander sender, ExpanderExpandingEventArgs args) => ViewModel.LoadRevisionsCommand.Execute(null);
}
