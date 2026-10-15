using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// Login-Seite: „Im Browser anmelden“ (PKCE + Loopback, RoG.Core.Auth.AppLoginFlow) oder „Ohne Anmeldung umsehen“
/// (Gastmodus, öffentliche Endpunkte ohne Token).
/// </summary>
public sealed partial class LoginViewModel(AppServices services) : ObservableObject
{
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool IsIdle => !IsBusy;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Anmeldung oder Gastmodus abgeschlossen – die Shell zeigt die Navigation.</summary>
    public event EventHandler? Completed;

    [RelayCommand]
    private async Task SignInAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            var http = services.Api.Http;
            var flow = new AppLoginFlow(services.Environment, http, services.Browser, services.Tokens);
            var scopes = await AppScopes.ResolveAsync(http, _cts.Token);
            var deviceName = DeviceName.ForWindows(Environment.UserName);
            await flow.LoginAsync(scopes, deviceName, services.LoopbackTexts(), cancellationToken: _cts.Token);
            await services.UpdateSettingsAsync(s => s with { GuestMode = false });
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // vom Nutzer abgebrochen – keine Meldung
        }
        catch (AppLoginException ex)
        {
            // access_denied u. a.: Text der Website ist schon übersetzt; sonst eigener Text zum Code.
            var own = services.Localizer.GetError(ex.Code);
            ErrorMessage = ex.Code == "access_denied" && !string.IsNullOrWhiteSpace(ex.Description) ? ex.Description : own;
        }
        catch (ApiProblemException ex)
        {
            ErrorMessage = services.Localizer.GetError(ex.Problem.Code);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task BrowseAsGuestAsync()
    {
        await services.UpdateSettingsAsync(s => s with { GuestMode = true });
        Completed?.Invoke(this, EventArgs.Empty);
    }
}
