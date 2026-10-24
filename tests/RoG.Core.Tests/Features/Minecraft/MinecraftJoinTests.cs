using System.Net;
using System.Text.Json.Nodes;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Minecraft;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Minecraft;

public class MinecraftJoinTests
{
    private const string Join = """
        {"data":{"java":{"address":"mc.rogclan.de","port":25565},"bedrock":{"address":"mc.rogclan.de","port":19132},
          "console":{"enabled":true,"gamertag":"RoG6328","hint":"Konsolen-Beitritt kann noch scheitern."}}}
        """;

    private static (RoGApi Api, StubHttpHandler Stub) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        return (new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub), stub);
    }

    [Fact]
    public async Task Join_data_is_parsed_without_default_java_port()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, Join));
        using (api)
        {
            var join = await new MinecraftService(api.Http).JoinAsync();

            Assert.NotNull(join);
            Assert.Equal("mc.rogclan.de", join.JavaText);
            Assert.Equal(new JoinEndpoint("mc.rogclan.de", 19132), join.Bedrock);
            Assert.True(join.ConsoleEnabled);
            Assert.Equal("RoG6328", join.Gamertag);
            Assert.Equal("Konsolen-Beitritt kann noch scheitern.", join.Hint);
            Assert.Null(join.Hints);
            Assert.Equal(HttpMethod.Get, stub.Requests[0].Method);
            Assert.EndsWith("/v1/minecraft/join", stub.Requests[0].Uri!.AbsolutePath, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Other_java_port_is_part_of_the_address_and_disabled_console_hides_the_gamertag()
    {
        var join = MinecraftService.ParseJoin(JsonNode.Parse("""
            {"java":{"address":"144.76.137.20","port":25570},"bedrock":{"address":"144.76.137.20","port":0},
             "console":{"enabled":false,"gamertag":"RoG6328","hint":"x"}}
            """));

        Assert.NotNull(join);
        Assert.Equal("144.76.137.20:25570", join.JavaText);
        Assert.Equal(MinecraftService.DefaultBedrockPort, join.Bedrock.Port);
        Assert.False(join.ConsoleEnabled);
        Assert.Null(join.Gamertag);
        Assert.Null(join.Hint);
    }

    [Fact]
    public void Missing_addresses_give_no_join_data()
    {
        Assert.Null(MinecraftService.ParseJoin(JsonNode.Parse("""{"java":{"port":25565},"bedrock":{"address":"x","port":1}}""")));
        Assert.Null(MinecraftService.ParseJoin(null));
    }

    [Fact]
    public async Task Update_sends_only_set_fields_and_reads_all_hints()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """
            {"data":{"java":{"address":"mc.rogclan.de","port":25565},"bedrock":{"address":"mc.rogclan.de","port":19133},
              "console":{"enabled":true,"gamertag":"RoG6328","hint":"Hallo","hints":{"de":"Hallo","en":"","fr":"Salut"}}}}
            """));
        using (api)
        {
            var join = await new MinecraftService(api.Http).UpdateJoinAsync(new MinecraftJoinUpdate(
                BedrockPort: 19133,
                Gamertag: " RoG6328 ",
                Hints: new Dictionary<string, string?> { ["de"] = " Hallo ", ["en"] = null, ["it"] = "ignoriert" }));

            var body = JsonNode.Parse(stub.Requests[0].Body!)!.AsObject();
            Assert.Equal(HttpMethod.Put, stub.Requests[0].Method);
            Assert.Equal(["bedrock_port", "console_gamertag", "console_hint"], body.Select(p => p.Key));
            Assert.Equal(19133, (int)body["bedrock_port"]!);
            Assert.Equal("RoG6328", (string?)body["console_gamertag"]);
            Assert.Equal("""{"de":"Hallo","en":""}""", body["console_hint"]!.ToJsonString());
            Assert.Equal("Salut", join!.Hints!["fr"]);
            Assert.Equal(string.Empty, join.Hints["en"]);
        }
    }

    [Fact]
    public async Task Loading_for_edit_uses_hints_from_get_when_present()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """
            {"data":{"java":{"address":"mc.rogclan.de","port":25565},"bedrock":{"address":"mc.rogclan.de","port":19132},
              "console":{"enabled":true,"gamertag":"RoG6328","hint":"Hallo","hints":{"de":"Hallo","en":"Hello","fr":""}}}}
            """));
        using (api)
        {
            var join = await new MinecraftService(api.Http).JoinForEditAsync();

            Assert.Equal(HttpMethod.Get, Assert.Single(stub.Requests).Method);
            Assert.Equal("Hello", join!.Hints!["en"]);
        }
    }

    [Fact]
    public async Task Loading_for_edit_falls_back_to_an_empty_update_without_hints()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, Join));
        using (api)
        {
            await new MinecraftService(api.Http).JoinForEditAsync();

            Assert.Equal(2, stub.Requests.Count);
            Assert.Equal(HttpMethod.Get, stub.Requests[0].Method);
            Assert.Equal(HttpMethod.Put, stub.Requests[1].Method);
            Assert.Equal("{}", stub.Requests[1].Body);
        }
    }

    [Theory]
    [InlineData("mc.rogclan.de", true)]
    [InlineData("144.76.137.20", true)]
    [InlineData("mc.rogclan.de:25565", false)]
    [InlineData("https://mc.rogclan.de", false)]
    [InlineData("-bad.de", false)]
    [InlineData("", false)]
    public void Addresses_are_validated_like_the_website(string address, bool valid) =>
        Assert.Equal(valid, MinecraftService.IsValidAddress(address));

    [Theory]
    [InlineData("RoG6328", true)]
    [InlineData("Rebel Gamer#1234", true)]
    [InlineData("ABCDEFGHIJKLMNOP", false)]
    [InlineData(" ", false)]
    public void Gamertags_are_validated_like_the_website(string gamertag, bool valid) =>
        Assert.Equal(valid, MinecraftService.IsValidGamertag(gamertag));

    [Theory]
    [InlineData(1, true)]
    [InlineData(65535, true)]
    [InlineData(0, false)]
    [InlineData(65536, false)]
    [InlineData(null, false)]
    public void Ports_are_validated(int? port, bool valid) => Assert.Equal(valid, MinecraftService.IsValidPort(port));
}
