using RoG.Core.Api;
using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Moderation;

public sealed record ModPerson(string Id, string Name, string? Slug);

public sealed record ModReport(
    string Id,
    string Type,
    string Reason,
    string? Comment,
    string? Excerpt,
    string Status,
    string? Resolution,
    ModPerson? Target,
    string? Reporter,
    DateTimeOffset? CreatedAt);

public sealed record ModWarning(string Id, string Reason, int Points, bool Active, DateTimeOffset? CreatedAt, DateTimeOffset? ExpiresAt);

public sealed record ModBan(string Id, string Scope, string Reason, bool Active, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, ModPerson? User);

public sealed record ModMember(ModPerson User, int ActivePoints, IReadOnlyList<ModWarning> Warnings, IReadOnlyList<ModBan> Bans, bool HasAvatarUpload);

public sealed record ModLogEntry(string Action, string? Actor, string? Target, DateTimeOffset? CreatedAt);

public sealed record WordFilter(string Id, string Pattern, string Action, string? Replacement);

/// <summary>Seite einer Cursor-Liste.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>
/// Moderation (Guide 28): Meldungen, Mitglied ansehen, verwarnen/zurücknehmen, sperren/aufheben, Sperrliste, Mod-Log,
/// Wortfilter; dazu „Inhalt melden“ für alle. Hierarchie und Rechte prüft der Server.
/// </summary>
public sealed class ModerationService(IRoGApiClient client)
{
    /// <summary>Melde-Gründe wie die Website.</summary>
    public static readonly IReadOnlyList<string> ReportReasons = ["spam", "insult", "harassment", "illegal", "cheating", "other"];

    public async Task<Page<ModReport>> ReportsAsync(string status = "open", string? cursor = null, CancellationToken cancellationToken = default)
    {
        var r = await client.V1_moderation_reports_indexAsync(status, null, cursor, 30, cancellationToken).ConfigureAwait(false);
        return new Page<ModReport>(
            r.Data.Select(d => new ModReport(
                d.Id, d.Type, d.Reason, ApiValues.Text(d.Comment), ApiValues.Text(d.Excerpt), d.Status, ApiValues.Text(d.Resolution),
                d.Target is { } t ? new ModPerson(t.Id, t.Name, t.Slug) : null,
                d.Reporter?.Name,
                ApiValues.Time(d.Created_at))).ToList(),
            r.Meta?.Next_cursor);
    }

    public async Task UpdateReportAsync(string reportId, string status, string? resolution, CancellationToken cancellationToken = default) =>
        await client.V1_moderation_reports_updateAsync(reportId, new Body110
        {
            Status = status switch { "resolved" => ReportStatus.Resolved, "dismissed" => ReportStatus.Dismissed, _ => ReportStatus.Open },
            Resolution = ApiValues.Text(resolution),
        }, cancellationToken).ConfigureAwait(false);

    public async Task<ModMember> MemberAsync(string userId, CancellationToken cancellationToken = default)
    {
        var d = (await client.V1_moderation_members_showAsync(userId, cancellationToken).ConfigureAwait(false)).Data;
        var user = new ModPerson(d.User.Id, d.User.Name, d.User.Slug);
        return new ModMember(
            user,
            d.Active_points,
            d.Warnings.Select(w => new ModWarning(w.Id, w.Reason, w.Points, w.Active, ApiValues.Time(w.Created_at), ApiValues.Time(w.Expires_at)))
                .OrderByDescending(w => w.CreatedAt).ToList(),
            d.Bans.Select(b => new ModBan(b.Id, b.Scope, b.Reason, b.Active, ApiValues.Time(b.Starts_at), ApiValues.Time(b.Ends_at), user))
                .OrderByDescending(b => b.StartsAt).ToList(),
            d.User.Has_avatar_upload);
    }

