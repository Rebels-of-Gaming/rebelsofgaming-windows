using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Apps;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// Verwaltung → App-Statistik (Website v0.53.0): Store-Downloads und angemeldete App-Geräte je Plattform, nur Summen.
/// Sichtbar mit Recht <c>apps.stats</c> und Scope <c>system:read</c>.
/// </summary>
public sealed partial class AppStatsAdminViewModel(AppServices services, Action<string, InfoBarSeverity> show) : ObservableObject
{
    private readonly AppStatsService _stats = new(services.Api.Http);
    private bool _loaded;

    public bool CanView { get; } = services.CurrentUser is { } me && me.HasScope(AppStatsService.Scope) && me.Can(AppStatsService.Permission);

    public ObservableCollection<StatLineViewModel> Platforms { get; } = [];

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LastImport { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public async Task EnsureLoadedAsync()
    {
        if (!_loaded)
        {
            _loaded = true;
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        IsBusy = true;
        try
        {
            var s = await _stats.GetAsync();
            Summary = F(StringKeys.AppStatsSummary, N(s.Downloads), N(s.Downloads30d), N(s.Devices), N(s.Active));
            LastImport = s.LastImport is { } at ? F(StringKeys.AppStatsLastImport, at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)) : string.Empty;
            Platforms.Clear();
            foreach (var p in s.Platforms)
            {
                var store = p.Store is null
                    ? L(StringKeys.AppStatsNoStore)
                    : F(StringKeys.AppStatsStore, N(p.Downloads), N(p.Downloads30d), N(p.Updates30d)) + (p.StoreConfigured ? string.Empty : " · " + L(StringKeys.AppStatsStoreNotConfigured));
                var devices = F(StringKeys.AppStatsDevices, N(p.Devices), N(p.ActiveDevices), N(p.Accounts));
                var versions = p.Versions.Count > 0 ? " · " + string.Join(", ", p.Versions.Select(v => $"{v.Version} ({N(v.Devices)})")) : string.Empty;
                Platforms.Add(new StatLineViewModel(PlatformName(p.Platform), store + "\n" + devices + versions));
            }
        }
        catch (Exception ex) when (ex is ApiProblemException or HttpRequestException or System.Text.Json.JsonException)
        {
            show(ex is ApiProblemException p ? services.Localizer.GetError(p.Problem.Code) : services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string PlatformName(string platform) => platform switch
    {
        "ios" => "iPhone & iPad",
        "android" => "Android",
        "windows" => "Windows",
        "linux" => "Linux",
        "macos" => "macOS",
        _ => platform,
    };

    private static string N(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private string L(string key) => services.Localizer.GetString(key);

    private string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, L(key), args);
}
