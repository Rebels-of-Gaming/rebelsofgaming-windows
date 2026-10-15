using System.Text.Json;
using RoG.Core.Auth;
using Windows.Security.Credentials;

namespace RoG.App.Services;

/// <summary>
/// Geräte-Token in der Windows-Anmeldeinformationsverwaltung (<see cref="PasswordVault"/>, verschlüsselt, an den
/// Windows-Benutzer gebunden). Nicht geheime Metadaten (Konto-ID, Name, Scopes, Gerätename) liegen als JSON im
/// App-Datenordner – nie der Token selbst.
/// </summary>
public sealed class PasswordVaultTokenStore(string metadataPath) : ITokenStore, IDisposable
{
    private const string Resource = "RebelsOfGaming.DeviceToken";
    private const int ElementNotFound = unchecked((int)0x80070490);

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private StoredToken? _cached;
    private bool _loaded;

    public async Task<StoredToken?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return _cached;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_loaded)
            {
                return _cached;
            }

            _cached = await LoadAsync(cancellationToken);
            _loaded = true;
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetAsync(StoredToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            RemoveAll();
            new PasswordVault().Add(new PasswordCredential(Resource, string.IsNullOrEmpty(token.UserId) ? "rog" : token.UserId, token.AccessToken));
            var meta = new Metadata(token.UserId, token.UserName, token.Scopes.ToList(), token.DeviceName, token.CreatedAt);
            await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(meta, s_json), cancellationToken);
            _cached = token;
            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            RemoveAll();
            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
            }

            _cached = null;
            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<StoredToken?> LoadAsync(CancellationToken cancellationToken)
    {
        PasswordCredential credential;
        try
        {
            var all = new PasswordVault().FindAllByResource(Resource);
            if (all.Count == 0)
            {
                return null;
            }

            credential = all[0];
            credential.RetrievePassword();
        }
        catch (Exception ex) when (ex.HResult == ElementNotFound)
        {
            return null;
        }

        Metadata? meta = null;
        if (File.Exists(metadataPath))
        {
            try
            {
                meta = JsonSerializer.Deserialize<Metadata>(await File.ReadAllTextAsync(metadataPath, cancellationToken), s_json);
            }
            catch (JsonException)
            {
                meta = null;
            }
        }

        return new StoredToken(
            credential.Password,
            meta?.UserId ?? credential.UserName,
            meta?.UserName ?? string.Empty,
            meta?.Scopes ?? [],
            meta?.DeviceName ?? string.Empty,
            meta?.CreatedAt ?? DateTimeOffset.MinValue);
    }

    private static void RemoveAll()
    {
        var vault = new PasswordVault();
        try
        {
            foreach (var credential in vault.FindAllByResource(Resource))
            {
                vault.Remove(credential);
            }
        }
        catch (Exception ex) when (ex.HResult == ElementNotFound)
        {
            // nichts gespeichert
        }
    }

    public void Dispose() => _lock.Dispose();

    private sealed record Metadata(string UserId, string UserName, List<string> Scopes, string DeviceName, DateTimeOffset CreatedAt);
}
