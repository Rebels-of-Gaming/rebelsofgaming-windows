using System.Net;
using System.Text.Json.Nodes;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Minecraft;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Minecraft;

public class MinecraftSecretsTests
{
    private static (RoGApi Api, StubHttpHandler Stub) Create(string json)
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, json));
        return (new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub), stub);
    }

    [Fact]
    public async Task Overview_is_sorted_by_plugin_order_and_keeps_guest_found_as_null()
    {
        var (api, stub) = Create("""
            {"data":[{"id":"lobby","label":"Hauptlobby","total":2,"found":null,"secrets":[
              {"id":"b","name":"Glocke","hint":null,"order":2,"finders":3,"found_at":null},
              {"id":"a","name":"Jukebox","hint":"Hör mal genau hin","order":1,"finders":12,"found_at":null}]}]}
            """);
        using (api)
        {
            var group = Assert.Single(await new MinecraftService(api.Http).SecretsAsync());

            Assert.EndsWith("/v1/minecraft/secrets", Assert.Single(stub.Requests).Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("Hauptlobby", group.Label);
            Assert.Null(group.Found);
            Assert.Equal(["Jukebox", "Glocke"], group.Secrets.Select(s => s.Name));
            Assert.Equal(12, group.Secrets[0].Finders);
            Assert.Equal("Hör mal genau hin", group.Secrets[0].Hint);
        }
    }

    [Fact]
    public async Task Member_progress_counts_found_secrets_per_group()
    {
        var (api, stub) = Create("""
            {"data":[{"id":"minigame-lobby","label":"Minigame-Lobby","found":1,"total":3,"secrets":[
              {"id":"x","name":"Schatz","hint":null,"found":true,"found_at":"2026-11-01T18:00:00Z"},
              {"id":"y","name":"Geheimgang","hint":"Hinter dem Wasserfall","found":false,"found_at":null}]}]}
            """);
        using (api)
        {
            var group = Assert.Single(await new MinecraftService(api.Http).MemberSecretsAsync("wahke"));

            Assert.EndsWith("/v1/members/wahke/minecraft-secrets", Assert.Single(stub.Requests).Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal((1, 3), (group.Found, group.Total));
            Assert.True(group.Secrets[0].Found);
            Assert.NotNull(group.Secrets[0].FoundAt);
            Assert.False(group.Secrets[1].Found);
        }
    }

    [Fact]
    public void Stats_carry_the_secret_progress()
    {
        var stats = MinecraftService.ParseStats(JsonNode.Parse("""
            {"playtime_seconds":10,"has_stats":true,"secrets":[{"id":"lobby","label":"Hauptlobby","found":5,"total":7,"secrets":[]}]}
            """));

        var group = Assert.Single(stats.Secrets);
        Assert.Equal((5, 7), (group.Found, group.Total));
        Assert.Empty(MinecraftService.ParseStats(JsonNode.Parse("{}")).Secrets);
    }
}
