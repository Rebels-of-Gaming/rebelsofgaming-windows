using System.Net.Http.Json;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Features.Account;

namespace RoG.Core.Features.Admin;

/// <summary>Eingereichter Galerie-Eintrag, der auf Freigabe wartet.</summary>
public sealed record PendingGalleryItem(string Id, string AlbumSlug, string AlbumTitle, string Kind, string? Caption, string? Author, Uri? Thumb, Uri? Full, DateTimeOffset? CreatedAt);

/// <summary>Gemeldeter Upload (Guide 20) mit Grund, meldender Person und Vorschau.</summary>
public sealed record UploadReport(string Id, string Reason, string? Reporter, DateTimeOffset? CreatedAt, string? Resolution, string? UploadTitle, string? Owner, Uri? Preview, Uri? Direct)
{
    public bool IsOpen => Resolution is null;
}

public sealed record StorageUsage(int Uploads, long Bytes, IReadOnlyList<(string Name, int Uploads, long Bytes)> TopUsers);

/// <summary>
/// Galerie-Freigaben (Guide 24, Recht <c>gallery.manage</c>) und Upload-Verwaltung (Guide 20, <c>uploads.manage</c>).
/// Eine Warteschlange offener Galerie-Einträge gibt es nicht (Lücke W16) – die App sammelt sie aus den Alben mit Freigabe.
/// </summary>
public sealed class MediaAdminService(HttpClient http)
{
    public static bool CanModerateGallery(UserProfile? me) =>
        me is not null && me.CanAnywhere("gallery.manage") && (me.HasScope("gallery:manage") || me.HasScope("gallery:write"));

    public static bool CanManageUploads(UserProfile? me) =>
        me is not null && me.Can("uploads.manage") && me.HasScope("uploads:manage");

