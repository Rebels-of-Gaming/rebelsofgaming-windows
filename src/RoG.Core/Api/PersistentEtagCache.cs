using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RoG.Core.Security;
using File = System.IO.File;

namespace RoG.Core.Api;

/// <summary>
/// Persistenter Antwort-Cache (Pflichtenheft 7): zuletzt gesehene Listen/Profile/News nach dem Start sofort und ohne
/// Netz verfügbar. Arbeitsspeicher wie <see cref="InMemoryEtagCache"/>, dazu je Eintrag eine mit
/// <see cref="IPayloadProtector"/> (DPAPI) verschlüsselte Datei. Direktnachrichten und Konto-/Geräte-Endpunkte werden
/// nie auf die Platte geschrieben.
/// </summary>
public sealed class PersistentEtagCache(string folder, IPayloadProtector protector, int capacity = 400, long maxBodyBytes = 1024 * 1024)
    : IEtagCache, IDisposable
{
    /// <summary>Pfadteile, die nie persistiert werden (DMs, Konto-Geheimnisse, kurzlebige Tokens).</summary>
    private static readonly string[] s_neverPersist =
    [
        "/conversations", "/messages", "/me/exports", "/devices", "/auth/", "/broadcasting", "/console-token", "/backups/", "/uploads/sign",
    ];

    private readonly InMemoryEtagCache _memory = new(capacity, maxBodyBytes);
    private readonly SemaphoreSlim _io = new(1, 1);

    private sealed record Stored(string Key, string ETag, string? ContentType, DateTimeOffset StoredAt, byte[] Body);

    public int Count => _memory.Count;

    public bool TryGet(string key, out EtagEntry entry) => _memory.TryGet(key, out entry);

    public void Store(string key, EtagEntry entry)
    {
        _memory.Store(key, entry);
        if (Persistable(key) && entry.Body.LongLength <= maxBodyBytes)
        {
            _ = WriteAsync(key, entry);
        }
    }

    public void Clear()
    {
        _memory.Clear();
        _io.Wait();
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException)
        {
            // gesperrte Datei – beim nächsten Mal
        }
        finally
        {
            _io.Release();
        }
    }

    /// <summary>Gespeicherte Einträge beim Start einlesen (neueste zuerst bis zur Kapazität); unlesbare werden verworfen.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        var files = new DirectoryInfo(folder).GetFiles("*.dat").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        foreach (var stale in files.Skip(capacity))
        {
            stale.Delete();
        }

        foreach (var file in files.Take(capacity))
        {
            try
            {
                var data = await protector.UnprotectAsync(await File.ReadAllBytesAsync(file.FullName, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                if (JsonSerializer.Deserialize<Stored>(data) is { } s && !_memory.TryGet(s.Key, out _))
                {
                    _memory.Store(s.Key, new EtagEntry(s.ETag, s.Body, s.ContentType, s.StoredAt));
                }
            }
#pragma warning disable CA1031 // fremde/beschädigte Datei – verwerfen
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                file.Delete();
            }
        }
    }

    /// <summary>Darf der Eintrag auf die Platte? Schlüssel = „wer|sprache|adresse“.</summary>
    public static bool Persistable(string key)
    {
        var url = key[(key.LastIndexOf('|') + 1)..];
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        return !s_neverPersist.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private async Task WriteAsync(string key, EtagEntry entry)
    {
        try
        {
            var data = await protector.ProtectAsync(JsonSerializer.SerializeToUtf8Bytes(new Stored(key, entry.ETag, entry.ContentType, entry.StoredAt, entry.Body))).ConfigureAwait(false);
            await _io.WaitAsync().ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32] + ".dat");
                await File.WriteAllBytesAsync(path + ".tmp", data).ConfigureAwait(false);
                File.Move(path + ".tmp", path, overwrite: true);
            }
            finally
            {
                _io.Release();
            }
        }
#pragma warning disable CA1031 // Cache ist Beiwerk – Schreibfehler (Platte voll, gesperrt) ignorieren
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    public void Dispose() => _io.Dispose();
}
