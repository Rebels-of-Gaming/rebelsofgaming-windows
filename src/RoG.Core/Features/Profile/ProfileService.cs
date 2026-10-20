using RoG.Core.Api;
using RoG.Core.Api.Generated;
using Stream = System.IO.Stream;

namespace RoG.Core.Features.Profile;

/// <summary>Bearbeitbare Profilfelder (Ausschnitt aus <c>GET /v1/me/profile</c>).</summary>
public sealed record ProfileData(
    string Motto,
    string Bio,
    string City,
    string Country,
    bool ShowOnMap,
    bool ShowOnlineStatus,
    bool HallOfFameConsent,
    Uri? Avatar,
    Uri? Banner,
    long AvatarMaxBytes,
    long BannerMaxBytes);

/// <summary>
/// Eigenes Profil: Texte, Ort (für die Mitgliederkarte, nur mit Opt-in), Online-Status- und Hall-of-Fame-Einwilligung,
/// Avatar und Banner (der Server entfernt Metadaten). Gesendet werden nur die Felder dieses Formulars.
/// </summary>
public sealed class ProfileService(IRoGApiClient client)
{
    public async Task<ProfileData> GetAsync(CancellationToken cancellationToken = default)
    {
        var p = (await client.V1_me_profile_showAsync(cancellationToken).ConfigureAwait(false)).Data;
        return new ProfileData(
            p.Motto ?? string.Empty,
            p.Bio ?? string.Empty,
            p.City ?? string.Empty,
            p.Country ?? string.Empty,
            p.Show_on_map,
            p.Show_online_status,
            p.Hall_of_fame_consent,
            ApiValues.WebUri(p.Avatar?.Url_small ?? p.Avatar?.Url),
            ApiValues.WebUri(p.Banner?.Url_small ?? p.Banner?.Url),
            p.Limits?.Avatar?.Max_bytes ?? 0,
            p.Limits?.Banner?.Max_bytes ?? 0);
    }

    public async Task SaveAsync(ProfileData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        await client.V1_me_profile_updateAsync(new Body98
        {
            Motto = data.Motto.Trim(),
            Bio = data.Bio.Trim(),
            City = data.City.Trim(),
            Country = data.Country.Trim(),
            Show_on_map = data.ShowOnMap,
            Show_online_status = data.ShowOnlineStatus,
            Hall_of_fame_consent = data.HallOfFameConsent,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetAvatarAsync(Stream image, string fileName, string mime, CancellationToken cancellationToken = default) =>
        await client.V1_me_avatar_storeAsync(new FileParameter(image, fileName, mime), cancellationToken).ConfigureAwait(false);

    public async Task SetBannerAsync(Stream image, string fileName, string mime, CancellationToken cancellationToken = default) =>
        await client.V1_me_banner_storeAsync(new FileParameter(image, fileName, mime), cancellationToken).ConfigureAwait(false);
}
