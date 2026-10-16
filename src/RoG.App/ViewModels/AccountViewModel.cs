using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Core.Features.Account;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Konto: Profil-Kopf, Sicherheit, Gerät, Abmelden; im Gastmodus „Anmelden“.</summary>
public sealed partial class AccountViewModel(AppServices services) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGuest), nameof(ShowProfile))]
    public partial bool IsSignedIn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProfile))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? RankText { get; set; }

    [ObservableProperty]
    public partial Brush? RankBrush { get; set; }

    [ObservableProperty]
    public partial ImageSource? Avatar { get; set; }

    [ObservableProperty]
    public partial string? Email { get; set; }

    [ObservableProperty]
    public partial string? Squads { get; set; }

    [ObservableProperty]
    public partial string? Joined { get; set; }

    [ObservableProperty]
    public partial string? TwoFactor { get; set; }

    [ObservableProperty]
    public partial string? DeviceName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignOut))]
    public partial bool IsSigningOut { get; set; }

    public bool CanSignOut => !IsSigningOut;

    public bool IsGuest => !IsSignedIn;

    public bool ShowProfile => IsSignedIn && !IsLoading;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public async Task LoadAsync()
    {
        var token = await services.Tokens.GetAsync();
        IsSignedIn = token is not null;
        if (token is null)
        {
            return;
        }

        DeviceName = token.DeviceName;
        Name = token.UserName;
        var user = services.CurrentUser;
        if (user is null)
        {
            IsLoading = true;
            try
            {
                user = services.CurrentUser = await services.Account.GetMeAsync();
            }
            catch (ApiProblemException ex)
            {
                ErrorMessage = services.Localizer.GetError(ex.Problem.Code);
            }
            catch (GeneratedApiException)
            {
                ErrorMessage = services.Localizer.GetError(ApiProblem.Codes.Unknown);
            }
            finally
            {
                IsLoading = false;
            }
        }

        if (user is not null)
        {
            ErrorMessage = null;
            Apply(user);
        }
    }

    private void Apply(UserProfile user)
    {
        Name = user.Name;
        RankText = user.RankLabel;
        RankBrush = ColorFromHex(user.RankColor) is { } c ? new SolidColorBrush(c) : null;
        Avatar = Uri.TryCreate(user.AvatarUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? new BitmapImage(uri) : null;
        Email = user.Email;
        Squads = user.SquadNames.Count > 0 ? string.Join(" · ", user.SquadNames) : null;
        Joined = DateTimeOffset.TryParse(user.JoinedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var joined)
            ? Format(StringKeys.AccountJoined, joined.ToLocalTime().ToString("d", CultureInfo.CurrentCulture))
            : null;
        TwoFactor = services.Localizer.GetString(user.TwoFactorEnabled ? StringKeys.AccountTwoFactorOn : StringKeys.AccountTwoFactorOff);
    }

    /// <summary>Abmelden (die Seite fragt vorher nach). Danach zeigt das Hauptfenster die Login-Seite.</summary>
    public async Task SignOutAsync()
    {
        IsSigningOut = true;
        try
        {
            await services.Account.LogoutAsync();
            await services.UpdateSettingsAsync(s => s with { GuestMode = false });
        }
        finally
        {
            IsSigningOut = false;
        }

        services.RequestSignIn();
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        await services.UpdateSettingsAsync(s => s with { GuestMode = false });
        services.RequestSignIn();
    }

    /// <summary>Kontoverwaltung der Website (E-Mail/Passwort, Passkeys, Sitzungen, Export, Löschen) – bis die App das selbst kann.</summary>
    [RelayCommand]
    private Task OpenWebsiteAccountAsync() => services.Browser.OpenAsync(new Uri(services.Environment.WebBaseUrl, "settings/account"));

    [RelayCommand]
    private Task OpenProfileAsync() =>
        services.CurrentUser is { } user
            ? services.Browser.OpenAsync(new Uri(services.Environment.WebBaseUrl, $"u/{Uri.EscapeDataString(user.Slug)}"))
            : Task.CompletedTask;

    private string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, services.Localizer.GetString(key), args);

    /// <summary>Rangfarbe aus der API (<c>#RRGGBB</c>); unbekannte Formate → keine Farbe (Gold aus dem Theme).</summary>
    internal static Windows.UI.Color? ColorFromHex(string? hex)
    {
        if (hex is null || hex.Length != 7 || hex[0] != '#'
            || !uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return null;
        }

        return Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
