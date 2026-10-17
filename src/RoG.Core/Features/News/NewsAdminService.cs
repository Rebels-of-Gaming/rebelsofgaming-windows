using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Features.Account;

namespace RoG.Core.Features.News;

public sealed record NewsCategory(string Id, string Slug, string Name, string? Color);

/// <summary>News in der Redaktion: Texte je Sprache (<c>de</c>, <c>en</c>, <c>fr</c>), Status und Einstellungen.</summary>
public sealed record NewsDraft(
    string? Id,
    IReadOnlyDictionary<string, string> Title,
    IReadOnlyDictionary<string, string> Teaser,
    IReadOnlyDictionary<string, string> Body,
    string? CategoryId = null,
    string Visibility = "public",
    string Status = "draft",
    DateTimeOffset? PublishedAt = null,
    bool Pinned = false,
    bool CommentsEnabled = true,
    bool PostToDiscord = true,
    string? Slug = null);

/// <summary>Ergebnis von Speichern: Id/Adresse und Hinweise der Website (<c>meta.warnings[].message</c>).</summary>
public sealed record NewsSaved(string Id, string Slug, IReadOnlyList<string> Warnings);

/// <summary>Was die angemeldete Person in der Redaktion darf (Guide 22) – plus Token-Scope <c>news:write</c>.</summary>
public sealed record NewsRights(bool Write, bool Publish, bool Manage)
{
    public static NewsRights For(UserProfile? me) => me is not null && me.HasScope("news:write")
        ? new(me.Can("news.write") || me.Can("news.publish"), me.Can("news.publish"), me.Can("news.manage"))
        : new(false, false, false);
}

/// <summary>
/// News schreiben (Guide 22): Entwurf anlegen, ändern, veröffentlichen/planen, löschen, Titelbild aus der Bibliothek.
/// Die Spec beschreibt <c>teaser</c>/<c>body</c> als Liste und <c>category_id</c> als Zahl (Lücke W15) – die App sendet
/// und liest diese Endpunkte selbst.
/// </summary>
public sealed class NewsAdminService(HttpClient http)
{
    public static readonly IReadOnlyList<string> Languages = ["de", "en", "fr"];

