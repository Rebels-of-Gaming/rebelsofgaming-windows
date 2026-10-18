using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Links;

public sealed record ShortLink(string Id, string Code, Uri? ShortUrl, Uri? Target, string? Title, bool Active, bool Blocked, int Clicks, DateTimeOffset? LastClickedAt, DateTimeOffset? CreatedAt);

/// <summary>Kurzlinks (Guide 21): eigene Links mit Klickzahlen, anlegen mit optionalem Wunsch-Code, QR-Code als PNG.</summary>
public sealed class ShortLinkService(IRoGApiClient client)
{
    public async Task<Page<ShortLink>> ListAsync(string? cursor = null, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_links_indexAsync(cursor: cursor, per_page: 50, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new Page<ShortLink>(response.Data.Select(d => Map(JsonSerializer.SerializeToElement(d))).ToList(), response.Meta?.Next_cursor);
    }

    public async Task<ShortLink> CreateAsync(Uri target, string? code = null, string? title = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Scheme != Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException("Nur http(s)-Ziele", nameof(target));
        }

        var response = await client.V1_links_storeAsync(new Body92 { Url = target, Code = ApiValues.Text(code), Title = ApiValues.Text(title) }, cancellationToken).ConfigureAwait(false);
        return Map(JsonSerializer.SerializeToElement(response.Data));
    }

    /// <summary>QR-Code als PNG-Stream (Aufrufer gibt frei).</summary>
    public async Task<System.IO.Stream> QrPngAsync(string id, CancellationToken cancellationToken = default)
    {
        var file = await client.V1_links_qrAsync(id, "png", cancellationToken).ConfigureAwait(false);
        var copy = new System.IO.MemoryStream();
        await file.Stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
        file.Dispose();
        copy.Position = 0;
        return copy;
    }

    internal static ShortLink Map(JsonElement e)
    {
        static string? S(JsonElement o, string n) => o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        static bool B(JsonElement o, string n) => o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.True;
        var clicks = e.TryGetProperty("clicks", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
        return new ShortLink(
            S(e, "id") ?? string.Empty,
            S(e, "code") ?? string.Empty,
            ApiValues.WebUri(S(e, "short_url")),
            ApiValues.WebUri(S(e, "url")),
            ApiValues.Text(S(e, "title")),
            B(e, "active"),
            B(e, "blocked"),
            clicks,
            ApiValues.Time(S(e, "last_clicked_at")),
            ApiValues.Time(S(e, "created_at")));
    }
}
