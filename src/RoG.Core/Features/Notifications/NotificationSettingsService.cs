using System.Net.Http.Json;
using System.Text.Json;

namespace RoG.Core.Features.Notifications;

/// <summary>Ereignisart mit möglichen Kanälen (<c>GET /v1/notifications/types</c>).</summary>
public sealed record NotificationType(string Key, string Group, string Label, IReadOnlyList<string> Defaults, IReadOnlyList<string> Channels, bool InboxOptional);

/// <summary>Eigene Einstellungen: Kanäle je Ereignis, Sammel-Mail, Ruhezeit, nutzbare Kanäle.</summary>
public sealed record NotificationSettings(
    IReadOnlyDictionary<string, IReadOnlyList<string>> Preferences,
    string Digest,
    string? QuietFrom,
    string? QuietUntil,
    IReadOnlySet<string> Available)
{
    /// <summary>
    /// Wirksame Kanäle eines Ereignisses: eigene Liste oder Voreinstellung; die Glocke (<c>in_app</c>) ist bei
    /// Ereignissen ohne <c>inbox_optional</c> immer an.
    /// </summary>
    public IReadOnlySet<string> Effective(NotificationType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var set = new HashSet<string>(Preferences.TryGetValue(type.Key, out var own) ? own : type.Defaults, StringComparer.Ordinal);
        if (!type.InboxOptional)
        {
            set.Add("in_app");
        }

        return set;
    }
}

/// <summary>
/// Benachrichtigungs-Einstellungen (Guide 12): Typen, eigene Kanäle, Sammel-Mail, Ruhezeit. Geschrieben wird in der
/// Objektform (<c>{"key": {"mail": true}}</c>) – nur so lässt sich die Glocke abschalten, und nichts anderes ändert sich.
/// Gelesen roh, weil die Spec <c>preferences</c> nicht als Wörterbuch beschreibt.
/// </summary>
public sealed class NotificationSettingsService(HttpClient http)
{
    public async Task<IReadOnlyList<NotificationType>> TypesAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await GetAsync("notifications/types", cancellationToken).ConfigureAwait(false);
        return ParseTypes(doc.RootElement);
    }

    public async Task<NotificationSettings> SettingsAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await GetAsync("me/notification-settings", cancellationToken).ConfigureAwait(false);
        return ParseSettings(doc.RootElement);
    }

    public Task SetChannelAsync(string typeKey, string channel, bool on, CancellationToken cancellationToken = default) =>
        PatchAsync(new Dictionary<string, object?>
        {
            ["preferences"] = new Dictionary<string, object> { [typeKey] = new Dictionary<string, bool> { [channel] = on } },
        }, cancellationToken);

    /// <summary><c>instant</c>, <c>daily</c> oder <c>weekly</c>.</summary>
    public Task SetDigestAsync(string digest, CancellationToken cancellationToken = default) =>
        PatchAsync(new Dictionary<string, object?> { ["digest"] = digest }, cancellationToken);

    /// <summary>Ruhezeit (HH:MM) oder beide <c>null</c> = aus.</summary>
    public Task SetQuietHoursAsync(string? from, string? until, CancellationToken cancellationToken = default) =>
        PatchAsync(new Dictionary<string, object?> { ["quiet_from"] = from, ["quiet_until"] = until }, cancellationToken);

    private async Task PatchAsync(object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri("me/notification-settings", UriKind.Relative)) { Content = JsonContent.Create(body) };
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string? S(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static IReadOnlyList<string> L(JsonElement o, string n) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];

    internal static IReadOnlyList<NotificationType> ParseTypes(JsonElement root) =>
        root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray().Select(t => new NotificationType(
                S(t, "key") ?? string.Empty,
                S(t, "group") ?? string.Empty,
                S(t, "label") ?? S(t, "key") ?? string.Empty,
                L(t, "defaults"),
                L(t, "channels"),
                t.TryGetProperty("inbox_optional", out var io) && io.ValueKind == JsonValueKind.True)).Where(t => t.Key.Length > 0).ToList()
            : [];

    internal static NotificationSettings ParseSettings(JsonElement root)
    {
        var d = root.TryGetProperty("data", out var data) ? data : default;
        var preferences = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (d.ValueKind == JsonValueKind.Object && d.TryGetProperty("preferences", out var prefs) && prefs.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in prefs.EnumerateObject())
            {
                preferences[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.Array => p.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList(),
                    JsonValueKind.Object => p.Value.EnumerateObject().Where(x => x.Value.ValueKind == JsonValueKind.True).Select(x => x.Name).ToList(),
                    _ => [],
                };
            }
        }

        var available = new HashSet<string>(StringComparer.Ordinal) { "in_app" };
        if (d.ValueKind == JsonValueKind.Object && d.TryGetProperty("available", out var av) && av.ValueKind == JsonValueKind.Object)
        {
            foreach (var a in av.EnumerateObject().Where(a => a.Value.ValueKind == JsonValueKind.True))
            {
                available.Add(a.Name);
            }
        }

        return new NotificationSettings(preferences, S(d, "digest") ?? "instant", S(d, "quiet_from"), S(d, "quiet_until"), available);
    }
}
