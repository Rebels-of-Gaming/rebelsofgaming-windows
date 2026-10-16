using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;

namespace RoG.App.Views;

/// <summary>Startseite; der Live-Ticker aktualisiert sich alle 30 Sekunden (Cache-Dauer der API), solange sie offen ist.</summary>
public sealed partial class HomePage : Page
{
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(30) };

    public HomePage()
    {
        ViewModel = new HomeViewModel(App.Services);
        InitializeComponent();
        _refresh.Tick += async (_, _) => await ViewModel.LoadAsync(silent: true);
    }

    public HomeViewModel ViewModel { get; }

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
