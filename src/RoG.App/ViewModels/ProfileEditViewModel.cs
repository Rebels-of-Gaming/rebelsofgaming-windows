using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Profile;
using RoG.Core.Features.Uploads;
using RoG.Core.Localization;
using Windows.Storage;

namespace RoG.App.ViewModels;

/// <summary>Profil bearbeiten (Konto-Seite): Texte, Ort, Einwilligungen, Avatar und Banner.</summary>
public sealed partial class ProfileEditViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly ProfileService _profile = new(services.Api.Client);
    private ProfileData? _loaded;

    [ObservableProperty]
    public partial string Motto { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Bio { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string City { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Country { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowOnMap { get; set; }

    [ObservableProperty]
    public partial bool ShowOnlineStatus { get; set; }

    [ObservableProperty]
    public partial bool HallOfFameConsent { get; set; }

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    protected override bool HasData => _loaded is not null;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var p = await _profile.GetAsync(cancellationToken);
        Motto = p.Motto;
        Bio = p.Bio;
        City = p.City;
        Country = p.Country;
        ShowOnMap = p.ShowOnMap;
        ShowOnlineStatus = p.ShowOnlineStatus;
        HallOfFameConsent = p.HallOfFameConsent;
        _loaded = p;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_loaded is null)
        {
            return;
        }

        IsSaving = true;
        try
        {
            await _profile.SaveAsync(_loaded with
            {
                Motto = Motto,
                Bio = Bio,
                City = City,
                Country = Country,
                ShowOnMap = ShowOnMap,
                ShowOnlineStatus = ShowOnlineStatus,
                HallOfFameConsent = HallOfFameConsent,
            });
            Show(Services.Localizer.GetString(StringKeys.ProfileSaved), InfoBarSeverity.Success);
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>Avatar oder Banner ersetzen (Bild aus Dateiauswahl oder Drag &amp; Drop).</summary>
    public async Task SetImageAsync(StorageFile file, bool banner)
    {
        if (UploadService.MimeFor(file.Name) is not { } mime || !mime.StartsWith("image/", StringComparison.Ordinal))
        {
            Show(Services.Localizer.GetString(StringKeys.UploadsUnsupported), InfoBarSeverity.Warning);
            return;
        }

        try
        {
            await using var stream = await file.OpenStreamForReadAsync();
            if (banner)
            {
                await _profile.SetBannerAsync(stream, file.Name, mime);
            }
            else
            {
                await _profile.SetAvatarAsync(stream, file.Name, mime);
            }

            // Verarbeitung läuft im Hintergrund (.media.processed) – Profil danach neu laden
            Services.CurrentUser = await Services.Account.GetMeAsync();
            Show(Services.Localizer.GetString(StringKeys.ProfileImageSaved), InfoBarSeverity.Success);
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or IOException)
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
