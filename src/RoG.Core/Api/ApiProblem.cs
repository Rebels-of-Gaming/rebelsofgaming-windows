using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RoG.Core.Api;

/// <summary>
/// Fehler der API nach RFC 9457 (<c>application/problem+json</c>) mit dem RoG-Feld <c>code</c>.
/// Die App wertet nur <see cref="Code"/> aus und zeigt den Text über den Übersetzungsschlüssel
/// <c>Error_&lt;code&gt;</c> – <see cref="Title"/>/<see cref="Detail"/> sind übersetzte Servertexte und dienen nur als Rückfall.
/// </summary>
public sealed record ApiProblem
{
    /// <summary>Maschinenlesbarer Fehlercode, z. B. <c>validation_failed</c>, <c>invalid_grant</c>, <c>rate_limited</c>.</summary>
    public required string Code { get; init; }

    /// <summary>HTTP-Status, 0 bei Netzwerkfehlern ohne Antwort.</summary>
    public int Status { get; init; }

    public string? Type { get; init; }

    public string? Title { get; init; }

    public string? Detail { get; init; }

    public string? Instance { get; init; }

    /// <summary>Feldfehler bei 422 (<c>errors</c>): Feldname → Meldungen.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Errors { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Wartezeit aus <c>Retry-After</c> (429/503).</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>Roher JSON-Body (nur bei gültigem Objekt) – für Zusatzfelder wie <c>current</c> bei <c>edit_conflict</c>.</summary>
    public string? RawBody { get; init; }

    /// <summary>Zusatzfeld des Problems (RFC 9457 „extension member“), z. B. <c>current</c>.</summary>
    public bool TryGetExtension(string name, out JsonElement value)
    {
        value = default;
        if (RawBody is null)
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(RawBody);
            if (doc.RootElement.TryGetProperty(name, out var found))
            {
                value = found.Clone();
                return true;
            }
        }
        catch (JsonException)
        {
            // RawBody ist nur gesetzt, wenn er gültig war
        }

        return false;
    }

    public bool IsUnauthorized => Status == (int)HttpStatusCode.Unauthorized;

    public bool IsForbidden => Status == (int)HttpStatusCode.Forbidden;

    public bool IsRateLimited => Status == (int)HttpStatusCode.TooManyRequests;

    public bool IsValidation => Status == (int)HttpStatusCode.UnprocessableEntity;

    public bool IsNetworkError => Status == 0;

    /// <summary>Codes ohne Antwort vom Server.</summary>
    public static class Codes
    {
        public const string NetworkError = "network_error";
        public const string Timeout = "timeout";
        public const string Unauthenticated = "unauthenticated";
        public const string Forbidden = "forbidden";
        public const string NotFound = "not_found";
        public const string RateLimited = "rate_limited";
        public const string ServerError = "server_error";
        public const string Unknown = "unknown_error";
    }

    /// <summary>Problem für einen Netzwerkfehler ohne Serverantwort.</summary>
    public static ApiProblem Network(bool timeout = false) =>
        new() { Code = timeout ? Codes.Timeout : Codes.NetworkError, Status = 0 };

    /// <summary>
    /// Liest ein Problem aus einer Fehlerantwort. Kommt kein gültiges <c>problem+json</c>, wird der Code aus dem
    /// Status abgeleitet. Der OAuth-Token-Endpunkt liefert zusätzlich <c>error</c> (RFC 6749) – das gilt als Code,
    /// wenn <c>code</c> fehlt.
    /// </summary>
    public static async Task<ApiProblem> FromResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        var status = (int)response.StatusCode;
        var retryAfter = ReadRetryAfter(response.Headers.RetryAfter, DateTimeOffset.UtcNow);
        string body;
        try
        {
            body = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            body = string.Empty;
        }

        return Parse(body, status, retryAfter);
    }

    /// <summary>Parst einen Fehler-Body (JSON) – robust gegen leere oder fremde Bodies.</summary>
    public static ApiProblem Parse(string? body, int status, TimeSpan? retryAfter = null)
    {
        string? code = null, type = null, title = null, detail = null, instance = null, raw = null;
        var errors = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    raw = body;
                    code = ReadString(root, "code") ?? ReadString(root, "error");
                    type = ReadString(root, "type");
                    title = ReadString(root, "title") ?? ReadString(root, "message");
                    detail = ReadString(root, "detail") ?? ReadString(root, "error_description");
                    instance = ReadString(root, "instance");
                    if (root.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt32(out var st) && st >= 400)
                    {
                        status = st;
                    }

                    if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var field in e.EnumerateObject())
                        {
                            var messages = field.Value.ValueKind == JsonValueKind.Array
                                ? field.Value.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.String).Select(m => m.GetString()!).ToList()
                                : field.Value.ValueKind == JsonValueKind.String ? [field.Value.GetString()!] : [];
                            errors[field.Name] = messages;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Kein JSON (z. B. HTML-Fehlerseite eines Proxys) – Code aus dem Status ableiten.
            }
        }

        return new ApiProblem
        {
            Code = string.IsNullOrWhiteSpace(code) ? CodeForStatus(status) : code,
            Status = status,
            Type = type,
            Title = title,
            Detail = detail,
            Instance = instance,
            Errors = errors,
            RetryAfter = retryAfter,
            RawBody = raw,
        };
    }

    /// <summary>Standard-Code je HTTP-Status, wenn der Server keinen liefert.</summary>
    public static string CodeForStatus(int status) => status switch
    {
        401 => Codes.Unauthenticated,
        403 => Codes.Forbidden,
        404 => Codes.NotFound,
        422 => "validation_failed",
        429 => Codes.RateLimited,
        >= 500 => Codes.ServerError,
        _ => Codes.Unknown,
    };

    /// <summary>Wertet <c>Retry-After</c> aus (Sekunden oder HTTP-Datum), nie negativ.</summary>
    public static TimeSpan? ReadRetryAfter(RetryConditionHeaderValue? header, DateTimeOffset now)
    {
        if (header is null)
        {
            return null;
        }

        if (header.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (header.Date is { } date)
        {
            var wait = date - now;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        return null;
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Status} {Code}{(Detail is null ? string.Empty : ": " + Detail)}");
}
