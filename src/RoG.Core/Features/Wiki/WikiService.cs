using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Core.Security;
using File = System.IO.File;

namespace RoG.Core.Features.Wiki;

public sealed record WikiNamespace(string Id, string Slug, string Name, string? Description, string Visibility, bool RequiresReview, int Pages, string UploadVisibility = "members");

/// <summary>Ergebnis „Seite anlegen“: Seite und Status der ersten Version (<c>pending</c> bei Prüfpflicht).</summary>
public sealed record WikiCreated(string PageId, string Status);

/// <summary>Im Wiki registriertes Bild (Guide 26 „Kategorien und Dateien“).</summary>
public sealed record WikiFile(string Slug, string Name, string Namespace, Uri? Source, Uri? WebUrl);

/// <summary>Seite im Baum, flach mit Einrückung (<see cref="Depth"/>) für eine Liste.</summary>
public sealed record WikiTreeEntry(string Id, string Slug, string Title, int Depth, DateTimeOffset? UpdatedAt);

public sealed record WikiTocEntry(int Level, string Anchor, string Text);

public sealed record WikiArticle(
    string Id,
    string Slug,
    string NamespaceSlug,
    string NamespaceName,
    string Locale,
    string Title,
    string Format,
    string? Content,
    string Html,
    IReadOnlyList<WikiTocEntry> Toc,
    IReadOnlyList<string> Categories,
    string? RevisionId,
    DateTimeOffset? UpdatedAt,
    bool? Watching,
    Uri? WebUrl,
    string? LastEditorId = null)
{
    /// <summary>Hat <paramref name="userId"/> die ausgelieferte Fassung zuletzt geändert? Dann kein „Melden“ (eigener Inhalt).</summary>
    public bool IsLastEditedBy(string? userId) => !string.IsNullOrEmpty(userId) && LastEditorId == userId;
}

public sealed record WikiRevision(string Id, string Title, string? Summary, string Status, string? Author, DateTimeOffset? CreatedAt, string Locale, string? AuthorId = null)
{
    /// <summary>Eigene Version – nicht melden.</summary>
    public bool IsBy(string? userId) => !string.IsNullOrEmpty(userId) && AuthorId == userId;
}

public sealed record WikiSearchHit(string Id, string NamespaceSlug, string Slug, string Title, string Snippet);

/// <summary>Ergebnis einer Bearbeitung: gespeichert oder als Vorschlag (Prüfpflicht).</summary>
public enum WikiSaveOutcome
{
    Saved,
    Pending,
}

/// <summary>Bearbeitungskonflikt (<c>409 edit_conflict</c>) mit der inzwischen aktuellen Fassung.</summary>
public sealed class WikiEditConflictException(string currentRevisionId, string currentContent, string? updatedBy, ApiProblem problem)
    : Exception(problem.Detail ?? "edit_conflict")
{
    public string CurrentRevisionId { get; } = currentRevisionId;

    public string CurrentContent { get; } = currentContent;

    public string? UpdatedBy { get; } = updatedBy;
}

/// <summary>
/// Wiki (Guide 26): Bereiche, Seitenbaum, Seiten (HTML der API), Versionen, Vorschau, Bearbeiten mit
/// <c>base_revision_id</c> (Konflikte), Beobachten, Kategorien, Suche (<c>q</c> fehlt in der Spec – Lücke W11).
/// </summary>
public sealed class WikiService(IRoGApiClient client, HttpClient http, Uri? wikiBaseUrl = null)
{
    public async Task<IReadOnlyList<WikiNamespace>> NamespacesAsync(CancellationToken cancellationToken = default) =>
        (await client.V1_wiki_namespaces_indexAsync(cancellationToken).ConfigureAwait(false)).Data
            .Select(n => new WikiNamespace(n.Id, n.Slug, n.Name, ApiValues.Text(n.Description), n.Visibility, n.Requires_review, n.Pages, ApiValues.Text(n.Upload_visibility) ?? "members"))
            .ToList();

    public async Task<IReadOnlyList<WikiTreeEntry>> TreeAsync(string namespaceSlug, CancellationToken cancellationToken = default)
    {
        var roots = (await client.V1_wiki_pages_indexAsync(namespaceSlug, cancellationToken).ConfigureAwait(false)).Data;
        var result = new List<WikiTreeEntry>();
        void Walk(IEnumerable<WikiTreePageResource> pages, int depth)
        {
            foreach (var p in pages)
            {
                result.Add(new WikiTreeEntry(p.Id, p.Slug, p.Title, depth, ApiValues.Time(p.Updated_at)));
                if (p.Children is { Count: > 0 } children && depth < 50)
                {
                    Walk(children, depth + 1);
                }
            }
        }

        Walk(roots, 0);
        return result;
    }

