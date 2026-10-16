using System.Net;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Control;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Control;

public class ServerConfigServiceTests
{
    private static (RoGApi Api, StubHttpHandler Stub) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);
        return (api, stub);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, ConfigSaveOutcome.Saved)]
    [InlineData(HttpStatusCode.Accepted, ConfigSaveOutcome.Pending)]
    public async Task Save_reports_saved_or_pending(HttpStatusCode status, ConfigSaveOutcome expected)
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(status, """{"data":{"pending":true}}"""));
        using (api)
        {
            var outcome = await new ServerConfigService(api.Client, api.Http).SaveAsync("et-main", "01F", "set a 1\n", ConfigAfter.Exec, "Slots");

            Assert.Equal(expected, outcome);
            var request = Assert.Single(stub.Requests);
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.EndsWith("/v1/servers/et-main/configs/01F", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("\"after\":\"exec\"", request.Body, StringComparison.Ordinal);
            Assert.Contains("\"note\":\"Slots\"", request.Body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Preview_maps_diff_and_errors()
    {
        var (api, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """
            {"data":{"diff":[{"type":"remove","text":"set a 1","old":1,"new":null},{"type":"add","text":"set a 2","old":null,"new":1}],
                     "errors":["sv_cheats 1 ist nicht erlaubt"],"changed":true}}
            """));
        using (api)
        {
            var preview = await new ServerConfigService(api.Client, api.Http).PreviewAsync("et-main", "01F", "set a 2\n");

            Assert.True(preview.Changed);
            Assert.Equal(["− set a 1", "+ set a 2"], preview.Diff.Select(d => d.Display));
            Assert.Equal("sv_cheats 1 ist nicht erlaubt", Assert.Single(preview.Errors));
        }
    }

    [Fact]
    public async Task Backups_are_listed_newest_first()
    {
        var (api, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """
            {"data":[
              {"uuid":"a","name":"alt","bytes":1048576,"created_at":"2026-10-01T10:00:00Z","completed_at":"2026-10-01T10:01:00Z","successful":true,"locked":true},
              {"uuid":"b","name":"neu","bytes":0,"created_at":"2026-10-16T10:00:00Z","completed_at":null,"successful":false,"locked":false}]}
            """));
        using (api)
        {
            var backups = await new ServerBackupService(api.Client).ListAsync("et-main");

            Assert.Equal(["b", "a"], backups.Select(b => b.Uuid));
            Assert.False(backups[0].Completed);
            Assert.True(backups[1].Locked);
        }
    }
}

public class ServerModerationServiceTests
{
    [Fact]
    public async Task Ban_sends_body_the_spec_is_missing()
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.Created, """{"data":{}}"""));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);

        await new ServerModerationService(api.Client, api.Http).BanAsync("mc", null, " Griefer_1 ", 60, "Griefing");

        var body = Assert.Single(stub.Requests).Body!;
        Assert.Contains("\"player_name\":\"Griefer_1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"minutes\":60", body, StringComparison.Ordinal);
        Assert.DoesNotContain("player_id", body, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ArgumentException>(() => new ServerModerationService(api.Client, api.Http).BanAsync("mc", " ", null, 0, null));
    }

    [Fact]
    public void Changes_are_parsed_with_diff_list_or_text()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {"data":[
              {"id":"c1","file":{"id":"f","path":"etmain/server.cfg"},"status":"pending","via":"editor","after":"save","note":"Name",
               "author":{"id":"u","name":"Kolibri","slug":"kolibri"},"created_at":"2026-10-13T18:02:11Z",
               "diff":[{"type":"remove","text":"a"},{"type":"add","text":"b"}],"outdated":false},
              {"id":"c2","file":{"id":"f","path":"x.cfg"},"status":"pending","via":"editor","after":"restart","note":null,
               "author":null,"created_at":null,"diff":"--- a\n+++ b","outdated":true}]}
            """);

        var changes = ServerModerationService.ParseChanges(doc.RootElement);

        Assert.Equal("− a\n+ b", changes[0].Diff);
        Assert.Equal("Kolibri", changes[0].Author);
        Assert.True(changes[1].Outdated);
        Assert.Equal("--- a\n+++ b", changes[1].Diff);
    }
}
