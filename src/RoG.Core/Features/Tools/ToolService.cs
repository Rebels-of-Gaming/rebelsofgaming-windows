using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoG.Core.Api;

namespace RoG.Core.Features.Tools;

public sealed record ToolGame(string Id, string Slug, string Name, string? Abbreviation);

public sealed record ToolSquad(string Id, string Slug, string Name);

/// <summary>Tool aus dem Tool-Baukasten (Website v0.31.0, Guide 42).</summary>
public sealed record ToolSummary(
    string Id,
    string Slug,
    string Name,
    string? Description,
    string Icon,
    string Visibility,
    string Status,
    ToolGame? Game,
    ToolSquad? Squad,
    int? Version,
    DateTimeOffset? UpdatedAt,
    Uri? Url)
{
    public bool IsDraft => Status == "draft";
}

/// <summary>Tools eines Spiels (Liste „nach Spiel gruppiert“); <see cref="Game"/> = <c>null</c> für Tools ohne Spiel.</summary>
public sealed record ToolGroup(ToolGame? Game, IReadOnlyList<ToolSummary> Tools);

/// <summary>Fertiges, abgeschottetes HTML-Dokument (<c>GET /v1/tools/{slug}/document</c>).</summary>
public sealed record ToolDocument(string Html, string Theme, string Locale, int? Version);

/// <summary>Datei eines Tools (<c>GET /v1/tools/{slug}/assets</c>, v0.32.0).</summary>
public sealed record ToolAsset(string Name, string Mime, long Size, int? Width, int? Height, string? Hash);

/// <summary>Bytes einer Datei mit dem gelieferten Typ.</summary>
public sealed record ToolAssetContent(string Mime, byte[] Bytes);

/// <summary>Antwort der Daten-Listen: <c>data</c> plus <c>meta.truncated</c>.</summary>
public sealed record ToolDataPage(JsonNode? Items, bool Truncated);

/// <summary>Was die Bridge von der API braucht – im Test ersetzbar.</summary>
public interface IToolBridgeApi
{
    Task<JsonNode?> ShowAsync(string slug, CancellationToken cancellationToken);

    Task<JsonNode?> SquadAsync(string slug, CancellationToken cancellationToken);

    Task<JsonNode?> DataGetAsync(string slug, string scope, string key, CancellationToken cancellationToken);

    Task<JsonNode?> DataSetAsync(string slug, string scope, string key, JsonNode? value, CancellationToken cancellationToken);

    Task DataDeleteAsync(string slug, string scope, string key, CancellationToken cancellationToken);

    Task<ToolDataPage> DataListAsync(string slug, string scope, string? prefix, CancellationToken cancellationToken);

    Task<ToolDataPage> MembersAsync(string slug, string key, CancellationToken cancellationToken);

    Task<IReadOnlyList<ToolAsset>> AssetsAsync(string slug, CancellationToken cancellationToken);

    Task<ToolAssetContent> AssetAsync(string slug, string name, long maxBytes, CancellationToken cancellationToken);
}

/// <summary>
/// Tool-Baukasten (Guide 42): Liste, Dokument und die API-Aufrufe der Bridge. Werte der Tools sind beliebiges JSON –
/// deshalb direkt über <c>/v1/</c> (wie <c>SocialSourceService</c>) und ohne ETag-Cache: Tool-Daten müssen frisch sein.
/// </summary>
public sealed class ToolService(HttpClient http) : IToolBridgeApi
{
    public async Task<IReadOnlyList<ToolSummary>> ListAsync(string? game = null, string? squad = null, bool includeDrafts = false, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(game))
        {
            query.Add("filter%5Bgame%5D=" + Uri.EscapeDataString(game));
        }

        if (!string.IsNullOrEmpty(squad))
        {
            query.Add("filter%5Bsquad%5D=" + Uri.EscapeDataString(squad));
        }

        if (includeDrafts)
        {
            query.Add("filter%5Bstatus%5D=all");
        }

