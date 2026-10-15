using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoG.Core.Api;
using RoG.Core.Config;

namespace RoG.Core.Auth;

/// <summary>Öffnet eine Adresse im Standardbrowser (App: <c>Windows.System.Launcher</c>).</summary>
public interface IBrowserLauncher
{
    Task<bool> OpenAsync(Uri uri, CancellationToken cancellationToken = default);
}

/// <summary>Fehler im App-Login. <see cref="Code"/> ist ein OAuth-/API-Code für den Übersetzungsschlüssel <c>Error_&lt;code&gt;</c>.</summary>
public sealed class AppLoginException : Exception
{
    public const string StateMismatch = "state_mismatch";
    public const string BrowserFailed = "browser_failed";
    public const string LoginTimeout = "login_timeout";
    public const string MissingCode = "missing_code";

    public AppLoginException()
        : this(ApiProblem.Codes.Unknown, description: null)
    {
    }

    public AppLoginException(string message)
        : this(ApiProblem.Codes.Unknown, message)
    {
    }

    public AppLoginException(string message, Exception innerException)
        : this(ApiProblem.Codes.Unknown, message, innerException)
    {
    }

    public AppLoginException(string code, string? description, Exception? innerException = null)
        : base(description ?? code, innerException)
    {
        Code = code;
        Description = description;
    }

    public string Code { get; }

    /// <summary>Text der Website (<c>error_description</c>), nur als Rückfall anzeigen.</summary>
    public string? Description { get; }
}

/// <summary>
/// App-Login im System-Browser nach Guide 35 der Website: OAuth 2.0 Authorization Code + PKCE (S256) mit
/// Loopback-Rücksprung <c>http://127.0.0.1:&lt;Port&gt;/callback</c> (RFC 8252 Abschnitt 7.3), danach Code-Tausch über
/// <c>POST /v1/auth/app/token</c>. Der <c>code_verifier</c> verlässt das Gerät nie; <c>state</c> wird immer geprüft.
/// </summary>
public sealed class AppLoginFlow
{
    /// <summary>Wie lange die App auf den Browser wartet. Die Website verwirft die Anfrage nach 15 Minuten.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private readonly RoGEnvironment _environment;
    private readonly HttpClient _http;
    private readonly IBrowserLauncher _browser;
    private readonly ITokenStore _tokens;
    private readonly TimeProvider _time;

