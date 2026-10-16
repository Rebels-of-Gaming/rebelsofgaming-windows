namespace RoG.Core.Security;

/// <summary>
/// Wann die App mit Windows Hello gesperrt wird (Pflichtenheft 3/5): beim Start und wenn sie länger als
/// <see cref="IdleTimeout"/> nicht im Vordergrund war. Die eigentliche Prüfung macht die App (UserConsentVerifier).
/// </summary>
public sealed class AppLock(Func<DateTimeOffset> clock)
{
    private DateTimeOffset? _leftAt;

    public AppLock()
        : this(() => DateTimeOffset.UtcNow)
    {
    }

    public bool Enabled { get; set; }

    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public bool IsLocked { get; private set; }

    /// <summary>App startet: gesperrt, wenn eingeschaltet.</summary>
    public bool OnStart()
    {
        IsLocked = Enabled;
        _leftAt = null;
        return IsLocked;
    }

    /// <summary>Fenster verliert den Vordergrund (minimiert, Tray, anderes Fenster).</summary>
    public void OnLeft() => _leftAt ??= clock();

    /// <summary>Fenster kommt zurück: sperren, wenn eingeschaltet und lange genug weg. Rückgabe = jetzt gesperrt.</summary>
    public bool OnReturned()
    {
        if (Enabled && !IsLocked && _leftAt is { } left && clock() - left >= IdleTimeout)
        {
            IsLocked = true;
        }

        _leftAt = null;
        return IsLocked;
    }

    /// <summary>Nach erfolgreicher Prüfung.</summary>
    public void Unlock()
    {
        IsLocked = false;
        _leftAt = null;
    }
}