    public async Task WarnAsync(string userId, string reason, int points, int? days, string? reportId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await client.V1_moderation_warnings_storeAsync(userId, new Body111
        {
            Reason = reason.Trim(),
            Points = Math.Clamp(points, 0, 100),
            Days = days is > 0 ? days : null,
            Report_id = reportId,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RevokeWarningAsync(string warningId, CancellationToken cancellationToken = default) =>
        await client.V1_moderation_warnings_revokeAsync(warningId, cancellationToken).ConfigureAwait(false);

    /// <summary>Sperren: <c>site</c> (kein Login, auch Discord/TeamSpeak), <c>forum</c> (Schreibsperre), <c>gameserver</c>.</summary>
    public async Task BanAsync(string userId, string scope, string reason, int? days, bool isPublic, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await client.V1_moderation_bans_storeAsync(userId, new Body112
        {
            Scope = scope switch { "site" => BanScope.Site, "gameserver" => BanScope.Gameserver, _ => BanScope.Forum },
            Reason = reason.Trim(),
            Days = days is > 0 ? days : null,
            Public = isPublic,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task LiftBanAsync(string banId, CancellationToken cancellationToken = default) =>
        await client.V1_moderation_bans_liftAsync(banId, cancellationToken).ConfigureAwait(false);

    public async Task<Page<ModBan>> BansAsync(string? cursor = null, CancellationToken cancellationToken = default)
    {
        var r = await client.V1_moderation_bans_indexAsync(cursor, 30, cancellationToken).ConfigureAwait(false);
        return new Page<ModBan>(
            r.Data.Select(b => new ModBan(b.Id, b.Scope, b.Reason, b.Active, ApiValues.Time(b.Starts_at), ApiValues.Time(b.Ends_at),
                new ModPerson(b.User.Id, b.User.Name, b.User.Slug))).ToList(),
            r.Meta?.Next_cursor);
    }

    public async Task<Page<ModLogEntry>> LogAsync(string? cursor = null, CancellationToken cancellationToken = default)
    {
        var r = await client.V1_moderation_logAsync(null, cursor, 50, cancellationToken).ConfigureAwait(false);
        return new Page<ModLogEntry>(
            r.Data.Select(e => new ModLogEntry(e.Action, e.Actor?.Name, e.Target?.Name, ApiValues.Time(e.Created_at))).ToList(),
            r.Meta?.Next_cursor);
    }

    public async Task<IReadOnlyList<WordFilter>> WordsAsync(CancellationToken cancellationToken = default) =>
        (await client.V1_moderation_words_indexAsync(cancellationToken).ConfigureAwait(false)).Data
            .Select(w => new WordFilter(w.Id, w.Pattern, w.Action, ApiValues.Text(w.Replacement)))
            .OrderBy(w => w.Pattern, StringComparer.CurrentCultureIgnoreCase).ToList();

    public async Task AddWordAsync(string pattern, string action, string? replacement, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        await client.V1_moderation_words_storeAsync(new Body113
        {
            Pattern = pattern.Trim(),
            Action = action switch { "block" => WordAction.Block, "review" => WordAction.Review, _ => WordAction.Replace },
            Replacement = action == "replace" ? ApiValues.Text(replacement) : null,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteWordAsync(string id, CancellationToken cancellationToken = default) =>
        await client.V1_moderation_words_destroyAsync(id, cancellationToken).ConfigureAwait(false);

    /// <summary>Inhalt melden (alle Angemeldeten, Scope <c>reports:write</c>; 10 je Stunde).</summary>
    public async Task ReportAsync(string type, string id, string reason, string? comment, CancellationToken cancellationToken = default) =>
        await client.V1_reports_storeAsync(new Body109
        {
            Type = type switch
            {
                "message" => Body109Type.Message,
                "profile" => Body109Type.Profile,
                "gallery_item" => Body109Type.Gallery_item,
                "news_comment" => Body109Type.News_comment,
                "forum_post" => Body109Type.Forum_post,
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            },
            Id = id,
            Reason = reason switch
            {
                "spam" => ReportReason.Spam,
                "insult" => ReportReason.Insult,
                "harassment" => ReportReason.Harassment,
                "illegal" => ReportReason.Illegal,
                "cheating" => ReportReason.Cheating,
                _ => ReportReason.Other,
            },
            Comment = ApiValues.Text(comment),
        }, cancellationToken).ConfigureAwait(false);
}
