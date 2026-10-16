using System.Globalization;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
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