    /// <summary>Offene Einträge aus allen Alben mit Freigabe, die ich verwalten darf – älteste zuerst.</summary>
    public async Task<IReadOnlyList<PendingGalleryItem>> PendingAsync(CancellationToken cancellationToken = default)
    {
        using var albums = await GetAsync("gallery/albums?per_page=100", cancellationToken).ConfigureAwait(false);
        var slugs = albums.RootElement.TryGetProperty("data", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(a => !a.TryGetProperty("requires_approval", out var r) || r.ValueKind != JsonValueKind.False)
                .Select(a => S(a, "slug")).OfType<string>().ToList()
            : [];
        var result = new List<PendingGalleryItem>();
        foreach (var slug in slugs)
        {
            using var album = await GetAsync("gallery/albums/" + Uri.EscapeDataString(slug), cancellationToken).ConfigureAwait(false);
            result.AddRange(ParsePending(album.RootElement));
        }

        return result.OrderBy(i => i.CreatedAt ?? DateTimeOffset.MaxValue).ToList();
    }

    internal static IEnumerable<PendingGalleryItem> ParsePending(JsonElement root)
    {
        var d = root.TryGetProperty("data", out var data) ? data : root;
        if (!d.TryGetProperty("can_manage", out var cm) || cm.ValueKind != JsonValueKind.True
            || !d.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        var slug = S(d, "slug") ?? string.Empty;
        var title = S(d, "title") ?? slug;
        foreach (var i in items.EnumerateArray().Where(i => S(i, "status") == "pending"))
        {
            var image = i.TryGetProperty("image", out var img) ? img : default;
            yield return new PendingGalleryItem(
                S(i, "id") ?? string.Empty,
                slug,
                title,
                S(i, "kind") ?? "image",
                ApiValues.Text(S(i, "caption")),
                i.TryGetProperty("user", out var u) ? S(u, "name") : null,
                ApiValues.WebUri(S(image, "thumb") ?? S(i, "poster_url")),
                ApiValues.WebUri(S(image, "full") ?? S(i, "video_url")),
                ApiValues.Time(S(i, "created_at")));
        }
    }

    public Task ApproveAsync(string itemId, CancellationToken cancellationToken = default) => PostAsync($"gallery/items/{Uri.EscapeDataString(itemId)}/approve", null, cancellationToken);

    /// <summary>Ablehnen löscht den Eintrag.</summary>
    public Task RejectAsync(string itemId, CancellationToken cancellationToken = default) => PostAsync($"gallery/items/{Uri.EscapeDataString(itemId)}/reject", null, cancellationToken);

    public async Task<IReadOnlyList<UploadReport>> ReportsAsync(bool handled, CancellationToken cancellationToken = default)
    {
        using var doc = await GetAsync("admin/uploads/reports?filter%5Bhandled%5D=" + (handled ? "1" : "0"), cancellationToken).ConfigureAwait(false);
        return ParseReports(doc.RootElement);
    }

    internal static IReadOnlyList<UploadReport> ParseReports(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return data.EnumerateArray().Select(r =>
        {
            var upload = r.TryGetProperty("upload", out var u) && u.ValueKind == JsonValueKind.Object ? u : default;
            var links = upload.ValueKind == JsonValueKind.Object && upload.TryGetProperty("links", out var l) ? l : default;
            var owner = upload.ValueKind == JsonValueKind.Object && upload.TryGetProperty("user", out var o) ? S(o, "name") : null;
            return new UploadReport(
                S(r, "id") ?? string.Empty,
                S(r, "reason") ?? string.Empty,
                r.TryGetProperty("reporter", out var rp) ? S(rp, "name") : null,
                ApiValues.Time(S(r, "created_at")),
                ApiValues.Text(S(r, "resolution")),
                ApiValues.Text(S(upload, "title")) ?? S(upload, "original_name"),
                owner,
                ApiValues.WebUri(S(links, "thumb") ?? S(links, "preview")),
                ApiValues.WebUri(S(links, "direct")));
        }).Where(r => r.Id.Length > 0).ToList();
    }

    /// <summary>Meldung erledigen: Upload behalten (<c>kept</c>) oder löschen (<c>deleted</c>).</summary>
    public Task ResolveAsync(string reportId, bool delete, CancellationToken cancellationToken = default) =>
        PostAsync($"admin/uploads/reports/{Uri.EscapeDataString(reportId)}/resolve", new Dictionary<string, string> { ["resolution"] = delete ? "deleted" : "kept" }, cancellationToken);

    public async Task<StorageUsage> StorageAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await GetAsync("admin/uploads/storage", cancellationToken).ConfigureAwait(false);
        var d = doc.RootElement.TryGetProperty("data", out var data) ? data : default;
        static long L(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.TryGetInt64(out var v) ? v : 0;
        var users = d.ValueKind == JsonValueKind.Object && d.TryGetProperty("users", out var us) && us.ValueKind == JsonValueKind.Array
            ? us.EnumerateArray().Select(u => (u.TryGetProperty("user", out var x) ? S(x, "name") ?? "—" : "—", (int)L(u, "uploads"), L(u, "bytes")))
                .OrderByDescending(u => u.Item3).Take(10).ToList()
            : [];
        return new StorageUsage((int)L(d, "uploads"), L(d, "bytes"), users);
    }

    private async Task PostAsync(string path, object? body, CancellationToken cancellationToken)
    {
        using var response = body is null
            ? await http.PostAsync(new Uri(path, UriKind.Relative), null, cancellationToken).ConfigureAwait(false)
            : await http.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string? S(JsonElement o, string n) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}

/// <summary>
/// Verwaltungsseiten der Website, die die App (noch) nicht nativ hat – Pfad auf der Admin-Domain und, wo die Website es
/// festlegt, das Recht dafür. Die Website prüft beim Öffnen selbst (2FA-Step-up).
/// </summary>
public static class AdminWebLinks
{
    public sealed record Link(string Key, string Path, string? Permission);

    public static readonly IReadOnlyList<Link> All =
    [
        new("roles", "roles", "roles.view"),
        new("permissions", "permissions/check", "permissions.check"),
        new("games", "games", "games.manage"),
        new("team", "team", "team.manage"),
        new("inactivity", "inactivity", null),
        new("badges", "badges", "badges.manage"),
        new("applications_settings", "applications/settings", null),
        new("event_types", "event-types", null),
        new("tournaments", "tournaments", null),
        new("hall_of_fame", "hall-of-fame", null),
        new("pages", "pages", null),
        new("home", "home", null),
        new("partners", "partners", null),
        new("menus", "menus", null),
        new("rules", "rules", null),
        new("legal", "legal", null),
        new("forum", "forum/categories", null),
        new("downloads", "downloads", null),
        new("gallery", "gallery", null),
        new("uploads", "uploads", null),
        new("wiki", "wiki", null),
        new("servers", "servers", null),
        new("integrations", "integrations", "integrations.view"),
        new("bot", "integrations/bot", "integrations.view"),
        new("webhooks", "integrations/webhooks", "webhooks.view"),
        new("social", "social", "social.publish"),
        new("mail_templates", "mail-templates", "notifications.templates"),
        new("translations", "translations", "translations.manage"),
        new("registration", "registration", null),
        new("settings", "settings", "settings.manage"),
        new("system", "system", "system.view"),
    ];

    /// <summary>Links, die für die Person in Frage kommen (bekanntes Recht vorhanden oder von der Website geprüft).</summary>
    public static IReadOnlyList<Link> For(UserProfile? me) =>
        me is null ? [] : All.Where(l => l.Permission is null || me.Can(l.Permission)).ToList();

    /// <summary>
    /// Website-Bereiche, die die App vollständig mit dem eigenen Editor kann (<see cref="AdminAreas"/>) – die übrigen
    /// bleiben Links (z. B. Rechte-Matrix, Menüs, Startseite, Integrationen). Teile, die der Editor nicht abbildet,
    /// öffnet er selbst über „Auf der Website öffnen“.
    /// </summary>
    public static readonly IReadOnlySet<string> Covered = new HashSet<string>(StringComparer.Ordinal)
    {
        "games", "team", "inactivity", "badges", "event_types", "tournaments", "pages", "partners", "forum", "gallery", "uploads",
        "wiki", "mail_templates", "registration", "settings", "servers",
    };

    /// <summary>Links ohne Gegenstück in der App.</summary>
    public static IReadOnlyList<Link> RemainingFor(UserProfile? me) => For(me).Where(l => !Covered.Contains(l.Key)).ToList();
}
