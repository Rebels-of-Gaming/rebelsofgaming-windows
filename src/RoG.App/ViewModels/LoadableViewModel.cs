using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.App.Services;
using RoG.Core.Api;

namespace RoG.App.ViewModels;

/// <summary>
/// Gemeinsame Grundlage für Seiten, die Daten laden: Lade-, Fehler- und Leerzustand, stille Aktualisierung
/// (Liste bleibt stehen, Fehler nur ohne Daten sichtbar), Fehlertexte über <c>Error_&lt;code&gt;</c>.
/// </summary>
public abstract partial class LoadableViewModel(AppServices services) : ObservableObject
{
    private bool _running;

    protected AppServices Services { get; } = services;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(ShowContent))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(IsEmpty), nameof(ShowContent))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Geladen, ohne Fehler, aber nichts zu zeigen.</summary>
    public bool IsEmpty => !IsLoading && !HasError && !HasData;

    public bool ShowContent => HasData && !HasError;

    /// <summary>Gibt es etwas anzuzeigen? (Liste nicht leer, Detail geladen …)</summary>
    protected abstract bool HasData { get; }

    /// <summary>Lädt die Daten (ohne Fehlerbehandlung – die übernimmt <see cref="LoadAsync"/>).</summary>
    protected abstract Task LoadCoreAsync(CancellationToken cancellationToken);

    public async Task LoadAsync(bool silent = false)
    {
        if (_running)
        {
            return;
        }

        _running = true;
        IsLoading = !silent || !HasData;
        try
        {
            await LoadCoreAsync(CancellationToken.None);
            ErrorMessage = null;
        }
        catch (ApiProblemException ex)
        {
            if (!silent || !HasData)
            {
                ErrorMessage = Services.Localizer.GetError(ex.Problem.Code);
            }
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            if (!silent || !HasData)
            {
                ErrorMessage = Services.Localizer.GetError(ApiProblem.Codes.Unknown);
            }
        }
        finally
        {
            _running = false;
            IsLoading = false;
            DataChanged();
        }
    }

    /// <summary>Nach Änderungen an den Daten aufrufen, damit Leer-/Inhaltszustand neu bewertet werden.</summary>
    protected void DataChanged()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowContent));
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    protected string Format(string key, params object?[] args) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Services.Localizer.GetString(key), args);
}
