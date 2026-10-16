using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Home;

/// <summary>
/// Eine Kachel des Live-Tickers aus <c>GET /v1/live</c> (online, im Voice, auf Servern, Server-Belegung, nächstes Event …).
/// <see cref="Label"/> und <see cref="Hint"/> kommen übersetzt von der API; <see cref="Tone"/> wählt die Status-Farbe.
/// </summary>
public sealed record LiveStat(string Key, string Label, string Icon, string Tone, int Value, string? Hint);

public sealed class LiveTickerService(IRoGApiClient client)
{
    public async Task<IReadOnlyList<LiveStat>> GetAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.V1_liveAsync(cancellationToken).ConfigureAwait(false);
        return response.Data.Select(d => new LiveStat(d.Key, d.Label, d.Icon, d.Tone, d.Value, string.IsNullOrWhiteSpace(d.Hint) ? null : d.Hint)).ToList();
    }
}
