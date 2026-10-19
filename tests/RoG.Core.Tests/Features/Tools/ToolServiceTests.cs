using System.Net;
using System.Text.Json.Nodes;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Tools;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Tools;

public class ToolServiceTests
{
    private static (ToolService Service, StubHttpHandler Stub, RoGApi Api) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);
        return (new ToolService(api.Http), stub, api);
    }

    private const string List = """
        {"data":[
          {"id":"t1","slug":"et-nickname","name":"ET-Nickname-Generator","description":null,"icon":"sparkles","visibility":"guests","status":"published",
           "game":{"id":"g1","slug":"et","name":"Wolfenstein: Enemy Territory","short":"ET"},"squad":null,"version":3,"updated_at":"2026-10-19T12:00:00Z","url":"https://rebelsofgaming.com/de/tools/et-nickname"},
          {"id":"t2","slug":"notes","name":"Notizen","description":"x","icon":"note","visibility":"members","status":"draft","game":null,"squad":{"id":"s1","slug":"la","name":"LA Squad"},"version":null,"updated_at":null,"url":"https://rebelsofgaming.com/de/tools/notes"},
          {"id":"t3","slug":"heroes","name":"Helden","description":null,"icon":"users","visibility":"squad","status":"published",
           "game":{"id":"g2","slug":"last-asylum","name":"Last Asylum","short":null},"squad":null,"version":1,"updated_at":null,"url":"https://rebelsofgaming.com/de/tools/heroes"},
          {"id":"t4","slug":"et-maps","name":"ET-Karten","description":null,"icon":"map","visibility":"guests","status":"published",
           "game":{"id":"g1","slug":"et","name":"Wolfenstein: Enemy Territory","short":"ET"},"squad":null,"version":1,"updated_at":null,"url":"https://rebelsofgaming.com/de/tools/et-maps"}]}
        """;

    [Fact]
    public async Task List_filters_and_groups_by_game()
    {
        var (service, stub, api) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, List));
        using (api)
        {
            var tools = await service.ListAsync(squad: "la", includeDrafts: true);
            var groups = ToolService.ByGame(tools);

            var query = Uri.UnescapeDataString(Assert.Single(stub.Requests).Uri!.Query);
            Assert.Contains("filter[squad]=la", query, StringComparison.Ordinal);
            Assert.Contains("filter[status]=all", query, StringComparison.Ordinal);
            Assert.Equal(4, tools.Count);
            Assert.True(tools[1].IsDraft);
            Assert.Equal("LA Squad", tools[1].Squad?.Name);
            Assert.Equal(["et", "last-asylum", null], groups.Select(g => g.Game?.Slug));
            Assert.Equal(["et-nickname", "et-maps"], groups[0].Tools.Select(t => t.Slug));
        }
    }

    [Fact]
    public async Task Document_is_read_from_data()
    {
        var (service, stub, api) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """{"data":{"document":"<!doctype html><html></html>","csp":"default-src 'none'","sandbox":"allow-scripts","sdk_version":"1","version":3,"theme":"dark","locale":"fr"}}"""));
        using (api)
        {
            var doc = await service.DocumentAsync("et-nickname");

            Assert.Equal("<!doctype html><html></html>", doc.Html);
            Assert.Equal("fr", doc.Locale);
            Assert.EndsWith("/v1/tools/et-nickname/document", Assert.Single(stub.Requests).Uri!.AbsolutePath, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Data_set_puts_value_and_escapes_the_path()
    {
        var (service, stub, api) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """{"data":{"key":"my.heroes","value":{"a":1},"updated_at":"2026-10-19T12:00:00Z","updated_by":null}}"""));
        using (api)
        {
            var data = await service.DataSetAsync("heroes", "personal", "my.heroes", JsonNode.Parse("""{"a":1}"""), CancellationToken.None);

            var request = Assert.Single(stub.Requests);
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.EndsWith("/v1/tools/heroes/data/personal/my.heroes", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("""{"value":{"a":1}}""", request.Body);
            Assert.True(request.Headers.ContainsKey(RoGHttpHandler.IdempotencyHeader));
            Assert.Equal(1, (int)data!["value"]!["a"]!);
        }
    }

    [Fact]
    public async Task Lists_carry_truncated()
    {
        var (service, stub, api) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """{"data":[{"user":{"id":"u","name":"Ben"},"value":2,"updated_at":null}],"meta":{"truncated":true}}"""));
        using (api)
        {
            var page = await service.MembersAsync("heroes", "my.heroes", CancellationToken.None);

            Assert.True(page.Truncated);
            Assert.Equal("Ben", (string?)page.Items![0]!["user"]!["name"]);
            Assert.EndsWith("/v1/tools/heroes/squad-data/my.heroes", Assert.Single(stub.Requests).Uri!.AbsolutePath, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Asset_bytes_use_encoded_segments_and_size_limit()
    {
        var (service, stub, api) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3, 4]) { Headers = { ContentType = new("image/png") } },
        });
        using (api)
        {
            var content = await service.AssetAsync("heroes", "heroes/red lady.png", 10, CancellationToken.None);
            await Assert.ThrowsAsync<ToolBridgeException>(() => service.AssetAsync("heroes", "heroes/a.png", 3, CancellationToken.None));

            Assert.Equal("image/png", content.Mime);
            Assert.Equal(4, content.Bytes.Length);
            Assert.EndsWith("/v1/tools/heroes/assets/heroes/red%20lady.png", stub.Requests[0].Uri!.AbsolutePath, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Dot_only_segments_never_reach_the_server()
    {
        var (service, stub, api) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{}"));
        using (api)
        {
            await Assert.ThrowsAsync<ToolBridgeException>(() => service.DataGetAsync("heroes", "personal", "..", CancellationToken.None));
            Assert.Empty(stub.Requests);
        }
    }
}
