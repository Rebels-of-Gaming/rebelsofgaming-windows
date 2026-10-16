using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.App.Views;
using RoG.Core.Api;
using RoG.Core.Localization;
using RoG.Core.Settings;
using Windows.Graphics;

namespace RoG.App;

/// <summary>Hauptfenster: Titelleiste, Seitenleiste (NavigationView) und Login-Ebene.</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Navigations-Schlüssel → Seite. Nicht aufgeführte Bereiche zeigen „Kommt bald“.</summary>
    private static readonly Dictionary<string, Type> s_pages = new(StringComparer.Ordinal)
    {
        [StringKeys.NavHome] = typeof(HomePage),
        [StringKeys.NavServers] = typeof(ServersPage),
        [StringKeys.NavNews] = typeof(NewsPage),
        [StringKeys.NavSquads] = typeof(SquadsPage),
        [StringKeys.NavMembers] = typeof(MembersPage),
        [StringKeys.NavCalendar] = typeof(EventsPage),
        [StringKeys.NavInfo] = typeof(InfoPage),
        [StringKeys.NavMatches] = typeof(MatchesPage),
        [StringKeys.NavAccount] = typeof(AccountPage),
    };

    private const string SettingsKey = "Settings";

    private readonly AppServices _services;

    public MainWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();

        Title = services.Localizer.GetString(StringKeys.AppDisplayName);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        RestorePlacement(services.Settings.Window);
        AppWindow.Closing += OnClosing;

        services.Api.Handler.Unauthorized += OnUnauthorized;
        services.SignInRequested += (_, _) => ShowLogin();
        services.CurrentUserChanged += (_, _) => UpdateAccountItem();
        _ = InitializeAsync();
    }

    /// <summary>Fenster nach vorn holen (zweiter App-Start, später Tray/Toasts).</summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        AppWindow.Show();
        Activate();
        NativeMethods.SetForegroundWindow(Win32Interop.GetWindowFromWindowId(AppWindow.Id));
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
        _services.CurrentUser = null;
        NavView.Visibility = Visibility.Collapsed;
        LoginFrame.Visibility = Visibility.Visible;
        LoginFrame.Navigate(typeof(LoginPage), new EventHandler((_, _) => ShowShell()));
    }

    private void ShowShell()
    {
        LoginFrame.Visibility = Visibility.Collapsed;
        LoginFrame.Content = null;
        NavView.Visibility = Visibility.Visible;
        ContentFrame.BackStack.Clear();

        var last = _services.Settings.LastSection;
        NavView.SelectedItem = last == SettingsKey
            ? NavView.SettingsItem
            : AllItems().FirstOrDefault(i => i.Tag as string == last && i.Visibility == Visibility.Visible) ?? NavView.MenuItems[0];
        _ = LoadUserAsync();
    }

    private IEnumerable<NavigationViewItem> AllItems() =>
        NavView.MenuItems.Concat(NavView.FooterMenuItems).OfType<NavigationViewItem>();

    /// <summary>Profil laden und Gerät melden (bei jedem Start, Guide 35). Offline bleibt der Name aus dem Token-Speicher.</summary>
    private async Task LoadUserAsync()
    {
        UpdateAccountItem();
        if (await _services.Tokens.GetAsync() is null)
        {
            return;
        }

        try
        {
            _services.CurrentUser = await _services.Account.GetMeAsync();
            _ = _services.Account.RegisterDeviceAsync(AppServices.AppVersion);
        }
        catch (ApiProblemException)
        {
            // offline/Serverfehler: Seiten zeigen ihren eigenen Fehlerzustand; 401 läuft über OnUnauthorized
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            // unerwartete Antwort – ohne Profil weiter
        }
    }

    private async void UpdateAccountItem()
    {
        var user = _services.CurrentUser;
        var token = user is null ? await _services.Tokens.GetAsync() : null;
        var name = user?.Name ?? token?.UserName;
        AccountItem.Content = string.IsNullOrEmpty(name) ? _services.Localizer.GetString(StringKeys.NavAccount) : name;

        // Verwaltung nur mit einem Verwaltungsrecht (Rechte aus /v1/me, nie über Rollennamen)
        AdminItem.Visibility = user is not null && user.Permissions.Values.Any(p => p.Any(IsAdminPermission))
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static bool IsAdminPermission(string permission) =>
        permission.EndsWith(".manage", StringComparison.Ordinal) || permission.StartsWith("roles.", StringComparison.Ordinal)
        || permission.StartsWith("moderation.", StringComparison.Ordinal) || permission.StartsWith("admin.", StringComparison.Ordinal);

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string key;
        if (args.IsSettingsSelected)
        {
            key = SettingsKey;
            ContentFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            key = tag;
            if (s_pages.TryGetValue(tag, out var page))
            {
                ContentFrame.Navigate(page);
            }
            else
            {
                ContentFrame.Navigate(typeof(PlaceholderPage), tag);
            }
        }
        else
        {
            return;
        }

        if (_services.Settings.LastSection != key)
        {
            _ = _services.UpdateSettingsAsync(s => s with { LastSection = key });
        }
    }

    private void RestorePlacement(WindowPlacement? saved)
    {
        var fallback = new RectInt32(0, 0, 1280, 820);
        if (saved is not null)
        {
            var area = DisplayArea.GetFromPoint(new PointInt32(saved.X, saved.Y), DisplayAreaFallback.Nearest).WorkArea;
            if (saved.FitInto(area.X, area.Y, area.Width, area.Height) is { } fit)
            {
                AppWindow.MoveAndResize(new RectInt32(fit.X, fit.Y, fit.Width, fit.Height));
                if (fit.Maximized && AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.Maximize();
                }

                return;
            }
        }

        AppWindow.Resize(new SizeInt32(fallback.Width, fallback.Height));
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        var maximized = sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        var placement = maximized
            ? _services.Settings.Window is { } old ? old with { Maximized = true } : new WindowPlacement(sender.Position.X, sender.Position.Y, 1280, 820, true)
            : new WindowPlacement(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height, false);

        // Synchron speichern: Nach Closing endet der Prozess, ein offenes await käme nicht mehr an.
        _services.UpdateSettingsNow(s => s with { Window = placement });
    }

    /// <summary>401 auf eine Anfrage mit Token: Token ist widerrufen oder abgelaufen → verwerfen, Login zeigen.</summary>
    private void OnUnauthorized(object? sender, ApiProblem problem) =>
        DispatcherQueue.TryEnqueue(async () =>
        {
            await _services.Tokens.ClearAsync();
            ShowLogin();
        });
}
