using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Messages;

public enum ConversationKind
{
    Direct,
    Group,
    Squad,
}

public sealed record ConversationSummary(
    string Id,
    ConversationKind Kind,
    string Name,
    Uri? Avatar,
    bool Unread,
    DateTimeOffset? LastMessageAt,
    string? LastMessagePreview,
    string? LastMessageAuthor);

public sealed record ChatAttachment(string Id, string Name, string Mime, long Size, bool IsImage);

public sealed record ChatMessage(
    string Id,
    string ConversationId,
    string? AuthorId,
    string AuthorName,
    string? Body,
    bool Deleted,
    string? QuoteAuthor,
    string? QuoteBody,
    IReadOnlyList<ChatAttachment> Attachments,
    DateTimeOffset? CreatedAt);

/// <summary>Direktnachrichten (Guide 11): Unterhaltungen, Verlauf, Senden, gelesen markieren, 1:1 starten.</summary>
public sealed class MessageService(IRoGApiClient client)
{
    public const int MaxLength = 5000;

    public async Task<Page<ConversationSummary>> ListAsync(string? search = null, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_conversations_indexAsync(
            cursor: cursor,
            per_page: 50,
            filtersearch: string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return new Page<ConversationSummary>(response.Data.Where(c => !c.Archived).Select(c => new ConversationSummary(
            c.Id,
            ParseKind(c.Type),
            c.Partner?.Name ?? c.Name,
            ApiValues.WebUri(c.Partner?.Avatar_url),
            c.Unread,
            ApiValues.Time(c.Last_message_at),
            c.Last_message is { Deleted: false } m ? ApiValues.Text(m.Body) : null,
            c.Last_message?.Author?.Name)).ToList(), response.Meta?.Next_cursor);
    }

    /// <summary>Verlauf, älteste zuerst (die API liefert neueste zuerst); <see cref="Page{T}.NextCursor"/> = ältere Nachrichten.</summary>
    public async Task<Page<ChatMessage>> GetMessagesAsync(string conversationId, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_conversations_messagesAsync(conversationId, cursor, 50, cancellationToken).ConfigureAwait(false);
        var items = response.Data.Select(Map).Reverse().ToList();
        return new Page<ChatMessage>(items, response.Meta?.Next_cursor);
    }

    public async Task<ChatMessage> SendAsync(string conversationId, string body, string? quoteId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        var text = body.Trim();
        if (text.Length == 0 || text.Length > MaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(body), $"1–{MaxLength} Zeichen");
        }

        var response = await client.V1_conversations_sendAsync(conversationId, new Body130 { Body = text, Quote_id = quoteId }, cancellationToken).ConfigureAwait(false);
        // Antwort hat dieselbe Form wie ein Verlaufseintrag
        return MapJson(JsonSerializer.SerializeToElement(response.Data)) ?? throw new InvalidOperationException("Leere Antwort");
    }

    /// <summary>Gelesen markieren; Rückgabe = neue Zahl der Unterhaltungen mit Ungelesenem.</summary>
    public async Task<int> MarkReadAsync(string conversationId, CancellationToken cancellationToken = default) =>
        (await client.V1_conversations_readAsync(conversationId, cancellationToken).ConfigureAwait(false)).Data.Unread;

    public async Task<int> UnreadCountAsync(CancellationToken cancellationToken = default) =>
        (await client.V1_messages_unreadAsync(cancellationToken).ConfigureAwait(false)).Data.Unread;

    /// <summary>1:1-Unterhaltung mit einem Mitglied öffnen (bestehende wird wiederverwendet); Rückgabe = Unterhaltungs-ID.</summary>
    public async Task<string> StartDirectAsync(string userId, CancellationToken cancellationToken = default) =>
        (await client.V1_conversations_storeAsync(new Body129 { User = userId }, cancellationToken).ConfigureAwait(false)).Data.Id;

    /// <summary>Nachricht aus dem Echtzeit-Ereignis <c>.message.sent</c> (gleiche Form wie der Verlauf).</summary>
    public static ChatMessage? FromRealtime(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return MapJson(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static ConversationKind ParseKind(string? type) => type switch
    {
        "group" => ConversationKind.Group,
        "squad" => ConversationKind.Squad,
        _ => ConversationKind.Direct,
    };

    private static ChatMessage Map(Data321 m) => new(
        m.Id,
        m.Conversation_id,
        m.Author?.Id,
        m.Author?.Name ?? "—",
        m.Deleted ? null : m.Body,
        m.Deleted,
        m.Quote?.Author,
        m.Quote?.Body,
        m.Attachments?.Select(a => new ChatAttachment(a.Id, a.Name, a.Mime, a.Size, a.Image)).ToList() ?? [],
        ApiValues.Time(m.Created_at));

    private static ChatMessage? MapJson(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        static string? S(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        var author = e.TryGetProperty("author", out var a) ? a : default;
        var quote = e.TryGetProperty("quote", out var q) ? q : default;
        var deleted = e.TryGetProperty("deleted", out var d) && d.ValueKind == JsonValueKind.True;
        var attachments = new List<ChatAttachment>();
        if (e.TryGetProperty("attachments", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in list.EnumerateArray())
            {
                var size = x.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number ? sz.GetInt64() : 0;
                var image = x.TryGetProperty("image", out var im) && im.ValueKind == JsonValueKind.True;
                if (S(x, "id") is { } aid)
                {
                    attachments.Add(new ChatAttachment(aid, S(x, "name") ?? aid, S(x, "mime") ?? "application/octet-stream", size, image));
                }
            }
        }

        return new ChatMessage(
            id.GetString()!,
            S(e, "conversation_id") ?? string.Empty,
            S(author, "id"),
            S(author, "name") ?? "—",
            deleted ? null : S(e, "body"),
            deleted,
            S(quote, "author"),
            S(quote, "body"),
            attachments,
            ApiValues.Time(S(e, "created_at")));
    }
}
