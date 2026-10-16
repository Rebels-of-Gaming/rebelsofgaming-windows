using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;

namespace RoG.App.Views;

/// <summary>Gameserver mit Live-Stand und „Verbinden“. Aktualisiert sich jede Minute, solange die Seite offen ist.</summary>
public sealed partial class ServersPage : Page
{
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMinutes(1) };

    public ServersPage()
    {
        ViewModel = new ServersViewModel(App.Services);
        InitializeComponent();
        _refresh.Tick += async (_, _) => await ViewModel.LoadAsync(silent: true);
    }

    public ServersViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _refresh.Start();
        await ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _refresh.Stop();
        base.OnNavigatedFrom(e);
    }
}
