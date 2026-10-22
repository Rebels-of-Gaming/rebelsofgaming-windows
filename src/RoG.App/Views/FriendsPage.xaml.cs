using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;
using RoG.Core.Localization;

namespace RoG.App.Views;

public sealed partial class FriendsPage : Page
{
    public FriendsPage()
    {
        ViewModel = new FriendsViewModel(App.Services);
        InitializeComponent();
        ViewModel.Confirm = ConfirmAsync;
    }

    public FriendsViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.Services.Realtime.FriendsChanged += OnFriendsChanged;
        await ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        App.Services.Realtime.FriendsChanged -= OnFriendsChanged;
        base.OnNavigatedFrom(e);
    }

    // .friends.changed kommt ohne Inhalt – still neu laden (auch nach Änderungen durch die andere Person oder aus Minecraft)
    private async void OnFriendsChanged(object? sender, EventArgs e) => await ViewModel.LoadAsync(silent: true);

    private void OnAddKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            ViewModel.AddCommand.Execute(null);
        }
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
