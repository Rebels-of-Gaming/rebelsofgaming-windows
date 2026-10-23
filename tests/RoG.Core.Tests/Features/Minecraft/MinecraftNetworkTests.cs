using System.Net;
using System.Text.Json.Nodes;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Minecraft;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Minecraft;

public class MinecraftNetworkTests
{
    private const string ServerId = "01JB4ZJ9W6Q8X3M5N7P2R4T6V8";
    private const string OtherId = "01JB4ZJ9W6Q8X3M5N7P2R4T6VA";
    private const string ReleaseId = "01jb5a0q0v7n3k2m1p9r8t6w4x";

    private const string Servers = """
        {"data":[
          {"id":"01JB4ZJ9W6Q8X3M5N7P2R4T6V8","slug":"rog-bedwars-1","name":"RoG BedWars 1","linked":true,
           "deploy":{"role":"minigame","folder":"bedwars","configured":true,"mismatch":false},
           "minecraft":{"network_id":"bedwars-1","role":"minigame","tps":19.87,"mspt":1.33,"players_online":3,"players_max":8,
             "minigame":{"game":"bedwars-solo","state":"waiting","arena":"sakura","players_max":8},"plugin_version":"0.4.0",
             "empty_since":null,"reported_at":"2026-10-23T17:06:24Z","fresh":true,"deploy_folder":"bedwars"}},
          {"id":"01JB4ZJ9W6Q8X3M5N7P2R4T6VA","slug":"rog-lobby","name":"RoG Lobby","linked":false,
           "deploy":{"role":null,"folder":null,"configured":false,"mismatch":false},"minecraft":null},
          {"name":"ohne ID"}]}
        """;

    private const string Pool = """
        {"data":[{"game":"bedwars-solo","configured":true,"enabled":true,"min_running":1,"start_threshold":60,"idle_minutes":10,
          "running":2,"joinable":1,"players":9,
          "instances":[{"id":"01JB4ZJ9W6Q8X3M5N7P2R4T6V8","slug":"rog-bedwars-1","name":"RoG BedWars 1","linked":true,"assigned":true,
            "excluded":false,"failures":3,"quarantined":true,"backoff":false,"status":"offline","minigame_state":null,"arena":null,
            "players_online":0,"capacity":8,"load":0,"joinable":false,"empty_since":null,"reported_at":null}],
          "suggestions":[{"id":"01JB4ZJ9W6Q8X3M5N7P2R4T6VA","slug":"rog-bedwars-3","name":"RoG BedWars 3"}],
          "last_action":{"id":41,"game":"bedwars-solo","server":{"id":"01JB4ZJ9W6Q8X3M5N7P2R4T6V9","name":"RoG BedWars 2"},
            "action":"start","reason":"full","ok":true,"error":null,"created_at":"2026-10-23T17:02:00Z"}},
          {"game":"skywars","configured":false,"enabled":false,"running":0,"joinable":0,"players":0,"instances":[],"suggestions":[],"last_action":null}],
         "meta":{"actions":[{"id":41,"game":"bedwars-solo","server":{"id":"x","name":"RoG BedWars 2"},"action":"start","reason":"full","ok":true,"error":null,"created_at":"2026-10-23T17:02:00Z"},
           {"id":40,"game":"bedwars-solo","server":null,"action":"stop","reason":"idle","ok":false,"error":"pterodactyl_error","created_at":"2026-10-23T16:00:00Z"}],
           "cooldown_minutes":3}}
        """;

