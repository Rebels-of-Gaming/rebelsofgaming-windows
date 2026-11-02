using System.Net;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Minecraft;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Minecraft;

public class MinecraftHeadsTests
{
    private const string Texture = "e3fc52264d8ad9e654f415bef01a23947edbccccf649373289bea4d149541f70";

    private const string Head = """
        {"id":7,"name":{"de":"Apfel","en":"Apple","fr":null},"category":"food","texture":"e3fc52264d8ad9e654f415bef01a23947edbccccf649373289bea4d149541f70",
         "tags":["obst","rot"],"price":null,"image_url":"https://cdn.rogclan.de/heads/7.png","image_status":"ready","created_by":null,
         "creator":{"id":"01A","name":"Wahke","slug":"wahke"},"created_at":"2026-10-25T10:00:00Z","updated_at":null}
        """;

    [Fact]
    public async Task List_filters_by_category_and_search_and_pages_with_cursor()
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":[" + Head + "],\"links\":{},\"meta\":{\"per_page\":50,\"next_cursor\":\"c2\"}}"));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);

        var page = await new MinecraftService(api.Http).HeadsAsync("food", " apf ", "c1");

        var query = Uri.UnescapeDataString(Assert.Single(stub.Requests).Uri!.Query);
        Assert.Contains("filter[category]=food", query, StringComparison.Ordinal);
        Assert.Contains("filter[search]=apf", query, StringComparison.Ordinal);
        Assert.Contains("cursor=c1", query, StringComparison.Ordinal);
        Assert.Equal("c2", page.NextCursor);
        var head = Assert.Single(page.Items);
        Assert.Equal("Apple", head.Name("en"));
        Assert.Equal("Apfel", head.Name("fr"));
        Assert.Null(head.Price);
        Assert.Equal("Wahke", head.Creator);
    }

    [Fact]
    public async Task Create_sends_normalized_texture_and_tags()
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.Created, "{\"data\":" + Head + "}"));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);

        await new MinecraftService(api.Http).CreateHeadAsync("http://textures.minecraft.net/texture/" + Texture.ToUpperInvariant(), "Apfel", null, null, "food", ["Obst", "obst", "rot!"], 250);

        using var body = JsonDocument.Parse(Assert.Single(stub.Requests).Body!);
        Assert.Equal(Texture, body.RootElement.GetProperty("texture").GetString());
        Assert.Equal(["obst"], body.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(250, body.RootElement.GetProperty("price").GetInt32());
    }

    [Fact]
    public void Patch_contains_only_changes_and_can_clear_translations_and_price()
    {
        var body = MinecraftService.BuildHeadPatch(new MinecraftHeadEdit(NameEn: "Apple", ClearFr: true, CategoryPrice: true));

        Assert.Equal("""{"name":{"en":"Apple","fr":null},"price":null}""", body.ToJsonString());
        Assert.Empty(MinecraftService.BuildHeadPatch(new MinecraftHeadEdit(Category: "unknown")));
    }

    [Theory]
    [InlineData("Apfel", true)]
    [InlineData("§cRot", false)]
    [InlineData("<red>Apfel</red>", false)]
    [InlineData("", false)]
    public void Head_names_are_plain_text(string name, bool expected) => Assert.Equal(expected, MinecraftService.IsValidHeadName(name));

    [Fact]
    public void Texture_must_be_40_to_64_hex() =>
        Assert.Null(MinecraftService.NormalizeTexture("xyz"));
}
