using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoG.Core.Api;

namespace RoG.Core.Features.Social;

/// <summary>Quelle eines Social-Media-Posts auf der Website: News, Termin, Clanwar, Galerie-Album.</summary>
public static class SocialSources
{
    public const string News = "news";
    public const string Event = "event";
    public const string Match = "match";

    /// <summary>Bei der Galerie ist die ID die Album-ID (Posts entstehen je Album und Tag).</summary>
    public const string Gallery = "gallery";
}

/// <summary>Symbol-Art für Listen: fertig, teilweise, fehlgeschlagen, läuft/geplant, Entwurf – oder keins.</summary>
public enum SocialBadge
{
    None,
    Posted,
    Partial,
    Failed,
    Pending,
    Draft,
}

public static class SocialStates
{
    /// <summary>Zustand → Symbol; verworfen/zurückgezogen/nichts zeigt keins (bewusst entfernt, kein Fehler).</summary>
    public static SocialBadge Badge(string? state) => state switch
    {
        "published" => SocialBadge.Posted,
        "partial" => SocialBadge.Partial,
        "failed" => SocialBadge.Failed,
        "scheduled" or "publishing" => SocialBadge.Pending,
        "draft" => SocialBadge.Draft,
        _ => SocialBadge.None,
    };
}

/// <summary>Ein Kanal (Ziel) des neuesten Posts einer Quelle.</summary>
public sealed record SocialTarget(
    string Id,
    string ChannelName,
    string? ProviderLabel,
    string Status,
    string? SkipReason,
    DateTimeOffset? PublishedAt,
    Uri? RemoteUrl,
    string? Error,
    bool Retryable);

/// <summary>
/// Social-Media-Status einer Quelle (<c>GET /v1/social/sources/{source}/{id}</c>). <see cref="State"/>: none · draft ·
/// scheduled · publishing · published · partial · failed · retracted · cancelled.
/// </summary>
public sealed record SocialSourceStatus(
    string Source,
    string SourceId,
    string State,
    bool Eligible,
    bool CanRedraft,
    int UsableChannels,
    bool AnyChannel,
    string? PostId,
    string? PostStatus,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<SocialTarget> Targets,
    int HistoryCount)
{
    /// <summary>Mindestens ein Kanal lässt sich erneut senden.</summary>
    public bool CanRetryFailed => PostId is not null && Targets.Any(t => t.Retryable);

    /// <summary>
    /// Der Inhalt steht schon auf mindestens einem Kanal – ein neuer Entwurf lässt diese Kanäle weg, außer man will
    /// bewusst erneut posten (<c>repost</c>, mit Rückfrage).
    /// </summary>
    public bool AlreadyPosted => State is "published" or "partial" || Targets.Any(t => t.Status == "published");

    /// <summary>„Neu vorbereiten“ ohne Rückfrage: Server erlaubt es, und es fehlen noch Kanäle.</summary>
    public bool CanRedraftMissing => CanRedraft && State != "published";
}

/// <summary>Ergebnis von „alle fehlgeschlagenen erneut senden“.</summary>
public sealed record SocialRetryResult(int Count, IReadOnlyList<(string Name, string Reason)> Unusable);

/// <summary>
/// Social-Media-Status an der Quelle und Aktionen dazu (Website v0.30.0): einzelnen Kanal oder alle fehlgeschlagenen
/// erneut senden, neu vorbereiten (neuer Entwurf, Freigabe bleibt Pflicht). Recht <c>social.publish</c>,
/// Scopes <c>social:read</c> bzw. <c>social:write</c> – die Antwort des Servers entscheidet.
/// </summary>
public sealed class SocialSourceService(HttpClient http)
{
    public const string ReadScope = "social:read";
    public const string WriteScope = "social:write";
    public const string Permission = "social.publish";

