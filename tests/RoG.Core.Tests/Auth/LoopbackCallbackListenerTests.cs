using System.Net;
using RoG.Core.Auth;

namespace RoG.Core.Tests.Auth;

public class LoopbackCallbackListenerTests
{
    private static readonly HttpClient s_browser = new() { Timeout = TimeSpan.FromSeconds(10) };

    [Fact]
    public async Task Listens_on_ipv4_loopback_with_random_port_and_callback_path()
    {
        await using var listener = LoopbackCallbackListener.Start();
        Assert.InRange(listener.Port, 1, 65535);
        Assert.Equal($"http://127.0.0.1:{listener.Port}/callback", listener.RedirectUri.ToString());
        Assert.True(listener.RedirectUri.IsLoopback);
    }

    [Fact]
    public async Task Two_listeners_get_different_ports()
    {
        await using var a = LoopbackCallbackListener.Start();
        await using var b = LoopbackCallbackListener.Start();
        Assert.NotEqual(a.Port, b.Port);
    }

    [Fact]
    public async Task Returns_code_and_state_and_shows_success_page()
    {
        await using var listener = LoopbackCallbackListener.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var wait = listener.WaitForCallbackAsync(cts.Token);

        using var response = await s_browser.GetAsync(new Uri(listener.RedirectUri, "?code=abc123&state=xyz%2B1"), cts.Token);
        var html = await response.Content.ReadAsStringAsync(cts.Token);
        var result = await wait;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("abc123", result.Code);
        Assert.Equal("xyz+1", result.State);
        Assert.False(result.IsError);
        Assert.Contains("Anmeldung abgeschlossen", html);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Ignores_other_paths_with_404_and_keeps_waiting()
    {
        await using var listener = LoopbackCallbackListener.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var wait = listener.WaitForCallbackAsync(cts.Token);

        using var favicon = await s_browser.GetAsync(new Uri($"http://127.0.0.1:{listener.Port}/favicon.ico"), cts.Token);
        Assert.Equal(HttpStatusCode.NotFound, favicon.StatusCode);
        Assert.False(wait.IsCompleted);

        using var response = await s_browser.GetAsync(new Uri(listener.RedirectUri, "?code=c&state=s"), cts.Token);
        Assert.Equal("c", (await wait).Code);
    }

    [Fact]
    public async Task Error_callback_shows_failure_page_in_given_language()
    {
        var texts = new LoopbackPageTexts("en", "Rebels of Gaming", "Done.", "Sign-in was not completed.");
        await using var listener = LoopbackCallbackListener.Start(texts);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var wait = listener.WaitForCallbackAsync(cts.Token);

        using var response = await s_browser.GetAsync(new Uri(listener.RedirectUri, "?error=access_denied&error_description=Abgebrochen&state=s"), cts.Token);
        var html = await response.Content.ReadAsStringAsync(cts.Token);
        var result = await wait;

        Assert.True(result.IsError);
        Assert.Equal("access_denied", result.Error);
        Assert.Equal("Abgebrochen", result.ErrorDescription);
        Assert.Contains("Sign-in was not completed.", html);
        Assert.Contains("lang=\"en\"", html);
    }

    [Fact]
    public async Task Waiting_can_be_cancelled()
    {
        await using var listener = LoopbackCallbackListener.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listener.WaitForCallbackAsync(cts.Token));
    }

    [Theory]
    [InlineData("/callback?code=a&state=b", true, "a", "b")]
    [InlineData("/callback", true, null, null)]
    [InlineData("/callback/extra?code=a", false, null, null)]
    [InlineData("/other?code=a", false, null, null)]
    [InlineData("/callback?code=first&code=second&state=s", true, "first", "s")]
    public void Parses_request_target(string target, bool accepted, string? code, string? state)
    {
        Assert.Equal(accepted, LoopbackCallbackListener.TryParseCallback(target, out var callback));
        if (accepted)
        {
            Assert.Equal(code, callback.Code);
            Assert.Equal(state, callback.State);
        }
    }

    [Fact]
    public void Query_values_are_url_decoded()
    {
        var q = LoopbackCallbackListener.ParseQuery("error_description=Konto+gesperrt%20%C3%BC&x");
        Assert.Equal("Konto gesperrt ü", q["error_description"]);
        Assert.Equal(string.Empty, q["x"]);
    }
}
