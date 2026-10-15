using System.Net;
using System.Net.Http.Headers;
using System.Text;
using RoG.Core.Api;

namespace RoG.Core.Tests.Api;

public class ApiProblemTests
{
    [Fact]
    public void Parses_rfc9457_validation_problem()
    {
        var problem = ApiProblem.Parse("""
            {"type":"https://api.rebelsofgaming.com/docs/errors#validation_failed","title":"Eingaben ungültig","status":422,
             "detail":"Bezeichnung (DE) ist ein Pflichtfeld.","instance":"/v1/roles","code":"validation_failed",
             "errors":{"label.de":["Bezeichnung (DE) ist ein Pflichtfeld."],"name":"einzeln"}}
            """, 422);

        Assert.Equal("validation_failed", problem.Code);
        Assert.Equal(422, problem.Status);
        Assert.True(problem.IsValidation);
        Assert.Equal("Eingaben ungültig", problem.Title);
        Assert.Equal("/v1/roles", problem.Instance);
        Assert.Equal(["Bezeichnung (DE) ist ein Pflichtfeld."], problem.Errors["label.de"]);
        Assert.Equal(["einzeln"], problem.Errors["name"]);
    }

    [Fact]
    public void Oauth_error_field_is_used_when_code_is_missing()
    {
        var problem = ApiProblem.Parse("""{"error":"invalid_grant","error_description":"Code abgelaufen"}""", 400);
        Assert.Equal("invalid_grant", problem.Code);
        Assert.Equal("Code abgelaufen", problem.Detail);
    }

    [Theory]
    [InlineData(401, "unauthenticated")]
    [InlineData(403, "forbidden")]
    [InlineData(404, "not_found")]
    [InlineData(429, "rate_limited")]
    [InlineData(502, "server_error")]
    [InlineData(418, "unknown_error")]
    public void Code_falls_back_to_status(int status, string code)
    {
        Assert.Equal(code, ApiProblem.Parse("<html>Bad Gateway</html>", status).Code);
        Assert.Equal(code, ApiProblem.Parse(string.Empty, status).Code);
    }

    [Fact]
    public void Laravel_message_without_code_counts_as_title()
    {
        var problem = ApiProblem.Parse("""{"message":"Unauthenticated."}""", 401);
        Assert.Equal("unauthenticated", problem.Code);
        Assert.Equal("Unauthenticated.", problem.Title);
        Assert.True(problem.IsUnauthorized);
    }

    [Fact]
    public void Retry_after_seconds_and_date()
    {
        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(30), ApiProblem.ReadRetryAfter(new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)), now));
        Assert.Equal(TimeSpan.FromMinutes(2), ApiProblem.ReadRetryAfter(new RetryConditionHeaderValue(now.AddMinutes(2)), now));
        Assert.Equal(TimeSpan.Zero, ApiProblem.ReadRetryAfter(new RetryConditionHeaderValue(now.AddMinutes(-2)), now));
        Assert.Null(ApiProblem.ReadRetryAfter(null, now));
    }

    [Fact]
    public async Task Reads_problem_from_response_including_retry_after()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{"status":429,"code":"too_many_attempts","title":"Zu viele Versuche"}""", Encoding.UTF8, "application/problem+json"),
        };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(12));

        var problem = await ApiProblem.FromResponseAsync(response);

        Assert.Equal("too_many_attempts", problem.Code);
        Assert.True(problem.IsRateLimited);
        Assert.Equal(TimeSpan.FromSeconds(12), problem.RetryAfter);
    }

    [Fact]
    public void Network_problem_has_status_zero()
    {
        Assert.True(ApiProblem.Network().IsNetworkError);
        Assert.Equal("timeout", ApiProblem.Network(timeout: true).Code);
    }
}
