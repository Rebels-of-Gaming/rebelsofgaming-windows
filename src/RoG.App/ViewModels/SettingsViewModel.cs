using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Localization;
using RoG.Core.Settings;

namespace RoG.App.ViewModels;

/// <summary>Eine Auswahl in einer ComboBox (Wert + Anzeigetext).</summary>
public sealed record SettingsOption(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Einstellungen: Sprache, Über die App (Versionen, Links, Lizenzen). Keine Umgebungswahl in der Oberfläche: Ein Token
/// der Produktion darf nie an Staging gehen (Umgebung nur über settings.json für Entwicklung).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly string _startLanguage;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        _startLanguage = services.Settings.Language;

        Languages =
        [
            new(LanguageResolver.System, services.Localizer.GetString(StringKeys.SettingsLanguageSystem)),
            new("de", "Deutsch"),
            new("en", "English"),
            new("fr", "Français"),
        ];
        SelectedLanguage = Languages.FirstOrDefault(l => l.Value == _startLanguage) ?? Languages[0];
        Toasts = services.Settings.ToastNotifications;
        CloseToTray = services.Settings.CloseToTray;
        AppVersionText = Format(StringKeys.SettingsAppVersion, AppServices.AppVersion);
        CopyrightText = Format(StringKeys.SettingsPageClanCopyright, ClanFounded, DateTime.Now.Year);
        PlatformText = Format(StringKeys.SettingsPlatformVersion, "…");
    }

    public IReadOnlyList<SettingsOption> Languages { get; }

    [ObservableProperty]
    public partial SettingsOption SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial bool RestartRequired { get; set; }

    [ObservableProperty]
    public partial bool Toasts { get; set; }

    [ObservableProperty]
    public partial bool CloseToTray { get; set; }

    [ObservableProperty]
    public partial bool Autostart { get; set; }

    /// <summary>App-Sperre mit Windows Hello (nur wählbar, wenn Hello eingerichtet ist).</summary>
    [ObservableProperty]
    public partial bool AppLock { get; set; }

    [ObservableProperty]
    public partial bool CanUseAppLock { get; set; }

    [ObservableProperty]
    public partial bool ShowAppLockHint { get; set; }

    private bool _appLockLoaded;

    [ObservableProperty]
    public partial bool CanChangeAutostart { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAutostartHint))]
    public partial string? AutostartHint { get; set; }

    public bool HasAutostartHint => !string.IsNullOrEmpty(AutostartHint);

    private bool _autostartLoaded;

    public string AppVersionText { get; }

    /// <summary>Gründungsjahr des Clans – Copyright-Zeile wie im Website-Footer (<c>rog.clan.founded</c>).</summary>
    private const int ClanFounded = 2020;

    /// <summary>„© 2020–Jahr Rebels of Gaming“, dahinter „Copyright by wahke“ als Link (wie die Website).</summary>
    public string CopyrightText { get; }

    [ObservableProperty]
    public partial string PlatformText { get; set; }

    public async Task LoadAsync()
    {
        await LoadAutostartAsync();
        CanUseAppLock = await HelloService.IsAvailableAsync();
        ShowAppLockHint = !CanUseAppLock;
        AppLock = _services.Settings.AppLock && CanUseAppLock;
        _appLockLoaded = true;
        try
        {
            var platform = await _services.Account.GetPlatformAsync();
            PlatformText = Format(StringKeys.SettingsPlatformVersion, platform.Version);
        }
        catch (ApiProblemException)
        {
            PlatformText = _services.Localizer.GetString(StringKeys.SettingsPlatformUnavailable);
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            PlatformText = _services.Localizer.GetString(StringKeys.SettingsPlatformUnavailable);
        }
    }

    partial void OnSelectedLanguageChanged(SettingsOption value) => _ = SaveAsync();

    partial void OnToastsChanged(bool value)
    {
        if (_services.Settings.ToastNotifications != value)
        {
            _ = _services.UpdateSettingsAsync(s => s with { ToastNotifications = value });
        }
    }

    /// <summary>Einschalten nur nach erfolgreicher Hello-Prüfung – sonst sperrt man sich womöglich aus.</summary>
    partial void OnAppLockChanged(bool value)
    {
        if (!_appLockLoaded || _services.Settings.AppLock == value)
        {
            return;
        }

        _ = ApplyAppLockAsync(value);
    }

    private async Task ApplyAppLockAsync(bool value)
    {
        if (value && !await HelloService.VerifyAsync(_services.Localizer.GetString(StringKeys.LockPrompt)))
        {
            _appLockLoaded = false;
            AppLock = false;
            _appLockLoaded = true;
            return;
        }

        await _services.UpdateSettingsAsync(s => s with { AppLock = value });
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        if (_services.Settings.CloseToTray != value)
        {
            _ = _services.UpdateSettingsAsync(s => s with { CloseToTray = value });
        }
    }

    /// <summary>Autostart über die StartupTask des Pakets; Windows kann sie per Richtlinie oder Task-Manager sperren.</summary>
    private async Task LoadAutostartAsync()
    {
        try
        {
            var task = await Windows.ApplicationModel.StartupTask.GetAsync("RoGStartup");
            ApplyAutostart(task.State);
        }
#pragma warning disable CA1031 // ohne Paketidentität/StartupTask: Schalter aus
        catch (Exception)
#pragma warning restore CA1031
        {
            CanChangeAutostart = false;
        }

        _autostartLoaded = true;
    }

    partial void OnAutostartChanged(bool value)
    {
        if (_autostartLoaded)
        {
            _ = SetAutostartAsync(value);
        }
    }

    private async Task SetAutostartAsync(bool enable)
    {
        try
        {
            var task = await Windows.ApplicationModel.StartupTask.GetAsync("RoGStartup");
            if (enable)
            {
                ApplyAutostart(await task.RequestEnableAsync());
            }
            else
            {
                task.Disable();
                ApplyAutostart(task.State);
            }

            await _services.UpdateSettingsAsync(s => s with { StartWithWindows = enable });
        }
#pragma warning disable CA1031 // Fehler beim Umschalten → Stand neu lesen
        catch (Exception)
#pragma warning restore CA1031
        {
            await LoadAutostartAsync();
        }
    }

    private void ApplyAutostart(Windows.ApplicationModel.StartupTaskState state)
    {
        _autostartLoaded = false;
        Autostart = state is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
        CanChangeAutostart = state is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.Disabled;
        AutostartHint = state switch
        {
            Windows.ApplicationModel.StartupTaskState.DisabledByUser => _services.Localizer.GetString(StringKeys.SettingsAutostartDisabledByUser),
            Windows.ApplicationModel.StartupTaskState.DisabledByPolicy or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy => _services.Localizer.GetString(StringKeys.SettingsAutostartPolicy),
            _ => null,
        };
        _autostartLoaded = true;
    }

    private async Task SaveAsync()
    {
        var language = SelectedLanguage.Value;
        if (_services.Settings.Language != language)
        {
            await _services.UpdateSettingsAsync(s => s with { Language = language });
        }

        // Die Sprache wirkt erst nach einem Neustart (Ressourcen werden beim Start festgelegt, Annahme 12)
        RestartRequired = language != _startLanguage;
    }

    [RelayCommand]
    private Task OpenWebsiteAsync() => _services.Browser.OpenAsync(_services.Environment.WebBaseUrl);

    [RelayCommand]
    private Task OpenStatusAsync() => _services.Browser.OpenAsync(_services.Environment.StatusUrl);

    [RelayCommand]
    private Task OpenPrivacyAsync() => OpenLegalAsync("privacy");

    [RelayCommand]
    private Task OpenTermsAsync() => OpenLegalAsync("terms");

    [RelayCommand]
    private Task OpenImprintAsync() => OpenLegalAsync("imprint");

    /// <summary>Kinderschutz-Standards der Plattform (Website v0.30.3).</summary>
    [RelayCommand]
    private Task OpenChildSafetyAsync() => OpenLegalAsync("child-safety");

    private Task OpenLegalAsync(string page) => _services.Browser.OpenAsync(new Uri(_services.Environment.WebBaseUrl, $"{_services.Language}/{page}"));

    private string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, _services.Localizer.GetString(key), args);
}