    public async Task<WikiArticle> PageAsync(string id, string? locale = null, CancellationToken cancellationToken = default) =>
        Map((await client.V1_wiki_pages_showAsync(id, locale, cancellationToken).ConfigureAwait(false)).Data);

    /// <summary>Seite über die Adresse (Links im HTML, Deep-Links); folgt Weiterleitungen wie die Website.</summary>
    public async Task<WikiArticle> PageBySlugAsync(string namespaceSlug, string slug, string? locale = null, CancellationToken cancellationToken = default)
    {
        var d = (await client.V1_wiki_pages_bySlugAsync(namespaceSlug, slug, locale, cancellationToken: cancellationToken).ConfigureAwait(false)).Data;
        return Map(JsonSerializer.Deserialize<Data492>(JsonSerializer.Serialize(d))!);
    }

    public async Task<IReadOnlyList<WikiRevision>> RevisionsAsync(string pageId, CancellationToken cancellationToken = default) =>
        (await client.V1_wiki_pages_revisionsAsync(pageId, cancellationToken: cancellationToken).ConfigureAwait(false)).Data
            .Select(r => new WikiRevision(r.Id, r.Title, ApiValues.Text(r.Summary), r.Status, r.Author?.Name, ApiValues.Time(r.Created_at), r.Locale, ApiValues.Text(r.Author?.Id)))
            .ToList();

    public async Task<string> RevisionContentAsync(string revisionId, CancellationToken cancellationToken = default) =>
        (await client.V1_wiki_revisions_showAsync(revisionId, cancellationToken).ConfigureAwait(false)).Data.Content ?? string.Empty;

    public async Task RestoreAsync(string revisionId, CancellationToken cancellationToken = default) =>
        await client.V1_wiki_revisions_restoreAsync(revisionId, cancellationToken).ConfigureAwait(false);

    /// <summary>Vorschlag freigeben (Prüfpflicht, Recht <c>wiki.approve</c>).</summary>
    public async Task ApproveAsync(string revisionId, CancellationToken cancellationToken = default) =>
        await client.V1_wiki_revisions_approveAsync(revisionId, cancellationToken).ConfigureAwait(false);

    public async Task RejectAsync(string revisionId, CancellationToken cancellationToken = default) =>
        await client.V1_wiki_revisions_rejectAsync(revisionId, cancellationToken).ConfigureAwait(false);

    /// <summary>Vorschau ohne Speichern (30/min) – liefert HTML wie die Seite.</summary>
    public async Task<string> RenderAsync(string pageId, string content, string format, string title, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["content"] = content, ["format"] = format, ["page_id"] = pageId, ["title"] = title };
        using var response = await http.PostAsync(new Uri("wiki/render", UriKind.Relative), JsonContent(body), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("html", out var html) ? html.GetString() ?? string.Empty : string.Empty;
    }

    /// <summary>
    /// Neue Version speichern. Mit <paramref name="baseRevisionId"/> erkennt der Server fremde Änderungen →
    /// <see cref="WikiEditConflictException"/> mit der aktuellen Fassung zum Zusammenführen.
    /// </summary>
    public async Task<WikiSaveOutcome> SaveAsync(string pageId, string locale, string title, string content, string format, string? summary, string? baseRevisionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await client.V1_wiki_pages_updateAsync(pageId, new Body177
            {
                Locale = locale,
                Title = title,
                Content = content,
                Format = format,
                Summary = ApiValues.Text(summary),
                Base_revision_id = baseRevisionId,
            }, cancellationToken).ConfigureAwait(false);
            return result.Data.Status == "pending" ? WikiSaveOutcome.Pending : WikiSaveOutcome.Saved;
        }
        catch (ApiProblemException ex) when (ex.Problem.Code == "edit_conflict" && ex.Problem.TryGetExtension("current", out var current))
        {
            static string? S(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            throw new WikiEditConflictException(
                S(current, "revision_id") ?? string.Empty,
                S(current, "content") ?? string.Empty,
                current.TryGetProperty("updated_by", out var by) ? S(by, "name") : null,
                ex.Problem);
        }
    }

    /// <summary>Neue Seite im Bereich (Guide 26): Titel, Inhalt, Format, optional Elternseite. Prüfpflicht → <c>pending</c>.</summary>
    public async Task<WikiCreated> CreatePageAsync(
        string namespaceSlug, string locale, string title, string content, string format, string? summary, string? parentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Titel fehlt", nameof(title));
        }

