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

    /// <summary>Seite für den App-Login im System-Browser (Website-Domain, nicht api.).</summary>
    public Uri AuthorizeUrl => new(WebBaseUrl, "auth/app/authorize");

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
            result[env.Name] = new RoGEnvironment(env.Name, Read("api"), Read("web"), Read("wiki"), Read("status"), clientId);
        }

        return result;
    }
}