    public async Task<IReadOnlyList<NewsCategory>> CategoriesAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await GetJsonAsync("news-categories", cancellationToken).ConfigureAwait(false);
        return doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray().Select(c => new NewsCategory(S(c, "id") ?? string.Empty, S(c, "slug") ?? string.Empty, S(c, "name") ?? string.Empty, S(c, "color")))
                .Where(c => c.Id.Length > 0).ToList()
            : [];
    }

    /// <summary>Entwürfe und geplante News der Redaktion (<c>filter[status]</c>).</summary>
    public async Task<IReadOnlyList<(string Id, string Slug, string Title, string Status, DateTimeOffset? PublishedAt)>> ListAsync(string status, CancellationToken cancellationToken = default)
    {
        using var doc = await GetJsonAsync("news?per_page=50&filter%5Bstatus%5D=" + Uri.EscapeDataString(status), cancellationToken).ConfigureAwait(false);
        return doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray().Select(n => (S(n, "id") ?? string.Empty, S(n, "slug") ?? string.Empty, S(n, "title") ?? string.Empty, S(n, "status") ?? string.Empty, ApiValues.Time(S(n, "published_at"))))
                .Where(n => n.Item1.Length > 0).ToList()
            : [];
    }

    /// <summary>News mit allen Übersetzungen zum Bearbeiten.</summary>
    public async Task<NewsDraft> GetAsync(string slugOrId, IReadOnlyList<NewsCategory> categories, CancellationToken cancellationToken = default)
    {
        using var doc = await GetJsonAsync("news/" + Uri.EscapeDataString(slugOrId), cancellationToken).ConfigureAwait(false);
        return ParseDraft(doc.RootElement, categories);
    }

    internal static NewsDraft ParseDraft(JsonElement root, IReadOnlyList<NewsCategory> categories)
    {
        var d = root.TryGetProperty("data", out var data) ? data : root;
        var translations = d.TryGetProperty("translations", out var t) && t.ValueKind == JsonValueKind.Object ? t : default;
        IReadOnlyDictionary<string, string> Texts(string field, string? fallback)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (translations.ValueKind == JsonValueKind.Object && translations.TryGetProperty(field, out var map) && map.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in map.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
                {
                    result[p.Name] = p.Value.GetString() ?? string.Empty;
                }
            }
            else if (fallback is not null)
            {
                result["de"] = fallback;
            }

            return result;
        }

        var categorySlug = d.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.Object ? S(c, "slug") : null;
        return new NewsDraft(
            S(d, "id"),
            Texts("title", S(d, "title")),
            Texts("teaser", S(d, "teaser")),
            Texts("body", S(d, "body")),
            categories.FirstOrDefault(x => x.Slug == categorySlug)?.Id,
            S(d, "visibility") ?? "public",
            S(d, "status") ?? "draft",
            ApiValues.Time(S(d, "published_at")),
            B(d, "pinned"),
            !d.TryGetProperty("comments_enabled", out var ce) || ce.ValueKind != JsonValueKind.False,
            PostToDiscord: true,
            Slug: S(d, "slug"));
    }

    /// <summary>Prüft wie die Website, bevor gesendet wird: Titel (DE) Pflicht, geplant nur mit Zeitpunkt in der Zukunft.</summary>
    public static IReadOnlyList<string> Validate(NewsDraft draft, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = new List<string>();
        if (!draft.Title.TryGetValue("de", out var de) || string.IsNullOrWhiteSpace(de))
        {
            errors.Add("title");
        }

        if (draft.Title.Values.Any(v => v.Trim().Length > 160) || draft.Teaser.Values.Any(v => v.Trim().Length > 400))
        {
            errors.Add("length");
        }

        if (draft.Status == "scheduled" && (draft.PublishedAt is not { } at || at <= now))
        {
            errors.Add("published_at");
        }

        return errors;
    }

    /// <summary>Body für <c>POST /v1/news</c> bzw. <c>PATCH /v1/news/{id}</c>; leere Übersetzungen entfallen.</summary>
    public static Dictionary<string, object?> BuildBody(NewsDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        static Dictionary<string, string> Clean(IReadOnlyDictionary<string, string> texts) =>
            texts.Where(p => Languages.Contains(p.Key) && !string.IsNullOrWhiteSpace(p.Value)).ToDictionary(p => p.Key, p => p.Value.Trim(), StringComparer.Ordinal);

        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["title"] = Clean(draft.Title),
            ["teaser"] = Clean(draft.Teaser),
            ["body"] = Clean(draft.Body),
            ["category_id"] = ApiValues.Text(draft.CategoryId),
            ["visibility"] = draft.Visibility,
            ["status"] = draft.Status,
            ["pinned"] = draft.Pinned,
            ["comments_enabled"] = draft.CommentsEnabled,
            ["post_to_discord"] = draft.PostToDiscord,
        };
        if (draft.Status == "scheduled" && draft.PublishedAt is { } at)
        {
            body["published_at"] = at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        return body;
    }

    public async Task<NewsSaved> SaveAsync(NewsDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var body = BuildBody(draft);
        using var response = draft.Id is { } id
            ? await http.PatchAsJsonAsync(new Uri("news/" + Uri.EscapeDataString(id), UriKind.Relative), body, cancellationToken).ConfigureAwait(false)
            : await http.PostAsJsonAsync(new Uri("news", UriKind.Relative), body, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseSaved(doc.RootElement);
    }

    internal static NewsSaved ParseSaved(JsonElement root)
    {
        var d = root.TryGetProperty("data", out var data) ? data : default;
        var warnings = root.TryGetProperty("meta", out var meta) && meta.TryGetProperty("warnings", out var w) && w.ValueKind == JsonValueKind.Array
            ? w.EnumerateArray().Select(x => S(x, "message") ?? S(x, "code") ?? string.Empty).Where(x => x.Length > 0).ToList()
            : [];
        return new NewsSaved(S(d, "id") ?? string.Empty, S(d, "slug") ?? string.Empty, warnings);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync(new Uri("news/" + Uri.EscapeDataString(id), UriKind.Relative), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Titelbild aus einem öffentlichen Upload (mind. 1200 × 400, Verarbeitung im Hintergrund).</summary>
    public async Task SetCoverAsync(string id, string uploadId, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(
            new Uri($"news/{Uri.EscapeDataString(id)}/cover", UriKind.Relative), new Dictionary<string, string> { ["upload_id"] = uploadId }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveCoverAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync(new Uri($"news/{Uri.EscapeDataString(id)}/cover", UriKind.Relative), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string? S(JsonElement o, string n) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool B(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.True;
}
