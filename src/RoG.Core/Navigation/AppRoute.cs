using RoG.Core.Config;

namespace RoG.Core.Navigation;

/// <summary>Bereiche der App (entsprechen den Navigations-Schlüsseln <c>Nav_*</c>).</summary>
public enum AppSection
{
    Home,
    News,
    Squads,
    Members,
    Calendar,
    Servers,
    Matches,
    Info,
    Forum,
    Wiki,
    Messages,
    Account,
    Search,
    Notifications,
    Settings,
    Votes,
    Uploads,
    Media,
    Admin,
}

/// <summary>Ziel in der App: Bereich und optional ein Eintrag (Slug/ID) bzw. bei der Suche der Suchbegriff.</summary>
public sealed record AppRoute(AppSection Section, string? Key = null)
{
    /// <summary>
    /// Bildet eine Adresse der Website/des Wikis (Suchtreffer, Links in Inhalten, später Toasts und App-URI-Handler)
    /// auf einen Bereich ab. Pfade wie in Guide 35 Abschnitt 5: <c>/{sprache}/news/*</c>, <c>/u/*</c>, <c>/squads/*</c>,
    /// <c>/events/*</c>, <c>/matches/*</c>, <c>/forum/*</c>, Wiki-Domain. Unbekannte Adressen → <c>null</c> (Browser).
    /// </summary>
    public static AppRoute? FromWebUrl(Uri? url, RoGEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (url is null || !url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        if (SameHost(url, environment.WikiBaseUrl))
        {
            return new AppRoute(AppSection.Wiki, url.AbsolutePath.Trim('/') is { Length: > 0 } page ? Uri.UnescapeDataString(page) : null);
        }

        if (!SameHost(url, environment.WebBaseUrl))
        {
            return null;
        }

        var parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToList();
        if (parts.Count > 0 && parts[0] is "de" or "en" or "fr")
        {
            parts.RemoveAt(0);
        }

        if (parts.Count == 0)
        {
            return new AppRoute(AppSection.Home);
        }

        var key = parts.Count > 1 ? parts[1] : null;
        return parts[0] switch
        {
            "news" => new AppRoute(AppSection.News, key),
            "u" or "members" => new AppRoute(AppSection.Members, key),
            "squads" => new AppRoute(AppSection.Squads, key),
            "events" or "calendar" => new AppRoute(AppSection.Calendar, key),
            "matches" or "hall-of-fame" or "tournaments" => new AppRoute(AppSection.Matches, parts[0] == "matches" ? key : null),
            "server" or "servers" => new AppRoute(AppSection.Servers, key),
            "forum" => new AppRoute(AppSection.Forum, string.Join('/', parts.Skip(1)) is { Length: > 0 } f ? f : null),
            "rules" or "about" or "partners" or "imprint" or "privacy" or "terms" or "contact" or "pages" or "p" => new AppRoute(AppSection.Info, parts[0]),
            "messages" => new AppRoute(AppSection.Messages, key),
            "settings" or "account" => new AppRoute(AppSection.Account),
            _ => null,
        };
    }

    /// <summary>Eigenes Schema der App.</summary>
    public const string Scheme = "rog";

    /// <summary>
    /// <c>rog://bereich/eintrag</c> (Sprungliste, Toasts, Links): <c>rog://news/slug</c>, <c>rog://u/slug</c>,
    /// <c>rog://squads/slug</c>, <c>rog://events/id</c>, <c>rog://servers</c>, <c>rog://messages</c>,
    /// <c>rog://notifications</c>, <c>rog://search?q=…</c>, <c>rog://settings</c>, <c>rog://open?url=https://…</c>.
    /// <c>rog://auth/*</c> gehört dem Login der Mobile-App → <c>null</c>.
    /// </summary>
    public static AppRoute? FromAppUri(Uri? uri, RoGEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (uri is null || !uri.IsAbsoluteUri || !uri.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var section = uri.Host.ToLowerInvariant();
        var key = uri.AbsolutePath.Trim('/') is { Length: > 0 } path ? Uri.UnescapeDataString(path) : null;
        var query = ParseQuery(uri.Query);
        return section switch
        {
            "" or "home" => new AppRoute(AppSection.Home),
            "news" => new AppRoute(AppSection.News, key),
            "u" or "members" => new AppRoute(AppSection.Members, key),
            "squads" => new AppRoute(AppSection.Squads, key),
            "events" or "calendar" => new AppRoute(AppSection.Calendar, key),
            "servers" => new AppRoute(AppSection.Servers, key),
            "matches" => new AppRoute(AppSection.Matches, key),
            "info" => new AppRoute(AppSection.Info, key),
            "forum" => new AppRoute(AppSection.Forum, key),
            "wiki" => new AppRoute(AppSection.Wiki, key),
            "messages" => new AppRoute(AppSection.Messages, key),
            "notifications" => new AppRoute(AppSection.Notifications),
            "account" => new AppRoute(AppSection.Account),
            "settings" => new AppRoute(AppSection.Settings),
            "votes" => new AppRoute(AppSection.Votes),
            "uploads" => new AppRoute(AppSection.Uploads),
            "media" => new AppRoute(AppSection.Media),
            "admin" => new AppRoute(AppSection.Admin),
            "search" => query.TryGetValue("q", out var q) && q.Length > 0 ? new AppRoute(AppSection.Search, q) : null,
            "open" => query.TryGetValue("url", out var url) && Uri.TryCreate(url, UriKind.Absolute, out var web) ? FromWebUrl(web, environment) : null,
            _ => null,
        };
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = pair.IndexOf('=', StringComparison.Ordinal);
            var name = Uri.UnescapeDataString(i < 0 ? pair : pair[..i]);
            var value = i < 0 ? string.Empty : Uri.UnescapeDataString(pair[(i + 1)..].Replace('+', ' '));
            result[name] = value;
        }

        return result;
    }

    private static bool SameHost(Uri url, Uri baseUrl) =>
        url.Host.Equals(baseUrl.Host, StringComparison.OrdinalIgnoreCase)
        || url.Host.Equals("www." + baseUrl.Host, StringComparison.OrdinalIgnoreCase);
}
