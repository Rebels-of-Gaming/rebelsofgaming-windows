using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using RoG.Core.Api;
using RoG.Core.Features.Account;

namespace RoG.Core.Features.Matches;

public sealed record MatchMapDraft(string Map, int? Own, int? Opponent);

public sealed record MatchPlayerDraft(string UserId, string Slot = "starter", string? Position = null);

public sealed record MatchLinkDraft(string Type, string? Label, string Url);

public sealed record MatchAttachment(string Id, string Name, bool Image, Uri? Url);

/// <summary>Match zum Eintragen/Ändern (Guide 14). <see cref="Report"/> ist der Bericht in der App-Sprache.</summary>
public sealed record MatchDraft(
    string? Id,
    string SquadId,
    string Type,
    string Status,
    string Opponent,
    DateTimeOffset PlayedAt,
    IReadOnlyList<MatchMapDraft> Maps,
    string? OpponentTag = null,
    string? OpponentUrl = null,
    int? ScoreOwn = null,
    int? ScoreOpponent = null,
    bool Published = false,
    string? Report = null,
    IReadOnlyList<MatchPlayerDraft>? Players = null,
    IReadOnlyList<MatchLinkDraft>? Links = null,
    IReadOnlyList<MatchAttachment>? Attachments = null);

/// <summary>
/// Matches eintragen (Recht <c>matches.manage</c> im Squad oder global, Scope <c>matches:write</c>): anlegen, ändern,
/// löschen, Screenshots/Dateien anhängen. <c>report</c> ist in der Spec eine Liste (Lücke W15) – die App sendet selbst.
/// </summary>
public sealed partial class MatchAdminService(HttpClient http)
{
    public static readonly IReadOnlyList<string> Types = ["clanwar", "scrim", "tournament"];

    public static readonly IReadOnlyList<string> Statuses = ["scheduled", "finished", "cancelled"];

    public static readonly IReadOnlyList<string> LinkTypes = ["vod", "demo", "other"];

    public static bool CanManage(UserProfile? me, string squadId) =>
        me is not null && me.HasScope("matches:write") && me.Can("matches.manage", "squad:" + squadId);

    public static bool CanManageAny(UserProfile? me) =>
        me is not null && me.HasScope("matches:write") && me.CanAnywhere("matches.manage");

    /// <summary>Maps aus Zeilen wie <c>supply 2:1</c>, <c>radar 2 - 0</c> oder nur <c>goldrush</c> (ohne Ergebnis).</summary>
    public static IReadOnlyList<MatchMapDraft> ParseMaps(string? text)
    {
        var maps = new List<MatchMapDraft>();
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var m = MapLine().Match(line);
            maps.Add(m.Success
                ? new MatchMapDraft(m.Groups["map"].Value.Trim(), int.Parse(m.Groups["own"].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups["opp"].Value, CultureInfo.InvariantCulture))
                : new MatchMapDraft(line, null, null));
        }

