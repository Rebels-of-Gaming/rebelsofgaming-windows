using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using RoG.Core.Auth;

namespace RoG.Core.Api;

/// <summary>Laufzeit-Kontext der Pipeline: Sprache und App-Version.</summary>
public interface IApiContext
{
    /// <summary>Sprache für <c>Accept-Language</c>: <c>de</c>, <c>en</c> oder <c>fr</c>.</summary>
    string Language { get; }

    /// <summary>App-Version für den User-Agent, z. B. <c>0.1.0</c>.</summary>
    string AppVersion { get; }
}

/// <summary>Einfacher, veränderbarer Kontext.</summary>
public sealed class ApiContext(string language = "de", string appVersion = "0.1.0") : IApiContext
{
    public string Language { get; set; } = language;

    public string AppVersion { get; set; } = appVersion;
}

/// <summary>
/// HTTP-Pipeline für alle API-Aufrufe (generierter Client, Login, Echtzeit-Autorisierung):
/// <list type="bullet">
/// <item><c>Authorization: Bearer</c> aus dem <see cref="ITokenStore"/>, <c>Accept: application/json</c>, <c>Accept-Language</c>, User-Agent</item>
/// <item><c>Idempotency-Key</c> für POST/PUT/PATCH/DELETE (eine UUID je Aktion, bei Wiederholungen gleich)</item>
/// <item>ETag/<c>If-None-Match</c> für GET – eine <c>304</c> wird transparent mit der gespeicherten Antwort beantwortet</item>
/// <item>Fehler nach RFC 9457 → <see cref="ApiProblemException"/>; 401 meldet <see cref="Unauthorized"/> (Token verwerfen, Login)</item>
/// <item>429/503 mit <c>Retry-After</c> sowie Netzwerkfehler werden begrenzt wiederholt</item>
/// </list>
/// </summary>
public sealed class RoGHttpHandler : DelegatingHandler
{
    /// <summary>Anfrage ohne Token senden (Login, öffentliche Scope-Liste).</summary>
    public static readonly HttpRequestOptionsKey<bool> SkipAuthorization = new("RoG.SkipAuthorization");

    /// <summary>Kein ETag-Cache für diese Anfrage (z. B. personenbezogene Suchergebnisse).</summary>
    public static readonly HttpRequestOptionsKey<bool> SkipEtag = new("RoG.SkipEtag");

    public const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>Längste Wartezeit, die die Pipeline selbst abwartet; längere <c>Retry-After</c> gehen als Fehler an die Oberfläche.</summary>
    public static readonly TimeSpan MaxAutomaticRetryDelay = TimeSpan.FromSeconds(10);

    private readonly ITokenStore _tokens;
    private readonly IApiContext _context;
    private readonly IEtagCache? _etags;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public RoGHttpHandler(ITokenStore tokens, IApiContext context, IEtagCache? etags = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _etags = etags;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Wie oft eine Anfrage höchstens wiederholt wird (Netzwerk, 429, 503).</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>Wird bei <c>401</c> auf eine Anfrage mit Token ausgelöst – die App verwirft den Token und zeigt den Login.</summary>
    public event EventHandler<ApiProblem>? Unauthorized;

    /// <summary>
    /// Eine GET-Anfrage wurde ohne Netz bzw. bei Serverstörung aus dem Cache beantwortet (Kopf <c>X-RoG-Cache: stale</c>) –
    /// die App zeigt „offline, letzter Stand“.
    /// </summary>
    public event EventHandler<Uri?>? ServedStale;

    /// <summary>Gespeicherte Antworten verwerfen (Abmelden, Kontowechsel).</summary>
    public void ClearCache() => _etags?.Clear();

    /// <summary>Kopf an Antworten aus dem Cache: <c>etag</c> (304) oder <c>stale</c> (Offline-Rückfall).</summary>
    public const string CacheHeader = "X-RoG-Cache";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var skipAuth = request.Options.TryGetValue(SkipAuthorization, out var s) && s;
        var token = skipAuth ? null : await _tokens.GetAsync(cancellationToken).ConfigureAwait(false);
        PrepareHeaders(request, token);

        var isGet = request.Method == HttpMethod.Get;
        var useEtag = isGet && _etags is not null && !(request.Options.TryGetValue(SkipEtag, out var se) && se);
        var cacheKey = useEtag ? CacheKey(request, token) : null;
        EtagEntry? cached = null;
        if (cacheKey is not null && _etags!.TryGet(cacheKey, out var entry))
        {
            cached = entry;
            if (entry.ETag.Length > 0)
            {
                request.Headers.IfNoneMatch.Clear();
                request.Headers.TryAddWithoutValidation("If-None-Match", entry.ETag);
            }
        }

        // Body puffern, damit Wiederholungen denselben Inhalt (und denselben Idempotency-Key) senden.
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        }

