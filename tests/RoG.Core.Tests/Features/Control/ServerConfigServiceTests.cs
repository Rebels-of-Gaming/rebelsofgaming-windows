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
