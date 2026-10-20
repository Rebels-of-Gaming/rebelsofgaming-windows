using RoG.Core.Features.Account;
using RoG.Core.Features.Servers;

namespace RoG.Core.Features.Admin;

/// <summary>
/// Bereiche der Website-Verwaltung, die die App mit dem allgemeinen Editor kann. Verträge aus den API-Controllern der
/// Website (Stand 17.10.2026); was der Editor nicht abbildet (Reihenfolge per Ziehen, verschachtelte Listen wie
/// Regel-Abschnitte, Bilder), öffnet <see cref="AdminArea.WebPath"/> im Browser.
/// Beschriftungen: <c>AdminArea_*</c>, <c>AdminField_*</c>, <c>AdminChoice_*</c> in den .resw-Dateien.
/// </summary>
public static class AdminAreas
{
    public const string GroupClan = "clan";
    public const string GroupContent = "content";
    public const string GroupTech = "tech";

    private static AdminField F(string key, AdminFieldType type, bool required = false, int? max = null, string? read = null, bool createOnly = false, string? reference = null, double? min = null, double? maxValue = null, string? help = null, string? label = null, bool always = false) =>
        new(key, label ?? "AdminField_" + key.Replace('.', '_'), type, required, max, null, read, createOnly, reference, min, maxValue, help, always);

    // prefix: eigene Beschriftungen, wenn ein Wert anderswo etwas anderes heißt (Symbol „flag“ ≠ Aktion „flag“)
    private static AdminField C(string key, IEnumerable<string> values, bool required = false, string? read = null, bool createOnly = false, string? label = null, bool multi = false, string? help = null, string prefix = "AdminChoice_") =>
        new(key, label ?? "AdminField_" + key.Replace('.', '_'), multi ? AdminFieldType.MultiChoice : AdminFieldType.Choice, required, null,
            values.Select(v => new AdminChoice(v, prefix + v.Replace('-', '_'))).ToList(), read, createOnly, HelpKey: help);

    private static readonly string[] Visibility3 = ["public", "members", "squad"];

    // ---- nur als Auswahlquelle für Verweise (keine eigenen Seiten) ----

    private static readonly AdminArea SquadsRef = new("squads", "AdminArea_squads", "squads.manage", "squads:read", "squads:write", "squads", []);

    private static readonly AdminArea MembersRef = new("members", "AdminArea_members", "members.view", "members:read", "members:write", "members", [], Paged: true);

