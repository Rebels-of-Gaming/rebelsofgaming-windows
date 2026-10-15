using System.Collections.Concurrent;

namespace RoG.Core.Api;

/// <summary>Zwischengespeicherte GET-Antwort mit ETag.</summary>
public sealed record EtagEntry(string ETag, byte[] Body, string? ContentType, DateTimeOffset StoredAt);

/// <summary>
/// Speicher für ETag-Antworten (<c>If-None-Match</c> → <c>304</c>). Der Schlüssel enthält Adresse, Sprache und eine
/// Kennung des Kontos, damit nie Daten eines anderen Kontos oder einer anderen Sprache zurückkommen.
/// </summary>
public interface IEtagCache
{
    bool TryGet(string key, out EtagEntry entry);

    void Store(string key, EtagEntry entry);

    void Clear();
}

/// <summary>Begrenzter Speicher im Arbeitsspeicher (älteste Einträge fliegen zuerst). Persistenz folgt mit dem Offline-Cache.</summary>
public sealed class InMemoryEtagCache(int capacity = 512, long maxBodyBytes = 2 * 1024 * 1024) : IEtagCache
{
    private readonly ConcurrentDictionary<string, EtagEntry> _entries = new(StringComparer.Ordinal);

    public int Count => _entries.Count;

    public bool TryGet(string key, out EtagEntry entry) => _entries.TryGetValue(key, out entry!);

    public void Store(string key, EtagEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Body.LongLength > maxBodyBytes)
        {
            _entries.TryRemove(key, out _);
            return;
        }

        _entries[key] = entry;
        if (_entries.Count > capacity)
        {
            foreach (var old in _entries.OrderBy(e => e.Value.StoredAt).Take(_entries.Count - capacity).ToList())
            {
                _entries.TryRemove(old.Key, out _);
            }
        }
    }

    public void Clear() => _entries.Clear();
}
