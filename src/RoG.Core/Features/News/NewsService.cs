using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Core.Content;

namespace RoG.Core.Features.News;

public sealed record NewsSummary(
    string Id,
    string Slug,
    Uri? WebUrl,
    string Title,
    string Teaser,
    bool Pinned,
    DateTimeOffset? PublishedAt,
    int ReadingMinutes,
    int? CommentsCount,
    string? CategoryName,
    string? CategoryColor,
    string? SquadName,
    string? AuthorName,
    Uri? Cover,
    string? CoverAlt);

public sealed record NewsArticle(NewsSummary Summary, RichDocument Body, bool CommentsEnabled);

/// <summary>Kommentar; bei blockierten Personen <see cref="AuthorBlocked"/> und ohne Text (außer mit „trotzdem anzeigen“).</summary>
public sealed record NewsComment(string Id, string AuthorName, string? AuthorSlug, DateTimeOffset? CreatedAt, string? Body, bool AuthorBlocked = false);

/// <summary>News (öffentlich, auch im Gastmodus): Liste mit Cursor, Artikel mit nativ gerendertem Text, Kommentare.</summary>
public sealed class NewsService(IRoGApiClient client, Uri webBaseUrl)
{
    private readonly HtmlContentParser _html = new(ContentPolicy.Platform, webBaseUrl);

    public async Task<Page<NewsSummary>> ListAsync(string? cursor = null, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_news_indexAsync(cursor: cursor, per_page: 20, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new Page<NewsSummary>(response.Data.Select(Map).ToList(), response.Meta?.Next_cursor);
    }

    public async Task<NewsArticle> GetAsync(string slug, CancellationToken cancellationToken = default)
    {
        var data = (await client.V1_news_showAsync(slug, cancellationToken).ConfigureAwait(false)).Data;
        var summary = new NewsSummary(
            data.Id, data.Slug, ApiValues.WebUri(data.Url), data.Title, ApiValues.Text(data.Teaser) ?? data.Summary, data.Pinned,
            ApiValues.Time(data.Published_at), data.Reading_minutes, data.Comments_count, data.Category?.Name, data.Category?.Color,
            data.Squad?.Name, data.Author?.Name,
            ApiValues.Image(data.Cover?.Url, data.Cover?.Variants?.Select(v => (v.Width, v.Url)), 1280), AltOf(data.Cover?.Alt));
        return new NewsArticle(summary, _html.Parse(data.Body_html), data.Comments_enabled);
    }

    /// <param name="showBlocked">Kommentare blockierter Personen doch mit Text laden (<c>?show_blocked=1</c>).</param>
    public async Task<IReadOnlyList<NewsComment>> GetCommentsAsync(string slug, bool showBlocked = false, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_news_comments_indexAsync(slug, showBlocked ? true : null, cancellationToken).ConfigureAwait(false);
        return response.Data.Where(c => !c.Hidden)
            .Select(c => new NewsComment(c.Id, c.Author?.Name ?? "—", c.Author?.Slug, ApiValues.Time(c.Created_at), ApiValues.Text(c.Body), c.Author_blocked))
            .ToList();
    }

    private static NewsSummary Map(Data369 d) => new(
        d.Id, d.Slug, ApiValues.WebUri(d.Url), d.Title, ApiValues.Text(d.Teaser) ?? d.Summary, d.Pinned,
        ApiValues.Time(d.Published_at), d.Reading_minutes, d.Comments_count, d.Category?.Name, d.Category?.Color,
        d.Squad?.Name, d.Author?.Name,
        ApiValues.Image(d.Cover?.Url, d.Cover?.Variants?.Select(v => (v.Width, v.Url)), 640), AltOf(d.Cover?.Alt));

    /// <summary><c>cover.alt</c> ist je Sprache übersetzt (Wörterbuch) – den ersten nicht leeren Text nehmen.</summary>
    private static string? AltOf(IDictionary<string, string>? alt) => alt?.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
