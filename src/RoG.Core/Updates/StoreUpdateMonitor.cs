namespace RoG.Core.Updates;

/// <summary>Ergebnis einer Store-Abfrage: Anzahl bereitstehender Pakete und ob eines davon Pflicht ist.</summary>
public sealed record StoreUpdateCheck(int Count, bool Mandatory);

public enum StoreInstallResult
{
    Completed,
    Canceled,
    Failed,
}

/// <summary>Was die Leiste zeigt: nichts, ein Update oder ein Pflicht-Update (deutlicher, ohne „Später“).</summary>
public enum StoreUpdateBanner
{
    None,
    Available,
    Mandatory,
}

/// <summary>Zugriff auf den Microsoft Store (Windows: <c>StoreContext</c>); hinter einer Schnittstelle für Tests.</summary>
public interface IStoreUpdateSource
{
    /// <summary>Aus dem Store installiert? Sideload/Entwicklung: <c>false</c> – dann wird nie gefragt und nichts angezeigt.</summary>
    bool IsStoreInstall { get; }

    Task<StoreUpdateCheck> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Lädt und installiert die zuletzt gefundenen Pakete; Fortschritt 0–1. Die App wird danach neu gestartet.</summary>
    Task<StoreInstallResult> InstallAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fragt beim Start und danach alle <see cref="Interval"/> im Hintergrund den Store nach Updates und entscheidet, was
/// die Leiste zeigt. Fehler (kein Netz, Store nicht erreichbar) ändern nichts und werden beim nächsten Mal wiederholt.
/// „Später“ blendet ein normales Update bis zur nächsten Abfrage aus; ein Pflicht-Update bleibt sichtbar.
/// </summary>
public sealed class StoreUpdateMonitor(IStoreUpdateSource source, TimeProvider? time = null)
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private DateTimeOffset? _lastCheck;
    private StoreUpdateCheck? _found;
    private bool _snoozed;
    private bool _installing;

    public event EventHandler? Changed;

    public StoreUpdateBanner Banner => _found is not { Count: > 0 } found || _installing
        ? StoreUpdateBanner.None
        : found.Mandatory ? StoreUpdateBanner.Mandatory : _snoozed ? StoreUpdateBanner.None : StoreUpdateBanner.Available;

    /// <summary>Ist wieder eine Abfrage fällig (nie bei Nicht-Store-Installationen)?</summary>
    public bool IsDue => source.IsStoreInstall && (_lastCheck is not { } last || _time.GetUtcNow() - last >= Interval);

    /// <summary>Abfrage, wenn fällig (oder <paramref name="force"/>); gibt die neue Anzeige zurück.</summary>
    public async Task<StoreUpdateBanner> CheckAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (!source.IsStoreInstall || _installing || (!force && !IsDue))
        {
            return Banner;
        }

        var before = Banner;
        try
        {
            var found = await source.CheckAsync(cancellationToken).ConfigureAwait(false);
            _lastCheck = _time.GetUtcNow();
            _found = found;
            _snoozed = false; // neue Abfrage: ein noch offenes Update erscheint wieder
        }
#pragma warning disable CA1031 // Store nicht erreichbar: nächste Abfrage versucht es erneut, die App läuft weiter
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _lastCheck = _time.GetUtcNow();
        }

        if (Banner != before)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return Banner;
    }

    /// <summary>„Später“: normales Update bis zur nächsten Abfrage ausblenden; Pflicht-Updates bleiben.</summary>
    public void Later()
    {
        if (_found is { Mandatory: false })
        {
            _snoozed = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>„Jetzt aktualisieren“. Bei Abbruch oder Fehler bleibt die Leiste, damit es erneut versucht werden kann.</summary>
    public async Task<StoreInstallResult> InstallAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_found is not { Count: > 0 } || _installing)
        {
            return StoreInstallResult.Canceled;
        }

        _installing = true;
        Changed?.Invoke(this, EventArgs.Empty);
        StoreInstallResult result;
        try
        {
            result = await source.InstallAsync(progress, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Store meldet Fehler als Ausnahme – in der Leiste als „fehlgeschlagen“ zeigen
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            result = StoreInstallResult.Failed;
        }
        finally
        {
            _installing = false;
        }

        if (result == StoreInstallResult.Completed)
        {
            _found = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }
}