    private static (RoGApi Api, StubHttpHandler Stub) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        return (new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub), stub);
    }

    [Fact]
    public async Task Network_servers_are_parsed_with_heartbeat_and_deploy()
    {
        var (api, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, Servers));
        using (api)
        {
            var servers = await new MinecraftService(api.Http).NetworkServersAsync();

            Assert.Equal(2, servers.Count);
            var bedwars = servers[0];
            Assert.True(bedwars.Linked);
            Assert.Equal(new NetworkDeploy("minigame", "bedwars", true, false), bedwars.Deploy);
            Assert.Equal(19.87, bedwars.State!.Tps);
            Assert.Equal(8, bedwars.State.PlayersMax);
            Assert.Equal("bedwars-solo", bedwars.State.MinigameGame);
            Assert.True(bedwars.State.Fresh);
            Assert.Null(servers[1].State);
            Assert.False(servers[1].Deploy.Configured);
        }
    }

    [Theory]
    [InlineData("minigame", "bedwars", """{"role":"minigame","folder":"bedwars"}""")]
    [InlineData("lobby", "ignored", """{"role":"lobby"}""")]
    [InlineData(null, null, """{"role":null}""")]
    public async Task Server_role_is_saved(string? role, string? folder, string expected)
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """{"data":{"id":"01JB4ZJ9W6Q8X3M5N7P2R4T6V8","name":"RoG BedWars 1","linked":true,"deploy":{"configured":true},"minecraft":null}}"""));
        using (api)
        {
            var server = await new MinecraftService(api.Http).SetServerRoleAsync(ServerId, role, folder);

            var request = Assert.Single(stub.Requests);
            Assert.Equal("PUT", request.Method.Method);
            Assert.EndsWith($"/v1/minecraft/network/servers/{ServerId}/role", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal(expected, request.Body);
            Assert.Equal("RoG BedWars 1", server!.Name);
        }
    }

    [Theory]
    [InlineData("survival", null)]
    [InlineData("minigame", null)]
    [InlineData("minigame", "BedWars")]
    [InlineData("minigame", "-bedwars")]
    public async Task Invalid_roles_and_folders_are_not_sent(string role, string? folder)
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{}"));
        using (api)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => new MinecraftService(api.Http).SetServerRoleAsync(ServerId, role, folder));
            Assert.Empty(stub.Requests);
        }
    }

    [Fact]
    public async Task Pool_is_parsed_with_instances_suggestions_and_log()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, Pool));
        using (api)
        {
            var pool = await new MinecraftService(api.Http).PoolAsync();

            Assert.Contains("limit=50", Assert.Single(stub.Requests).Uri!.Query, StringComparison.Ordinal);
            Assert.Equal(3, pool.CooldownMinutes);
            var group = pool.Groups[0];
            Assert.True(group.Enabled);
            Assert.Equal(2, group.Running);
            var instance = Assert.Single(group.Instances);
            Assert.True(instance.Quarantined);
            Assert.Equal(3, instance.Failures);
            Assert.Equal((OtherId, "RoG BedWars 3"), Assert.Single(group.Suggestions));
            Assert.Equal("full", group.LastAction!.Reason);
            Assert.Equal("RoG BedWars 2", group.LastAction.ServerName);

            var skywars = pool.Groups[1];
            Assert.False(skywars.Configured);
            Assert.Equal(1, skywars.MinRunning);
            Assert.Equal(60, skywars.StartThreshold);
            Assert.Equal(10, skywars.IdleMinutes);

            Assert.Equal(2, pool.Actions.Count);
            Assert.False(pool.Actions[1].Ok);
            Assert.Null(pool.Actions[1].ServerName);
        }
    }

    [Fact]
    public async Task Pool_group_is_saved_with_clamped_values_and_unique_servers()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """{"data":{"game":"bedwars-solo","configured":true}}"""));
        using (api)
        {
            await new MinecraftService(api.Http).SavePoolAsync("bedwars-solo", true, 0, 150, 500, [ServerId, ServerId, OtherId]);

            var request = Assert.Single(stub.Requests);
            Assert.Equal("PUT", request.Method.Method);
            Assert.EndsWith("/v1/minecraft/pool/bedwars-solo", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            var body = JsonNode.Parse(request.Body!)!;
            Assert.True((bool)body["enabled"]!);
            Assert.Equal(1, (int)body["min_running"]!);
            Assert.Equal(100, (int)body["start_threshold"]!);
            Assert.Equal(240, (int)body["idle_minutes"]!);
            Assert.Equal(2, body["servers"]!.AsArray().Count);
        }
    }

    [Theory]
    [InlineData("Bedwars")]
    [InlineData("bed wars")]
    [InlineData("../x")]
    public async Task Invalid_game_names_are_not_sent(string game)
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{}"));
        using (api)
        {
            var service = new MinecraftService(api.Http);
            await Assert.ThrowsAsync<ArgumentException>(() => service.SavePoolAsync(game, true, 1, 60, 10, []));
            await Assert.ThrowsAsync<ArgumentException>(() => service.DeletePoolAsync(game));
            Assert.Empty(stub.Requests);
        }
    }

    [Theory]
    [InlineData(true, false, """{"excluded":true}""")]
    [InlineData(null, true, """{"reset_failures":true}""")]
    [InlineData(false, true, """{"excluded":false,"reset_failures":true}""")]
    public async Task Pool_server_is_patched(bool? excluded, bool reset, string expected)
    {
        var (api, stub) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        using (api)
        {
            await new MinecraftService(api.Http).UpdatePoolServerAsync("bedwars-solo", ServerId, excluded, reset);

            var request = Assert.Single(stub.Requests);
            Assert.Equal("PATCH", request.Method.Method);
            Assert.EndsWith($"/v1/minecraft/pool/bedwars-solo/servers/{ServerId}", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal(expected, request.Body);
        }
    }

    [Fact]
    public async Task Nothing_to_patch_sends_nothing()
    {
        var (api, stub) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        using (api)
        {
            await new MinecraftService(api.Http).UpdatePoolServerAsync("bedwars-solo", ServerId, null, resetFailures: false);

            Assert.Empty(stub.Requests);
        }
    }

    [Fact]
    public async Task Release_check_is_started_and_parsed()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Accepted, """
            {"data":{"id":"01jb5a0q0v7n3k2m1p9r8t6w4x","status":"ready","error":null,"tag":"v0.4.0","published_at":"2026-10-20T18:30:00Z",
              "sha256":"9f2c","size":4194304,"signed":true,"signature_required":true,
              "signature":{"key_id":"E1A2","trusted_comment":"timestamp:1","prehashed":false},
              "folders":{"lobby":[{"name":"rog-core-0.4.0.jar","size":1203456}],
                         "bedwars":[{"name":"rog-bedwars-0.4.0.jar","size":812345},{"name":"rog-core-0.4.0.jar","size":1203456}]},
              "ignored":["rog-minecraft-plugins/bedwars/LuckPerms-5.4.jar"],
              "created_at":"2026-10-23T17:08:00Z","expires_at":"2026-10-23T18:08:00Z"}}
            """));
        using (api)
        {
            var release = await new MinecraftService(api.Http).CheckReleaseAsync();

            Assert.Equal("POST", Assert.Single(stub.Requests).Method.Method);
            Assert.True(release!.IsReady);
            Assert.False(release.IsChecking);
            Assert.Equal("v0.4.0", release.Tag);
            Assert.True(release.IsSigned);
            Assert.Equal("E1A2", release.SignatureKeyId);
            Assert.Equal(["bedwars", "lobby"], release.Folders.Select(f => f.Folder));
            Assert.Equal(2, release.Folders[0].Files.Count);
            Assert.Single(release.Ignored);
            Assert.NotNull(release.ExpiresAt);
        }
    }

    [Fact]
    public async Task Deploy_is_started_with_confirmation_and_parsed()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Accepted, """
            {"data":{"id":"01JB5A0Q0V7N3K2M1P9R8T6W4X","status":"running","restart":true,
              "release":{"id":"01jb5a0q0v7n3k2m1p9r8t6w4x","tag":"v0.4.0","published_at":null,"signed":false},
              "sha256":"9f2c","error":null,"started_by":{"id":"01J9","name":"Wahke"},"created_at":"2026-10-23T17:10:00Z","started_at":null,"finished_at":null,
              "targets":[{"server":{"id":"01JB4ZJ9W6Q8X3M5N7P2R4T6V8","name":"RoG BedWars 1"},"folder":"bedwars","status":"done",
                "message":null,"uploaded":["rog-bedwars-0.4.0.jar"],"deleted":[],"restarted":true,"finished_at":null},
                {"server":{"id":"01JB4ZJ9W6Q8X3M5N7P2R4T6VA","name":"RoG Lobby"},"folder":null,"status":"skipped","message":"no_role","uploaded":[],"deleted":[],"restarted":false}]}}
            """));
        using (api)
        {
            var deploy = await new MinecraftService(api.Http).StartDeployAsync(ReleaseId, [ServerId], restart: false);

            var body = JsonNode.Parse(Assert.Single(stub.Requests).Body!)!;
            Assert.Equal(ReleaseId, (string)body["release"]!);
            Assert.True((bool)body["confirm"]!);
            Assert.False((bool)body["restart"]!);
            Assert.Equal(ServerId, (string)body["servers"]![0]!);
            Assert.True(deploy!.IsRunning);
            Assert.Equal("v0.4.0", deploy.Tag);
            Assert.Equal("Wahke", deploy.StartedBy);
            Assert.Equal(2, deploy.Targets.Count);
            Assert.Equal(["rog-bedwars-0.4.0.jar"], deploy.Targets[0].Uploaded);
            Assert.True(deploy.Targets[0].Restarted);
            Assert.Equal("no_role", deploy.Targets[1].Message);
        }
    }

    [Fact]
    public async Task Deploy_without_selection_sends_no_server_list()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Accepted, """{"data":{"id":"01JB5A0Q0V7N3K2M1P9R8T6W4X","status":"queued","targets":[]}}"""));
        using (api)
        {
            await new MinecraftService(api.Http).StartDeployAsync(ReleaseId, [], restart: true);

            Assert.Null(JsonNode.Parse(Assert.Single(stub.Requests).Body!)!["servers"]);
        }
    }

    [Fact]
    public async Task Running_deploy_is_reported_as_problem()
    {
        var (api, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Conflict, """{"type":"about:blank","title":"Conflict","status":409,"code":"deploy_running"}"""));
        using (api)
        {
            var ex = await Assert.ThrowsAsync<ApiProblemException>(() => new MinecraftService(api.Http).StartDeployAsync(ReleaseId, null, restart: true));

            Assert.Equal("deploy_running", ex.Problem.Code);
        }
    }

    [Theory]
    [InlineData("bedwars-solo", true)]
    [InlineData("a", true)]
    [InlineData("", false)]
    [InlineData("-x", false)]
    [InlineData("Skywars", false)]
    public void Names_follow_the_website_pattern(string value, bool expected) =>
        Assert.Equal(expected, MinecraftService.IsValidName(value));
}
