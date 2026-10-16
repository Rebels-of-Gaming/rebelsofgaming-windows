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
        AppVersionText = Format(StringKeys.SettingsAppVersion, AppServices.AppVersion);
        PlatformText = Format(StringKeys.SettingsPlatformVersion, "…");
    }

    public IReadOnlyList<SettingsOption> Languages { get; }

    [ObservableProperty]
    public partial SettingsOption SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial bool RestartRequired { get; set; }

    public string AppVersionText { get; }

    [ObservableProperty]
    public partial string PlatformText { get; set; }

    public async Task LoadAsync()
    {
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
    private Task OpenPrivacyAsync() => _services.Browser.OpenAsync(new Uri(_services.Environment.WebBaseUrl, $"{_services.Language}/privacy"));

    private string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, _services.Localizer.GetString(key), args);
}
