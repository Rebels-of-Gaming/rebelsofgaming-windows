using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.App.Views;
using RoG.Core.Api;
using RoG.Core.Localization;
using RoG.Core.Navigation;
using RoG.Core.Settings;
using Windows.Graphics;

namespace RoG.App;

/// <summary>Hauptfenster: Titelleiste, Seitenleiste (NavigationView) und Login-Ebene.</summary>
public sealed partial class MainWindow : Window, IDisposable
{
    private TrayIcon? _tray;
    private bool _exiting;
    private bool _trayHintShown;

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
        [StringKeys.NavNotifications] = typeof(NotificationsPage),
        [StringKeys.NavMessages] = typeof(MessagesPage),
        [StringKeys.NavForum] = typeof(ForumPage),
        [StringKeys.NavUploads] = typeof(UploadsPage),
        [StringKeys.NavMedia] = typeof(MediaPage),
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
        Activated += OnWindowActivated;

        services.Api.Handler.Unauthorized += OnUnauthorized;
        services.SignInRequested += (_, _) => ShowLogin();
        services.CurrentUserChanged += (_, _) => UpdateAccountItem();
        services.Navigator = Navigate;
        services.Realtime.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RealtimeService.UnreadNotifications))
            {
                UpdateNotificationsBadge();
            }
        };
        services.Realtime.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RealtimeService.Badge))
            {
                UpdateTrayTooltip();
            }
        };
        CreateTray();
        AddShortcuts();
        _ = JumpListService.UpdateAsync(services.Localizer);
        ToastService.Invoked += (_, url) => DispatcherQueue.TryEnqueue(() =>
        {
            BringToFront();
            OpenTarget(url);
        });
        _ = InitializeAsync();
    }

    /// <summary>Start ohne sichtbares Fenster (Autostart): Inhalte laden, Fenster bleibt im Infobereich.</summary>
    public void StartHidden()
    {
        if (_tray is null)
        {
            Activate();
            return;
        }

        AppWindow.Hide();
    }

    /// <summary>Fenster nach vorn holen (zweiter App-Start, Tray, Toasts).</summary>
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

    private readonly RoG.Core.Security.AppLock _lock = new();

    private void SyncLockSettings()
    {
        _lock.Enabled = _services.Settings.AppLock;
        _lock.IdleTimeout = TimeSpan.FromMinutes(Math.Max(1, _services.Settings.AppLockMinutes));
    }

    /// <summary>Vordergrund verloren/zurück: App-Sperre nach Leerlauf (nur angemeldet – Gäste haben nichts zu schützen).</summary>
    private async void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            _lock.OnLeft();
            return;
        }

        SyncLockSettings();
        if (LockOverlay.Visibility != Visibility.Visible && _lock.OnReturned() && await _services.Tokens.GetAsync() is not null)
        {
            ShowLock();
        }
    }

    private void ShowLock()
    {
        LockError.Visibility = Visibility.Collapsed;
        LockOverlay.Visibility = Visibility.Visible;
        NavView.IsEnabled = false;
        LoginFrame.IsEnabled = false;
        UnlockButton.Focus(FocusState.Programmatic);
    }

    private async void OnUnlockClick(object sender, RoutedEventArgs e)
    {
        UnlockButton.IsEnabled = false;
        try
        {
            if (await HelloService.VerifyAsync(_services.Localizer.GetString(StringKeys.LockPrompt)))
            {
                _lock.Unlock();
                LockOverlay.Visibility = Visibility.Collapsed;
                NavView.IsEnabled = true;
                LoginFrame.IsEnabled = true;
            }
            else
            {
                LockError.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            UnlockButton.IsEnabled = true;
        }
    }

    /// <summary>Ausweg, wenn Hello nicht mehr geht: abmelden (Token weg) statt entsperren.</summary>
    private async void OnLockSignOutClick(object sender, RoutedEventArgs e)
    {
        await _services.Account.LogoutAsync();
        _lock.Unlock();
        LockOverlay.Visibility = Visibility.Collapsed;
        NavView.IsEnabled = true;
        LoginFrame.IsEnabled = true;
        ShowLogin();
    }

    private async Task InitializeAsync()
    {
        SyncLockSettings();
        var token = await _services.Tokens.GetAsync();
        if (token is not null && _lock.OnStart())
        {
            ShowLock();
        }

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
        _ = _services.Realtime.StopAsync();
        _services.Realtime.UnreadNotifications = 0;
        _services.Realtime.UnreadMessages = 0;
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
        if (_pendingRoute is { } route)
        {
            _pendingRoute = null;
            Navigate(route);
        }
    }

    private AppRoute? _pendingRoute;

    /// <summary>
    /// Start oder zweiter Start der App: <c>rog:</c>-Link (Protokoll), Sprungliste (Start-Argument) oder Klick auf einen
    /// Toast (App war geschlossen). Solange die Login-Seite offen ist, wartet das Ziel bis zur Shell.
    /// </summary>
    public void HandleActivation(Microsoft.Windows.AppLifecycle.AppActivationArguments args)
    {
        ArgumentNullException.ThrowIfNull(args);
        AppRoute? route = null;
        switch (args.Kind)
        {
            case Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Protocol when args.Data is Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs protocol:
                route = AppRoute.FromAppUri(protocol.Uri, _services.Environment);
                break;
            case Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Launch when args.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch
                && Uri.TryCreate(launch.Arguments?.Trim(), UriKind.Absolute, out var launchUri):
                route = AppRoute.FromAppUri(launchUri, _services.Environment);
                break;
            case Microsoft.Windows.AppLifecycle.ExtendedActivationKind.AppNotification when args.Data is Microsoft.Windows.AppNotifications.AppNotificationActivatedEventArgs toast:
                OpenTarget(ToastService.UrlFrom(toast));
                return;
        }

        if (route is not null)
        {
            Open(route);
        }
    }

    /// <summary>Ziel eines Toasts: Website-Link → Bereich in der App, sonst Glocke.</summary>
    private void OpenTarget(string? url)
    {
        var route = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? AppRoute.FromWebUrl(uri, _services.Environment) : null;
        Open(route ?? new AppRoute(AppSection.Notifications));
    }

    private void Open(AppRoute route)
    {
        if (NavView.Visibility == Visibility.Visible)
        {
            Navigate(route);
        }
        else
        {
            _pendingRoute = route;
        }
    }

    private void UpdateNotificationsBadge()
    {
        var count = _services.Realtime.UnreadNotifications;
        NotificationsBadge.Value = count;
        NotificationsBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private IEnumerable<NavigationViewItem> AllItems() =>
        NavView.MenuItems.Concat(NavView.FooterMenuItems).OfType<NavigationViewItem>();

    /// <summary>Profil laden und Gerät melden (bei jedem Start, Guide 35). Offline bleibt der Name aus dem Token-Speicher.</summary>
    private async Task LoadUserAsync()
    {
        UpdateAccountItem();
        if (await _services.Tokens.GetAsync() is not { } token)
        {
            await _services.Realtime.StartAsync(null);
            return;
        }

        // Echtzeit sofort mit der Konto-ID aus dem Token-Speicher, nicht erst nach /v1/me
        _ = _services.Realtime.StartAsync(token.UserId);
        try
        {
            _services.CurrentUser = await _services.Account.GetMeAsync();
            _ = _services.Account.RegisterDeviceAsync(AppServices.AppVersion);
            _ = PromptPendingRulesAsync();
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

    private bool _rulesPrompted;

    /// <summary>
    /// Offene Bestätigungen (Regeln, Rechtstexte mit Pflicht) einmal je Start ansprechen – „Jetzt lesen“ öffnet sie unter
    /// Info, dort wird bestätigt. Fehler bleiben still (die Info-Seite zeigt den Stand ohnehin).
    /// </summary>
    private async Task PromptPendingRulesAsync()
    {
        if (_rulesPrompted)
        {
            return;
        }

        _rulesPrompted = true;
        try
        {
            var pending = (await new RoG.Core.Features.Info.InfoService(_services.Api.Client, _services.Environment.WebBaseUrl).ListAsync())
                .Where(e => e.NeedsAcceptance).ToList();
            if (pending.Count == 0 || Content?.XamlRoot is not { } root)
            {
                return;
            }

            var l = _services.Localizer;
            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = l.GetString(StringKeys.RulesPromptTitle),
                Content = l.GetString(StringKeys.RulesPromptText) + "\n\n" + string.Join("\n", pending.Select(p => "• " + p.Title)),
                PrimaryButtonText = l.GetString(StringKeys.RulesPromptRead),
                CloseButtonText = l.GetString(StringKeys.RulesPromptLater),
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                Navigate(new AppRoute(AppSection.Info, pending[0].Key));
            }
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException or InvalidOperationException)
        {
            // still – z. B. schon ein anderer Dialog offen
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

    /// <summary>Navigations-Schlüssel je Bereich (Suche hat keinen Eintrag in der Seitenleiste).</summary>
    private static string? TagOf(AppSection section) => section switch
    {
        AppSection.Home => StringKeys.NavHome,
        AppSection.News => StringKeys.NavNews,
        AppSection.Squads => StringKeys.NavSquads,
        AppSection.Members => StringKeys.NavMembers,
        AppSection.Calendar => StringKeys.NavCalendar,
        AppSection.Servers => StringKeys.NavServers,
        AppSection.Matches => StringKeys.NavMatches,
        AppSection.Info => StringKeys.NavInfo,
        AppSection.Forum => StringKeys.NavForum,
        AppSection.Wiki => StringKeys.NavWiki,
        AppSection.Messages => StringKeys.NavMessages,
        AppSection.Account => StringKeys.NavAccount,
        AppSection.Notifications => StringKeys.NavNotifications,
        _ => null,
    };

    private string? _pendingKey;

    /// <summary>Bereich öffnen, optional mit Eintrag; <c>false</c>, wenn es den Bereich in der App (noch) nicht gibt.</summary>
    public bool Navigate(AppRoute route)
    {
        if (route.Section == AppSection.Settings)
        {
            NavView.SelectedItem = NavView.SettingsItem;
            return true;
        }

        if (route.Section == AppSection.Votes)
        {
            NavView.SelectedItem = null;
            ContentFrame.Navigate(typeof(VotesPage));
            return true;
        }

        if (route.Section == AppSection.Search)
        {
            NavView.SelectedItem = null;
            ContentFrame.Navigate(typeof(SearchPage), route.Key ?? string.Empty);
            return true;
        }

        var tag = TagOf(route.Section);
        if (tag is null || !s_pages.TryGetValue(tag, out var page))
        {
            return false;
        }

        var item = AllItems().FirstOrDefault(i => i.Tag as string == tag);
        if (item is not null && !ReferenceEquals(NavView.SelectedItem, item))
        {
            _pendingKey = route.Key;
            NavView.SelectedItem = item; // navigiert in OnNavigationSelectionChanged
        }
        else
        {
            ContentFrame.Navigate(page, route.Key);
        }

        return true;
    }

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = args.QueryText?.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            Navigate(new AppRoute(AppSection.Search, query));
        }
    }

    private void OnSearchAccelerator(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (NavView.PaneDisplayMode == NavigationViewPaneDisplayMode.LeftMinimal || !NavView.IsPaneOpen)
        {
            NavView.IsPaneOpen = true;
        }

        SearchBox.Focus(FocusState.Keyboard);
    }

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
                ContentFrame.Navigate(page, _pendingKey);
                _pendingKey = null;
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

    /// <summary>Strg+1…9 = Bereiche der Seitenleiste, Strg+, = Einstellungen, Strg+Umschalt+N = Benachrichtigungen.</summary>
    private void AddShortcuts()
    {
        for (var i = 0; i < 9; i++)
        {
            var index = i;
            var accelerator = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
            {
                Key = Windows.System.VirtualKey.Number1 + i,
                Modifiers = Windows.System.VirtualKeyModifiers.Control,
            };
            accelerator.Invoked += (_, args) =>
            {
                var items = NavView.MenuItems.OfType<NavigationViewItem>().Where(x => x.Visibility == Visibility.Visible).ToList();
                if (NavView.Visibility == Visibility.Visible && index < items.Count)
                {
                    NavView.SelectedItem = items[index];
                    args.Handled = true;
                }
            };
            NavView.KeyboardAccelerators.Add(accelerator);
        }

        var settings = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = (Windows.System.VirtualKey)0xBC, Modifiers = Windows.System.VirtualKeyModifiers.Control };
        settings.Invoked += (_, args) =>
        {
            NavView.SelectedItem = NavView.SettingsItem;
            args.Handled = true;
        };
        NavView.KeyboardAccelerators.Add(settings);
    }

    /// <summary>Tray-Symbol: Linksklick öffnet, Menü mit Bereichen und Beenden.</summary>
    private void CreateTray()
    {
        try
        {
            var l = _services.Localizer;
            _tray = new TrayIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"), l.GetString(StringKeys.AppDisplayName))
            {
                MenuItems = [l.GetString(StringKeys.TrayOpen), l.GetString(StringKeys.NavNotifications), l.GetString(StringKeys.NavMessages), l.GetString(StringKeys.NavServers), null, l.GetString(StringKeys.TrayExit)],
            };
            _tray.Activated += (_, _) => BringToFront();
            _tray.MenuItemInvoked += (_, index) =>
            {
                switch (index)
                {
                    case 0:
                        BringToFront();
                        break;
                    case 1:
                        BringToFront();
                        Open(new AppRoute(AppSection.Notifications));
                        break;
                    case 2:
                        BringToFront();
                        Open(new AppRoute(AppSection.Messages));
                        break;
                    case 3:
                        BringToFront();
                        Open(new AppRoute(AppSection.Servers));
                        break;
                    case 5:
                        ExitApp();
                        break;
                }
            };
        }
#pragma warning disable CA1031 // ohne Tray weiter (dann schließt das Fenster die App)
        catch (Exception)
#pragma warning restore CA1031
        {
            _tray = null;
        }
    }

    private void UpdateTrayTooltip()
    {
        var count = _services.Realtime.Badge;
        var name = _services.Localizer.GetString(StringKeys.AppDisplayName);
        _tray?.SetTooltip(count > 0 ? $"{name} – " + string.Format(System.Globalization.CultureInfo.CurrentCulture, _services.Localizer.GetString(StringKeys.TrayTooltipUnread), count) : name);
    }

    /// <summary>„Beenden“ im Tray-Menü: wirklich schließen statt in den Infobereich.</summary>
    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    public void Dispose()
    {
        _tray?.Dispose();
        _tray = null;
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        var maximized = sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        var placement = maximized
            ? _services.Settings.Window is { } old ? old with { Maximized = true } : new WindowPlacement(sender.Position.X, sender.Position.Y, 1280, 820, true)
            : new WindowPlacement(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height, false);

        // Synchron speichern: Nach Closing endet der Prozess, ein offenes await käme nicht mehr an.
        _services.UpdateSettingsNow(s => s with { Window = placement });

        // Schließen → in den Infobereich, solange eingestellt und das Tray-Symbol da ist (Toasts kommen weiter)
        if (!_exiting && _tray is not null && _services.Settings.CloseToTray)
        {
            args.Cancel = true;
            sender.Hide();
            if (!_trayHintShown && _services.Settings.ToastNotifications)
            {
                _trayHintShown = true;
                ToastService.ShowInfo(_services.Localizer.GetString(StringKeys.AppDisplayName), _services.Localizer.GetString(StringKeys.TrayStillRunning));
            }

            return;
        }

        Dispose();
        ToastService.Unregister();
    }

    /// <summary>401 auf eine Anfrage mit Token: Token ist widerrufen oder abgelaufen → verwerfen, Login zeigen.</summary>
    private void OnUnauthorized(object? sender, ApiProblem problem) =>
        DispatcherQueue.TryEnqueue(async () =>
        {
            await _services.Tokens.ClearAsync();
            ShowLogin();
        });
}
