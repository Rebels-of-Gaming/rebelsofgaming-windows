using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;
using RoG.Core.Features.Minecraft;
using RoG.Core.Localization;

namespace RoG.App.Views;

/// <summary>
/// Weltkarte: Live-Spieler alle 5 s, solange die Seite offen ist (Guide 48); Links <c>/minecraft/karte?server=…</c>
/// und <c>rog://map?…</c> wählen Server/Welt und zentrieren auf <c>x</c>/<c>z</c>.
/// </summary>
public sealed partial class MinecraftMapPage : Page
{
    private readonly DispatcherTimer _live = new() { Interval = MinecraftService.LiveInterval };

    public MinecraftMapPage()
    {
        ViewModel = new MinecraftMapViewModel(App.Services);
        InitializeComponent();
        Map.TileIndexLoader = ViewModel.LoadTilesAsync;
        ViewModel.Confirm = ConfirmAsync;
        ViewModel.WorldChanged += (_, e) => Map.SetWorld(e.World, e.X, e.Z);
        ViewModel.PlayersChanged += (_, players) => Map.SetPlayers(players);
        ViewModel.TilesReset += (_, _) => Map.ReloadTiles();
        _live.Tick += async (_, _) => await ViewModel.RefreshLiveAsync();
    }

    public MinecraftMapViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.UseLink(e.Parameter as string);
        _live.Start();
        await ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _live.Stop();
        base.OnNavigatedFrom(e);
    }

    private void OnPointClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MapPointItem point)
        {
            Map.CenterOn(point.X, point.Z);
            Map.Focus(FocusState.Programmatic);
        }
    }

    private async void OnRefreshInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.RefreshCommand.ExecuteAsync(null);
    }

    private async Task<bool> ConfirmAsync(string title, string text)
    {
        var l = App.Services.Localizer;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = l.GetString(StringKeys.CommonConfirm),
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
