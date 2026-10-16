using System.Globalization;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Account;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Core.Settings;
using Windows.ApplicationModel;
using Windows.Storage;

namespace RoG.App.Services;

/// <summary>
/// Composition Root: Einstellungen, Sprache, Token-Speicher, API-Client, Übersetzungen. Eine Instanz je Prozess.
/// Logik liegt in RoG.Core – hier nur die Windows-spezifische Verdrahtung.
/// </summary>
public sealed class AppServices : IDisposable
{
    private AppServices(
        ISettingsStore settingsStore,
        AppSettings settings,
        string language,
        RoGEnvironment environment,
        ITokenStore tokens,
        ApiContext apiContext,
        RoGApi api,
        ILocalizer localizer,
        IBrowserLauncher browser)
    {
        SettingsStore = settingsStore;
        Settings = settings;
        Language = language;
        Environment = environment;
        Tokens = tokens;
        ApiContext = apiContext;
        Api = api;
        Localizer = localizer;
        Browser = browser;
        Account = new AccountService(api.Client, tokens);
    }

    public ISettingsStore SettingsStore { get; }

    public AppSettings Settings { get; private set; }

    /// <summary>Aktive Sprache: de, en oder fr.</summary>
    public string Language { get; }

    public RoGEnvironment Environment { get; }

    public ITokenStore Tokens { get; }

    public ApiContext ApiContext { get; }

    public RoGApi Api { get; }

    public ILocalizer Localizer { get; }

    public IBrowserLauncher Browser { get; }

    public AccountService Account { get; }

    /// <summary>Angemeldete Person (<c>/v1/me</c>); <c>null</c> im Gastmodus oder solange noch nicht geladen.</summary>
    public UserProfile? CurrentUser
    {
        get;
        set
        {
            field = value;
            CurrentUserChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CurrentUserChanged;

    /// <summary>Login-Seite zeigen (nach dem Abmelden oder „Anmelden“ im Gastmodus); das Hauptfenster reagiert.</summary>
    public event EventHandler? SignInRequested;

    public void RequestSignIn() => SignInRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Spiele-Links und Zwischenablage für „Verbinden“ auf Gameservern.</summary>
    public ServerConnector ServerConnector { get; } = new(new WindowsExternalLauncher(), new WindowsClipboard());

    /// <summary>„2.0.0“ aus der Paketversion (vierte Stelle ist im Store immer 0).</summary>
    public static string AppVersion
    {
        get
        {
            var v = Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static async Task<AppServices> CreateAsync()
    {
        var localFolder = ApplicationData.Current.LocalFolder.Path;
        var settingsStore = new JsonFileSettingsStore(Path.Combine(localFolder, "settings.json"));
        var settings = await settingsStore.LoadAsync();

        var language = LanguageResolver.Resolve(settings.Language, CultureInfo.CurrentUICulture);
        // Paket-Apps: steuert, welche Strings/<Sprache>/Resources.resw geladen wird. Wechsel wirkt nach Neustart.
        Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = LanguageResolver.ToResourceTag(language);

        var environment = RoGEnvironment.Get(settings.Environment);
        var tokens = new PasswordVaultTokenStore(Path.Combine(localFolder, "account.json"));
        var apiContext = new ApiContext(language, AppVersion);
        var api = new RoGApi(environment, tokens, apiContext);

        return new AppServices(settingsStore, settings, language, environment, tokens, apiContext, api, new ResourceLocalizer(), new LauncherBrowser());
    }

    public async Task UpdateSettingsAsync(Func<AppSettings, AppSettings> change)
    {
        Settings = change(Settings);
        await SettingsStore.SaveAsync(Settings);
    }

    /// <summary>
    /// Synchron speichern, z. B. beim Schließen des Fensters (danach endet der Prozess). Läuft auf dem Threadpool,
    /// damit kein <c>await</c> auf den blockierten UI-Thread zurückwill.
    /// </summary>
    public void UpdateSettingsNow(Func<AppSettings, AppSettings> change)
    {
        Settings = change(Settings);
        var snapshot = Settings;
        Task.Run(() => SettingsStore.SaveAsync(snapshot)).GetAwaiter().GetResult();
    }

    /// <summary>Texte für die Seite, die der Browser nach dem Login-Rücksprung zeigt.</summary>
    public LoopbackPageTexts LoopbackTexts() => new(
        Language,
        Localizer.GetString(StringKeys.LoopbackTitle),
        Localizer.GetString(StringKeys.LoopbackSuccess),
        Localizer.GetString(StringKeys.LoopbackFailure));

    public void Dispose()
    {
        Api.Dispose();
        (Tokens as IDisposable)?.Dispose();
    }
}
