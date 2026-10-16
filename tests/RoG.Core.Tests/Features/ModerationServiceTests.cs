using System.Net;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Moderation;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features;

public class ModerationServiceTests
{
    private static (ModerationService Service, StubHttpHandler Stub) Create(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(status, json));
        var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);
        return (new ModerationService(api.Client), stub);
    }

    [Fact]
    public async Task Reports_are_mapped_with_cursor()
    {
        var (service, stub) = Create("""
            {"data":[{"id":"r1","type":"news_comment","reportable_id":"c1","reason":"insult","comment":"beleidigt","excerpt":"Du …",
              "status":"open","resolution":null,"target":{"id":"u1","name":"Troll","slug":"troll"},"reporter":{"id":"u2","name":"Nyx"},
              "created_at":"2026-10-16T10:00:00Z","handled_at":null}],
             "links":{"first":null,"last":null,"prev":null,"next":null},"meta":{"path":"x","per_page":30,"next_cursor":"abc","prev_cursor":null}}
            """);

        var page = await service.ReportsAsync();

        var report = Assert.Single(page.Items);
        Assert.Equal("Troll", report.Target?.Name);
        Assert.Equal("Nyx", report.Reporter);
        Assert.Equal("abc", page.NextCursor);
        Assert.Contains("filter%5Bstatus%5D=open", stub.Requests[0].Uri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ban_and_report_send_api_values()
    {
        var (service, stub) = Create("""{"data":{"id":"x","status":"open"}}""", HttpStatusCode.Created);

        await service.BanAsync("u1", "forum", " Spam ", 0, isPublic: false);
        await service.ReportAsync("forum_post", "p1", "spam", "Werbung");

        Assert.Contains("\"scope\":\"forum\"", stub.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"Spam\"", stub.Requests[0].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"days\"", stub.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"forum_post\"", stub.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"spam\"", stub.Requests[1].Body, StringComparison.Ordinal);
    }
}