        var root = await SendAsync(HttpMethod.Get, "tools" + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty), null, cancellationToken).ConfigureAwait(false);
        return root?["data"] is JsonArray items ? items.Select(ParseSummary).OfType<ToolSummary>().ToList() : [];
    }

    public async Task<ToolDocument> DocumentAsync(string slug, string theme = "dark", CancellationToken cancellationToken = default)
    {
        var root = await SendAsync(HttpMethod.Get, $"tools/{Seg(slug)}/document?theme={(theme == "light" ? "light" : "dark")}", null, cancellationToken).ConfigureAwait(false);
        var data = root?["data"];
        var html = Str(data, "document") ?? throw new InvalidDataException("Tool document missing.");
        return new ToolDocument(html, Str(data, "theme") ?? "dark", Str(data, "locale") ?? "de", Int(data, "version"));
    }

    /// <summary>Spiel-Gruppen in der Reihenfolge der API (sortiert nach <c>sort</c>); Tools ohne Spiel zuletzt.</summary>
    public static IReadOnlyList<ToolGroup> ByGame(IEnumerable<ToolSummary> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return tools
            .GroupBy(t => t.Game?.Id ?? string.Empty)
            .OrderBy(g => g.Key.Length == 0 ? 1 : 0)
            .Select(g => new ToolGroup(g.First().Game, g.ToList()))
            .ToList();
    }

    internal static ToolSummary? ParseSummary(JsonNode? n)
    {
        if (Str(n, "id") is not { } id || Str(n, "slug") is not { } slug)
        {
            return null;
        }

        var game = n!["game"] is JsonObject g && Str(g, "id") is { } gid
            ? new ToolGame(gid, Str(g, "slug") ?? string.Empty, Str(g, "name") ?? string.Empty, Str(g, "short"))
            : null;
        var squad = n["squad"] is JsonObject s && Str(s, "id") is { } sid
            ? new ToolSquad(sid, Str(s, "slug") ?? string.Empty, Str(s, "name") ?? string.Empty)
            : null;
        return new ToolSummary(
            id,
            slug,
            Str(n, "name") ?? slug,
            Str(n, "description"),
            Str(n, "icon") ?? string.Empty,
            Str(n, "visibility") ?? string.Empty,
            Str(n, "status") ?? "published",
            game,
            squad,
            Int(n, "version"),
            Str(n, "updated_at") is { } u && DateTimeOffset.TryParse(u, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null,
            ApiValues.WebUri(Str(n, "url")));
    }

    // --- Bridge ------------------------------------------------------------------------------------------------

    public async Task<JsonNode?> ShowAsync(string slug, CancellationToken cancellationToken) =>
        (await SendAsync(HttpMethod.Get, $"tools/{Seg(slug)}", null, cancellationToken).ConfigureAwait(false))?["data"]?.DeepClone();

    public async Task<JsonNode?> SquadAsync(string slug, CancellationToken cancellationToken) =>
        (await SendAsync(HttpMethod.Get, $"tools/{Seg(slug)}/squad", null, cancellationToken).ConfigureAwait(false))?["data"]?.DeepClone();

    public async Task<JsonNode?> DataGetAsync(string slug, string scope, string key, CancellationToken cancellationToken) =>
        (await SendAsync(HttpMethod.Get, DataPath(slug, scope, key), null, cancellationToken).ConfigureAwait(false))?["data"]?.DeepClone();

    public async Task<JsonNode?> DataSetAsync(string slug, string scope, string key, JsonNode? value, CancellationToken cancellationToken) =>
        (await SendAsync(HttpMethod.Put, DataPath(slug, scope, key), new JsonObject { ["value"] = value?.DeepClone() }, cancellationToken).ConfigureAwait(false))?["data"]?.DeepClone();

    public async Task DataDeleteAsync(string slug, string scope, string key, CancellationToken cancellationToken) =>
        await SendAsync(HttpMethod.Delete, DataPath(slug, scope, key), null, cancellationToken).ConfigureAwait(false);

    public async Task<ToolDataPage> DataListAsync(string slug, string scope, string? prefix, CancellationToken cancellationToken)
    {
        var path = $"tools/{Seg(slug)}/data/{Seg(scope)}" + (string.IsNullOrEmpty(prefix) ? string.Empty : "?prefix=" + Uri.EscapeDataString(prefix));
        return Page(await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false));
    }

    public async Task<ToolDataPage> MembersAsync(string slug, string key, CancellationToken cancellationToken) =>
        Page(await SendAsync(HttpMethod.Get, $"tools/{Seg(slug)}/squad-data/{Seg(key)}", null, cancellationToken).ConfigureAwait(false));

    public async Task<IReadOnlyList<ToolAsset>> AssetsAsync(string slug, CancellationToken cancellationToken)
    {
        var root = await SendAsync(HttpMethod.Get, $"tools/{Seg(slug)}/assets", null, cancellationToken).ConfigureAwait(false);
        return root?["data"] is JsonArray items
            ? items.Where(i => Str(i, "name") is not null && Str(i, "mime") is not null)
                .Select(i => new ToolAsset(Str(i, "name")!, Str(i, "mime")!, Long(i, "size") ?? 0, Int(i, "width"), Int(i, "height"), Str(i, "hash")))
                .ToList()
            : [];
    }

    /// <summary>Bytes einer Datei; Name aus der Liste des Tools (Ordner mit <c>/</c>, je Teil kodiert).</summary>
    public async Task<ToolAssetContent> AssetAsync(string slug, string name, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var path = $"tools/{Seg(slug)}/assets/" + string.Join('/', name.Split('/').Select(Seg));
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Options.Set(RoGHttpHandler.SkipEtag, true);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is { } length && length > maxBytes)
        {
            throw new ToolBridgeException("value_too_large", "Asset too large.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new ToolBridgeException("value_too_large", "Asset too large.");
            }

            buffer.Write(chunk, 0, read);
        }

        var mime = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "application/octet-stream";
        return new ToolAssetContent(mime, buffer.ToArray());
    }

    private static string DataPath(string slug, string scope, string key) => $"tools/{Seg(slug)}/data/{Seg(scope)}/{Seg(key)}";

    // Punkte allein würde Uri zu „..“-Pfaden auflösen → hier gar nicht erst zulassen (Bridge prüft vorher)
    private static string Seg(string value) =>
        value.Length == 0 || value.Trim('.').Length == 0 ? throw new ToolBridgeException("validation_failed", "Invalid name.") : Uri.EscapeDataString(value);

    private static ToolDataPage Page(JsonNode? root) =>
        new(root?["data"]?.DeepClone() ?? new JsonArray(), root?["meta"]?["truncated"]?.GetValueKind() == JsonValueKind.True);

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        }

        request.Options.Set(RoGHttpHandler.SkipEtag, true);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    private static string? Str(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? ApiValues.Text(v.GetValue<string>()) : null;

    private static int? Int(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var i) ? i : null;

    private static long? Long(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<long>(out var i) ? i : null;
}

/// <summary>Fehler der Bridge mit Code für das Tool (<c>error.code</c>).</summary>
public sealed class ToolBridgeException : Exception
{
    public ToolBridgeException()
    {
        Code = "host_error";
    }

    public ToolBridgeException(string message)
        : base(message)
    {
        Code = "host_error";
    }

    public ToolBridgeException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "host_error";
    }

    public ToolBridgeException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
