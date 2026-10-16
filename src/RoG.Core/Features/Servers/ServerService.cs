using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Servers;

/// <summary>Gameserver aus der API (<c>GET /v1/servers</c>, auch im Gastmodus). Spielerliste nur für Mitglieder.</summary>
public sealed class ServerService(IRoGApiClient client)
{
    private const int PageSize = 50;

    /// <summary>Obergrenze gegen Endlosschleifen bei fehlerhaften Cursorn.</summary>
    private const int MaxPages = 10;

    /// <summary>Alle sichtbaren Server in der Reihenfolge der API (Cursor-Pagination wird abgearbeitet), optional nur eines Squads.</summary>
    public async Task<IReadOnlyList<GameServer>> GetAllAsync(CancellationToken cancellationToken = default) => await GetAllAsync(null, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<GameServer>> GetAllAsync(string? squadSlug, CancellationToken cancellationToken = default)
    {
        var result = new List<GameServer>();
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var response = await client.V1_servers_indexAsync(cursor, PageSize, filtersquad: squadSlug, cancellationToken: cancellationToken).ConfigureAwait(false);
            result.AddRange(response.Data.Select(Map));
            cursor = response.Meta?.Next_cursor;
            if (string.IsNullOrEmpty(cursor))
            {
                break;
            }
        }

        return result;
    }

    internal static GameServer Map(ServerResource s)
    {
        var live = s.Live;
        return new GameServer(
            s.Id,
            s.Slug,
            s.Name,
            s.Game?.Name,
            s.Game?.Short,
            s.Address,
            GameServer.ParseQueryType(s.Query_type),
            string.IsNullOrWhiteSpace(s.Connect_url) ? null : s.Connect_url.Trim(),
            live?.Online,
            string.IsNullOrWhiteSpace(live?.Map) ? null : live.Map,
            live?.Players ?? 0,
            live?.Max_players ?? 0,
            live?.Passworded ?? false,
            live?.Player_list?.Select(p => new ServerPlayer(p.Name, p.Score, p.Ping, p.Member?.Name)).ToList(),
            s.Control is { Abilities: { } a } c ? new ServerAbilities(a.Power, a.Console, a.Console_command, c.Can_command) : null);
    }
}
