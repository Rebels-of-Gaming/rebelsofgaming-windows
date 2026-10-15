using System.Globalization;
using System.Text.Json;
using RoG.Core.Api;

namespace RoG.Core.Realtime;

/// <summary>
/// Öffentliche Verbindungsdaten für Reverb (Pusher-Protokoll) aus <c>GET /v1</c> → <c>data.realtime</c>.
/// Nie Geheimnisse; <c>null</c> von der API bedeutet „Echtzeit aus“ – die App lädt dann per API nach.
/// </summary>
public sealed record RealtimeConfig(string Key, string Host, int Port, string Scheme, Uri AuthEndpoint)
{
    /// <summary>Pusher-Protokollversion, die Reverb spricht.</summary>
    public const int ProtocolVersion = 7;

    public bool UseTls => string.Equals(Scheme, "https", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Scheme, "wss", StringComparison.OrdinalIgnoreCase);

    /// <summary><c>wss://host:port/app/{key}?protocol=7&amp;client=rog-windows&amp;version=…&amp;flash=false</c></summary>
    public Uri BuildSocketUri(string clientVersion)
    {
        var scheme = UseTls ? "wss" : "ws";
        var query = string.Create(CultureInfo.InvariantCulture,
            $"protocol={ProtocolVersion}&client=rog-windows&version={Uri.EscapeDataString(clientVersion)}&flash=false");
        return new UriBuilder(scheme, Host, Port, $"/app/{Uri.EscapeDataString(Key)}") { Query = query }.Uri;
    }

    /// <summary>Liest <c>data.realtime</c> aus der Antwort von <c>GET /v1</c>; <c>null</c>, wenn Echtzeit aus ist oder Felder fehlen.</summary>
    public static RealtimeConfig? FromStatusResponse(string json, Uri apiBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(apiBaseUrl);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("realtime", out var rt)
                || rt.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var key = rt.TryGetProperty("key", out var k) ? k.GetString() : null;
            var host = rt.TryGetProperty("host", out var h) ? h.GetString() : null;
            var port = rt.TryGetProperty("port", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;
            var scheme = rt.TryGetProperty("scheme", out var s) ? s.GetString() ?? "https" : "https";
            var auth = rt.TryGetProperty("auth_endpoint", out var a) ? a.GetString() : null;
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(host) || port <= 0)
            {
                return null;
            }

            var authUri = string.IsNullOrEmpty(auth) ? new Uri(apiBaseUrl, "broadcasting/auth") : new Uri(apiBaseUrl, auth);
            return new RealtimeConfig(key, host, port, scheme, authUri);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Holt die Daten über <c>GET /v1</c> (öffentlich). Netzwerkfehler → <c>null</c> (App läuft ohne Echtzeit).</summary>
    public static async Task<RealtimeConfig?> FetchAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, string.Empty);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return FromStatusResponse(json, http.BaseAddress ?? throw new InvalidOperationException("HttpClient.BaseAddress fehlt."));
        }
        catch (ApiProblemException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
