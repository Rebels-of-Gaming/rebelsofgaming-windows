using System.Net;
using System.Text.Json.Nodes;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Minecraft;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Minecraft;

public class MinecraftMapTests
{
    private const string WorldId = "01JB4ZJ9W6Q8X3M5N7P2R4T6V8";

    private const string Map = """
        {"data":{"live_players":true,"live_players_mode":"public",
          "quota":{"used":1048576,"total":53687091200,"warn_at":42949672960,"per_server":10737418240,"warned":false},
          "servers":[{"id":"rog-freebuild","network_id":"freebuild-1","name":"RoG Freebuild","hidden":false,"public":true,"bytes":2048,
            "worlds":[{"id":"01JB4ZJ9W6Q8X3M5N7P2R4T6V8","world":"farmwelt","name":"Farmwelt","environment":"normal","loaded":true,
              "spawn":{"x":10,"z":-20},"tile_size":512,"zoom_levels":4,
              "tiles":{"count":1342,"region_size":32,"url":"https://cdn.rogclan.de/minecraft-map/01JB/9f2c1a0b7d3e/{zoom}/{x}_{z}.{version}.png","updated_at":"2026-10-24T12:00:00Z"},
              "markers":[{"type":"warp","id":"shop","label":"shop","x":120,"z":-40},{"label":"ohne ID"}],
              "reported_at":"2026-10-24T12:00:00Z","hidden":false,"names":{"de":"Farmwelt","en":"Farm world","fr":""},"bytes":1024,"generation":"x"},
              {"world":"ohne ID"}]},
            {"name":"ohne Slug"}]}}
        """;

