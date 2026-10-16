using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.App.Views;
using RoG.Core.Api;
using RoG.Core.Localization;

namespace RoG.App;

/// <summary>Hauptfenster: Titelleiste, Seitenleiste (NavigationView) und Login-Ebene.</summary>
public sealed partial class MainWindow : Window
{
    private readonly AppServices _services;

    public MainWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();

        Title = services.Localizer.GetString(StringKeys.AppDisplayName);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));

        services.Api.Handler.Unauthorized += OnUnauthorized;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        var token = await _services.Tokens.GetAsync();
        if (token is not null || _services.Settings.GuestMode)
        {
            ShowShell();
        }
        else
        {
            ShowLogin();
        }
    }

    private void ShowLogin()
    {
        NavView.Visibility = Visibility.Collapsed;
        LoginFrame.Visibility = Visibility.Visible;
        LoginFrame.Navigate(typeof(LoginPage), new EventHandler((_, _) => ShowShell()));
    }

    private void ShowShell()
    {
        LoginFrame.Visibility = Visibility.Collapsed;
        LoginFrame.Content = null;
        NavView.Visibility = Visibility.Visible;
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string key })
        {
            return;
        }

        if (key == StringKeys.NavHome)
        {
            ContentFrame.Navigate(typeof(HomePage));
        }
        else if (key == StringKeys.NavServers)
        {
            ContentFrame.Navigate(typeof(ServersPage));
        }
        else
        {
            ContentFrame.Navigate(typeof(PlaceholderPage), key);
        }
    }

    /// <summary>401 auf eine Anfrage mit Token: Token ist widerrufen oder abgelaufen → verwerfen, Login zeigen.</summary>
    private void OnUnauthorized(object? sender, ApiProblem problem) =>
        DispatcherQueue.TryEnqueue(async () =>
        {
            await _services.Tokens.ClearAsync();
            ShowLogin();
        });
}
