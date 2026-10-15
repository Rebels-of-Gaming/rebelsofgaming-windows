using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Auth;

public class AppLoginFlowTests
{
    private static readonly RoGEnvironment s_env = RoGEnvironment.Get(RoGEnvironment.Production);

    /// <summary>Simuliert den System-Browser: liest die Autorisierungs-Adresse und springt an die redirect_uri zurück.</summary>
    private sealed class FakeBrowser(Func<Dictionary<string, string>, string> callbackQuery) : IBrowserLauncher
    {
        public Uri? Opened { get; private set; }

        public Dictionary<string, string> Query { get; private set; } = [];

        public Task<bool> OpenAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Opened = uri;
            Query = LoopbackCallbackListener.ParseQuery(uri.Query.TrimStart('?'));
            var redirect = new Uri(Query["redirect_uri"]);
            _ = Task.Run(async () =>
            {
                using var browser = new HttpClient();
                using var _ = await browser.GetAsync(new Uri(redirect, "?" + callbackQuery(Query)), CancellationToken.None);
            }, CancellationToken.None);
            return Task.FromResult(true);
        }
    }

    private static (AppLoginFlow Flow, StubHttpHandler Stub, InMemoryTokenStore Tokens) Create(
        FakeBrowser browser, Func<HttpRequestMessage, int, HttpResponseMessage> api)
    {
        var tokens = new InMemoryTokenStore();
        var stub = new StubHttpHandler(api);
        var handler = new RoGHttpHandler(tokens, new ApiContext("de"), delay: (_, _) => Task.CompletedTask) { InnerHandler = stub };
        var http = new HttpClient(handler) { BaseAddress = s_env.ApiBaseUrl };
        return (new AppLoginFlow(s_env, http, browser, tokens), stub, tokens);
    }

    private static string TokenJson(string token = "42|secret") => $$$"""
        {"access_token":"{{{token}}}","token_type":"Bearer","scope":"profile:read devices:manage",
         "data":{"token":"{{{token}}}","user":{"id":"01J9USER","name":"Wahke","slug":"wahke"},"scopes":["profile:read","devices:manage"]}}
        """;

    [Fact]
    public void Authorize_uri_contains_all_parameters_from_guide_35()
    {
        var uri = AppLoginFlow.BuildAuthorizeUri(s_env, new Uri("http://127.0.0.1:50123/callback"), "CHALLENGE", "STATE",
            ["profile:read", "devices:manage", "profile:read"], "Windows-App – PC von Wahke");
        var q = LoopbackCallbackListener.ParseQuery(uri.Query.TrimStart('?'));

        Assert.Equal("https://rebelsofgaming.com/auth/app/authorize", uri.GetLeftPart(UriPartial.Path));
        Assert.Equal("code", q["response_type"]);
        Assert.Equal("rog-app", q["client_id"]);
        Assert.Equal("http://127.0.0.1:50123/callback", q["redirect_uri"]);
        Assert.Equal("CHALLENGE", q["code_challenge"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal("STATE", q["state"]);
        Assert.Equal("profile:read devices:manage", q["scope"]);
        Assert.Equal("Windows-App – PC von Wahke", q["device_name"]);
    }

    [Fact]
    public async Task Full_login_exchanges_code_with_matching_verifier_and_stores_token()
    {
        var browser = new FakeBrowser(q => $"code=CODE123&state={Uri.EscapeDataString(q["state"])}");
        string? challenge = null;
        var (flow, stub, tokens) = Create(browser, (req, _) =>
        {
            challenge = browser.Query["code_challenge"];
            return StubHttpHandler.Json(HttpStatusCode.OK, TokenJson());
        });

        var token = await flow.LoginAsync(["profile:read", "devices:manage"], "Windows-App – PC von Test", timeout: TimeSpan.FromSeconds(10));

        Assert.Equal("42|secret", token.AccessToken);
        Assert.Equal("01J9USER", token.UserId);
        Assert.Equal("Wahke", token.UserName);
        Assert.Equal(["profile:read", "devices:manage"], token.Scopes);
        Assert.Equal(token, await tokens.GetAsync());

        var exchange = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Post, exchange.Method);
        Assert.Equal("https://api.rebelsofgaming.com/v1/auth/app/token", exchange.Uri!.ToString());
        Assert.False(exchange.Headers.ContainsKey("Authorization"));

        using var body = JsonDocument.Parse(exchange.Body!);
        var root = body.RootElement;
        Assert.Equal("authorization_code", root.GetProperty("grant_type").GetString());
        Assert.Equal("CODE123", root.GetProperty("code").GetString());
        Assert.Equal("rog-app", root.GetProperty("client_id").GetString());
        Assert.Equal(browser.Query["redirect_uri"], root.GetProperty("redirect_uri").GetString());
        Assert.Equal("Windows-App – PC von Test", root.GetProperty("device_name").GetString());

        // Der Verifier passt zur Challenge aus Schritt 1 und verlässt das Gerät erst beim Tausch.
        var verifier = root.GetProperty("code_verifier").GetString()!;
        Assert.True(Pkce.IsValidVerifier(verifier));
        Assert.Equal(challenge, Pkce.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
        Assert.DoesNotContain(verifier, browser.Opened!.ToString());
    }

    [Fact]
    public async Task Wrong_state_is_rejected_before_any_token_exchange()
    {
        var browser = new FakeBrowser(_ => "code=CODE123&state=forged");
        var (flow, stub, tokens) = Create(browser, (_, _) => StubHttpHandler.Json(HttpStatusCode.OK, TokenJson()));

        var ex = await Assert.ThrowsAsync<AppLoginException>(() => flow.LoginAsync(["profile:read"], "PC", timeout: TimeSpan.FromSeconds(10)));

        Assert.Equal(AppLoginException.StateMismatch, ex.Code);
        Assert.Empty(stub.Requests);
        Assert.Null(await tokens.GetAsync());
    }

    [Fact]
    public async Task Error_from_website_is_reported_with_code_and_description()
    {
        var browser = new FakeBrowser(q => $"error=access_denied&error_description=Abgebrochen&state={Uri.EscapeDataString(q["state"])}");
        var (flow, stub, _) = Create(browser, (_, _) => StubHttpHandler.Json(HttpStatusCode.OK, TokenJson()));

        var ex = await Assert.ThrowsAsync<AppLoginException>(() => flow.LoginAsync(["profile:read"], "PC", timeout: TimeSpan.FromSeconds(10)));

        Assert.Equal("access_denied", ex.Code);
        Assert.Equal("Abgebrochen", ex.Description);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Error_with_wrong_state_counts_as_state_mismatch()
    {
        var browser = new FakeBrowser(_ => "error=access_denied&state=forged");
        var (flow, _, _) = Create(browser, (_, _) => StubHttpHandler.Json(HttpStatusCode.OK, TokenJson()));

        var ex = await Assert.ThrowsAsync<AppLoginException>(() => flow.LoginAsync(["profile:read"], "PC", timeout: TimeSpan.FromSeconds(10)));
        Assert.Equal(AppLoginException.StateMismatch, ex.Code);
    }

    [Fact]
    public async Task Invalid_grant_from_token_endpoint_becomes_api_problem()
    {
        var browser = new FakeBrowser(q => $"code=OLD&state={Uri.EscapeDataString(q["state"])}");
        var (flow, _, tokens) = Create(browser, (_, _) => StubHttpHandler.Json(HttpStatusCode.BadRequest,
            """{"type":"about:blank","title":"Ungültiger Code","status":400,"code":"invalid_grant","error":"invalid_grant"}""",
            "application/problem+json"));

        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => flow.LoginAsync(["profile:read"], "PC", timeout: TimeSpan.FromSeconds(10)));

        Assert.Equal("invalid_grant", ex.Problem.Code);
        Assert.Equal(400, ex.Problem.Status);
        Assert.Null(await tokens.GetAsync());
    }

    [Fact]
    public async Task Missing_browser_callback_times_out()
    {
        var tokens = new InMemoryTokenStore();
        using var http = new HttpClient(new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK))) { BaseAddress = s_env.ApiBaseUrl };
        var flow = new AppLoginFlow(s_env, http, new NeverReturningBrowser(), tokens);

        var ex = await Assert.ThrowsAsync<AppLoginException>(() => flow.LoginAsync(["profile:read"], "PC", timeout: TimeSpan.FromMilliseconds(200)));
        Assert.Equal(AppLoginException.LoginTimeout, ex.Code);
    }

    [Fact]
    public async Task Browser_that_cannot_open_fails_fast()
    {
        var tokens = new InMemoryTokenStore();
        using var http = new HttpClient(new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK))) { BaseAddress = s_env.ApiBaseUrl };
        var flow = new AppLoginFlow(s_env, http, new NeverReturningBrowser(canOpen: false), tokens);

        var ex = await Assert.ThrowsAsync<AppLoginException>(() => flow.LoginAsync(["profile:read"], "PC"));
        Assert.Equal(AppLoginException.BrowserFailed, ex.Code);
    }

    private sealed class NeverReturningBrowser(bool canOpen = true) : IBrowserLauncher
    {
        public Task<bool> OpenAsync(Uri uri, CancellationToken cancellationToken = default) => Task.FromResult(canOpen);
    }
}
