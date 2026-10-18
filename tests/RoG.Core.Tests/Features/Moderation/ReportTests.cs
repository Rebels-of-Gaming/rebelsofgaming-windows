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
}