        var retryable = isGet || request.Headers.Contains(IdempotencyHeader);
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(attempt == 0 ? request : await CloneAsync(request).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (retryable && attempt < MaxRetries)
            {
                await _delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (HttpRequestException ex)
            {
                if (isGet && cached is not null)
                {
                    return Stale(request, cached);
                }

                throw new ApiProblemException(ApiProblem.Network(), ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient.Timeout
                if (isGet && cached is not null)
                {
                    return Stale(request, cached);
                }

                throw new ApiProblemException(ApiProblem.Network(timeout: true), ex);
            }

            if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
            {
                response.Dispose();
                return FromCache(request, cached);
            }

            if (response.IsSuccessStatusCode)
            {
                if (cacheKey is not null)
                {
                    await StoreEtagAsync(cacheKey, response, cancellationToken).ConfigureAwait(false);
                }

                return response;
            }

            var problem = await ApiProblem.FromResponseAsync(response, cancellationToken).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            response.Dispose();

            if (retryable && attempt < MaxRetries && (status == 429 || status == 503)
                && problem.RetryAfter is { } wait && wait <= MaxAutomaticRetryDelay)
            {
                await _delay(wait, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (problem.IsUnauthorized && token is not null)
            {
                Unauthorized?.Invoke(this, problem);
            }

            // Serverstörung: letzten Stand zeigen statt Fehlerseite (externe Ausfälle machen keine Seite kaputt)
            if (isGet && cached is not null && status >= 500)
            {
                return Stale(request, cached);
            }

            throw new ApiProblemException(problem);
        }
    }

    private void PrepareHeaders(HttpRequestMessage request, StoredToken? token)
    {
        if (request.Headers.Accept.Count == 0)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/problem+json"));
        }

        request.Headers.AcceptLanguage.Clear();
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(_context.Language));
        if (request.Headers.UserAgent.Count == 0)
        {
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("RoG-Windows", _context.AppVersion));
        }

        if (token is not null && request.Headers.Authorization is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        }

        var isWrite = request.Method == HttpMethod.Post || request.Method == HttpMethod.Put
            || request.Method == HttpMethod.Patch || request.Method == HttpMethod.Delete;
        if (isWrite && !request.Headers.Contains(IdempotencyHeader))
        {
            request.Headers.TryAddWithoutValidation(IdempotencyHeader, Guid.NewGuid().ToString());
        }
    }

    private HttpResponseMessage Stale(HttpRequestMessage request, EtagEntry cached)
    {
        var response = FromCache(request, cached);
        response.Headers.Remove(CacheHeader);
        response.Headers.TryAddWithoutValidation(CacheHeader, "stale");
        ServedStale?.Invoke(this, request.RequestUri);
        return response;
    }

    /// <summary>
    /// Antwort merken: mit ETag für <c>If-None-Match</c>, ohne ETag nur als Rückfall ohne Netz. Nie bei <c>no-store</c>.
    /// </summary>
    private async Task StoreEtagAsync(string key, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var etag = response.Headers.ETag?.ToString() ?? string.Empty;
        if (response.Headers.CacheControl?.NoStore == true)
        {
            return;
        }

        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        _etags!.Store(key, new EtagEntry(etag, body, response.Content.Headers.ContentType?.ToString(), DateTimeOffset.UtcNow));

        // Inhalt wurde gelesen – für den Aufrufer neu bereitstellen.
        var replacement = new ByteArrayContent(body);
        foreach (var header in response.Content.Headers)
        {
            replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content = replacement;
    }

    private static HttpResponseMessage FromCache(HttpRequestMessage request, EtagEntry cached)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(cached.Body) };
        if (cached.ContentType is not null)
        {
            response.Content.Headers.TryAddWithoutValidation("Content-Type", cached.ContentType);
        }

        if (cached.ETag.Length > 0)
        {
            response.Headers.TryAddWithoutValidation("ETag", cached.ETag);
        }

        response.Headers.TryAddWithoutValidation(CacheHeader, "etag");
        return response;
    }

    /// <summary>Cache-Schlüssel: Adresse + Sprache + Hash des Tokens (Gast = „guest“). Der Token selbst wird nie gespeichert.</summary>
    internal string CacheKey(HttpRequestMessage request, StoredToken? token)
    {
        var who = token is null
            ? "guest"
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.AccessToken)))[..16];
        return $"{who}|{_context.Language}|{request.RequestUri}";
    }

    internal static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(300 * Math.Pow(2, attempt));

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri) { Version = original.Version, VersionPolicy = original.VersionPolicy };
        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in original.Options)
        {
            ((IDictionary<string, object?>)clone.Options)[option.Key] = option.Value;
        }

        if (original.Content is not null)
        {
            var bytes = await original.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in original.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
