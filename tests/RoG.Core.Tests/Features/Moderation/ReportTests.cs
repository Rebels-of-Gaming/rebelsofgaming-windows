using System.Net;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Moderation;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Moderation;

public class ReportTests
{
    private static (RoGApi Api, StubHttpHandler Stub) Create(string json, HttpStatusCode status = HttpStatusCode.Created)
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(status, json));
        return (new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub), stub);
    }

    [Theory]
    [InlineData("wiki_page", "fr", "\"locale\":\"fr\"")]
    [InlineData("wiki_revision", "fr", null)]
    [InlineData("gallery_item", null, null)]
    public async Task Report_sends_type_and_locale_only_for_wiki_pages(string type, string? locale, string? expectedLocale)
    {
        var (api, stub) = Create("""{"data":{"id":"r1","status":"open"}}""");
        using (api)
        {
            await new ModerationService(api.Client).ReportAsync(type, "01J", "spam", null, locale);

            var body = Assert.Single(stub.Requests).Body!;
            Assert.Contains($"\"type\":\"{type}\"", body, StringComparison.Ordinal);
            if (expectedLocale is null)
            {
                Assert.DoesNotContain("\"locale\":\"", body, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains(expectedLocale, body, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task Moderation_reports_carry_link_and_locale()
    {
        var (api, _) = Create("""
            {"data":[{"id":"r1","type":"wiki_page","reportable_id":"01J","reason":"spam","comment":null,"excerpt":"x",
              "url":"https://rebelsofgaming.com/fr/wiki/main/regeln","locale":"fr","status":"open","resolution":null,
              "target":null,"reporter":{"name":"Wahke"},"created_at":"2026-10-18T10:00:00Z","handled_at":null}],
             "meta":{"next_cursor":null}}
            """, HttpStatusCode.OK);
        using (api)
        {
            var report = Assert.Single((await new ModerationService(api.Client).ReportsAsync()).Items);

            Assert.Equal(new Uri("https://rebelsofgaming.com/fr/wiki/main/regeln"), report.Url);
            Assert.Equal("fr", report.Locale);
        }
    }

    [Fact]
    public async Task Minecraft_reports_carry_source_and_players()
    {
        // Form wie in Guide 28 „Meldungen aus dem Minecraft-Netzwerk“ (v0.38.0)
        var (api, stub) = Create("""
            {"data":[{"id":"r2","type":"minecraft_player","reportable_id":"069a79f4-44e9-4726-a5be-fca90e38aaf5","reason":"harassment","priority":0,
              "comment":"Beleidigt mich","excerpt":"[22.10. 12:00] Notch → jeb_: du bist so schlecht","url":null,"locale":null,"status":"open",
              "source":"minecraft",
              "details":{"reporter":{"uuid":"853c80ef-3c37-49fd-aa49-938b674adae6","name":"jeb_","edition":"java"},
                         "reported":{"uuid":"069a79f4-44e9-4726-a5be-fca90e38aaf5","name":"Notch","edition":"java"},
                         "server":{"id":"01J","name":"RoG Network"},"network_server":"lobby","plugin_key_id":"01J",
                         "messages":[{"from_uuid":"069a79f4-44e9-4726-a5be-fca90e38aaf5","to_uuid":"853c80ef-3c37-49fd-aa49-938b674adae6","message":"du bist so schlecht","at":"2026-10-22T12:00:00Z"}]},
              "resolution":null,"target":null,"reporter":{"id":"01J","name":"Jeb"},"created_at":"2026-10-22T12:01:00Z","handled_at":null}],
             "meta":{"next_cursor":null}}
            """, HttpStatusCode.OK);
        using (api)
        {
            var report = Assert.Single((await new ModerationService(api.Client).ReportsAsync(source: "minecraft")).Items);

            Assert.Contains("filter[source]=minecraft", Uri.UnescapeDataString(stub.Requests[0].Uri!.Query), StringComparison.Ordinal);
            Assert.True(report.FromMinecraft);
            Assert.Equal("Notch", report.Game!.Reported!.Name);
            Assert.Equal("jeb_", report.Game.Reporter!.Name);
            Assert.Equal("RoG Network", report.Game.ServerName);
            Assert.Equal("lobby", report.Game.NetworkServer);
            Assert.Equal(1, report.Game.MessageCount);
            Assert.Null(report.Target);
        }
    }

    [Fact]
    public void Child_safety_is_the_first_reason()
    {
        Assert.Equal("child_safety", ModerationService.ReportReasons[0]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.OK)] // v0.30.4: erneute Meldung öffnet eine alte wieder
    public async Task Child_safety_report_is_sent_and_reopen_counts_as_success(HttpStatusCode status)
    {
        var (api, stub) = Create("""{"data":{"id":"r1","status":"open"}}""", status);
        using (api)
        {
            await new ModerationService(api.Client).ReportAsync("gallery_item", "01J", "child_safety", null);

            Assert.Contains("\"reason\":\"child_safety\"", Assert.Single(stub.Requests).Body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Too_many_child_safety_reports_carry_retry_after()
    {
        var stub = new StubHttpHandler((_, _) =>
        {
            var r = StubHttpHandler.Json(HttpStatusCode.TooManyRequests, """{"type":"about:blank","title":"Too Many Requests","status":429,"code":"too_many_attempts"}""");
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            return r;
        });
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);

        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => new ModerationService(api.Client).ReportAsync("profile", "01U", "child_safety", null));

        Assert.True(ex.Problem.IsRateLimited);
        Assert.Equal(TimeSpan.FromSeconds(90), ex.Problem.RetryAfter);
    }

    [Fact]
    public async Task Moderation_list_filters_by_reason_and_puts_child_safety_first()
    {
        var (api, stub) = Create("""
            {"data":[
              {"id":"r1","type":"profile","reportable_id":"01U","reason":"spam","comment":null,"excerpt":null,"url":null,"locale":null,
               "status":"open","resolution":null,"priority":0,"target":null,"reporter":null,"created_at":"2026-10-18T11:00:00Z","handled_at":null},
              {"id":"r2","type":"profile","reportable_id":"01X","reason":"child_safety","comment":null,"excerpt":null,"url":null,"locale":null,
               "status":"open","resolution":null,"priority":1,"target":null,"reporter":null,"created_at":"2026-10-18T10:00:00Z","handled_at":null}],
             "meta":{"next_cursor":"c2"}}
            """, HttpStatusCode.OK);
        using (api)
        {
            var page = await new ModerationService(api.Client).ReportsAsync("open", cursor: "c1", reason: "child_safety");

            var query = Uri.UnescapeDataString(Assert.Single(stub.Requests).Uri!.Query);
            Assert.Contains("filter[reason]=child_safety", query, StringComparison.Ordinal);
            Assert.Contains("cursor=c1", query, StringComparison.Ordinal);
            Assert.DoesNotContain("filter[reason]=c1", query, StringComparison.Ordinal);
            Assert.Equal(["r2", "r1"], page.Items.Select(r => r.Id));
            Assert.True(page.Items[0].IsChildSafety);
            Assert.Equal(1, page.Items[0].Priority);
        }
    }
}
