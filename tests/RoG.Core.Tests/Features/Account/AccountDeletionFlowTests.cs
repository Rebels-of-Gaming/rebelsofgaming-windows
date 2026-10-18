using System.Net;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Account;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Account;

public class AccountDeletionFlowTests
{
    private static ApiProblem Problem(string code, int status, IReadOnlyDictionary<string, IReadOnlyList<string>>? errors = null, TimeSpan? retryAfter = null) =>
        new() { Code = code, Status = status, Errors = errors ?? new Dictionary<string, IReadOnlyList<string>>(), RetryAfter = retryAfter };

    [Fact]
    public void Leave_clan_first_offers_leaving_and_blocks_delete()
    {
        var flow = AccountDeletionFlow.Start.AfterDeleteFailed(Problem("leave_clan_first", 409));

        Assert.Equal(AccountDeletionStage.LeaveRequired, flow.Stage);
        Assert.True(flow.CanLeave);
        Assert.False(flow.CanDelete);
        Assert.False(flow.HasError);
    }

    [Fact]
    public void Left_or_already_out_allows_delete()
    {
        var required = AccountDeletionFlow.Start.AfterDeleteFailed(Problem("leave_clan_first", 409));

        Assert.True(AccountDeletionFlow.AfterLeft().CanDelete);
        var already = required.AfterLeaveFailed(Problem("not_a_member", 409));
        Assert.Equal(AccountDeletionStage.Left, already.Stage);
        Assert.True(already.CanDelete);
    }

    [Theory]
    [InlineData("owner_cannot_leave")]
    [InlineData("owner_cannot_delete")]
    public void Owner_is_blocked(string code)
    {
        var required = AccountDeletionFlow.Start.AfterDeleteFailed(Problem("leave_clan_first", 409));
        var flow = code == "owner_cannot_leave" ? required.AfterLeaveFailed(Problem(code, 409)) : AccountDeletionFlow.Start.AfterDeleteFailed(Problem(code, 409));

        Assert.Equal(AccountDeletionStage.Blocked, flow.Stage);
        Assert.Equal(code, flow.ErrorCode);
        Assert.False(flow.CanDelete);
        Assert.False(flow.CanLeave);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(405)]
    public void Older_website_falls_back_to_hint(int status)
    {
        var required = AccountDeletionFlow.Start.AfterDeleteFailed(Problem("leave_clan_first", 409));

        var flow = required.AfterLeaveFailed(Problem("not_found", status));

        Assert.Equal(AccountDeletionStage.LeaveUnavailable, flow.Stage);
        Assert.False(flow.CanLeave);
        // erneutes Löschen bleibt beim Hinweis statt wieder den Austritt anzubieten
        Assert.Equal(AccountDeletionStage.LeaveUnavailable, flow.AfterDeleteFailed(Problem("leave_clan_first", 409)).Stage);
    }

    [Fact]
    public void Validation_and_throttling_keep_the_stage()
    {
        var required = AccountDeletionFlow.Start.AfterDeleteFailed(Problem("leave_clan_first", 409));

        var wrong = required.AfterLeaveFailed(Problem("validation_failed", 422, new Dictionary<string, IReadOnlyList<string>> { ["confirmation"] = ["Falsches Passwort."] }));
        Assert.Equal(AccountDeletionStage.LeaveRequired, wrong.Stage);
        Assert.Equal("Falsches Passwort.", wrong.FieldError);

        var throttled = required.AfterLeaveFailed(Problem("too_many_attempts", 429, retryAfter: TimeSpan.FromSeconds(60)));
        Assert.Equal(AccountDeletionStage.LeaveRequired, throttled.Stage);
        Assert.Equal("too_many_attempts", throttled.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(60), throttled.RetryAfter);
        Assert.Null(throttled.FieldError);
    }

    [Fact]
    public async Task Leave_clan_posts_confirmation()
    {
        var tokens = new InMemoryTokenStore();
        await tokens.SetAsync(new StoredToken("42|tok", "01U", "Wahke", ["profile:write"], "PC", DateTimeOffset.UtcNow));
        string? body = null;
        HttpRequestMessage? request = null;
        var stub = new StubHttpHandler((r, _) =>
        {
            request = r;
            body = r.Content!.ReadAsStringAsync().Result;
            return StubHttpHandler.Json(HttpStatusCode.OK, """{"data":{"is_member":false,"status":"former","rank":null}}""");
        });
        var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), tokens, new ApiContext(), innerHandler: stub);

        var stillMember = await new AccountDataService(api.Client, tokens).LeaveClanAsync("geheim");

        Assert.False(stillMember);
        Assert.Equal(HttpMethod.Post, request!.Method);
        Assert.EndsWith("/v1/me/leave-clan", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("\"confirmation\":\"geheim\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leave_clan_conflict_surfaces_problem()
    {
        var tokens = new InMemoryTokenStore();
        await tokens.SetAsync(new StoredToken("42|tok", "01U", "Wahke", ["profile:write"], "PC", DateTimeOffset.UtcNow));
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.Conflict, """{"type":"about:blank","title":"Conflict","status":409,"code":"owner_cannot_leave"}"""));
        var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), tokens, new ApiContext(), innerHandler: stub);

        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => new AccountDataService(api.Client, tokens).LeaveClanAsync("geheim"));

        Assert.Equal(AccountDeletionStage.Blocked, AccountDeletionFlow.Start.AfterLeaveFailed(ex.Problem).Stage);
    }
}
