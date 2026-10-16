using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Core.Content;

namespace RoG.Core.Features.Forum;

public sealed record ForumCategory(string Id, string Slug, string Name, string? Description, string? ParentId, string? SquadName, bool Locked, bool CanWrite, int Threads, int Posts, DateTimeOffset? LastPostAt);

public sealed record ForumThreadSummary(
    string Id,
    string Title,
    string? AuthorName,
    bool Pinned,
    bool Locked,
    bool HasPoll,
    bool Unread,
    int Replies,
    int Views,
    DateTimeOffset? LastPostAt,
    string? LastPoster,
    bool AuthorBlocked);

public sealed record PollOption(string Id, string Label, int Votes, bool Mine);

public sealed record ForumPoll(string Id, string Question, bool Multiple, bool Closed, int Voters, IReadOnlyList<PollOption> Options);

public sealed record ForumThread(ForumThreadSummary Summary, string CategoryName, bool CanReply, ForumPoll? Poll);

public sealed record PostReaction(string Key, string Emoji, int Count, bool Mine);

public sealed record ForumPost(
    string Id,
    string? AuthorId,
    string AuthorName,
    string? AuthorSlug,
    bool AuthorBlocked,
    RichDocument Content,
    bool Pending,
    bool Hidden,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? EditedAt,
    IReadOnlyList<PostReaction> Reactions,
    IReadOnlyList<string> Attachments,
    bool CanEdit);

public sealed record ReactionOption(string Key, string Emoji, string Label);

/// <summary>Forum (Guide 09): Bereiche, Themen, Beiträge (BBCode als HTML der API), Antworten, Reaktionen, Umfragen.</summary>
public sealed class ForumService(IRoGApiClient client, Uri webBaseUrl)
{
    public const int MaxBodyLength = 20000;

    private readonly HtmlContentParser _html = new(ContentPolicy.Platform, webBaseUrl);

