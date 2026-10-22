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
    DateTimeOffset? CreatedAt,
    Uri? Url = null,
    string? Locale = null,
    int Priority = 0,
    string? Source = null,
    ModGameReport? Game = null)
{
    /// <summary>Meldung per <c>/report</c> im Minecraft-Netzwerk (Website v0.38.0).</summary>
    public bool FromMinecraft => Source == ModerationService.MinecraftSource;

    /// <summary>Kinderschutz-Meldung (Website v0.30.3) – rot gekennzeichnet, steht oben.</summary>
    public bool IsChildSafety => Reason == ModerationService.ChildSafety;
}

/// <summary>Spieler aus einer Spiel-Meldung (auch Gäste ohne RoG-Konto).</summary>
public sealed record ModGamePlayer(string? Uuid, string Name, string? Edition);

/// <summary>
/// <c>details</c> einer Meldung aus dem Minecraft-Netzwerk: beide Spieler, Server des Plugin-Schlüssels, Netzwerk-Server.
/// Der Verlauf steht als Text in <c>excerpt</c> – vom Minecraft-Server übermittelt, nicht serverseitig geprüft.
/// </summary>
public sealed record ModGameReport(ModGamePlayer? Reporter, ModGamePlayer? Reported, string? ServerName, string? NetworkServer, int MessageCount);

public sealed record ModWarning(string Id, string Reason, int Points, bool Active, DateTimeOffset? CreatedAt, DateTimeOffset? ExpiresAt);

public sealed record ModBan(string Id, string Scope, string Reason, bool Active, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, ModPerson? User);

public sealed record ModMember(ModPerson User, int ActivePoints, IReadOnlyList<ModWarning> Warnings, IReadOnlyList<ModBan> Bans, bool HasAvatarUpload);

public sealed record ModLogEntry(string Action, string? Actor, string? Target, DateTimeOffset? CreatedAt);

public sealed record WordFilter(string Id, string Pattern, string Action, string? Replacement);

/// <summary>
/// Moderation (Guide 28): Meldungen, Mitglied ansehen, verwarnen/zurücknehmen, sperren/aufheben, Sperrliste, Mod-Log,
/// Wortfilter; dazu „Inhalt melden“ für alle. Hierarchie und Rechte prüft der Server.
/// </summary>
public sealed class ModerationService(IRoGApiClient client)
{
    public const string ChildSafety = "child_safety";

    /// <summary>Melde-Gründe wie die Website – Kinderschutz immer zuerst (v0.30.3).</summary>
    public const string MinecraftSource = "minecraft";

    /// <summary>Herkunft einer Meldung (<c>filter[source]</c>).</summary>
    public static readonly IReadOnlyList<string> ReportSources = ["website", MinecraftSource];

    public static readonly IReadOnlyList<string> ReportReasons = [ChildSafety, "spam", "insult", "harassment", "illegal", "cheating", "other"];

    /// <param name="reason">Filter auf einen Grund (<c>filter[reason]</c>), <c>null</c> = alle.</param>
    /// <param name="source">Filter auf die Herkunft (<c>website</c>, <c>minecraft</c>), <c>null</c> = alle.</param>
    public async Task<Page<ModReport>> ReportsAsync(string status = "open", string? cursor = null, string? reason = null, string? source = null, CancellationToken cancellationToken = default)
    {
        var r = await client.V1_moderation_reports_indexAsync(
            status, filtertype: null, filterreason: reason, filtersource: source is not null && ReportSources.Contains(source) ? source : null,
            cursor: cursor, per_page: 30, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new Page<ModReport>(
            r.Data.Select(d => new ModReport(
                d.Id, d.Type, d.Reason, ApiValues.Text(d.Comment), ApiValues.Text(d.Excerpt), d.Status, ApiValues.Text(d.Resolution),
                d.Target is { } t ? new ModPerson(t.Id, t.Name, t.Slug) : null,
                d.Reporter?.Name,
                ApiValues.Time(d.Created_at),
                ApiValues.WebUri(d.Url),
                ApiValues.Text(d.Locale),
                d.Priority,
                ApiValues.Text(d.Source),
                d.Details is { } g
                    ? new ModGameReport(
                        Player(g.Reporter?.Uuid, g.Reporter?.Name, g.Reporter?.Edition),
                        Player(g.Reported?.Uuid, g.Reported?.Name, g.Reported?.Edition),
                        ApiValues.Text(g.Server?.Name),
                        ApiValues.Text(g.Network_server),
                        g.Messages?.Count ?? 0)
                    : null))
                // Server sortiert schon; stabil nachsortieren, falls eine ältere Website die Priorität nicht kennt
                .OrderByDescending(m => m.Priority > 0 || (m.IsChildSafety && m.Status == "open")).ToList(),
            r.Meta?.Next_cursor);
    }

    private static ModGamePlayer? Player(string? uuid, string? name, string? edition) =>
        ApiValues.Text(name) is { } n ? new ModGamePlayer(ApiValues.Text(uuid), n, ApiValues.Text(edition)) : null;

    public async Task UpdateReportAsync(string reportId, string status, string? resolution, CancellationToken cancellationToken = default) =>
        await client.V1_moderation_reports_updateAsync(reportId, new Body142
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
        await client.V1_moderation_warnings_storeAsync(userId, new Body143
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
        await client.V1_moderation_bans_storeAsync(userId, new Body144
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
        await client.V1_moderation_words_storeAsync(new Body145
        {
            Pattern = pattern.Trim(),
            Action = action switch { "block" => WordAction.Block, "review" => WordAction.Review, _ => WordAction.Replace },
            Replacement = action == "replace" ? ApiValues.Text(replacement) : null,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteWordAsync(string id, CancellationToken cancellationToken = default) =>
        await client.V1_moderation_words_destroyAsync(id, cancellationToken).ConfigureAwait(false);

    /// <summary>Inhalt melden (alle Angemeldeten, Scope <c>reports:write</c>; 10 je Stunde).</summary>
    /// <param name="locale">Bei <c>wiki_page</c>: Sprache der angezeigten Fassung (de/en/fr), sonst ignoriert.</param>
    public async Task ReportAsync(string type, string id, string reason, string? comment, string? locale = null, CancellationToken cancellationToken = default) =>
        await client.V1_reports_storeAsync(new Body141
        {
            Type = type switch
            {
                "message" => Body141Type.Message,
                "profile" => Body141Type.Profile,
                "gallery_item" => Body141Type.Gallery_item,
                "news_comment" => Body141Type.News_comment,
                "forum_post" => Body141Type.Forum_post,
                "wiki_page" => Body141Type.Wiki_page,
                "wiki_revision" => Body141Type.Wiki_revision,
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            },
            Locale = type == "wiki_page" ? ReportLocale(locale) : null,
            Id = id,
            Reason = reason switch
            {
                ChildSafety => ReportReason.Child_safety,
                "spam" => ReportReason.Spam,
                "insult" => ReportReason.Insult,
                "harassment" => ReportReason.Harassment,
                "illegal" => ReportReason.Illegal,
                "cheating" => ReportReason.Cheating,
                _ => ReportReason.Other,
            },
            Comment = ApiValues.Text(comment),
        }, cancellationToken).ConfigureAwait(false);

    private static Body141Locale? ReportLocale(string? locale) => locale?.ToLowerInvariant() switch
    {
        "de" => Body141Locale.De,
        "en" => Body141Locale.En,
        "fr" => Body141Locale.Fr,
        _ => null,
    };
}