    public static IReadOnlyList<AdminArea> All { get; } =
    [
        // ===== Clan =====
        new("games", "AdminArea_games", "games.manage", "games:read", "games:write", "games",
            [
                F("name", AdminFieldType.Text, required: true, max: 80),
                F("slug", AdminFieldType.Text, max: 80, help: "AdminHelp_slug"),
                F("short", AdminFieldType.Text, required: true, max: 12),
                C("platform", ["pc", "console", "mobile", "crossplay"], required: true),
                F("genre", AdminFieldType.Text, max: 40),
                F("color", AdminFieldType.Color),
                F("emoji", AdminFieldType.Text, max: 16),
                F("channel_prefix", AdminFieldType.Text, max: 24),
                F("clan_tag", AdminFieldType.Text, max: 32),
                F("clan_name", AdminFieldType.Text, max: 80),
                C("id_format", ["steam_id", "guid", "player_tag", "riot_id", "battletag", "uid"]),
                C("stats_source", ["server", "api", "manual", "none"]),
                C("stats_provider", ["supercell_coc", "supercell_bs", "supercell_cr", "faceit", "riot"]),
                F("sort", AdminFieldType.WholeNumber, min: 0, maxValue: 9999),
                F("active", AdminFieldType.Bool),
            ],
            IdField: "slug", SubtitleFields: ["short", "platform"], DetailPath: "games/{id}", CreatePath: "games", UpdatePath: "games/{id}", DeletePath: "games/{id}",
            WebPath: "games", HelpKey: "AdminHelp_games", Group: GroupClan),

        new("event-types", "AdminArea_event_types", "events.types", "events:read", "events:write", "event-types",
            [
                F("key", AdminFieldType.Text, max: 40, createOnly: true, help: "AdminHelp_key"),
                F("name", AdminFieldType.Translated, required: true, max: 40, read: "translations.name"),
                F("color", AdminFieldType.Color, required: true, help: "AdminHelp_contrast"),
                F("emoji", AdminFieldType.Text, max: 16),
                C("default_visibility", Visibility3),
                F("squad_id", AdminFieldType.Reference, read: "squad.id", reference: "squads", help: "AdminHelp_global"),
                F("sort", AdminFieldType.WholeNumber, min: 0, maxValue: 9999),
                F("active", AdminFieldType.Bool),
            ],
            SubtitleFields: ["key", "squad.name"], CreatePath: "event-types", UpdatePath: "event-types/{id}", DeletePath: "event-types/{id}",
            WebPath: "event-types", HelpKey: "AdminHelp_event_types", Group: GroupClan),

        new("badges", "AdminArea_badges", "badges.manage", "members:read", "members:write", "badges",
            [
                F("key", AdminFieldType.Text, max: 64, help: "AdminHelp_key"),
                F("name", AdminFieldType.Translated, required: true, max: 60),
                F("description", AdminFieldType.TranslatedMultiline, max: 255),
                F("icon", AdminFieldType.Text, max: 32, help: "AdminHelp_icon"),
                F("color", AdminFieldType.Color),
                F("sort", AdminFieldType.WholeNumber, min: 0, maxValue: 9999),
            ],
            SubtitleFields: ["key", "type"], CreatePath: "badges", UpdatePath: "badges/{id}", DeletePath: "badges/{id}",
            WebPath: "badges", HelpKey: "AdminHelp_badges", Group: GroupClan),

        new("team-groups", "AdminArea_team_groups", "team.manage", "members:read", "members:write", "team",
            [
                F("name", AdminFieldType.Translated, required: true, max: 60),
                F("description", AdminFieldType.TranslatedMultiline, max: 255),
                F("sort", AdminFieldType.WholeNumber, min: 0, maxValue: 9999, help: "AdminHelp_writeOnly"),
            ],
            SubtitleFields: ["description"], CreatePath: "team-groups", UpdatePath: "team-groups/{id}", DeletePath: "team-groups/{id}",
            WebPath: "team", HelpKey: "AdminHelp_team_groups", Group: GroupClan),

        new("tournaments", "AdminArea_tournaments", "matches.manage", "matches:read", "matches:write", "tournaments",
            [
                F("name", AdminFieldType.Text, required: true, max: 120),
                F("slug", AdminFieldType.Text, max: 120, help: "AdminHelp_slug"),
                F("organizer", AdminFieldType.Text, max: 120),
                C("organizer_type", ["internal", "external"]),
                F("game_id", AdminFieldType.Reference, read: "game.id", reference: "games"),
                F("squad_id", AdminFieldType.Reference, read: "squad.id", reference: "squads"),
                F("starts_on", AdminFieldType.Date, required: true),
                F("ends_on", AdminFieldType.Date),
                F("placement", AdminFieldType.WholeNumber, min: 1, maxValue: 10000),
                F("participants_count", AdminFieldType.WholeNumber, min: 1, maxValue: 10000),
                F("url", AdminFieldType.Url, max: 500),
                F("description", AdminFieldType.TranslatedMultiline, max: 5000),
            ],
            IdField: "slug", SubtitleFields: ["starts_on", "game.short", "squad.name"], DetailPath: "tournaments/{id}", CreatePath: "tournaments",
            UpdatePath: "tournaments/{id}", DeletePath: "tournaments/{id}", WebPath: "tournaments", Group: GroupClan),

        new("trophies", "AdminArea_trophies", "halloffame.manage", "halloffame:read", "halloffame:write", "trophies",
            [
                C("kind", ["trophy", "milestone", "award"], required: true),
                F("title", AdminFieldType.Translated, required: true, max: 160, read: "translations.title"),
                F("description", AdminFieldType.TranslatedMultiline, max: 2000, read: "translations.description"),
                F("achieved_on", AdminFieldType.Date, required: true),
                F("placement", AdminFieldType.WholeNumber, min: 1, maxValue: 9999, help: "AdminHelp_placement"),
                F("squad_id", AdminFieldType.Reference, read: "squad.id", reference: "squads"),
                F("game_id", AdminFieldType.Reference, read: "game.id", reference: "games"),
                F("tournament_id", AdminFieldType.Reference, reference: "tournaments"),
                F("sort", AdminFieldType.WholeNumber, min: 0, maxValue: 9999),
            ],
            TitleField: "title", SubtitleFields: ["kind", "achieved_on"], DetailPath: "trophies/{id}", CreatePath: "trophies", UpdatePath: "trophies/{id}",
            DeletePath: "trophies/{id}", WebPath: "hall-of-fame", Group: GroupClan),

        new("history", "AdminArea_history", "halloffame.manage", "halloffame:read", "halloffame:write", "history",
            [
                F("occurred_on", AdminFieldType.Date, required: true),
                C("precision", ["day", "month", "year"]),
                F("title", AdminFieldType.Translated, required: true, max: 160),
                F("body", AdminFieldType.TranslatedMultiline, max: 5000),
                C("icon", ["flag", "fist", "trophy", "users", "gamepad", "server", "discord", "globe", "sparkles", "crown", "swords", "calendar", "megaphone", "home"], prefix: "AdminIcon_"),
                F("squad_id", AdminFieldType.Reference, reference: "squads"),
                F("highlighted", AdminFieldType.Bool),
                F("sort", AdminFieldType.WholeNumber, min: 0, maxValue: 9999),
            ],
            TitleField: "title", SubtitleFields: ["date_label"], CreatePath: "history", UpdatePath: "history/{id}", DeletePath: "history/{id}",
            WebPath: "hall-of-fame", Group: GroupClan),

        new("partners", "AdminArea_partners", "partners.manage", "cms:read", "cms:write", "partners",
            [
                F("name", AdminFieldType.Text, required: true, max: 120),
                F("url", AdminFieldType.Url, max: 500),
                F("description", AdminFieldType.TranslatedMultiline, max: 1000),
                C("tier", ["main", "partner", "supporter"]),
                F("starts_on", AdminFieldType.Date),
                F("ends_on", AdminFieldType.Date),
                F("active", AdminFieldType.Bool),
                F("sort", AdminFieldType.WholeNumber, min: 0, maxValue: 9999, help: "AdminHelp_writeOnly"),
            ],
            SubtitleFields: ["tier", "ends_on"], CreatePath: "partners", UpdatePath: "partners/{id}", DeletePath: "partners/{id}",
            WebPath: "partners", HelpKey: "AdminHelp_images", Group: GroupClan),

        new("rules", "AdminArea_rules", "rules.manage", "rules:read", "rules:write", "rules",
            [
                C("scope", ["clan", "game", "squad"], required: true, createOnly: true),
                F("game_id", AdminFieldType.Reference, reference: "games", createOnly: true),
                F("squad_id", AdminFieldType.Reference, reference: "squads", createOnly: true),
                F("slug", AdminFieldType.Text, max: 80, help: "AdminHelp_slug"),
                F("title", AdminFieldType.Translated, required: true, max: 120),
                F("intro", AdminFieldType.TranslatedMultiline, max: 2000),
                C("visibility", Visibility3),
                F("requires_acceptance", AdminFieldType.Bool),
                F("enforced", AdminFieldType.Bool),
                F("major", AdminFieldType.Bool, help: "AdminHelp_major"),
                F("note", AdminFieldType.Text, max: 255, help: "AdminHelp_note"),
            ],
            IdField: "slug", TitleField: "title", SubtitleFields: ["scope", "visibility", "version"], DetailPath: "rules/{id}", CreatePath: "rules",
            UpdatePath: "rules/{id}", DeletePath: "rules/{id}", WebPath: "rules", HelpKey: "AdminHelp_rules", Group: GroupClan),

        new("inactivity-rules", "AdminArea_inactivity_rules", "inactivity.manage", "inactivity:read", "inactivity:write", "inactivity/rules",
            [
                F("role_id", AdminFieldType.Reference, read: "role.id", reference: "roles", always: true, help: "AdminHelp_defaultRule"),
                C("sources", ["website", "discord", "discord_voice", "teamspeak", "gameserver"], required: true, multi: true),
                F("warn_days", AdminFieldType.WholeNumber, required: true, min: 1, maxValue: 365),
                F("escalate_days", AdminFieldType.WholeNumber, required: true, min: 1, maxValue: 365),
                C("action", ["none", "flag", "former"], required: true),
                F("action_days", AdminFieldType.WholeNumber, min: 1, maxValue: 730),
                F("enabled", AdminFieldType.Bool),
            ],
            IdField: "id", TitleField: "role.label", SubtitleFields: ["warn_days", "escalate_days", "action"], CreatePath: "inactivity/rules",
            UpdatePath: "inactivity/rules", UpdateMethod: "PUT", FullReplace: true, WebPath: "inactivity", Group: GroupClan),

        new("inactivity-exceptions", "AdminArea_inactivity_exceptions", "inactivity.manage", "inactivity:read", "inactivity:write", "inactivity/exceptions",
            [
                F("user_id", AdminFieldType.Reference, required: true, read: "member.id", reference: "members"),
                F("until", AdminFieldType.Date, help: "AdminHelp_until"),
                F("reason", AdminFieldType.Text, required: true, max: 255),
            ],
            TitleField: "member.name", SubtitleFields: ["until", "reason"], CreatePath: "inactivity/exceptions", DeletePath: "inactivity/exceptions/{id}",
            WebPath: "inactivity", Group: GroupClan),

        new("applications-questions", "AdminArea_applications_questions", "applications.settings", "applications:read", "applications:write", "applications/settings/questions",
            [
                C("type", ["text", "textarea", "select", "number", "yes_no", "url"], required: true),
                F("label", AdminFieldType.Translated, required: true, max: 200),
                F("help", AdminFieldType.TranslatedMultiline, max: 300),
                F("game_id", AdminFieldType.Reference, reference: "games", help: "AdminHelp_allGames"),
                F("required", AdminFieldType.Bool),
                F("active", AdminFieldType.Bool),
                F("position", AdminFieldType.WholeNumber, min: 0, maxValue: 9999),
            ],
            TitleField: "label", SubtitleFields: ["type", "game.name"], CreatePath: "applications/settings/questions",
            UpdatePath: "applications/settings/questions/{id}", DeletePath: "applications/settings/questions/{id}",
            WebPath: "applications/settings", HelpKey: "AdminHelp_questions", Group: GroupClan),

        new("applications-tasks", "AdminArea_applications_tasks", "applications.settings", "applications:read", "applications:write", "applications/settings/tasks",
            [
                F("title", AdminFieldType.Translated, required: true, max: 160),
                F("game_id", AdminFieldType.Reference, reference: "games"),
                F("squad_id", AdminFieldType.Reference, reference: "squads"),
                F("position", AdminFieldType.WholeNumber, min: 0, maxValue: 9999),
            ],
            TitleField: "title", SubtitleFields: ["game.name"], CreatePath: "applications/settings/tasks", DeletePath: "applications/settings/tasks/{id}",
            WebPath: "applications/settings", HelpKey: "AdminHelp_tasks", Group: GroupClan),

        new("registration-invites", "AdminArea_registration_invites", "registration.manage", "registration:manage", "registration:manage", "registration/invites",
            [
                F("note", AdminFieldType.Text, max: 120),
                F("max_uses", AdminFieldType.WholeNumber, min: 1, maxValue: 10000, help: "AdminHelp_unlimited"),
                F("expires_at", AdminFieldType.DateTime),
            ],
            TitleField: "code", SubtitleFields: ["note", "uses", "expires_at"], CreatePath: "registration/invites", DeletePath: "registration/invites/{id}",
            WebPath: "registration", HelpKey: "AdminHelp_invites", Group: GroupClan),

        new("registration-settings", "AdminArea_registration_settings", "registration.manage", "registration:manage", "registration:manage", string.Empty,
            [
                C("mode", ["open", "closed", "invite"]),
                F("min_age", AdminFieldType.WholeNumber, min: 16, maxValue: 99),
                F("blocked_domains", AdminFieldType.Lines, help: "AdminHelp_domains"),
            ],
            DetailPath: "registration/settings", UpdatePath: "registration/settings", UpdateMethod: "PUT", WebPath: "registration", Group: GroupClan),

        new("settings-clan", "AdminArea_settings_clan", "settings.manage", "settings:read", "settings:write", string.Empty,
            [
                F("name", AdminFieldType.Text, max: 80),
                F("short", AdminFieldType.Text, max: 12),
                F("founded", AdminFieldType.WholeNumber, min: 1990, maxValue: 2100),
                F("discord_invite", AdminFieldType.Url),
                F("maintenance.enabled", AdminFieldType.Bool),
                F("maintenance.message", AdminFieldType.TranslatedMultiline, max: 500, help: "AdminHelp_defaultText"),
                F("apps.ios_app_id", AdminFieldType.Text, max: 12),
                F("apps.android_package", AdminFieldType.Text, max: 120),
                F("apps.banner", AdminFieldType.Bool),
            ],
            DetailPath: "settings/clan", UpdatePath: "settings/clan", WebPath: "settings", Group: GroupClan),

        // ===== Inhalte =====
        new("pages", "AdminArea_pages", "cms.pages", "cms:read", "cms:write", "pages",
            [
                F("title", AdminFieldType.Translated, required: true, max: 160, read: "translations.title"),
                F("body", AdminFieldType.TranslatedMultiline, max: 100000, read: "translations.body", help: "AdminHelp_markdown"),
                F("slug", AdminFieldType.Text, max: 80, help: "AdminHelp_slug"),
                C("status", ["draft", "published"]),
                C("visibility", ["public", "members"]),
                F("show_in_footer", AdminFieldType.Bool),
                F("sort", AdminFieldType.WholeNumber, min: 0, maxValue: 9999),
                F("note", AdminFieldType.Text, max: 200, help: "AdminHelp_note"),
            ],
            IdField: "slug", WriteIdField: "id", TitleField: "title", SubtitleFields: ["slug", "status", "visibility"], DetailPath: "pages/{id}",
            CreatePath: "pages", UpdatePath: "pages/{id}", DeletePath: "pages/{id}", WebPath: "pages", Group: GroupContent),

        new("downloads", "AdminArea_downloads", "downloads.manage", "downloads:read", "downloads:write", "downloads",
            [
                F("title", AdminFieldType.Translated, required: true, max: 160),
                F("description", AdminFieldType.TranslatedMultiline, max: 20000, help: "AdminHelp_markdown"),
                F("slug", AdminFieldType.Text, max: 120, help: "AdminHelp_slug"),
                F("category_id", AdminFieldType.Reference, read: "category.slug", reference: "download-categories"),
                F("game_id", AdminFieldType.Reference, read: "game_info.id", reference: "games"),
                C("visibility", Visibility3),
                F("squad_id", AdminFieldType.Reference, reference: "squads"),
                F("sort", AdminFieldType.WholeNumber, min: 0, maxValue: 9999, help: "AdminHelp_writeOnly"),
            ],
            IdField: "slug", WriteIdField: "id", TitleField: "title", SubtitleFields: ["category.name", "visibility"], DetailPath: "downloads/{id}",
            CreatePath: "downloads", UpdatePath: "downloads/{id}", DeletePath: "downloads/{id}", WebPath: "downloads", HelpKey: "AdminHelp_downloads", Group: GroupContent),

        new("download-categories", "AdminArea_download_categories", "downloads.manage", "downloads:read", "downloads:write", "download-categories",
            [
                F("name", AdminFieldType.Translated, required: true, max: 60),
                F("description", AdminFieldType.TranslatedMultiline, max: 500, help: "AdminHelp_writeOnly"),
                F("slug", AdminFieldType.Text, max: 60, help: "AdminHelp_slug"),
                F("icon", AdminFieldType.Text, max: 30, help: "AdminHelp_icon"),
                F("sort", AdminFieldType.WholeNumber, min: 0, maxValue: 9999, help: "AdminHelp_writeOnly"),
            ],
            SubtitleFields: ["slug"], CreatePath: "download-categories", UpdatePath: "download-categories/{id}", WebPath: "downloads", Group: GroupContent),

        new("forum-categories", "AdminArea_forum_categories", "forum.manage", "forum:read", "forum:moderate", "forum/categories",
            [
                F("name", AdminFieldType.Translated, required: true, max: 80),
                F("description", AdminFieldType.TranslatedMultiline, max: 300),
                F("slug", AdminFieldType.Text, max: 80, help: "AdminHelp_slug"),
                F("parent_id", AdminFieldType.Reference, reference: "forum-categories"),
                F("icon", AdminFieldType.Text, max: 32, help: "AdminHelp_icon"),
                F("position", AdminFieldType.WholeNumber, min: 0, maxValue: 9999, help: "AdminHelp_writeOnly"),
                F("is_locked", AdminFieldType.Bool),
            ],
            SubtitleFields: ["slug", "squad.name"], CreatePath: "forum/categories", UpdatePath: "forum/categories/{id}", DeletePath: "forum/categories/{id}",
            WebPath: "forum", HelpKey: "AdminHelp_forum", Group: GroupContent),

        new("gallery-albums", "AdminArea_gallery_albums", "gallery.manage", "gallery:read", "gallery:manage", "gallery/albums",
            [
                F("title", AdminFieldType.Translated, required: true, max: 120),
                F("description", AdminFieldType.TranslatedMultiline, max: 1000),
                F("slug", AdminFieldType.Text, max: 120, help: "AdminHelp_slug"),
                C("visibility", Visibility3),
                F("squad_id", AdminFieldType.Reference, read: "squad", reference: "squads"),
                F("member_uploads", AdminFieldType.Bool),
                F("requires_approval", AdminFieldType.Bool),
                F("sort", AdminFieldType.WholeNumber, min: 0, maxValue: 9999, help: "AdminHelp_writeOnly"),
            ],
            IdField: "slug", WriteIdField: "id", TitleField: "title", SubtitleFields: ["visibility", "squad"], DetailPath: "gallery/albums/{id}",
            CreatePath: "gallery/albums", UpdatePath: "gallery/albums/{id}", DeletePath: "gallery/albums/{id}", WebPath: "gallery", Group: GroupContent),

        new("wiki-namespaces", "AdminArea_wiki_namespaces", "wiki.manage", "wiki:read", "wiki:manage", "wiki/namespaces",
            [
                F("name", AdminFieldType.Translated, required: true, max: 60),
                F("description", AdminFieldType.TranslatedMultiline, max: 500),
                F("slug", AdminFieldType.Text, max: 60, help: "AdminHelp_slug"),
                F("icon", AdminFieldType.Text, max: 30, help: "AdminHelp_icon"),
                F("game_id", AdminFieldType.Reference, reference: "games"),
                F("squad_id", AdminFieldType.Reference, reference: "squads"),
                C("visibility", Visibility3),
                F("requires_review", AdminFieldType.Bool),
                F("sort", AdminFieldType.WholeNumber, min: 0, maxValue: 9999, help: "AdminHelp_writeOnly"),
            ],
            SubtitleFields: ["slug", "visibility"], CreatePath: "wiki/namespaces", UpdatePath: "wiki/namespaces/{id}", WebPath: "wiki", Group: GroupContent),

        new("wiki-templates", "AdminArea_wiki_templates", "wiki.manage", "wiki:read", "wiki:manage", "wiki/templates",
            [
                F("name", AdminFieldType.Text, required: true, max: 60),
                F("label", AdminFieldType.Translated, required: true, max: 80),
                F("color", AdminFieldType.Color),
            ],
            TitleField: "label", SubtitleFields: ["name"], UpdatePath: "wiki/templates/{id}", WebPath: "wiki", HelpKey: "AdminHelp_wikiTemplates", Group: GroupContent),

        new("mail-templates", "AdminArea_mail_templates", "notifications.templates", "notifications:templates", "notifications:templates", "mail-templates",
            [
                F("subject", AdminFieldType.Translated, max: 150, read: "fields.subject"),
                F("intro", AdminFieldType.TranslatedMultiline, max: 1000, read: "fields.intro"),
            ],
            IdField: "key", TitleField: "label", SubtitleFields: ["key"], ItemsPath: "data.templates", UpdatePath: "mail-templates/{id}", UpdateMethod: "PUT",
            FullReplace: true, WebPath: "mail-templates", HelpKey: "AdminHelp_mail", Group: GroupContent),

        new("upload-limits", "AdminArea_upload_limits", "uploads.manage", "uploads:manage", "uploads:manage", "admin/uploads/limits",
            [
                F("max_mb", AdminFieldType.WholeNumber, min: 1, maxValue: 50, help: "AdminHelp_default"),
                F("daily", AdminFieldType.WholeNumber, min: 1, maxValue: 1000, help: "AdminHelp_default"),
                F("quota_mb", AdminFieldType.WholeNumber, min: 1, maxValue: 51200, help: "AdminHelp_default"),
                F("video_max_mb", AdminFieldType.WholeNumber, min: 1, help: "AdminHelp_default"),
                F("video_seconds", AdminFieldType.WholeNumber, min: 1, help: "AdminHelp_default"),
            ],
            IdField: "role", TitleField: "label", SubtitleFields: ["max_mb", "daily", "quota_mb"], UpdatePath: "admin/uploads/limits/{id}", UpdateMethod: "PUT",
            FullReplace: true, WebPath: "uploads", Group: GroupContent),

        // ===== Technik =====
        new("roles", "AdminArea_roles", "roles.manage|roles.view", "roles:read", "roles:write", "roles",
            [
                F("label", AdminFieldType.Translated, required: true, max: 40, read: "labels"),
                F("name", AdminFieldType.Text, max: 64, createOnly: true, help: "AdminHelp_key"),
                C("type", ["rank", "squad", "extra"], required: true),
                F("color", AdminFieldType.Color),
                F("attachment_limit_mb", AdminFieldType.WholeNumber, min: 1, maxValue: 50, help: "AdminHelp_default"),
                F("icon", AdminFieldType.Text, max: 32, help: "AdminHelp_icon"),
                F("sort", AdminFieldType.WholeNumber, min: 1, maxValue: 9999, help: "AdminHelp_roleSort"),
            ],
            TitleField: "label", SubtitleFields: ["name", "type", "sort"], Paged: true, DetailPath: "roles/{id}", CreatePath: "roles", UpdatePath: "roles/{id}",
            DeletePath: "roles/{id}", WebPath: "roles", HelpKey: "AdminHelp_roles", Group: GroupTech),

        new("servers", "AdminArea_servers", "servers.manage", "servers:read", "servers:manage", "servers",
            [
                F("name", AdminFieldType.Text, required: true, max: 80),
                F("slug", AdminFieldType.Text, max: 80, help: "AdminHelp_slug"),
                F("game_id", AdminFieldType.Reference, read: "game.id", reference: "games"),
                F("squad_id", AdminFieldType.Reference, read: "squad.id", reference: "squads"),
                F("description", AdminFieldType.TranslatedMultiline, max: 2000, read: "admin.description_translations"),
                F("host", AdminFieldType.Text, required: true, max: 255, read: "admin.host"),
                F("port", AdminFieldType.WholeNumber, required: true, min: 1, maxValue: 65535, read: "admin.port"),
                F("query_port", AdminFieldType.WholeNumber, min: 1, maxValue: 65535, read: "admin.query_port"),
                C("query_type", ProvisionForm.QueryTypes, required: true),
                F("query_enabled", AdminFieldType.Bool, read: "admin.query_enabled"),
                F("connect_url", AdminFieldType.Text, max: 255, help: "AdminHelp_connect"),
                F("pterodactyl_id", AdminFieldType.Text, max: 64, read: "admin.pterodactyl_id"),
                C("visibility", ["public", "members", "hidden"], required: true),
                F("production", AdminFieldType.Bool, read: "admin.production"),
                F("config_approval", AdminFieldType.Bool, read: "admin.config_approval"),
                F("announce_restarts", AdminFieldType.Bool, read: "admin.announce_restarts"),
                C("join_password_visibility", ["members", "squad", "managers"], read: "admin.join_password.visibility"),
                F("sort", AdminFieldType.WholeNumber, min: 0, maxValue: 65535, read: "admin.sort"),
            ],
            SubtitleFields: ["game.name", "address", "visibility"], Paged: true, DetailPath: "servers/{id}", CreatePath: "servers", UpdatePath: "servers/{id}",
            DeletePath: "servers/{id}", WebPath: "servers", HelpKey: "AdminHelp_servers", Group: GroupTech),
    ];

    /// <summary>Bereiche, die nur als Auswahlquelle dienen.</summary>
    private static readonly IReadOnlyList<AdminArea> References = [SquadsRef, MembersRef];

    public static AdminArea? Find(string key) =>
        All.FirstOrDefault(a => a.Key == key) ?? References.FirstOrDefault(a => a.Key == key);

    /// <summary>Bereiche, die die Person sehen darf, in Gruppen-Reihenfolge.</summary>
    public static IReadOnlyList<AdminArea> VisibleTo(UserProfile? me) => All.Where(a => a.IsVisibleTo(me)).ToList();
}
