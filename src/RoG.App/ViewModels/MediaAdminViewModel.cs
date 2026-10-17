using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Admin;
using RoG.Core.Features.Control;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Verwaltung: Galerie-Freigaben, Upload-Meldungen mit Speicherübersicht, Links zur Website-Verwaltung.</summary>
public sealed partial class MediaAdminViewModel(AppServices services, Action<string, InfoBarSeverity> show) : ObservableObject
{
    private readonly MediaAdminService _admin = new(services.Api.Http);

    public AppServices Services => services;

    public bool CanGallery { get; } = MediaAdminService.CanModerateGallery(services.CurrentUser);

    public bool CanUploads { get; } = MediaAdminService.CanManageUploads(services.CurrentUser);

    public IReadOnlyList<AdminWebLinkViewModel> WebLinks { get; } = services.Environment.AdminBaseUrl is { } admin
        ? AdminWebLinks.For(services.CurrentUser).Select(l => new AdminWebLinkViewModel(services.Localizer.GetString("AdminWeb_" + l.Key), new Uri(admin, l.Path), services)).ToList()
        : [];

    public bool HasWebLinks => WebLinks.Count > 0;

    public ObservableCollection<PendingGalleryItemViewModel> Pending { get; } = [];

    public ObservableCollection<UploadReportItemViewModel> Reports { get; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool NoPending { get; set; }

    [ObservableProperty]
    public partial bool NoReports { get; set; }

    [ObservableProperty]
    public partial string StorageText { get; set; } = string.Empty;

    /// <summary>Rückfrage – setzt die Seite.</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; } = (_, _) => Task.FromResult(false);

    private string L(string key) => services.Localizer.GetString(key);

    [RelayCommand]
    public async Task LoadGalleryAsync()
    {
        IsBusy = true;
        try
        {
            await RunAsync(async () =>
            {
                var items = await _admin.PendingAsync();
                Pending.Clear();
                foreach (var i in items)
                {
                    Pending.Add(new PendingGalleryItemViewModel(i, this));
                }

                NoPending = Pending.Count == 0;
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task DecideAsync(PendingGalleryItemViewModel item, bool approve)
    {
        if (!approve && !await Confirm(L(StringKeys.MediaAdminRejectTitle), item.Title))
        {
            return;
        }

        if (await RunAsync(() => approve ? _admin.ApproveAsync(item.Id) : _admin.RejectAsync(item.Id)))
        {
            Pending.Remove(item);
            NoPending = Pending.Count == 0;
            show(L(approve ? StringKeys.MediaAdminApproved : StringKeys.MediaAdminRejected), InfoBarSeverity.Success);
        }
    }

    [RelayCommand]
    public async Task LoadUploadsAsync()
    {
        IsBusy = true;
        try
        {
            await RunAsync(async () =>
            {
                var reports = await _admin.ReportsAsync(handled: false);
                Reports.Clear();
                foreach (var r in reports)
                {
                    Reports.Add(new UploadReportItemViewModel(r, this));
                }

                NoReports = Reports.Count == 0;
                var storage = await _admin.StorageAsync();
                StorageText = string.Format(CultureInfo.CurrentCulture, L(StringKeys.MediaAdminStorage), storage.Uploads, ServerPaths.FormatSize(storage.Bytes))
                    + (storage.TopUsers.Count > 0 ? "\n" + string.Join("\n", storage.TopUsers.Select(u => $"{u.Name}: {u.Uploads} · {ServerPaths.FormatSize(u.Bytes)}")) : string.Empty);
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task ResolveAsync(UploadReportItemViewModel item, bool delete)
    {
        if (delete && !await Confirm(L(StringKeys.MediaAdminDeleteUploadTitle), item.Title))
        {
            return;
        }

        if (await RunAsync(() => _admin.ResolveAsync(item.Id, delete)))
        {
            Reports.Remove(item);
            NoReports = Reports.Count == 0;
            show(L(delete ? StringKeys.MediaAdminUploadDeleted : StringKeys.MediaAdminUploadKept), InfoBarSeverity.Success);
        }
    }

    private async Task<bool> RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (ApiProblemException ex)
        {
            show(services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (HttpRequestException)
        {
            show(services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }

        return false;
    }
}

public sealed partial class PendingGalleryItemViewModel(PendingGalleryItem item, MediaAdminViewModel owner)
{
    public string Id => item.Id;

    public string Title { get; } = item.Caption ?? item.AlbumTitle;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        item.AlbumTitle,
        item.Author,
        item.CreatedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
        item.Kind == "image" ? null : item.Kind,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public ImageSource? Thumb { get; } = item.Thumb is null ? null : new BitmapImage(item.Thumb) { DecodePixelWidth = 320 };

    [RelayCommand]
    private Task ApproveAsync() => owner.DecideAsync(this, approve: true);

    [RelayCommand]
    private Task RejectAsync() => owner.DecideAsync(this, approve: false);

    [RelayCommand]
    private Task OpenAsync() => item.Full is { } url ? owner.Services.Browser.OpenAsync(url) : Task.CompletedTask;
}

public sealed partial class UploadReportItemViewModel(UploadReport report, MediaAdminViewModel owner)
{
    public string Id => report.Id;

    public string Title { get; } = report.UploadTitle ?? "—";

    public string Reason => report.Reason;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        report.Owner,
        report.Reporter is { } r ? "← " + r : null,
        report.CreatedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
    }.Where(s => !string.IsNullOrEmpty(s)));

    public ImageSource? Thumb { get; } = report.Preview is null ? null : new BitmapImage(report.Preview) { DecodePixelWidth = 320 };

    [RelayCommand]
    private Task KeepAsync() => owner.ResolveAsync(this, delete: false);

    [RelayCommand]
    private Task DeleteAsync() => owner.ResolveAsync(this, delete: true);

    [RelayCommand]
    private Task OpenAsync() => report.Direct is { } url ? owner.Services.Browser.OpenAsync(url) : Task.CompletedTask;
}

public sealed partial class AdminWebLinkViewModel(string label, Uri url, AppServices services)
{
    public string Label => label;

    public string Address { get; } = url.Host + url.AbsolutePath;

    [RelayCommand]
    private Task OpenAsync() => services.Browser.OpenAsync(url);
}