        var d = (await client.V1_wiki_pages_storeAsync(namespaceSlug, new Body176
        {
            Locale = locale,
            Title = title.Trim(),
            Content = content,
            Format = format,
            Summary = ApiValues.Text(summary),
            Parent_id = ApiValues.Text(parentId),
        }, cancellationToken).ConfigureAwait(false)).Data;
        return new WikiCreated(d.Page_id, d.Status);
    }

    /// <summary>
    /// Hochgeladenes Bild im Wiki registrieren (<c>POST /v1/wiki/files</c> – Body fehlt in der Spec, Lücke W13).
    /// Die Sichtbarkeit des Uploads muss zum Bereich passen (<see cref="UploadVisibilityFor"/>).
    /// </summary>
    public async Task<WikiFile> RegisterFileAsync(string uploadId, string namespaceSlug, string name, string? description, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["upload_id"] = uploadId, ["namespace"] = namespaceSlug, ["name"] = name.Trim() };
        if (ApiValues.Text(description) is { } d)
        {
            body["description"] = d;
        }

        using var response = await http.PostAsync(new Uri("wiki/files", UriKind.Relative), JsonContent(body), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseFile(doc.RootElement);
    }

    internal static WikiFile ParseFile(JsonElement root)
    {
        var d = root.TryGetProperty("data", out var data) ? data : root;
        static string? S(JsonElement o, string n) => o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        return new WikiFile(
            S(d, "slug") ?? string.Empty,
            S(d, "name") ?? string.Empty,
            S(d, "namespace") ?? string.Empty,
            ApiValues.WebUri(S(d, "src")),
            ApiValues.WebUri(S(d, "url")));
    }

    /// <summary>Sichtbarkeit für Bild-Uploads je Bereich (<c>upload_visibility</c>): <c>squad</c> → privat mit geheimem Link.</summary>
    public static Uploads.UploadVisibility UploadVisibilityFor(string? uploadVisibility) => uploadVisibility switch
    {
        "public" => Uploads.UploadVisibility.Public,
        "private" or "squad" => Uploads.UploadVisibility.Private,
        _ => Uploads.UploadVisibility.Members,
    };

    /// <summary>Einbinden im Editor: Wikitext <c>[[Datei:Name|mini]]</c>, Markdown als Bild mit direktem Link.</summary>
    public static string FileEmbed(string format, string name, Uri? direct) =>
        format == "markdown"
            ? $"![{name.Replace("]", string.Empty, StringComparison.Ordinal)}]({direct})"
            : $"[[Datei:{name}|mini]]";

    public async Task<bool> ToggleWatchAsync(string pageId, CancellationToken cancellationToken = default) =>
        (await client.V1_wiki_pages_watchAsync(pageId, cancellationToken).ConfigureAwait(false)).Data.Watching;

    public async Task<IReadOnlyList<WikiSearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var q = query.Trim();
        if (q.Length < 2)
        {
            return [];
        }

        using var response = await http.GetAsync(new Uri("wiki/search?q=" + Uri.EscapeDataString(q), UriKind.Relative), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseSearch(doc.RootElement);
    }

    public async Task<IReadOnlyList<(string Slug, string Name, int Count)>> CategoriesAsync(CancellationToken cancellationToken = default) =>
        (await client.V1_wiki_categories_indexAsync(cancellationToken).ConfigureAwait(false)).Data.Select(c => (c.Slug, c.Name, c.Count)).ToList();

    public async Task<IReadOnlyList<WikiSearchHit>> CategoryPagesAsync(string slug, CancellationToken cancellationToken = default) =>
        (await client.V1_wiki_categories_showAsync(slug, cancellationToken).ConfigureAwait(false)).Data.Pages
            .Select(p => new WikiSearchHit(p.Id, p.Namespace, p.Slug, p.Title, string.Empty)).ToList();

    /// <summary>Link im Seiten-HTML auf eine Wiki-Seite? (<c>wiki.…/{bereich}/{seite}[#anker]</c>) → Bereich und Adresse.</summary>
    public (string Namespace, string Slug)? ParseWikiLink(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (wikiBaseUrl is null || !link.IsAbsoluteUri || !string.Equals(link.Host, wikiBaseUrl.Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = link.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || segments[0] is "file" or "help" or "category" or "search" or "recent")
        {
            return null;
        }

        return (Uri.UnescapeDataString(segments[0]), Uri.UnescapeDataString(segments[^1]));
    }

    internal static IReadOnlyList<WikiSearchHit> ParseSearch(JsonElement root)
    {
        static string S(JsonElement o, string n) => o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? string.Empty : string.Empty;
        return root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray().Select(h => new WikiSearchHit(S(h, "id"), S(h, "namespace"), S(h, "slug"), S(h, "title"), S(h, "snippet"))).Where(h => h.Id.Length > 0).ToList()
            : [];
    }

    internal static WikiArticle Map(Data492 d) => new(
        d.Id,
        d.Slug,
        d.Namespace.Slug,
        d.Namespace.Name,
        d.Locale,
        ApiValues.Text(d.Display_title) is { } display && !display.Contains('<', StringComparison.Ordinal) ? display : d.Title,
        d.Format,
        d.Content,
        d.Html ?? string.Empty,
        d.Toc?.Select(t => new WikiTocEntry(t.Level, t.Id, t.Text)).ToList() ?? [],
        d.Categories?.Where(c => !c.Hidden).Select(c => c.Name).ToList() ?? [],
        ApiValues.Text(d.Revision_id),
        ApiValues.Time(d.Updated_at),
        d.Watching,
        Uri.TryCreate(d.Url, UriKind.Absolute, out var u) ? u : null,
        ApiValues.Text(d.Last_editor?.Id));

    private static StringContent JsonContent(object body) => new(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
}

/// <summary>
/// Offline-Kopien gelesener Wiki-Seiten (Pflichtenheft „offline verfügbar“): je Seite und Sprache eine Datei im
/// App-Datenordner, verschlüsselt über <see cref="IPayloadProtector"/> (DPAPI). Inhalte sind gerendertes HTML, nie Tokens.
/// </summary>
public sealed class WikiOfflineStore(string folder, IPayloadProtector protector)
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = false };

    public async Task SaveAsync(WikiArticle page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        Directory.CreateDirectory(folder);
        var target = PathFor(page.Id, page.Locale);
        var temp = target + ".tmp";
        var data = await protector.ProtectAsync(JsonSerializer.SerializeToUtf8Bytes(page, s_json), cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(temp, data, cancellationToken).ConfigureAwait(false);
        File.Move(temp, target, overwrite: true);
    }

    public async Task<WikiArticle?> LoadAsync(string id, string locale, CancellationToken cancellationToken = default)
    {
        var path = PathFor(id, locale);
        return File.Exists(path) ? await ReadAsync(path, cancellationToken).ConfigureAwait(false) : null;
    }

    /// <summary>Alle offline gespeicherten Seiten einer Sprache (für die Liste ohne Netz).</summary>
    public async Task<IReadOnlyList<WikiArticle>> AllAsync(string locale, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var pages = new List<WikiArticle>();
        foreach (var path in Directory.EnumerateFiles(folder, $"*.{locale}.dat"))
        {
            if (await ReadAsync(path, cancellationToken).ConfigureAwait(false) is { } page)
            {
                pages.Add(page);
            }
        }

        return pages.OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Beim Abmelden: Inhalte können Mitglieder-Bereiche enthalten.</summary>
    public void Clear()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private async Task<WikiArticle?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var data = await protector.UnprotectAsync(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<WikiArticle>(data, s_json);
        }
#pragma warning disable CA1031 // fremde/beschädigte Datei (anderer Benutzer, alte Version) – verwerfen
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            File.Delete(path);
            return null;
        }
    }

    private string PathFor(string id, string locale)
    {
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains("..", StringComparison.Ordinal) || locale.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Ungültige Seiten-ID", nameof(id));
        }

        return Path.Combine(folder, $"{id}.{locale}.dat");
    }
}

/// <summary>Zeilenweiser Vergleich zweier Fassungen (LCS) für die Versionsansicht.</summary>
public static class LineDiff
{
    public static IReadOnlyList<(char Kind, string Text)> Compare(string oldText, string newText)
    {
        var a = Split(oldText);
        var b = Split(newText);
        if ((long)a.Length * b.Length > 4_000_000)
        {
            // Sehr große Seiten: nur ganz ersetzen statt quadratisch rechnen
            return [.. a.Select(l => ('-', l)), .. b.Select(l => ('+', l))];
        }

        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var result = new List<(char, string)>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                result.Add((' ', a[x]));
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                result.Add(('-', a[x++]));
            }
            else
            {
                result.Add(('+', b[y++]));
            }
        }

        result.AddRange(a.Skip(x).Select(l => ('-', l)));
        result.AddRange(b.Skip(y).Select(l => ('+', l)));
        return result;
    }

    private static string[] Split(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
}