    public async Task<IReadOnlyList<ForumCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.V1_forum_categories_indexAsync(cancellationToken).ConfigureAwait(false);
        return response.Data.Select(c => new ForumCategory(
            c.Id, c.Slug, c.Name, ApiValues.Text(c.Description), c.Parent_id, c.Squad?.Name, c.Is_locked, c.Access?.Can_write ?? false,
            c.Threads_count, c.Posts_count, ApiValues.Time(c.Last_post_at))).ToList();
    }

    public async Task<Page<ForumThreadSummary>> GetThreadsAsync(string categorySlug, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_forum_threads_indexAsync(categorySlug, cursor, 30, cancellationToken).ConfigureAwait(false);
        return new Page<ForumThreadSummary>(response.Data.Select(t => new ForumThreadSummary(
            t.Id, t.Title, t.Author?.Name, t.Is_pinned, t.Is_locked, t.Has_poll ?? false, t.Unread, t.Replies_count, t.Views,
            ApiValues.Time(t.Last_post_at), t.Last_poster?.Name, t.Author_blocked)).ToList(), response.Meta?.Next_cursor);
    }

    public async Task<ForumThread> GetThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        var t = (await client.V1_forum_threads_showAsync(threadId, cancellationToken).ConfigureAwait(false)).Data;
        var summary = new ForumThreadSummary(t.Id, t.Title, t.Author?.Name, t.Is_pinned, t.Is_locked, t.Has_poll ?? false, t.Unread,
            t.Replies_count, t.Views, ApiValues.Time(t.Last_post_at), t.Last_poster?.Name, t.Author_blocked);
        ForumPoll? poll = t.Poll is { } p
            ? new ForumPoll(p.Id, p.Question, p.Multiple, p.Is_closed, p.Voters, p.Options.Select(o => new PollOption(o.Id, o.Label, o.Votes, o.Mine)).ToList())
            : null;
        var canReply = !t.Is_locked && (t.Category?.Access?.Can_write ?? false);
        return new ForumThread(summary, t.Category?.Name ?? string.Empty, canReply, poll);
    }

    /// <summary>Beiträge, älteste zuerst (Guide 09).</summary>
    public async Task<Page<ForumPost>> GetPostsAsync(string threadId, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_forum_posts_indexAsync(threadId, cursor, 30, cancellationToken).ConfigureAwait(false);
        return new Page<ForumPost>(response.Data.Select(Map).ToList(), response.Meta?.Next_cursor);
    }

    /// <summary>Beitrag einer blockierten Person trotzdem anzeigen (einzeln mit Inhalt).</summary>
    public async Task<ForumPost> GetPostAsync(string postId, CancellationToken cancellationToken = default)
    {
        var p = (await client.V1_forum_posts_showAsync(postId, cancellationToken).ConfigureAwait(false)).Data;
        return Map(System.Text.Json.JsonSerializer.Deserialize<Data116>(System.Text.Json.JsonSerializer.Serialize(p))!);
    }

    /// <summary>Antworten (BBCode); Rückgabe = neuer Beitrag (kann <c>pending</c> sein: Spamschutz).</summary>
    public async Task ReplyAsync(string threadId, string bbcode, CancellationToken cancellationToken = default)
    {
        await client.V1_forum_posts_storeAsync(threadId, new Body34 { Body = CheckBody(bbcode) }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Neues Thema (optional mit Umfrage); Rückgabe = Themen-ID.</summary>
    public async Task<string> CreateThreadAsync(string categorySlug, string title, string bbcode, string? pollQuestion = null, IReadOnlyList<string>? pollOptions = null, bool pollMultiple = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var body = new Body32 { Title = title.Trim(), Body = CheckBody(bbcode) };
        if (!string.IsNullOrWhiteSpace(pollQuestion) && pollOptions is { Count: >= 2 })
        {
            body.Poll = new Poll { Question = pollQuestion.Trim(), Options = pollOptions.Select(o => o.Trim()).Where(o => o.Length > 0).ToList(), Multiple = pollMultiple };
        }

        var created = await client.V1_forum_threads_storeAsync(categorySlug, body, cancellationToken).ConfigureAwait(false);
        return created.Data.Id;
    }

    /// <summary>Reaktion umschalten (Schlüssel aus <see cref="GetReactionsAsync"/>).</summary>
    public async Task ToggleReactionAsync(string postId, string reactionKey, CancellationToken cancellationToken = default) =>
        await client.V1_forum_posts_reactAsync(postId, new Body36 { Reaction = reactionKey }, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<ReactionOption>> GetReactionsAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.V1_forum_reactions_indexAsync("1", cancellationToken).ConfigureAwait(false);
        return response.Data.Where(r => r.Active).OrderBy(r => r.Position).Select(r => new ReactionOption(r.Key, r.Emoji, r.Label)).ToList();
    }

    public async Task VoteAsync(string pollId, IReadOnlyList<string> optionIds, CancellationToken cancellationToken = default) =>
        await client.V1_forum_polls_voteAsync(pollId, new Body38 { Options = optionIds.ToList() }, cancellationToken).ConfigureAwait(false);

    /// <summary>Vorschau: BBCode → Dokument (über die API, gleiche Darstellung wie nach dem Absenden).</summary>
    public async Task<RichDocument> PreviewAsync(string bbcode, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_forum_previewAsync(new Body39 { Body = bbcode }, cancellationToken).ConfigureAwait(false);
        return _html.Parse(response.Data.Html);
    }

    private static string CheckBody(string bbcode)
    {
        ArgumentNullException.ThrowIfNull(bbcode);
        var text = bbcode.Trim();
        if (text.Length == 0 || text.Length > MaxBodyLength)
        {
            throw new ArgumentOutOfRangeException(nameof(bbcode), $"1–{MaxBodyLength} Zeichen");
        }

        return text;
    }

    private ForumPost Map(Data116 p) => new(
        p.Id,
        p.Author?.Id,
        p.Author?.Name ?? "—",
        p.Author?.Slug,
        p.Author_blocked,
        _html.Parse(p.Html),
        p.Status == "pending",
        p.Status == "hidden",
        ApiValues.Time(p.Created_at),
        ApiValues.Time(p.Edited_at),
        p.Reactions?.Select(r => new PostReaction(r.Reaction, r.Emoji ?? r.Reaction, r.Count, r.Mine)).ToList() ?? [],
        p.Attachments?.Select(a => a.Name).ToList() ?? [],
        p.Can_edit);
}
