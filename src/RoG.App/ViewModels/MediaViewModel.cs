using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Media;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Medien: Galerie (Alben → Bilder → Vollbild) und Downloads (mit Fortschritt, Prüfsumme, „Im Ordner anzeigen“).</summary>
public sealed partial class MediaViewModel : LoadableViewModel
{
    private readonly MediaService _media;

    public MediaViewModel(AppServices services)
        : base(services)
    {
        _media = new MediaService(services.Api.Client, services.StorageHttp);
        Downloads = new DownloadsViewModel(services, _media);
    }

    public ObservableCollection<AlbumItemViewModel> Albums { get; } = [];

    public ObservableCollection<GalleryItemViewModel> Items { get; } = [];

    public DownloadsViewModel Downloads { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAlbums), nameof(ShowItems))]
    public partial AlbumItemViewModel? Album { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingItems { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewerOpen))]
    public partial GalleryItemViewModel? Viewed { get; set; }

    public bool ShowAlbums => Album is null;

    public bool ShowItems => Album is not null;

    public bool IsViewerOpen => Viewed is not null;

    protected override bool HasData => Albums.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var albums = await _media.GetAlbumsAsync(cancellationToken);
        Albums.Clear();
        foreach (var a in albums)
        {
            Albums.Add(new AlbumItemViewModel(a, Services.Localizer));
        }
    }

    partial void OnAlbumChanged(AlbumItemViewModel? value)
    {
        Items.Clear();
        if (value is not null)
        {
            _ = LoadItemsAsync(value.Slug);
        }
    }

    private async Task LoadItemsAsync(string slug)
    {
        IsLoadingItems = true;
        try
        {
            foreach (var item in await _media.GetItemsAsync(slug))
            {
                Items.Add(new GalleryItemViewModel(item));
            }
        }
        catch (ApiProblemException ex)
        {
            ErrorMessage = Services.Localizer.GetError(ex.Problem.Code);
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            ErrorMessage = Services.Localizer.GetError(ApiProblem.Codes.Unknown);
        }
        finally
        {
            IsLoadingItems = false;
        }
    }

    [RelayCommand]
    private void BackToAlbums() => Album = null;

    [RelayCommand]
    private void CloseViewer() => Viewed = null;

    /// <summary>Im Vollbild blättern (Pfeiltasten).</summary>
    public void Step(int delta)
    {
        if (Viewed is null || Items.Count == 0)
        {
            return;
        }

        var index = (Items.IndexOf(Viewed) + delta + Items.Count) % Items.Count;
        Viewed = Items[index];
    }
}

public sealed class AlbumItemViewModel(GalleryAlbum album, ILocalizer localizer)
{
    public string Slug => album.Slug;

    public string Title => album.Title;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.MediaItems), album.Items),
        album.SquadName,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public ImageSource? Cover { get; } = album.Cover is null ? null : new BitmapImage(album.Cover) { DecodePixelWidth = 480 };
}

public sealed partial class GalleryItemViewModel(GalleryItem item)
{
    public GalleryItem Item => item;

    public string Caption => item.Caption ?? string.Empty;

    public string AutomationName => item.Caption ?? item.Author ?? item.Id;

    public ImageSource? Thumb { get; } = item.Thumb is null ? null : new BitmapImage(item.Thumb) { DecodePixelWidth = 320 };

    /// <summary>Großansicht erst beim Öffnen laden.</summary>
    public ImageSource? Full => (item.Full ?? item.Medium) is { } uri ? new BitmapImage(uri) : null;

    public string Meta { get; } = string.Join(" · ", new[] { item.Author, item.CreatedAt?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) }.Where(s => !string.IsNullOrEmpty(s)));

    public bool IsVideo => item.IsVideo;

    [RelayCommand]
    private Task OpenInBrowserAsync() => (item.VideoUrl ?? item.WebUrl ?? item.Full) is { } url ? App.Services.Browser.OpenAsync(url) : Task.CompletedTask;
}

public sealed partial class DownloadsViewModel(AppServices services, MediaService media) : LoadableViewModel(services)
{
    public ObservableCollection<DownloadItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var entries = await media.GetDownloadsAsync(cancellationToken);
        Items.Clear();
        foreach (var e in entries)
        {
            Items.Add(new DownloadItemViewModel(e, this, Services.Localizer));
        }
    }

    internal async Task DownloadAsync(DownloadItemViewModel item)
    {
        if (item.Entry.VersionId is not { } version)
        {
            return;
        }

        item.IsBusy = true;
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            item.SavedPath = await media.DownloadAsync(version, folder, new Progress<double>(p => item.Progress = p * 100));
            Show(Format(StringKeys.MediaDownloaded, Path.GetFileName(item.SavedPath)), InfoBarSeverity.Success);
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
        finally
        {
            item.IsBusy = false;
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

public sealed partial class DownloadItemViewModel(DownloadEntry entry, DownloadsViewModel owner, ILocalizer localizer) : ObservableObject
{
    public DownloadEntry Entry => entry;

    public string Title => entry.Title;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        entry.Version is null ? null : "v" + entry.Version,
        entry.Category,
        entry.Game,
        entry.Size > 0 ? $"{entry.Size / 1048576.0:0.0} MB" : null,
        string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.MediaDownloadCount), entry.Downloads),
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string Changelog => entry.Changelog ?? string.Empty;

    public bool HasChangelog => entry.Changelog is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaved))]
    public partial string? SavedPath { get; set; }

    public bool CanDownload => entry.Available && entry.VersionId is not null && !IsBusy;

    public bool IsSaved => SavedPath is not null;

    [RelayCommand]
    private Task DownloadAsync() => owner.DownloadAsync(this);

    /// <summary>Explorer mit markierter Datei öffnen.</summary>
    [RelayCommand]
    private void ShowInFolder()
    {
        if (SavedPath is { } path && File.Exists(path))
        {
            using var explorer = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
        }
    }
}