    private static (RoGApi Api, StubHttpHandler Stub) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        return (new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub), stub);
    }

    [Fact]
    public async Task Map_overview_is_parsed_with_admin_fields()
    {
        var (api, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, Map));
        using (api)
        {
            var map = await new MinecraftService(api.Http).MapAsync();

            Assert.True(map.LivePlayers);
            Assert.Equal(MinecraftService.LivePublic, map.LiveMode);
            Assert.Equal(53687091200, map.Quota!.Total);
            var server = Assert.Single(map.Servers);
            Assert.Equal("freebuild-1", server.NetworkId);
            Assert.True(server.IsPublic);
            Assert.Equal(2048, server.Bytes);
            var world = Assert.Single(server.Worlds);
            Assert.Equal("Farmwelt", world.Name);
            Assert.Equal((10, -20), (world.SpawnX, world.SpawnZ));
            Assert.Equal(4, world.ZoomLevels);
            Assert.Equal(32, world.RegionSize);
            Assert.Equal(1342, world.TileCount);
            Assert.Equal(new MapMarker("warp", "shop", "shop", 120, -40), Assert.Single(world.Markers));
            Assert.Equal("Farm world", world.Names!["en"]);
            Assert.Equal(1024, world.Bytes);
        }
    }

    [Fact]
    public void Guest_map_has_no_admin_fields_and_falls_back_to_the_boolean()
    {
        var map = MinecraftService.ParseMap(JsonNode.Parse("""
            {"live_players":false,"servers":[{"id":"rog-lobby","network_id":null,"name":"Lobby","worlds":[
              {"id":"01JB4ZJ9W6Q8X3M5N7P2R4T6V8","world":"world","name":"Oberwelt","environment":"normal","loaded":false,"spawn":{"x":0,"z":0},
               "tile_size":512,"zoom_levels":2,"tiles":{"count":0,"url":"https://cdn.rogclan.de/x/{zoom}/{x}_{z}.{version}.png","updated_at":null},"markers":[],"reported_at":null}]}]}
            """));

        Assert.False(map.LivePlayers);
        Assert.Equal(MinecraftService.LiveOff, map.LiveMode);
        Assert.Null(map.Quota);
        var server = Assert.Single(map.Servers);
        Assert.Null(server.IsPublic);
        Assert.Null(server.Bytes);
        var world = Assert.Single(server.Worlds);
        Assert.Equal(MinecraftMapGeometry.DefaultRegionSize, world.RegionSize);
        Assert.Null(world.Names);
        Assert.Null(world.Bytes);
    }

    [Fact]
    public async Task Tile_index_is_requested_per_region()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """
            {"data":{"world":"01JB4ZJ9W6Q8X3M5N7P2R4T6V8","zoom":0,"region":{"x":-1,"z":0,"size":32},
              "tiles":[[-1,2,"4be2c0a91d7f"],[-1,7,"08cd11e3a5b2"],[1,"x"],["a",2,"00"]]}}
            """));
        using (api)
        {
            var tiles = await new MinecraftService(api.Http).MapTilesAsync(WorldId, 0, -1, 0);

            Assert.Equal([new MapTileRef(-1, 2, "4be2c0a91d7f"), new MapTileRef(-1, 7, "08cd11e3a5b2")], tiles);
            Assert.Equal("/v1/minecraft/map/worlds/" + WorldId + "/tiles", stub.Requests[0].Uri!.AbsolutePath);
            Assert.Equal("?zoom=0&region=-1,0", stub.Requests[0].Uri!.Query);
        }
    }

    [Fact]
    public async Task Invalid_world_id_is_rejected_before_sending()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{}"));
        using (api)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => new MinecraftService(api.Http).MapTilesAsync("../x", 0, 0, 0));
            await Assert.ThrowsAsync<ArgumentException>(() => new MinecraftService(api.Http).MapLiveAsync("a b"));
            Assert.Empty(stub.Requests);
        }
    }

    [Fact]
    public async Task Live_players_are_parsed_and_disabled_live_has_none()
    {
        var (api, stub) = Create((_, i) => StubHttpHandler.Json(HttpStatusCode.OK, i == 0
            ? """
              {"data":{"server":"rog-freebuild","enabled":true,"updated_at":"2026-10-24T12:00:05Z","players":[
                {"uuid":"069a79f4-44e9-4726-a5be-fca90e38aaf5","name":"wahke","world":"world","world_id":"01JB4ZJ9W6Q8X3M5N7P2R4T6V8","x":12.5,"z":-40.2,"yaw":90,"bedrock":false,"head_url":null},
                {"name":"ohne UUID"}]}}
              """
            : """{"data":{"server":"rog-freebuild","enabled":false,"updated_at":null,"players":[{"uuid":"u","name":"n"}]}}"""));
        using (api)
        {
            var service = new MinecraftService(api.Http);
            var live = await service.MapLiveAsync("freebuild-1");
            var off = await service.MapLiveAsync("freebuild-1");

            var player = Assert.Single(live.Players);
            Assert.Equal("wahke", player.Name);
            Assert.Equal(12.5, player.X);
            Assert.Equal(-40.2, player.Z);
            Assert.Equal(90, player.Yaw);
            Assert.Null(player.HeadUrl);
            Assert.EndsWith("?server=freebuild-1", stub.Requests[0].Uri!.Query, StringComparison.Ordinal);
            Assert.False(off.Enabled);
            Assert.Empty(off.Players);
        }
    }

    [Fact]
    public async Task Admin_updates_send_only_what_is_set()
    {
        var (api, stub) = Create((r, _) => r.Method == HttpMethod.Delete
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : StubHttpHandler.Json(HttpStatusCode.OK, r.RequestUri!.AbsolutePath.EndsWith("settings", StringComparison.Ordinal)
                ? """{"data":{"live_players":true,"live_players_mode":"auth"}}"""
                : r.RequestUri.AbsolutePath.Contains("/servers/", StringComparison.Ordinal)
                    ? """{"data":{"id":"rog-freebuild","hidden":true}}"""
                    : """{"data":{"id":"01JB4ZJ9W6Q8X3M5N7P2R4T6V8","world":"farmwelt","name":"Farmwelt","hidden":true,"names":{"de":"Farmwelt","en":"","fr":""}}}"""));
        using (api)
        {
            var service = new MinecraftService(api.Http);
            Assert.Equal(MinecraftService.LiveAuth, await service.SetMapLiveModeAsync(MinecraftService.LiveAuth));
            Assert.True(await service.SetMapServerHiddenAsync("rog-freebuild", hidden: true, clearTiles: true));
            await service.SetMapServerHiddenAsync("rog-freebuild", hidden: false, clearTiles: true);
            var world = await service.UpdateMapWorldAsync(WorldId, names: new Dictionary<string, string?> { ["de"] = " Farmwelt ", ["en"] = null });
            await service.UpdateMapWorldAsync(WorldId, hidden: false, clearTiles: true);
            Assert.Null(await service.UpdateMapWorldAsync(WorldId));
            await service.ClearMapTilesAsync(WorldId);

            Assert.Equal("""{"live_players":"auth"}""", stub.Requests[0].Body);
            Assert.Equal("""{"hidden":true,"clear_tiles":true}""", stub.Requests[1].Body);
            Assert.Equal("""{"hidden":false}""", stub.Requests[2].Body);
            Assert.Equal("""{"names":{"de":"Farmwelt","en":""}}""", stub.Requests[3].Body);
            Assert.Equal("""{"hidden":false}""", stub.Requests[4].Body);
            Assert.Equal(6, stub.Requests.Count);
            Assert.Equal(HttpMethod.Delete, stub.Requests[5].Method);
            Assert.EndsWith("/minecraft/map/worlds/" + WorldId + "/tiles", stub.Requests[5].Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.True(world!.Hidden);
            await Assert.ThrowsAsync<ArgumentException>(() => service.SetMapLiveModeAsync("everyone"));
        }
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(511, 0, 0)]
    [InlineData(512, 0, 1)]
    [InlineData(-1, 0, -1)]
    [InlineData(-512, 0, -1)]
    [InlineData(-513, 0, -2)]
    [InlineData(1024, 1, 1)]
    [InlineData(-2049, 2, -2)]
    public void Tile_index_follows_the_guide(double block, int zoom, int index) =>
        Assert.Equal(index, MinecraftMapGeometry.TileIndex(block, zoom));

    [Theory]
    [InlineData(0, 32, 0)]
    [InlineData(31, 32, 0)]
    [InlineData(32, 32, 1)]
    [InlineData(-1, 32, -1)]
    [InlineData(-33, 32, -2)]
    public void Region_index_floors(int tile, int size, int region) =>
        Assert.Equal(region, MinecraftMapGeometry.RegionIndex(tile, size));

    [Fact]
    public void World_border_limits_match_the_website()
    {
        Assert.Equal(58594, MinecraftMapGeometry.MaxTileIndex(0));
        Assert.Equal((58594 / 32) + 1, MinecraftMapGeometry.MaxRegionIndex(0, 32));
        Assert.Equal(3663, MinecraftMapGeometry.MaxTileIndex(4));
    }

    [Theory]
    [InlineData(8, 4, 0)]
    [InlineData(1, 4, 0)]
    [InlineData(0.5, 4, 1)]
    [InlineData(0.25, 4, 2)]
    [InlineData(0.01, 4, 4)]
    [InlineData(0.01, 2, 2)]
    public void Tile_zoom_matches_the_scale(double pixelsPerBlock, int levels, int zoom) =>
        Assert.Equal(zoom, MinecraftMapGeometry.ZoomFor(pixelsPerBlock, levels));

    [Fact]
    public void Tile_uri_fills_the_template_and_needs_https_and_a_hex_version()
    {
        const string Template = "https://cdn.rogclan.de/minecraft-map/01JB/9f2c/{zoom}/{x}_{z}.{version}.png";

        Assert.Equal("https://cdn.rogclan.de/minecraft-map/01JB/9f2c/2/-1_3.4be2c0a91d7f.png", MinecraftMapGeometry.TileUri(Template, 2, -1, 3, "4be2c0a91d7f")!.ToString());
        Assert.Null(MinecraftMapGeometry.TileUri(Template.Replace("https", "http", StringComparison.Ordinal), 0, 0, 0, "ab"));
        Assert.Null(MinecraftMapGeometry.TileUri(Template, 0, 0, 0, "../x"));
        Assert.Null(MinecraftMapGeometry.TileUri(null, 0, 0, 0, "ab"));
    }

    [Fact]
    public void Viewport_maps_blocks_to_screen_with_north_up()
    {
        var view = new MapViewport(4);
        view.Resize(800, 600);
        view.CenterOn(100, -50);

        Assert.Equal((400d, 300d), view.BlockToScreen(100, -50));
        Assert.Equal((410d, 290d), view.BlockToScreen(110, -60));
        Assert.Equal((100d, -50d), view.ScreenToBlock(400, 300));

        view.Pan(20, -10); // Karte nach rechts und oben gezogen → Mitte wandert nach Westen und Süden
        Assert.Equal(80, view.CenterX);
        Assert.Equal(-40, view.CenterZ);
    }

    [Fact]
    public void Zooming_keeps_the_block_under_the_pointer()
    {
        var view = new MapViewport(4);
        view.Resize(800, 600);
        var before = view.ScreenToBlock(600, 100);

        view.ZoomAt(0.5, 600, 100);

        var after = view.ScreenToBlock(600, 100);
        Assert.Equal(0.5, view.PixelsPerBlock);
        Assert.Equal(1, view.TileZoom);
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Z, after.Z, 6);
    }

    [Fact]
    public void Scale_is_clamped_to_the_zoom_levels()
    {
        var view = new MapViewport(2);
        view.SetScale(100);
        Assert.Equal(MinecraftMapGeometry.MaxPixelsPerBlock, view.PixelsPerBlock);
        Assert.False(view.CanZoomIn);

        view.SetScale(0.0001);
        Assert.Equal(0.125, view.PixelsPerBlock);
        Assert.False(view.CanZoomOut);
        Assert.Equal(2, view.TileZoom);
    }

    [Fact]
    public void Visible_tiles_cover_the_viewport_center_first()
    {
        var view = new MapViewport(4);
        view.Resize(1000, 600);
        view.CenterOn(0, 0);

        var tiles = view.VisibleTiles();

        // x: −500 … 500 → Kacheln −1 … 0; z: −300 … 300 → −1 … 0
        Assert.Equal(4, tiles.Count);
        Assert.Equal((0, 0), tiles[0]);
        Assert.Contains((-1, -1), tiles);
        // Kacheln −1 und 0 liegen in den Regionen −1 und 0
        var regions = view.VisibleRegions(32);
        Assert.Equal(4, regions.Count);
        Assert.Equal((0, 0), regions[0]);

        var (left, top, size) = view.TileRect(0, -1, -1);
        Assert.Equal((-12d, -212d, 512d), (left, top, size));
    }

    [Fact]
    public void Byte_sizes_reach_gigabytes()
    {
        Assert.Equal("512 B", MinecraftMapGeometry.FormatBytes(512));
        Assert.Equal("50 GB", MinecraftMapGeometry.FormatBytes(53687091200));
        Assert.EndsWith(" MB", MinecraftMapGeometry.FormatBytes(3_500_000), StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_viewport_has_no_tiles() => Assert.Empty(new MapViewport(4).VisibleTiles());

    [Theory]
    [InlineData("server=freebuild-1&world=farmwelt&x=120.7&z=-40", "freebuild-1", "farmwelt", 120, -40)]
    [InlineData("?server=rog-freebuild", "rog-freebuild", null, null, null)]
    [InlineData("server=a%20b&x=99999999&z=abc", null, null, null, null)]
    [InlineData("", null, null, null, null)]
    public void Map_links_are_parsed(string query, string? server, string? world, int? x, int? z) =>
        Assert.Equal(new MapLink(server, world, x, z), MapLink.Parse(query));

    [Fact]
    public void Map_link_round_trips_as_route_key()
    {
        var link = new MapLink("freebuild-1", "farmwelt", 120, -40);

        Assert.Equal("server=freebuild-1&world=farmwelt&x=120&z=-40", link.ToKey());
        Assert.Equal(link, MapLink.Parse(link.ToKey()));
        Assert.Equal(string.Empty, new MapLink(null, null, null, null).ToKey());
    }
}
