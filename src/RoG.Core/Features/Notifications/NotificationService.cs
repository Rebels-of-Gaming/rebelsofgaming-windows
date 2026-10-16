using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Notifications;

/// <summary>Eintrag im Postfach (Glocke). <see cref="Url"/> = Ziel auf der Website (in der App über <c>AppRoute</c>).</summary>
public sealed record NotificationItem(string Id, string? Type, string Title, string? Body, Uri? Url, string Icon, DateTimeOffset? ReadAt, DateTimeOffset? CreatedAt)
{
    public bool IsUnread => ReadAt is null;

    /// <summary>
    /// Aus den Daten des Echtzeit-Ereignisses (<c>{id, type, title, body, url, icon}</c>); <c>null</c>, wenn Pflichtfelder fehlen.
    /// </summary>
    public static NotificationItem? FromRealtime(string json, DateTimeOffset now)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var e = doc.RootElement;
            var id = Str(e, "id");
            var title = Str(e, "title");
            if (id is null || title is null)
            {
                return null;
            }

            return new NotificationItem(id, Str(e, "type"), title, Str(e, "body"), ApiValues.WebUri(Str(e, "url")), Str(e, "icon") ?? "bell", null, now);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}

/// <summary>Postfach: Liste (alle/ungelesen), Zähler, gelesen markieren.</summary>
public sealed class NotificationService(IRoGApiClient client)
{
    public async Task<Page<NotificationItem>> ListAsync(string? cursor = null, bool unreadOnly = false, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_notifications_indexAsync(unreadOnly ? true : null, cursor, 30, cancellationToken).ConfigureAwait(false);
        return new Page<NotificationItem>(
            response.Data.Select(n => new NotificationItem(n.Id, n.Type, n.Title, ApiValues.Text(n.Body), ApiValues.WebUri(n.Url), n.Icon, ApiValues.Time(n.Read_at), ApiValues.Time(n.Created_at))).ToList(),
            response.Meta?.Next_cursor);
    }

    public async Task<int> UnreadCountAsync(CancellationToken cancellationToken = default) =>
        (await client.V1_notifications_unreadAsync(cancellationToken).ConfigureAwait(false)).Data.Unread;

    public async Task MarkReadAsync(string id, CancellationToken cancellationToken = default) =>
        await client.V1_notifications_readAsync(id, cancellationToken).ConfigureAwait(false);

    public async Task MarkAllReadAsync(CancellationToken cancellationToken = default) =>
        await client.V1_notifications_readAllAsync(cancellationToken).ConfigureAwait(false);
}
