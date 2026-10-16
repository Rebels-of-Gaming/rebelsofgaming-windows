using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Core.Features.Squads;

namespace RoG.Core.Features.Games;

public sealed record GameInfo(string Slug, string Name, string ShortName, string Platform, string Genre, string Emoji, string? ClanTag, Uri? Header, IReadOnlyList<SquadLink> Links);

/// <summary>Spiele des Clans (Guide „Spiele“): aktive Spiele mit Store-, Ref- und Einladungslinks.</summary>
public sealed class GameService(IRoGApiClient client)
{
    public async Task<IReadOnlyList<GameInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.V1_games_indexAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.Data
            .Where(g => g.Active)
            .Select(g => new GameInfo(
                g.Slug,
                g.Name,
                g.Short,
                g.Platform,
                g.Genre,
                g.Emoji,
                ApiValues.Text(g.Clan_tag),
                ApiValues.WebUri(g.Header?.Url),
                g.Links?
                    .Select(l => Uri.TryCreate(l.Url ?? l.Short_url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
                        ? new SquadLink(l.Label, u, ApiValues.Text(l.Code))
                        : null)
                    .OfType<SquadLink>()
                    .ToList() ?? []))
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
