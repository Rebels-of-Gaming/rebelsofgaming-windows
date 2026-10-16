using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Core.Content;

namespace RoG.Core.Features.Search;

public sealed record SearchHit(string Title, string? Excerpt, Uri? Url, string? Meta);

public sealed record SearchGroup(string Key, string Label, IReadOnlyList<SearchHit> Hits);

/// <summary>Globale Suche über alle Bereiche (<c>GET /v1/search</c>); Treffer nach Bereich gruppiert.</summary>
public sealed class SearchService(IRoGApiClient client)
{
    public const int MinLength = 2;

    public async Task<IReadOnlyList<SearchGroup>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var q = query.Trim();
        if (q.Length < MinLength)
        {
            return [];
        }

        var response = await client.V1_searchAsync(q, limit: 8, cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.Data
            .Where(g => !g.Failed && g.Hits.Count > 0)
            .Select(g => new SearchGroup(g.Key, g.Label, g.Hits
                .Select(h => new SearchHit(
                    HtmlText.ToPlain(h.Title, 160),
                    h.Excerpt is null ? null : HtmlText.ToPlain(h.Excerpt, 240),
                    ApiValues.WebUri(h.Url),
                    ApiValues.Text(h.Meta)))
                .ToList()))
            .ToList();
    }
}
