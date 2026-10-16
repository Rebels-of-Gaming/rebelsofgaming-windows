using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Account;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Konto → Geräte &amp; Daten: angemeldete Geräte, Datenexport, Konto löschen.</summary>
public sealed partial class AccountDataViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly AccountDataService _data = new(services.Api.Client, services.Tokens);

    public ObservableCollection<DeviceItemViewModel> Devices { get; } = [];

    public ObservableCollection<ExportItemViewModel> Exports { get; } = [];

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    protected override bool HasData => Devices.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var devices = await _data.GetDevicesAsync(cancellationToken);
        Devices.Clear();
        foreach (var d in devices)
        {
            Devices.Add(new DeviceItemViewModel(d, this, Services.Localizer));
        }

        try
        {
            var exports = await _data.GetExportsAsync(cancellationToken);
            Exports.Clear();
            foreach (var e in exports)
            {
                Exports.Add(new ExportItemViewModel(e, this, Services.Localizer));
            }
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // Exporte sind Beiwerk
        }
    }

    internal async Task RemoveAsync(DeviceItemViewModel item)
    {
        await RunAsync(async () =>
        {
            await _data.RemoveDeviceAsync(item.Device);
            if (item.Device.Current)
            {
                Services.RequestSignIn();
                return;
            }

            Devices.Remove(item);
            Show(Services.Localizer.GetString(StringKeys.AccountDeviceRemoved), InfoBarSeverity.Success);
        });
    }

    [RelayCommand]
    private Task RequestExportAsync() => RunAsync(async () =>
    {
        await _data.RequestExportAsync();
        Show(Services.Localizer.GetString(StringKeys.AccountExportRequested), InfoBarSeverity.Success);
        await LoadAsync(silent: true);
    });

    internal Task DownloadAsync(ExportItemViewModel item) => RunAsync(async () =>
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var path = await _data.DownloadExportAsync(item.Id, folder);
        Show(Format(StringKeys.MediaDownloaded, Path.GetFileName(path)), InfoBarSeverity.Success);
    });

    /// <summary>Konto löschen – die Seite fragt vorher mit Bestätigungsfeld; danach zurück zum Login.</summary>
    public async Task<string?> DeleteAccountAsync(string confirmation)
    {
        try
        {
            await _data.DeleteAccountAsync(confirmation);
            await Services.UpdateSettingsAsync(s => s with { GuestMode = false });
            Services.RequestSignIn();
            return null;
        }
        catch (ApiProblemException ex)
        {
            return ex.Problem.Code == "validation_failed" && ex.Problem.Detail is { } d ? d : Services.Localizer.GetError(ex.Problem.Code);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or ArgumentException)
        {
            return Services.Localizer.GetError(ApiProblem.Codes.Unknown);
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or IOException or UnauthorizedAccessException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    private void Show(string text, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }
}

public sealed partial class DeviceItemViewModel(DeviceInfo device, AccountDataViewModel owner, ILocalizer localizer)
{
    public DeviceInfo Device => device;

    public string Name { get; } = device.Current ? $"{device.Name} ({localizer.GetString(StringKeys.AccountThisDevice)})" : device.Name;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        device.Platform,
        device.AppVersion is null ? null : "v" + device.AppVersion,
        device.LastActiveAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
    }.Where(s => !string.IsNullOrEmpty(s)));

    [RelayCommand]
    private Task RemoveAsync() => owner.RemoveAsync(this);
}

public sealed partial class ExportItemViewModel(DataExport export, AccountDataViewModel owner, ILocalizer localizer)
{
    public string Id => export.Id;

    public string Text { get; } = string.Join(" · ", new[]
    {
        export.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
        export.IsReady ? localizer.GetString(StringKeys.AccountExportReady) : localizer.GetString(StringKeys.AccountExportPending),
        export.Size is { } s ? $"{s / 1048576.0:0.0} MB" : null,
    }.Where(x => !string.IsNullOrEmpty(x)));

    public bool IsReady => export.IsReady;

    [RelayCommand]
    private Task DownloadAsync() => owner.DownloadAsync(this);
}