    /// <param name="http">Client mit der RoG-Pipeline und <see cref="RoGEnvironment.ApiBaseUrl"/> als BaseAddress.</param>
    public AppLoginFlow(RoGEnvironment environment, HttpClient http, IBrowserLauncher browser, ITokenStore tokens, TimeProvider? time = null)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Baut die Autorisierungs-Adresse (Schritt 1 in Guide 35).</summary>
    public static Uri BuildAuthorizeUri(
        RoGEnvironment environment,
        Uri redirectUri,
        string codeChallenge,
        string state,
        IEnumerable<string> scopes,
        string? deviceName)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(redirectUri);
        ArgumentNullException.ThrowIfNull(scopes);
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"),
            new("client_id", environment.ClientId),
            new("redirect_uri", redirectUri.ToString()),
            new("code_challenge", codeChallenge),
            new("code_challenge_method", "S256"),
            new("state", state),
            new("scope", string.Join(' ', scopes.Distinct(StringComparer.Ordinal))),
        };
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            parameters.Add(new("device_name", deviceName));
        }

        var query = string.Join('&', parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        return new UriBuilder(environment.AuthorizeUrl) { Query = query }.Uri;
    }

    /// <summary>
    /// Führt den kompletten Login aus und speichert den Token im <see cref="ITokenStore"/>.
    /// </summary>
    /// <exception cref="AppLoginException">Abbruch, Fehler der Website, falscher state, Timeout.</exception>
    /// <exception cref="ApiProblemException">Code-Tausch abgelehnt (z. B. <c>invalid_grant</c>, <c>too_many_devices</c>).</exception>
    public async Task<StoredToken> LoginAsync(
        IReadOnlyCollection<string> scopes,
        string deviceName,
        LoopbackPageTexts? pageTexts = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var verifier = Pkce.CreateVerifier();
        var challenge = Pkce.CreateChallenge(verifier);
        var state = Pkce.CreateState();

        await using var listener = LoopbackCallbackListener.Start(pageTexts);
        var authorizeUri = BuildAuthorizeUri(_environment, listener.RedirectUri, challenge, state, scopes, deviceName);

        if (!await _browser.OpenAsync(authorizeUri, cancellationToken).ConfigureAwait(false))
        {
            throw new AppLoginException(AppLoginException.BrowserFailed, description: null);
        }

        LoopbackCallback callback;
        using (var timeoutCts = new CancellationTokenSource(timeout ?? DefaultTimeout, _time))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
        {
            try
            {
                callback = await listener.WaitForCallbackAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new AppLoginException(AppLoginException.LoginTimeout, description: null);
            }
        }

        // state zuerst: ein fremder Rücksprung darf weder Fehler noch Code auslösen.
        if (!Pkce.StateMatches(state, callback.State))
        {
            throw new AppLoginException(AppLoginException.StateMismatch, description: null);
        }

        if (callback.Error is not null)
        {
            throw new AppLoginException(callback.Error, callback.ErrorDescription);
        }

        if (string.IsNullOrEmpty(callback.Code))
        {
            throw new AppLoginException(AppLoginException.MissingCode, description: null);
        }

        var token = await ExchangeCodeAsync(callback.Code, verifier, listener.RedirectUri, deviceName, cancellationToken).ConfigureAwait(false);
        await _tokens.SetAsync(token, cancellationToken).ConfigureAwait(false);
        return token;
    }

    /// <summary>Schritt 3: Code gegen den Geräte-Token tauschen (<c>POST /v1/auth/app/token</c>, ohne Token).</summary>
    public async Task<StoredToken> ExchangeCodeAsync(string code, string verifier, Uri redirectUri, string deviceName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        var request = new TokenRequest("authorization_code", code, verifier, _environment.ClientId, redirectUri.ToString(), deviceName);
        using var message = new HttpRequestMessage(HttpMethod.Post, "auth/app/token")
        {
            Content = JsonContent.Create(request, options: s_json),
        };
        message.Options.Set(RoGHttpHandler.SkipAuthorization, true);

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(s_json, cancellationToken).ConfigureAwait(false)
            ?? throw new ApiProblemException(new ApiProblem { Code = ApiProblem.Codes.Unknown, Status = (int)response.StatusCode });

        var scopes = body.Data?.Scopes
            ?? (body.Scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var accessToken = body.AccessToken ?? body.Data?.Token
            ?? throw new ApiProblemException(new ApiProblem { Code = ApiProblem.Codes.Unknown, Status = (int)response.StatusCode });

        return new StoredToken(
            accessToken,
            body.Data?.User?.Id ?? string.Empty,
            body.Data?.User?.Name ?? string.Empty,
            scopes,
            deviceName,
            _time.GetUtcNow());
    }

    private sealed record TokenRequest(
        [property: JsonPropertyName("grant_type")] string GrantType,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("code_verifier")] string CodeVerifier,
        [property: JsonPropertyName("client_id")] string ClientId,
        [property: JsonPropertyName("redirect_uri")] string RedirectUri,
        [property: JsonPropertyName("device_name")] string DeviceName);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("scope")] string? Scope,
        [property: JsonPropertyName("data")] TokenData? Data);

    private sealed record TokenData(
        [property: JsonPropertyName("token")] string? Token,
        [property: JsonPropertyName("user")] TokenUser? User,
        [property: JsonPropertyName("scopes")] List<string>? Scopes);

    private sealed record TokenUser(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("slug")] string? Slug);
}
