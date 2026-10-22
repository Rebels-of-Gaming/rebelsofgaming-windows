using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RoG.Core.Api;
using RoG.Core.Features.Members;

namespace RoG.Core.Features.Friends;

/// <summary>Die andere Person einer Freundschaft bzw. Anfrage.</summary>
public sealed record FriendUser(string Id, string Slug, string Name, Uri? AvatarUrl);

/// <summary>Freund mit Online-Status (<c>null</c>, wenn die Person ihn nicht freigegeben hat) und „befreundet seit“.</summary>
public sealed record FriendEntry(FriendUser User, Presence? Status, DateTimeOffset? Since);

/// <summary>Offene Anfrage; <see cref="Incoming"/> = an mich, sonst von mir. <c>status: accepted</c> nach Gegenanfrage.</summary>
public sealed record FriendRequest(string Id, bool Incoming, string Status, FriendUser User, DateTimeOffset? CreatedAt)
{
    public bool IsAccepted => Status == FriendsService.Accepted;
}

/// <summary>Eine Seite der Freundesliste mit Gesamtzahl und Höchstzahl je Konto.</summary>
public sealed record FriendPage(IReadOnlyList<FriendEntry> Items, string? NextCursor, int? Total, int? Limit)
{
    public bool HasMore => !string.IsNullOrEmpty(NextCursor);
}

/// <summary>
/// Freunde (Website v0.38.0, Guide 45): eigene Liste und offene Anfragen (<c>friends:read</c>), anfragen, annehmen,
/// ablehnen, zurückziehen, entfernen (<c>friends:write</c>). Gegenseitig, privat (nur die eigene Liste), Grenzen und
/// Sperren prüft der Server (<c>409 already_friends</c>, <c>request_cooldown</c>, <c>422 friend_limit_reached</c> …).
/// Direkt über <c>/v1/</c> ohne ETag-Cache, damit die Liste nach einer Aktion bzw. <c>.friends.changed</c> sofort stimmt.
/// </summary>
public sealed partial class FriendsService(HttpClient http)
{
    public const string ReadScope = "friends:read";
    public const string WriteScope = "friends:write";

    public const string Incoming = "incoming";
    public const string Outgoing = "outgoing";
    public const string Accepted = "accepted";

    public async Task<FriendPage> FriendsAsync(string? cursor = null, CancellationToken cancellationToken = default)
    {
        var root = await SendAsync(HttpMethod.Get, "me/friends?" + Query(null, cursor), null, cancellationToken).ConfigureAwait(false);
        var items = root?["data"] is JsonArray list ? list.Select(ParseFriend).OfType<FriendEntry>().ToList() : [];
        var meta = root?["meta"];
        return new FriendPage(items, Str(meta, "next_cursor"), Int(meta, "total"), Int(meta, "limit"));
    }

    /// <summary>Offene Anfragen, neueste zuerst; <paramref name="direction"/> <c>incoming</c>/<c>outgoing</c> oder beide.</summary>
    public async Task<Page<FriendRequest>> RequestsAsync(string? direction = null, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var root = await SendAsync(HttpMethod.Get, "me/friends/requests?" + Query(direction, cursor), null, cancellationToken).ConfigureAwait(false);
        var items = root?["data"] is JsonArray list ? list.Select(ParseRequest).OfType<FriendRequest>().ToList() : [];
        return new Page<FriendRequest>(items, Str(root?["meta"], "next_cursor"));
    }

    /// <summary>Anfrage an Konto-ID oder Slug; hatte die Person schon angefragt, ist die Antwort deren angenommene Anfrage.</summary>
    public async Task<FriendRequest> SendAsync(string user, CancellationToken cancellationToken = default)
    {
        var target = NormalizeUser(user) ?? throw new ArgumentException("Invalid user.", nameof(user));
        var data = (await SendAsync(HttpMethod.Post, "me/friends/requests", new JsonObject { ["user"] = target }, cancellationToken).ConfigureAwait(false))?["data"];
        return ParseRequest(data) ?? throw new InvalidDataException("Friend request without data.");
    }

