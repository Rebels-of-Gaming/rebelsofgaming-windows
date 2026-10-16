using System.Net.Http.Json;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Security;
using File = System.IO.File;

namespace RoG.Core.Offline;

public enum OutboxKind
{
    /// <summary>Direktnachricht – nur im Speicher (DMs nie auf der Platte, Pflichtenheft 7).</summary>
    DirectMessage,
    ForumReply,
    EventRsvp,
}

/// <summary>Wartende Schreibaktion mit festem Idempotency-Key (Wiederholung erzeugt nie Doppelte).</summary>
public sealed record OutboxItem(string Key, OutboxKind Kind, string Target, string Payload, string Label, DateTimeOffset CreatedAt, int Attempts = 0);

/// <summary>Endgültig abgelehnte Aktion (z. B. 403/422) – die Oberfläche meldet sie und verwirft sie.</summary>
public sealed record OutboxFailure(OutboxItem Item, ApiProblem Problem);

/// <summary>
/// Offline-Warteschlange (Pflichtenheft 7): Schreibaktionen ohne Netz werden mit Idempotency-Key gemerkt und beim
/// nächsten <see cref="FlushAsync"/> der Reihe nach gesendet. Netzfehler/5xx/429 → bleibt; andere Fehler → verworfen
/// mit <see cref="Failed"/>. Forenbeiträge und Zusagen werden verschlüsselt gespeichert, DMs nur im Speicher.
/// </summary>
public sealed class Outbox(HttpClient http, string? file, IPayloadProtector protector) : IDisposable
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);
    private readonly List<OutboxItem> _items = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public event EventHandler? Changed;

    public event EventHandler<OutboxFailure>? Failed;

    public IReadOnlyList<OutboxItem> Items
    {
        get
        {
            lock (_items)
            {
                return _items.ToList();
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_items)
            {
                return _items.Count;
            }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (file is null || !File.Exists(file))
        {
            return;
        }

        try
        {
            var data = await protector.UnprotectAsync(await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            var stored = JsonSerializer.Deserialize<List<OutboxItem>>(data, s_json) ?? [];
            lock (_items)
            {
                _items.InsertRange(0, stored.Where(i => i.Kind != OutboxKind.DirectMessage));
            }
        }
#pragma warning disable CA1031 // unlesbare Datei (anderer Benutzer, alte Version) – verwerfen
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            File.Delete(file);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<OutboxItem> EnqueueAsync(OutboxKind kind, string target, object payload, string label, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        var item = new OutboxItem(Guid.NewGuid().ToString(), kind, target, JsonSerializer.Serialize(payload, s_json), label, DateTimeOffset.UtcNow);
        lock (_items)
        {
            _items.Add(item);
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return item;
    }

    /// <summary>Wartende Aktionen senden; Rückgabe = Anzahl erfolgreich gesendeter. Bricht beim ersten Netzfehler ab.</summary>
    public async Task<int> FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!await _lock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return 0; // läuft schon
        }

        var sent = 0;
        try
        {
            while (Items is { Count: > 0 } pending)
            {
                var item = pending[0];
                try
                {
                    using var request = Build(item);
                    using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    Remove(item);
                    sent++;
                }
                catch (ApiProblemException ex) when (ex.Problem.IsNetworkError || ex.Problem.IsRateLimited || ex.Problem.Status >= 500 || ex.Problem.IsUnauthorized)
                {
                    Replace(item, item with { Attempts = item.Attempts + 1 });
                    break; // später erneut
                }
                catch (ApiProblemException ex)
                {
                    Remove(item);
                    Failed?.Invoke(this, new OutboxFailure(item, ex.Problem));
                }
                catch (HttpRequestException)
                {
                    break;
                }
            }
        }
        finally
        {
            await SaveAsync(CancellationToken.None).ConfigureAwait(false);
            _lock.Release();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return sent;
    }

    /// <summary>Beim Abmelden: alles verwerfen (gehört zum Konto).</summary>
    public void Clear()
    {
        lock (_items)
        {
            _items.Clear();
        }

        if (file is not null && File.Exists(file))
        {
            File.Delete(file);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _lock.Dispose();

    internal static HttpRequestMessage Build(OutboxItem item)
    {
        var target = Uri.EscapeDataString(item.Target);
        var (method, path) = item.Kind switch
        {
            OutboxKind.DirectMessage => (HttpMethod.Post, $"conversations/{target}/messages"),
            OutboxKind.ForumReply => (HttpMethod.Post, $"forum/threads/{target}/posts"),
            OutboxKind.EventRsvp => (HttpMethod.Put, $"events/{target}/rsvp"),
            _ => throw new ArgumentOutOfRangeException(nameof(item)),
        };
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(item.Payload, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(RoGHttpHandler.IdempotencyHeader, item.Key);
        return request;
    }

    private void Remove(OutboxItem item)
    {
        lock (_items)
        {
            _items.RemoveAll(i => i.Key == item.Key);
        }
    }

    private void Replace(OutboxItem item, OutboxItem updated)
    {
        lock (_items)
        {
            var index = _items.FindIndex(i => i.Key == item.Key);
            if (index >= 0)
            {
                _items[index] = updated;
            }
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return;
        }

        var persistent = Items.Where(i => i.Kind != OutboxKind.DirectMessage).ToList();
        if (persistent.Count == 0)
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }

            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var data = await protector.ProtectAsync(JsonSerializer.SerializeToUtf8Bytes(persistent, s_json), cancellationToken).ConfigureAwait(false);
        var temp = file + ".tmp";
        await File.WriteAllBytesAsync(temp, data, cancellationToken).ConfigureAwait(false);
        File.Move(temp, file, overwrite: true);
    }
}
