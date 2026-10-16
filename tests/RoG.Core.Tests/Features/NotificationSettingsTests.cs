using System.Net;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Notifications;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features;

public class NotificationSettingsTests
{
    [Fact]
    public void Effective_channels_follow_preferences_defaults_and_inbox_rule()
    {
        using var types = System.Text.Json.JsonDocument.Parse("""
            {"data":[{"key":"forum.reply","group":"forum","label":"Antwort","defaults":["in_app"],"channels":["in_app","mail","discord","push"],"inbox_optional":false},
                     {"key":"news.published","group":"news","label":"News","defaults":["in_app"],"channels":["in_app","mail"],"inbox_optional":true}]}
            """);
        using var settings = System.Text.Json.JsonDocument.Parse("""
            {"data":{"preferences":{"forum.reply":["mail"],"news.published":[]},"digest":"daily","quiet_from":"22:00","quiet_until":"08:00",
                     "available":{"mail":true,"discord":false,"push":true},"vapid_public_key":null}}
            """);

        var t = NotificationSettingsService.ParseTypes(types.RootElement);
        var s = NotificationSettingsService.ParseSettings(settings.RootElement);

        Assert.Equal(new HashSet<string> { "mail", "in_app" }, s.Effective(t[0]));
        Assert.Empty(s.Effective(t[1]));
        Assert.Contains("mail", s.Available);
        Assert.DoesNotContain("discord", s.Available);
        Assert.Equal("daily", s.Digest);
    }

    [Fact]
    public async Task Channel_change_uses_object_form()
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """{"data":{}}"""));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);

        await new NotificationSettingsService(api.Http).SetChannelAsync("news.published", "in_app", false);

        Assert.Equal(HttpMethod.Patch, stub.Requests[0].Method);
        Assert.Contains("{\"preferences\":{\"news.published\":{\"in_app\":false}}}", stub.Requests[0].Body, StringComparison.Ordinal);
    }
}
