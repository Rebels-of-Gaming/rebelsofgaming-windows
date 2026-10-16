using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;

namespace RoG.App.Views;

/// <summary>
/// Server: Gameserver mit „Verbinden“ (aktualisiert sich jede Minute), TeamSpeak-Viewer mit „Verbinden“ und
/// Discord-Voice – je ein Reiter. Voice lädt erst, wenn der Reiter geöffnet wird.
/// </summary>
public sealed partial class ServersPage : Page
{
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMinutes(1) };
    private int _tab;

    public ServersPage()
    {
        ViewModel = new ServersViewModel(App.Services);
        Voice = new VoiceViewModel(App.Services);
        InitializeComponent();
        _refresh.Tick += async (_, _) => await RefreshCurrentAsync(silent: true);
    }

    public ServersViewModel ViewModel { get; }

    public VoiceViewModel Voice { get; }

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

    private async void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        _tab = sender.SelectedItem is null ? 0 : sender.Items.IndexOf(sender.SelectedItem);
        if (GamePanel is null || GameIntro is null || TeamSpeakPanel is null || DiscordPanel is null)
        {
            return; // feuert schon beim Aufbau (IsSelected im XAML), bevor die Felder gesetzt sind
        }

        GameIntro.Visibility = GamePanel.Visibility = _tab == 0 ? Visibility.Visible : Visibility.Collapsed;
        TeamSpeakPanel.Visibility = _tab == 1 ? Visibility.Visible : Visibility.Collapsed;
        DiscordPanel.Visibility = _tab == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (_tab > 0)
        {
            await Voice.LoadAsync(silent: true);
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshCurrentAsync(silent: false);

    private Task RefreshCurrentAsync(bool silent) => _tab == 0 ? ViewModel.LoadAsync(silent) : Voice.LoadAsync(silent);
}
