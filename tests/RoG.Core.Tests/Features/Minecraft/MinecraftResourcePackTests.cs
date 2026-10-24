using System.Net;
using System.Text.Json.Nodes;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Minecraft;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Minecraft;

public class MinecraftResourcePackTests
{
    private const string Sha1 = "3f786850e387550fdab836ed7e6dc881de23001b";

    private const string Pack = """
        {"data":{"url":"https://cdn.rogclan.de/minecraft/resourcepack/rog-sounds-3f786850e387550fdab836ed7e6dc881de23001b.zip",
          "latest_url":"https://cdn.rogclan.de/minecraft/resourcepack/rog-sounds-latest.zip",
          "sha1":"3f786850e387550fdab836ed7e6dc881de23001b","size":1048576,"updated_at":"2026-10-24T18:00:00Z"}}
        """;

    private static (RoGApi Api, StubHttpHandler Stub) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        return (new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub), stub);
    }

    [Fact]
    public async Task Pack_is_parsed()
    {
        var (api, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, Pack));
        using (api)
        {
            var pack = await new MinecraftService(api.Http).ResourcePackAsync();

            Assert.NotNull(pack);
            Assert.EndsWith("rog-sounds-" + Sha1 + ".zip", pack.Url.AbsolutePath, StringComparison.Ordinal);
            Assert.EndsWith("rog-sounds-latest.zip", pack.LatestUrl!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal(Sha1, pack.Sha1);
            Assert.Equal(1048576, pack.Size);
            Assert.NotNull(pack.UpdatedAt);
        }
    }

    [Fact]
    public async Task Missing_pack_is_null()
    {
        var (api, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.NotFound,
            """{"type":"about:blank","title":"Nicht gefunden","status":404,"code":"resource_pack_not_found"}"""));
        using (api)
        {
            Assert.Null(await new MinecraftService(api.Http).ResourcePackAsync());
        }
    }

    [Fact]
    public void Invalid_urls_or_hashes_give_no_pack()
    {
        Assert.Null(MinecraftService.ParseResourcePack(JsonNode.Parse("""{"url":"http://x/a.zip","sha1":"3f786850e387550fdab836ed7e6dc881de23001b"}""")));
        Assert.Null(MinecraftService.ParseResourcePack(JsonNode.Parse("""{"url":"https://x/a.zip","sha1":"abc"}""")));
        var pack = MinecraftService.ParseResourcePack(JsonNode.Parse("""{"url":"https://x/a.zip","latest_url":null,"sha1":"3F786850E387550FDAB836ED7E6DC881DE23001B","size":-1}"""));
        Assert.Null(pack!.LatestUrl);
        Assert.Equal(Sha1, pack.Sha1);
        Assert.Equal(0, pack.Size);
    }

    [Fact]
    public async Task Upload_sends_the_zip_as_multipart_file()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Created, Pack));
        using (api)
        {
            using var content = new MemoryStream([0x50, 0x4B, 0x03, 0x04]);
            var pack = await new MinecraftService(api.Http).UploadResourcePackAsync(content, @"C:\Packs\rog-sounds.ZIP");

            var request = Assert.Single(stub.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/v1/minecraft/resource-pack", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("name=file", request.Body, StringComparison.Ordinal);
            Assert.Contains("filename=rog-sounds.ZIP", request.Body, StringComparison.Ordinal);
            Assert.Contains("application/zip", request.Body, StringComparison.Ordinal);
            Assert.Equal(Sha1, pack!.Sha1);
        }
    }

    [Fact]
    public async Task Only_zip_files_are_uploaded()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Created, Pack));
        using (api)
        {
            using var content = new MemoryStream();
            await Assert.ThrowsAsync<ArgumentException>(() => new MinecraftService(api.Http).UploadResourcePackAsync(content, "pack.rar"));
            Assert.Empty(stub.Requests);
        }
    }

    [Fact]
    public async Task Delete_calls_the_endpoint()
    {
        var (api, stub) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        using (api)
        {
            await new MinecraftService(api.Http).DeleteResourcePackAsync();

            Assert.Equal(HttpMethod.Delete, Assert.Single(stub.Requests).Method);
        }
    }

    [Theory]
    [InlineData("pack.zip", true)]
    [InlineData("PACK.ZIP", true)]
    [InlineData("pack.zip.exe", false)]
    [InlineData("pack", false)]
    [InlineData(null, false)]
    public void Resource_pack_names_must_be_zip(string? name, bool valid) =>
        Assert.Equal(valid, MinecraftService.IsResourcePackName(name));
}
