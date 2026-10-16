using System.Net;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Uploads;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Uploads;

public class UploadServiceTests
{
    private const string UploadJson = """
        {"id":"01UP","code":"aB3xYz","status":"ready","error":null,"title":null,"description":null,"alt":null,"alt_text":"","library":false,
         "folder":null,"original_name":"shot.png","mime":"image/webp","kind":"image","size":1234,"width":10,"height":10,"duration_ms":null,
         "animated":false,"visibility":"public","album_id":null,"views":0,"expires_at":null,"created_at":"2026-10-16T20:00:00Z","usages_count":0,
         "user":null,"variants":[],"links":{"preview":"https://i.rogclan.de/aB3xYz","direct":"https://i.rogclan.de/aB3xYz.webp","thumb":"https://i.rogclan.de/aB3xYz/thumb.webp",
         "medium":null,"poster":null,"bbcode":"[img]https://i.rogclan.de/aB3xYz.webp[/img]","markdown":"![](https://i.rogclan.de/aB3xYz.webp)","html":"<img>","manage":"x","delete":null,"signed":null}}
        """;

    [Fact]
    public async Task Presign_put_to_storage_without_token_then_complete()
    {
        var tokens = new InMemoryTokenStore();
        await tokens.SetAsync(new StoredToken("42|secret", "01U", "Wahke", ["uploads:write"], "PC", DateTimeOffset.UtcNow));
        var api = new StubHttpHandler((r, _) => r.RequestUri!.AbsolutePath.EndsWith("/presign", StringComparison.Ordinal)
            ? StubHttpHandler.Json(HttpStatusCode.Created, "{\"data\":{\"upload\":" + UploadJson.Replace("\"ready\"", "\"awaiting\"", StringComparison.Ordinal)
                + ",\"upload_url\":\"https://storage.example.test/bucket/x?sig=1\",\"method\":\"PUT\",\"headers\":{\"Content-Type\":\"image/png\",\"x-amz-acl\":\"private\"},\"expires_at\":\"2026-10-16T20:15:00Z\"}}")
            : StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":" + UploadJson + "}"));
        var storage = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var rog = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), tokens, new ApiContext(), innerHandler: api);
        var service = new UploadService(rog.Client, new HttpClient(storage));
        var reports = new List<double>();

        var item = await service.UploadAsync(new MemoryStream(new byte[1234]), "shot.png", 1234, UploadVisibility.Public, new SyncProgress(reports));

        using var presign = JsonDocument.Parse(api.Requests[0].Body!);
        Assert.Equal("image/png", presign.RootElement.GetProperty("mime").GetString());
        Assert.Equal(1234, presign.RootElement.GetProperty("size").GetInt32());

        var put = Assert.Single(storage.Requests);
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal("storage.example.test", put.Uri!.Host);
        Assert.False(put.Headers.ContainsKey("Authorization"));
        Assert.Equal("private", put.Headers["x-amz-acl"]);
        Assert.Equal("image/png", put.Headers["Content-Type"]);

        Assert.EndsWith("/uploads/01UP/complete", api.Requests[1].Uri!.AbsolutePath, StringComparison.Ordinal);
        Assert.True(item.IsReady);
        Assert.Equal("https://i.rogclan.de/aB3xYz.webp", item.Links.Direct!.ToString());
        Assert.Equal(1.0, reports[^1]);
    }

    [Theory]
    [InlineData("a.PNG", "image/png")]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("setup.exe", null)]
    public void Mime_from_extension(string name, string? mime) => Assert.Equal(mime, UploadService.MimeFor(name));

    private sealed class SyncProgress(List<double> target) : IProgress<double>
    {
        public void Report(double value) => target.Add(value);
    }
}
