namespace RoG.Core.Auth;

/// <summary>Geräte-Token der App mit Metadaten aus der Token-Antwort.</summary>
public sealed record StoredToken(
    string AccessToken,
    string UserId,
    string UserName,
    IReadOnlyList<string> Scopes,
    string DeviceName,
    DateTimeOffset CreatedAt)
{
    /// <summary>Ob der Token einen Scope hat (die API entscheidet trotzdem bei jeder Anfrage).</summary>
    public bool HasScope(string scope) => Scopes.Contains(scope, StringComparer.Ordinal);

    /// <summary>Nie den Token ausgeben (Logs, Debugger).</summary>
    public override string ToString() => $"StoredToken({UserName}, {Scopes.Count} scopes, {DeviceName})";
}

/// <summary>
/// Sicherer Speicher für den Geräte-Token. Unter Windows implementiert die App ihn über
/// <c>Windows.Security.Credentials.PasswordVault</c> (Anmeldeinformationsverwaltung, an den Windows-Benutzer gebunden).
/// Der Token gehört nie in Einstellungen, Logs oder Caches.
/// </summary>
public interface ITokenStore
{
    Task<StoredToken?> GetAsync(CancellationToken cancellationToken = default);

    Task SetAsync(StoredToken token, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>Flüchtiger Speicher für Tests und den Gastmodus.</summary>
public sealed class InMemoryTokenStore : ITokenStore
{
    private StoredToken? _token;

    public Task<StoredToken?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(Volatile.Read(ref _token));

    public Task SetAsync(StoredToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        Volatile.Write(ref _token, token);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Volatile.Write(ref _token, null);
        return Task.CompletedTask;
    }
}