    public async Task<SocialSourceStatus> GetAsync(string source, string id, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"social/sources/{Uri.EscapeDataString(source)}/{Uri.EscapeDataString(id)}", null, cancellationToken).ConfigureAwait(false);
        return Parse(doc.RootElement.GetProperty("data"));
    }

    public static SocialSourceStatus Parse(JsonElement d)
    {
        var post = d.TryGetProperty("post", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
        var targets = new List<SocialTarget>();
        if (post.ValueKind == JsonValueKind.Object && post.TryGetProperty("targets", out var ts) && ts.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in ts.EnumerateArray())
            {
                var channel = t.TryGetProperty("channel", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
                targets.Add(new SocialTarget(
                    Str(t, "id") ?? string.Empty,
                    Str(channel, "name") ?? Str(t, "channel_id") ?? "—",
                    Str(channel, "provider_label") ?? Str(channel, "provider"),
                    Str(t, "status") ?? "pending",
                    Str(t, "skip_reason"),
                    Time(t, "published_at"),
                    ApiValues.WebUri(Str(t, "remote_url")),
                    Str(t, "error"),
                    Bool(t, "retryable")));
            }
        }

        var channels = d.TryGetProperty("channels", out var ch) && ch.ValueKind == JsonValueKind.Object ? ch : default;
        return new SocialSourceStatus(
            Str(d, "source") ?? string.Empty,
            Str(d, "source_id") ?? string.Empty,
            Str(d, "state") ?? "none",
            Bool(d, "eligible"),
            Bool(d, "can_redraft"),
            channels.ValueKind == JsonValueKind.Object && channels.TryGetProperty("usable", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetInt32() : 0,
            Bool(channels, "any"),
            Str(post, "id"),
            Str(post, "status"),
            Time(post, "scheduled_at"),
            Time(post, "completed_at"),
            targets,
            d.TryGetProperty("history", out var h) && h.ValueKind == JsonValueKind.Array ? h.GetArrayLength() : 0);
    }

    /// <summary>Einen Kanal erneut senden (failed bzw. skipped mit änderbarem Grund, nur freigegebene Posts).</summary>
    public async Task RetryTargetAsync(string postId, string targetId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"social/posts/{Uri.EscapeDataString(postId)}/targets/{Uri.EscapeDataString(targetId)}/retry", null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SocialRetryResult> RetryFailedAsync(string postId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Post, $"social/posts/{Uri.EscapeDataString(postId)}/retry-failed", null, cancellationToken).ConfigureAwait(false);
        var d = doc.RootElement.TryGetProperty("data", out var x) ? x : default;
        var unusable = new List<(string, string)>();
        if (d.ValueKind == JsonValueKind.Object && d.TryGetProperty("unusable", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            unusable.AddRange(list.EnumerateArray().Select(e => (Str(e, "name") ?? "—", Str(e, "reason") ?? "unavailable")));
        }

        return new SocialRetryResult(d.ValueKind == JsonValueKind.Object && d.TryGetProperty("count", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 0, unusable);
    }

    /// <summary>Neuen Entwurf aus der Quelle anlegen (bzw. offenen zurückbekommen). Liefert die Post-ID.</summary>
    /// <param name="repost">Auch Kanäle, auf denen der Inhalt schon steht (neuer Beitrag) – nur nach Rückfrage.</param>
    public async Task<string?> RedraftAsync(string source, string id, bool repost = false, CancellationToken cancellationToken = default)
    {
        var body = repost ? new JsonObject { ["repost"] = true } : new JsonObject();
        using var doc = await SendAsync(HttpMethod.Post, $"social/sources/{Uri.EscapeDataString(source)}/{Uri.EscapeDataString(id)}/redraft", body, cancellationToken).ConfigureAwait(false);
        return doc.RootElement.TryGetProperty("data", out var d) ? Str(d, "id") : null;
    }

    /// <summary>Höchstens so viele IDs je Sammelabfrage (Website: 1–100).</summary>
    public const int MaxBatch = 100;

    /// <summary>
    /// Zustand je Quell-ID für Listen (z. B. News) über <c>GET /v1/social/sources/{source}?ids[]=…</c> (W22), in
    /// Blöcken zu 100. Der Zustand kommt fertig vom Server; <c>none</c> (nie gepostet, unbekannte ID) fehlt im Ergebnis.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> StatesAsync(string source, IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var states = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var chunk in ids.Where(i => !string.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).Chunk(MaxBatch))
        {
            var query = string.Join("&", chunk.Select(i => "ids%5B%5D=" + Uri.EscapeDataString(i)));
            using var doc = await SendAsync(HttpMethod.Get, $"social/sources/{Uri.EscapeDataString(source)}?{query}", null, cancellationToken).ConfigureAwait(false);
            foreach (var (id, state) in ParseStates(doc.RootElement))
            {
                states[id] = state;
            }
        }

        return states;
    }

    public static IReadOnlyDictionary<string, string> ParseStates(JsonElement root)
    {
        var states = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return states;
        }

        foreach (var item in data.EnumerateArray())
        {
            if (Str(item, "id") is { } id && Str(item, "state") is { } state && state != "none")
            {
                states[id] = state;
            }
        }

        return states;
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        }

        // Status soll nach einer Aktion sofort stimmen
        request.Options.Set(RoGHttpHandler.SkipEtag, true);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? ApiValues.Text(v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText())
            : null;

    private static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? Time(JsonElement e, string name) =>
        Str(e, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;
}