        return maps;
    }

    public static string FormatMaps(IEnumerable<MatchMapDraft> maps) =>
        string.Join("\n", maps.Select(m => m.Own is { } o && m.Opponent is { } p
            ? $"{m.Map} {o.ToString(CultureInfo.InvariantCulture)}:{p.ToString(CultureInfo.InvariantCulture)}"
            : m.Map));

    /// <summary>
    /// Links aus Zeilen: <c>[vod|demo|other] URL [Bezeichnung]</c>; ohne Typ wird Twitch/YouTube zu <c>vod</c>, sonst <c>other</c>.
    /// </summary>
    public static IReadOnlyList<MatchLinkDraft> ParseLinks(string? text)
    {
        var links = new List<MatchLinkDraft>();
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var parts = raw.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var typed = LinkTypes.Contains(parts[0].ToLowerInvariant());
            var url = typed ? (parts.Length > 1 ? parts[1] : string.Empty) : parts[0];
            var label = typed ? (parts.Length > 2 ? parts[2] : null) : (parts.Length > 1 ? string.Join(' ', parts[1..]) : null);
            var type = typed ? parts[0].ToLowerInvariant()
                : Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Host.EndsWith("twitch.tv", StringComparison.OrdinalIgnoreCase) || u.Host.Contains("youtu", StringComparison.OrdinalIgnoreCase)) ? "vod" : "other";
            links.Add(new MatchLinkDraft(type, label, url));
        }

        return links;
    }

    public static string FormatLinks(IEnumerable<MatchLinkDraft> links) =>
        string.Join("\n", links.Select(l => $"{l.Type} {l.Url}{(string.IsNullOrEmpty(l.Label) ? string.Empty : " " + l.Label)}"));

    /// <summary>Prüft wie die Website: Gegner, Maps (max. 15, Name max. 60), Links nur http(s).</summary>
    public static IReadOnlyList<string> Validate(MatchDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(draft.SquadId))
        {
            errors.Add("squad");
        }

        if (string.IsNullOrWhiteSpace(draft.Opponent) || draft.Opponent.Trim().Length > 120)
        {
            errors.Add("opponent");
        }

        if (draft.Maps.Count > 15 || draft.Maps.Any(m => m.Map.Length is 0 or > 60))
        {
            errors.Add("maps");
        }

        if ((draft.Links ?? []).Any(l => !Uri.TryCreate(l.Url, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)))
        {
            errors.Add("links");
        }

        if (draft.OpponentUrl is { Length: > 0 } url && !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            errors.Add("opponent_url");
        }

        return errors;
    }

    /// <summary>Body für <c>POST /v1/matches</c> bzw. <c>PATCH …/{match}</c>. Spieler nur, wenn gesetzt (sonst bleibt das Line-up).</summary>
    public static Dictionary<string, object?> BuildBody(MatchDraft draft, string locale)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["squad_id"] = draft.SquadId,
            ["type"] = draft.Type,
            ["status"] = draft.Status,
            ["opponent"] = draft.Opponent.Trim(),
            ["opponent_tag"] = ApiValues.Text(draft.OpponentTag),
            ["opponent_url"] = ApiValues.Text(draft.OpponentUrl),
            ["played_at"] = draft.PlayedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            ["published"] = draft.Published,
            ["score_own"] = draft.ScoreOwn,
            ["score_opponent"] = draft.ScoreOpponent,
            ["maps"] = draft.Maps.Select(m => new Dictionary<string, object?> { ["map"] = m.Map, ["score_own"] = m.Own, ["score_opponent"] = m.Opponent }).ToList(),
            ["links"] = (draft.Links ?? []).Select(l => new Dictionary<string, object?> { ["type"] = l.Type, ["label"] = ApiValues.Text(l.Label), ["url"] = l.Url.Trim() }).ToList(),
        };
        if (draft.Report is not null)
        {
            body["report"] = new Dictionary<string, string> { [locale] = draft.Report.Trim() };
        }

        if (draft.Players is { } players)
        {
            body["players"] = players.DistinctBy(p => p.UserId)
                .Select(p => new Dictionary<string, object?> { ["user_id"] = p.UserId, ["slot"] = p.Slot, ["position"] = ApiValues.Text(p.Position) }).ToList();
        }

        return body;
    }

    public async Task<MatchDraft> GetAsync(string match, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(new Uri("matches/" + Uri.EscapeDataString(match), UriKind.Relative), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Parse(doc.RootElement);
    }

    internal static MatchDraft Parse(JsonElement root)
    {
        var d = root.TryGetProperty("data", out var data) ? data : root;
        static string? S(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        static int? I(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i) ? i : null;
        static JsonElement O(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) ? p : default;
        static IEnumerable<JsonElement> A(JsonElement o, string n) => O(o, n) is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray() : [];

        var maps = A(d, "maps").OrderBy(m => I(m, "position") ?? 0)
            .Select(m => new MatchMapDraft(S(m, "map") ?? string.Empty, I(O(m, "score"), "own"), I(O(m, "score"), "opponent"))).ToList();
        var players = A(d, "lineup").Select(p => new MatchPlayerDraft(S(O(p, "member"), "id") ?? string.Empty, S(p, "slot") ?? "starter", S(p, "position")))
            .Where(p => p.UserId.Length > 0).ToList();
        var links = A(d, "links").Select(l => new MatchLinkDraft(S(l, "type") ?? "other", S(l, "label"), S(l, "url") ?? string.Empty)).Where(l => l.Url.Length > 0).ToList();
        var attachments = A(d, "attachments")
            .Select(a => new MatchAttachment(S(a, "id") ?? string.Empty, S(a, "name") ?? string.Empty, O(a, "image").ValueKind == JsonValueKind.True, ApiValues.WebUri(S(a, "url"))))
            .Where(a => a.Id.Length > 0).ToList();
        var score = O(d, "score");
        return new MatchDraft(
            S(d, "id"),
            S(O(d, "squad"), "id") ?? string.Empty,
            S(d, "type") ?? "clanwar",
            S(d, "status") ?? "finished",
            S(O(d, "opponent"), "name") ?? string.Empty,
            ApiValues.Time(S(d, "played_at")) ?? DateTimeOffset.Now,
            maps,
            S(O(d, "opponent"), "tag"),
            S(O(d, "opponent"), "url"),
            maps.Count == 0 ? I(score, "own") : null,
            maps.Count == 0 ? I(score, "opponent") : null,
            O(d, "published").ValueKind == JsonValueKind.True,
            S(d, "report"),
            players,
            links,
            attachments);
    }

    /// <summary>Speichert und gibt die Match-ID zurück.</summary>
    public async Task<string> SaveAsync(MatchDraft draft, string locale, CancellationToken cancellationToken = default)
    {
        var body = BuildBody(draft, locale);
        using var response = draft.Id is { } id
            ? await http.PatchAsJsonAsync(new Uri("matches/" + Uri.EscapeDataString(id), UriKind.Relative), body, cancellationToken).ConfigureAwait(false)
            : await http.PostAsJsonAsync(new Uri("matches", UriKind.Relative), body, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return doc.RootElement.TryGetProperty("data", out var d) && d.TryGetProperty("id", out var i) && i.GetString() is { } s ? s : draft.Id ?? string.Empty;
    }

    public async Task DeleteAsync(string match, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync(new Uri("matches/" + Uri.EscapeDataString(match), UriKind.Relative), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Screenshots/Dateien anhängen (höchstens 5 je Anfrage; Bilder, PDF, ZIP/7z – Server prüft den Inhalt).</summary>
    public async Task AttachAsync(string match, IReadOnlyList<(Stream Content, string Name)> files, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count is 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(files), "1–5 Dateien");
        }

        using var form = new MultipartFormDataContent();
        foreach (var (content, name) in files)
        {
            var part = new StreamContent(content);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(part, "files[]", name);
        }

        using var response = await http.PostAsync(new Uri($"matches/{Uri.EscapeDataString(match)}/attachments", UriKind.Relative), form, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveAttachmentAsync(string match, string attachment, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync(
            new Uri($"matches/{Uri.EscapeDataString(match)}/attachments/{Uri.EscapeDataString(attachment)}", UriKind.Relative), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    [GeneratedRegex(@"^(?<map>.+?)\s+(?<own>\d{1,6})\s*[:\-]\s*(?<opp>\d{1,6})$")]
    private static partial Regex MapLine();
}
