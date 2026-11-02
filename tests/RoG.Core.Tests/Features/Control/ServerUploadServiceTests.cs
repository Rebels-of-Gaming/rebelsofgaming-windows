using System.Net;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Control;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Control;

public class ServerUploadServiceTests
{
    private const string Upload = """
        {"id":"01U","directory":"etmain","filename":"supply_v2.pk3","path":"etmain/supply_v2.pk3","size":5,"checksum":null,
         "status":"awaiting","error":null,"requires_approval":true,"approval_reason":"code",
         "entries":{"total":2,"items":["maps/supply.bsp","qagame.mp.i386.so"],"code":["qagame.mp.i386.so"]},
         "overwrite":false,"via":"app","note":"Neue Version","author":{"id":"01A","name":"Wahke","slug":"wahke"},
         "decided_by":null,"decided_at":null,"decision_note":null,"expires_at":null,"completed_at":null,"created_at":"2026-11-02T10:00:00Z","updated_at":null}
        """;

    [Fact]
    public async Task Targets_list_extensions_limits_and_approval()
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """
            {"data":{"custom":false,"targets":[{"directory":"plugins","extensions":["jar"],"max_mb":100,"approval":false,"approval_extensions":["jar"]},
              {"directory":"etmain","extensions":"pk3, .PK3","max_mb":200,"approval":false,"approval_extensions":[]}]}}
            """));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);

        var targets = await new ServerUploadService(api.Http, new HttpClient(stub)).TargetsAsync("rog-mc");

        Assert.EndsWith("/v1/servers/rog-mc/files/uploads/targets", stub.Requests[0].Uri!.AbsolutePath, StringComparison.Ordinal);
        Assert.True(targets[0].NeedsApproval("RoGCore.JAR"));
        Assert.Equal(["pk3", "pk3"], targets[1].Extensions);
        Assert.Equal(200L * 1024 * 1024, targets[1].MaxBytes);
        Assert.False(targets[1].NeedsApproval("map.pk3"));
    }

    [Fact]
    public async Task Upload_registers_puts_to_storage_without_token_and_completes()
    {
        var stub = new StubHttpHandler((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/v1/servers/rog-et/files/uploads" => StubHttpHandler.Json(HttpStatusCode.Created,
                "{\"data\":{\"upload\":" + Upload + ",\"upload_url\":\"https://s3.rogclan.de/tmp/01U?X-Amz-Signature=abc\",\"method\":\"PUT\",\"headers\":{\"Content-Type\":\"application/octet-stream\"},\"expires_at\":\"2026-11-02T10:15:00Z\"}}"),
            "/tmp/01U" => new HttpResponseMessage(HttpStatusCode.OK),
            _ => StubHttpHandler.Json(HttpStatusCode.Accepted, "{\"data\":" + Upload.Replace("\"awaiting\"", "\"verifying\"", StringComparison.Ordinal) + "}"),
        });
        var tokens = new InMemoryTokenStore();
        await tokens.SetAsync(new StoredToken("42|tok", "01A", "Wahke", ["servers:files"], "PC", DateTimeOffset.UtcNow));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), tokens, new ApiContext(), innerHandler: stub);
        using var storage = new HttpClient(stub);
        var progress = new List<double>();

        var result = await new ServerUploadService(api.Http, storage).UploadAsync(
            "rog-et", "etmain", @"C:\Maps\supply v2.pk3", new MemoryStream([1, 2, 3, 4, 5]), 5, note: " Neue Version ", progress: new SyncProgress(progress));

        Assert.Equal("verifying", result!.Status);
        Assert.Equal(3, stub.Requests.Count);
        using (var body = JsonDocument.Parse(stub.Requests[0].Body!))
        {
            Assert.Equal("supply_v2.pk3", body.RootElement.GetProperty("filename").GetString());
            Assert.Equal(5, body.RootElement.GetProperty("size").GetInt64());
            Assert.Equal("Neue Version", body.RootElement.GetProperty("note").GetString());
            Assert.False(body.RootElement.TryGetProperty("overwrite", out _));
        }

        Assert.Equal(HttpMethod.Put, stub.Requests[1].Method);
        Assert.False(stub.Requests[1].Headers.ContainsKey("Authorization"));
        Assert.EndsWith("/01U/complete", stub.Requests[2].Uri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(1d, progress[^1]);
    }

    [Fact]
    public void Parses_archive_entries_for_reviewers()
    {
        var upload = ServerUploadService.Parse(System.Text.Json.Nodes.JsonNode.Parse(Upload))!;

        Assert.Equal("code", upload.ApprovalReason);
        Assert.Equal(["qagame.mp.i386.so"], upload.CodeEntries);
        Assert.Equal(2, upload.EntriesTotal);
        Assert.Equal("Wahke", upload.Author!.Name);
        Assert.True(upload.IsCancellable);
        Assert.True(upload.IsOpen);
    }

    [Theory]
    [InlineData("map.pk3", 10, null)]
    [InlineData("map.zip", 10, "extension_not_allowed")]
    [InlineData("map.pk3", 300L * 1024 * 1024, "file_too_large")]
    [InlineData("noext", 10, "invalid_filename")]
    [InlineData("map.pk3", 0, "invalid_file")]
    public void Validates_like_the_server(string name, long size, string? expected)
    {
        var target = new UploadTarget("etmain", ["pk3"], 200, false, []);
        Assert.Equal(expected, ServerUploadService.Validate(target, name, size));
    }

    [Fact]
    public void File_names_are_cleaned_like_the_website()
    {
        Assert.Equal("supply_v2__final_.pk3", ServerUploadService.SafeFileName("/home/x/supply v2 (final).pk3"));
        Assert.Equal("pk3", ServerUploadService.Extension("Map.PK3"));
        Assert.Equal(string.Empty, ServerUploadService.Extension(".hidden"));
    }

    private sealed class SyncProgress(List<double> values) : IProgress<double>
    {
        public void Report(double value) => values.Add(value);
    }
}