    public async Task<FriendEntry> AcceptAsync(string requestId, CancellationToken cancellationToken = default)
    {
        var data = (await SendAsync(HttpMethod.Post, $"me/friends/requests/{Id(requestId)}/accept", null, cancellationToken).ConfigureAwait(false))?["data"];
        return ParseFriend(data) ?? throw new InvalidDataException("Accepted friend without data.");
    }

    /// <summary>Ablehnen – die andere Person erfährt nichts, kann aber 7 Tage nicht erneut anfragen.</summary>
    public async Task DeclineAsync(string requestId, CancellationToken cancellationToken = default) =>
        await SendAsync(HttpMethod.Post, $"me/friends/requests/{Id(requestId)}/decline", null, cancellationToken).ConfigureAwait(false);

    /// <summary>Eigene Anfrage zurückziehen.</summary>
    public async Task CancelAsync(string requestId, CancellationToken cancellationToken = default) =>
        await SendAsync(HttpMethod.Delete, "me/friends/requests/" + Id(requestId), null, cancellationToken).ConfigureAwait(false);

    /// <summary>Freundschaft beenden (für beide, ohne Benachrichtigung).</summary>
    public async Task RemoveAsync(string user, CancellationToken cancellationToken = default)
    {
        var target = NormalizeUser(user) ?? throw new ArgumentException("Invalid user.", nameof(user));
        await SendAsync(HttpMethod.Delete, "me/friends/" + Uri.EscapeDataString(target), null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Konto-ID oder Slug (auch aus einem Profil-Link <c>…/u/slug</c> oder mit <c>@</c>); höchstens 64 Zeichen.</summary>
    public static string? NormalizeUser(string? input)
    {
        var v = input?.Trim();
        if (string.IsNullOrEmpty(v))
        {
            return null;
        }

        if (Uri.TryCreate(v, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp))
        {
            var parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var i = Array.FindIndex(parts, p => p is "u" or "members");
            v = i >= 0 && i + 1 < parts.Length ? parts[i + 1] : null;
        }

        v = v?.TrimStart('@');
        return v is { Length: > 0 and <= 64 } && UserPattern().IsMatch(v) ? v : null;
    }

    public static FriendEntry? ParseFriend(JsonNode? n) =>
        n is JsonObject && ParseUser(n["user"]) is { } user
            ? new FriendEntry(user, Str(n, "status") is { } s ? MemberService.ParsePresence(s) : null, ApiValues.Time(Str(n, "since")))
            : null;

    public static FriendRequest? ParseRequest(JsonNode? n) =>
        n is JsonObject && Str(n, "id") is { } id && ParseUser(n["user"]) is { } user
            ? new FriendRequest(id, Str(n, "direction") != Outgoing, Str(n, "status") ?? "pending", user, ApiValues.Time(Str(n, "created_at")))
            : null;

    private static FriendUser? ParseUser(JsonNode? n) =>
        Str(n, "id") is { } id && Str(n, "slug") is { } slug
            ? new FriendUser(id, slug, Str(n, "name") ?? slug, ApiValues.WebUri(Str(n, "avatar_url")))
            : null;

    private static string Query(string? direction, string? cursor)
    {
        var query = new List<string> { "per_page=100" };
        if (direction is Incoming or Outgoing)
        {
            query.Add("filter%5Bdirection%5D=" + direction);
        }

        if (!string.IsNullOrEmpty(cursor))
        {
            query.Add("cursor=" + Uri.EscapeDataString(cursor));
        }

        return string.Join('&', query);
    }

    private static string Id(string id) =>
        UlidPattern().IsMatch(id) ? id : throw new ArgumentException("Invalid id.", nameof(id));

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
        n is JsonObject o && o[name] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;

    [GeneratedRegex("^[A-Za-z0-9_.-]+$")]
    private static partial Regex UserPattern();

    [GeneratedRegex("^[0-9A-HJKMNP-TV-Za-hjkmnp-tv-z]{26}$")]
    private static partial Regex UlidPattern();
}
