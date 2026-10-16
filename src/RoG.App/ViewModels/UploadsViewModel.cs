using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Uploads;
using RoG.Core.Localization;
using Windows.Storage;

namespace RoG.App.ViewModels;

/// <summary>
/// Uploader: Dateien per Drag &amp; Drop, Dateiauswahl oder Strg+V (Screenshot aus der Zwischenablage) hochladen,
/// Direktlink automatisch kopieren, eigene Uploads mit Links (Direkt, BBCode, Markdown).
/// </summary>
public sealed partial class UploadsViewModel : LoadableViewModel
{
    private readonly UploadService _uploads;
    private string? _cursor;

    public UploadsViewModel(AppServices services)
        : base(services)
    {
        _uploads = new UploadService(services.Api.Client, services.StorageHttp);
        Visibilities =
        [
            new(UploadService.VisibilityValue(UploadVisibility.Public), services.Localizer.GetString(StringKeys.UploadsPublic)),
            new(UploadService.VisibilityValue(UploadVisibility.Members), services.Localizer.GetString(StringKeys.UploadsMembers)),
            new(UploadService.VisibilityValue(UploadVisibility.Private), services.Localizer.GetString(StringKeys.UploadsPrivate)),
        ];
        SelectedVisibility = Visibilities.FirstOrDefault(v => v.Value == services.Settings.UploadVisibility) ?? Visibilities[0];
        CopyLink = services.Settings.CopyLinkAfterUpload;
    }

    public ObservableCollection<UploadItemViewModel> Items { get; } = [];

    public IReadOnlyList<SettingsOption> Visibilities { get; }

    [ObservableProperty]
    public partial SettingsOption SelectedVisibility { get; set; }

    [ObservableProperty]
    public partial bool CopyLink { get; set; }

    [ObservableProperty]
    public partial bool IsUploading { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string? ProgressText { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUploader), nameof(IsEmptyForMember))]
    public partial bool IsGuest { get; set; }

    public bool ShowUploader => !IsGuest;

    /// <summary>Leerzustand nur für Angemeldete (Gäste sehen den Hinweis zum Anmelden).</summary>
    public bool IsEmptyForMember => IsEmpty && !IsGuest;

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IsGuest = await Services.Tokens.GetAsync(cancellationToken) is null;
        if (IsGuest)
        {
            Items.Clear();
            return;
        }

        var page = await _uploads.ListAsync(null, cancellationToken);
        Items.Clear();
        foreach (var item in page.Items)
        {
            Items.Add(new UploadItemViewModel(item, this));
        }

        _cursor = page.NextCursor;
        HasMore = page.HasMore;
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_cursor is null)
        {
            return;
        }

        try
        {
            var page = await _uploads.ListAsync(_cursor);
            foreach (var item in page.Items)
            {
                Items.Add(new UploadItemViewModel(item, this));
            }

            _cursor = page.NextCursor;
            HasMore = page.HasMore;
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // „Mehr laden“ erneut
        }
    }

    partial void OnSelectedVisibilityChanged(SettingsOption value)
    {
        if (value is not null && Services.Settings.UploadVisibility != value.Value)
        {
            _ = Services.UpdateSettingsAsync(s => s with { UploadVisibility = value.Value });
        }
    }

    partial void OnCopyLinkChanged(bool value)
    {
        if (Services.Settings.CopyLinkAfterUpload != value)
        {
            _ = Services.UpdateSettingsAsync(s => s with { CopyLinkAfterUpload = value });
        }
    }

    /// <summary>Dateien nacheinander hochladen (Drag &amp; Drop, Dateiauswahl, eingefügte Dateien).</summary>
    public async Task UploadFilesAsync(IReadOnlyList<StorageFile> files)
    {
        if (IsUploading || files.Count == 0)
        {
            return;
        }

        IsUploading = true;
        var visibility = SelectedVisibility.Value switch
        {
            "members" => UploadVisibility.Members,
            "private" => UploadVisibility.Private,
            _ => UploadVisibility.Public,
        };
        UploadItem? last = null;
        var failed = 0;
        try
        {
            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                if (UploadService.MimeFor(file.Name) is null)
                {
                    failed++;
                    continue;
                }

                ProgressText = string.Format(CultureInfo.CurrentCulture, Services.Localizer.GetString(StringKeys.UploadsUploading), file.Name, i + 1, files.Count);
                Progress = 0;
                var props = await file.GetBasicPropertiesAsync();
                await using var stream = await file.OpenStreamForReadAsync();
                try
                {
                    last = await _uploads.UploadAsync(stream, file.Name, (long)props.Size, visibility, new Progress<double>(p => Progress = p * 100));
                    Items.Insert(0, new UploadItemViewModel(last, this));
                    DataChanged();
                }
                catch (ApiProblemException ex)
                {
                    failed++;
                    Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
                }
                catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or OverflowException)
                {
                    failed++;
                    Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
                }
            }
        }
        finally
        {
            IsUploading = false;
            ProgressText = null;
        }

        if (last?.Links.Direct is { } link)
        {
            var copied = CopyLink && TryCopy(link.ToString());
            Show(Services.Localizer.GetString(copied ? StringKeys.UploadsDoneCopied : StringKeys.UploadsDone), failed == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        else if (failed > 0 && !IsFeedbackOpen)
        {
            Show(Services.Localizer.GetString(StringKeys.UploadsUnsupported), InfoBarSeverity.Warning);
        }
    }

    internal bool TryCopy(string text)
    {
        try
        {
            Services.Clipboard.SetText(text);
            return true;
        }
#pragma warning disable CA1031 // Zwischenablage kann gesperrt sein
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    internal void Copied(bool ok) =>
        Show(Services.Localizer.GetString(ok ? StringKeys.CommonLinkCopied : StringKeys.CommonCopyFailed), ok ? InfoBarSeverity.Success : InfoBarSeverity.Error);

    [RelayCommand]
    private void SignIn() => Services.RequestSignIn();

    private void Show(string text, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }
}

public sealed partial class UploadItemViewModel(UploadItem item, UploadsViewModel owner)
{
    public string Title => item.Title ?? item.OriginalName;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        item.CreatedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
        item.Size > 0 ? $"{item.Size / 1024.0:0} KB" : null,
        item.Failed ? item.ErrorMessage : item.IsReady ? null : App.Services.Localizer.GetString(StringKeys.UploadsProcessing),
    }.Where(s => !string.IsNullOrEmpty(s)));

    public ImageSource? Thumb { get; } = item.Links.Thumb is { } t && item.Visibility == UploadVisibility.Public ? new BitmapImage(t) { DecodePixelWidth = 160 } : null;

    public string Direct => item.Links.Direct?.ToString() ?? string.Empty;

    public bool HasLinks => item.Links.Direct is not null && !item.Failed;

    [RelayCommand]
    private void CopyDirect() => owner.Copied(owner.TryCopy(Direct));

    [RelayCommand]
    private void CopyBbCode() => owner.Copied(item.Links.BbCode is { } b && owner.TryCopy(b));

    [RelayCommand]
    private void CopyMarkdown() => owner.Copied(item.Links.Markdown is { } m && owner.TryCopy(m));

    [RelayCommand]
    private Task OpenAsync() => item.Links.Preview is { } p ? App.Services.Browser.OpenAsync(p) : Task.CompletedTask;
}
