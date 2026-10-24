using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;

namespace RoG.App.Views;

/// <summary>
/// Server: Gameserver mit „Verbinden“ (aktualisiert sich jede Minute), Minecraft-Beitritt, TeamSpeak-Viewer mit
/// „Verbinden“ und Discord-Voice – je ein Reiter. Minecraft und Voice laden erst, wenn der Reiter geöffnet wird;
/// <c>/minecraft</c> bzw. <c>rog://servers/minecraft</c> öffnet direkt den Minecraft-Reiter.
/// </summary>
public sealed partial class ServersPage : Page
{
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMinutes(1) };
    private const int MinecraftTab = 1;
    private int _tab;

    public ServersPage()
    {
        ViewModel = new ServersViewModel(App.Services);
        Voice = new VoiceViewModel(App.Services);
        Join = new MinecraftJoinViewModel(App.Services) { Confirm = ConfirmAsync };
        InitializeComponent();
        _refresh.Tick += async (_, _) => await RefreshCurrentAsync(silent: true);
    }

    public ServersViewModel ViewModel { get; }

    public VoiceViewModel Voice { get; }

    public MinecraftJoinViewModel Join { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _refresh.Start();
        if (e.Parameter is string key && key == RoG.Core.Navigation.AppRoute.MinecraftKey)
        {
            TabBar.SelectedItem = TabBar.Items[MinecraftTab];
        }

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
        if (GamePanel is null || GameIntro is null || MinecraftPanel is null || TeamSpeakPanel is null || DiscordPanel is null)
        {
            return; // feuert schon beim Aufbau (IsSelected im XAML), bevor die Felder gesetzt sind
        }

        GameIntro.Visibility = GamePanel.Visibility = _tab == 0 ? Visibility.Visible : Visibility.Collapsed;
        MinecraftPanel.Visibility = _tab == MinecraftTab ? Visibility.Visible : Visibility.Collapsed;
        TeamSpeakPanel.Visibility = _tab == 2 ? Visibility.Visible : Visibility.Collapsed;
        DiscordPanel.Visibility = _tab == 3 ? Visibility.Visible : Visibility.Collapsed;
        if (_tab == MinecraftTab)
        {
            await Join.LoadAsync();
        }
        else if (_tab > 0)
        {
            await Voice.LoadAsync(silent: true);
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshCurrentAsync(silent: false);

    private void OnLeaderboardsClick(object sender, RoutedEventArgs e) => App.Services.Navigate(new RoG.Core.Navigation.AppRoute(RoG.Core.Navigation.AppSection.Leaderboards));

    private void OnMapClick(object sender, RoutedEventArgs e) => App.Services.Navigate(new RoG.Core.Navigation.AppRoute(RoG.Core.Navigation.AppSection.MinecraftMap));

    private void OnLinkAccountClick(object sender, RoutedEventArgs e) =>
        App.Services.Navigate(new RoG.Core.Navigation.AppRoute(RoG.Core.Navigation.AppSection.Account, RoG.Core.Navigation.AppRoute.MinecraftKey));

    private async void OnJoinEditExpanding(Expander sender, ExpanderExpandingEventArgs args) => await Join.LoadEditAsync();

    private async void OnPackUploadClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".zip");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
        if (await picker.PickSingleFileAsync() is not { } file)
        {
            return;
        }

        var size = (await file.GetBasicPropertiesAsync()).Size;
        using var stream = await file.OpenStreamForReadAsync();
        await Join.UploadPackAsync(stream, file.Name, size);
    }

    private async Task<bool> ConfirmAsync(string title, string text)
    {
        var l = App.Services.Localizer;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = l.GetString(RoG.Core.Localization.StringKeys.CommonConfirm),
            CloseButtonText = l.GetString(RoG.Core.Localization.StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void OnBanListClick(object sender, RoutedEventArgs e) => App.Services.Navigate(new RoG.Core.Navigation.AppRoute(RoG.Core.Navigation.AppSection.Bans));

    private Task RefreshCurrentAsync(bool silent) => _tab switch
    {
        0 => ViewModel.LoadAsync(silent),
        // Beitrittsdaten ändern sich selten – nur auf Knopfdruck neu laden, nicht im Minutentakt
        MinecraftTab => silent ? Task.CompletedTask : Join.LoadAsync(force: true),
        _ => Voice.LoadAsync(silent),
    };
}
