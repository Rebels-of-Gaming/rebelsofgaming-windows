using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;

namespace RoG.App.Views;

public sealed partial class MessagesPage : Page
{
    public MessagesPage()
    {
        ViewModel = new MessagesViewModel(App.Services);
        InitializeComponent();
    }

    public MessagesViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string { Length: > 0 } id)
        {
            await ViewModel.OpenAsync(id);
        }
        else
        {
            await ViewModel.LoadAsync();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.Dispose();
        base.OnNavigatedFrom(e);
    }

    private void OnDetailClosed(object? sender, EventArgs e) => ViewModel.CloseChat();

    /// <summary>Chat in eigenem Fenster öffnen (bleibt offen, auch wenn die Seite wechselt).</summary>
    private void OnPopOutClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Chat is { } chat)
        {
            ChatWindow.Open(chat.ConversationId, chat.Title);
            ViewModel.CloseChat();
        }
    }

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
