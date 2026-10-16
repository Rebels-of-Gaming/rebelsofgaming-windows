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

    private static bool SameHost(Uri url, Uri baseUrl) =>
        url.Host.Equals(baseUrl.Host, StringComparison.OrdinalIgnoreCase)
        || url.Host.Equals("www." + baseUrl.Host, StringComparison.OrdinalIgnoreCase);
}
