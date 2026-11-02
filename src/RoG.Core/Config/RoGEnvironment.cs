using System.Reflection;
using System.Text.Json;

namespace RoG.Core.Config;

/// <summary>
/// Eine Zielumgebung der Plattform (Produktion, Staging, lokal). Die Domains stehen nur in
/// <c>Config/environments.json</c> (eingebettete Ressource) – nirgends sonst im Code.
/// </summary>
public sealed record RoGEnvironment(string Name, Uri ApiBaseUrl, Uri WebBaseUrl, Uri WikiBaseUrl, Uri StatusUrl, string ClientId)
{
    public const string Production = "production";
    public const string Staging = "staging";
    public const string Local = "local";

    private static readonly Lazy<IReadOnlyDictionary<string, RoGEnvironment>> s_all = new(Load);

    /// <summary>Alle bekannten Umgebungen nach Name.</summary>
    public static IReadOnlyDictionary<string, RoGEnvironment> All => s_all.Value;

    /// <summary>Umgebung nach Name, unbekannte Namen fallen auf Produktion zurück.</summary>
    public static RoGEnvironment Get(string? name) =>
        name is not null && All.TryGetValue(name, out var env) ? env : All[Production];

    /// <summary>
    /// Umgebung für den App-Start: in Release-Builds (<paramref name="allowLocal"/> = <c>false</c>) gibt es
    /// <c>local</c> (http, Entwickler-Rechner) nicht – dann Produktion.
    /// </summary>
    public static RoGEnvironment Resolve(string? name, bool allowLocal) =>
        !allowLocal && name == Local ? All[Production] : Get(name);

    /// <summary>
    /// Eintrag in der Windows-Anmeldeinformationsverwaltung: je Umgebung ein eigener, damit ein Staging-Token nie an
    /// die Produktion geht (und umgekehrt). Produktion behält den bisherigen Namen – angemeldete Nutzer bleiben angemeldet.
    /// </summary>
    public string TokenResource => Name == Production ? "RebelsOfGaming.DeviceToken" : "RebelsOfGaming.DeviceToken." + Name;

    /// <summary>Datei für die Konto-Metadaten (ohne Token) im App-Datenordner, je Umgebung.</summary>
    public string AccountFileName => Name == Production ? "account.json" : $"account.{Name}.json";

    /// <summary>Domains der Plattform (Website, CDN), von denen die App Bilder direkt lädt – samt Subdomains.</summary>
    public IReadOnlyList<string> MediaDomains { get; init; } = [];

    /// <summary>
    /// Darf die App dieses Bild direkt laden? Nur von den eigenen Domains (Website-Regel: fremde Bilder nur über den
    /// eigenen, signierten Bild-Cache <c>/media/external</c>), nur https – http nur, wenn die API selbst http ist (local).
    /// </summary>
    public bool IsOwnMedia(Uri? uri)
    {
        if (uri is not { IsAbsoluteUri: true } || !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && ApiBaseUrl.Scheme == Uri.UriSchemeHttp)))
        {
            return false;
        }

        var host = uri.IdnHost.TrimEnd('.');
        return MediaDomains.Any(d => host.Equals(d, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Verwaltung der Website (Admin-Domain) für Bereiche, die die App nicht nativ hat; <c>null</c> = unbekannt.</summary>
    public Uri? AdminBaseUrl { get; init; }

    /// <summary>Seite für den App-Login im System-Browser (Website-Domain, nicht api.).</summary>
    public Uri AuthorizeUrl => new(WebBaseUrl, "auth/app/authorize");

    /// <summary>
    /// Seite der Website in der App-Sprache: alle Seiten (Profil <c>u/{slug}</c>, <c>settings/account</c>, Regeln …)
    /// liegen unter <c>/{de|en|fr}/</c>, ohne Präfix antwortet die Website mit 404. Unbekannte Sprache → <c>de</c>.
    /// </summary>
    public Uri WebPage(string? language, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var lang = language is "de" or "en" or "fr" ? language : "de";
        return new Uri(WebBaseUrl, $"{lang}/{path.TrimStart('/')}");
    }

    /// <summary>Öffentliches Profil auf der Website (<c>/{sprache}/u/{slug}</c>).</summary>
    public Uri ProfilePage(string? language, string slug) => WebPage(language, "u/" + Uri.EscapeDataString(slug));

    private static Dictionary<string, RoGEnvironment> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RoG.Core.Config.environments.json")
            ?? throw new InvalidOperationException("environments.json fehlt als eingebettete Ressource.");
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        var clientId = root.GetProperty("clientId").GetString() ?? "rog-app";
        var result = new Dictionary<string, RoGEnvironment>(StringComparer.Ordinal);
        foreach (var env in root.GetProperty("environments").EnumerateObject())
        {
            Uri Read(string key) => new(env.Value.GetProperty(key).GetString()!, UriKind.Absolute);
            result[env.Name] = new RoGEnvironment(env.Name, Read("api"), Read("web"), Read("wiki"), Read("status"), clientId)
            {
                AdminBaseUrl = env.Value.TryGetProperty("admin", out var admin) && admin.GetString() is { } a ? new Uri(a, UriKind.Absolute) : null,
                MediaDomains = env.Value.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Array
                    ? media.EnumerateArray().Select(m => m.GetString()).OfType<string>().ToList()
                    : [],
            };
        }

        return result;
    }
}
