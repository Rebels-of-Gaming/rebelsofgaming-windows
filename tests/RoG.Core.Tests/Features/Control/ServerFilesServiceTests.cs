using System.Net;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Control;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Control;

public class ServerFilesServiceTests
{
    private static (RoGApi Api, StubHttpHandler Stub) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);
        return (api, stub);
    }

    [Fact]
    public void Paths_are_normalized_and_split_into_crumbs()
    {
        Assert.Equal("etmain/server.cfg", ServerPaths.Normalize("/etmain\\server.cfg/"));
        Assert.Equal("etmain", ServerPaths.Parent("etmain/server.cfg"));
        Assert.Equal(string.Empty, ServerPaths.Parent("server.cfg"));
        Assert.Equal("etmain/x.cfg", ServerPaths.Combine("etmain/", "x.cfg"));
        Assert.Equal(["/", "a", "b"], ServerPaths.Crumbs("a/b").Select(c => c.Name));
        Assert.Equal("a/b", ServerPaths.Crumbs("a/b")[^1].Path);
    }

    [Fact]
    public void Backup_names_end_in_bak_or_old_and_never_collide()
    {
        Assert.True(ServerPaths.IsBackupName("x.cfg.BAK"));
        Assert.True(ServerPaths.IsBackupName("x.old"));
        Assert.False(ServerPaths.IsBackupName("server.cfg"));
        Assert.Equal("a/x.cfg.bak", ServerPaths.SuggestBackup("a/x.cfg", ["a/y.cfg"]));
        Assert.Equal("a/x.cfg.3.bak", ServerPaths.SuggestBackup("a/x.cfg", ["a/x.cfg.bak", "a/x.cfg.2.bak"]));
    }

    [Fact]
    public async Task Listing_sorts_folders_first()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """
            {"data":[
              {"name":"server.cfg","is_file":true,"size":2048,"modified_at":"2026-10-16T10:00:00Z","mimetype":"text/plain","path":"etmain/server.cfg","editable":true,"operable":true,"backup":false},
              {"name":"maps","is_file":false,"size":0,"modified_at":null,"mimetype":null,"path":"etmain/maps","editable":false,"operable":false,"backup":false},
              {"name":"a.cfg.bak","is_file":true,"size":10,"modified_at":null,"mimetype":null,"path":"etmain/a.cfg.bak","editable":false,"operable":true,"backup":true}]}
            """));
        using (api)
        {
            var files = await new ServerFilesService(api.Client, api.Http).ListAsync("et", "etmain");

            Assert.Equal(["maps", "a.cfg.bak", "server.cfg"], files.Select(f => f.Name));
            Assert.True(files[1].IsBackup);
            Assert.Equal("2 KB", files[2].SizeText.Replace(',', '.'));
            Assert.Contains("directory=etmain", Assert.Single(stub.Requests).Uri!.Query, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Copy_sends_path_and_target_and_rejects_non_backup_targets()
    {
        var (api, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Created, """{"data":{"path":"etmain/server.cfg.bak"}}"""));
        using (api)
        {
            var service = new ServerFilesService(api.Client, api.Http);

            var path = await service.CopyAsync("et", "etmain/server.cfg", "etmain/server.cfg.bak");

            Assert.Equal("etmain/server.cfg.bak", path);
            var request = Assert.Single(stub.Requests);
            Assert.EndsWith("/v1/servers/et/files/copy", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("\"to\":\"etmain/server.cfg.bak\"", request.Body, StringComparison.Ordinal);
            await Assert.ThrowsAsync<ArgumentException>(() => service.RenameAsync("et", "a.bak", "a.cfg"));
        }
    }

    [Fact]
    public async Task Whitelist_sends_member_or_player_name()
    {
        var (api, stub) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        using (api)
        {
            var service = new ServerWhitelistService(api.Client, api.Http);

            await service.AddAsync("mc", null, " Steve ");
            await service.RemoveAsync("mc", "01U", "ignored");

            Assert.Contains("\"player_name\":\"Steve\"", stub.Requests[0].Body, StringComparison.Ordinal);
            Assert.Contains("\"action\":\"remove\"", stub.Requests[1].Body, StringComparison.Ordinal);
            Assert.Contains("\"user_id\":\"01U\"", stub.Requests[1].Body, StringComparison.Ordinal);
            Assert.DoesNotContain("player_name", stub.Requests[1].Body, StringComparison.Ordinal);
        }
    }
}

public class ConfigTemplateServiceTests
{
    private const string Form = """
        {"data":{"template":{"key":"quake3_server","label":"Server-Einstellungen","description":"","syntax":"cfg",
          "fields":[{"name":"sv_hostname","type":"text","label":"Servername","max":64},
                    {"name":"sv_maxclients","type":"number","label":"Slots","min":1,"max":64},
                    {"name":"g_password","type":"password","label":"Passwort","max":32},
                    {"name":"difficulty","type":"select","label":"Schwierigkeit","options":["easy","hard"]},
                    {"name":"pvp","type":"bool","label":"PvP"},
                    {"name":"maps","type":"list","label":"Maps","max":3}]},
          "values":{"sv_hostname":"^1RoG","sv_maxclients":20,"g_password":null,"pvp":"true","maps":["oasis","goldrush"]},
          "password_set":{"g_password":true}}}
        """;

    [Fact]
    public void Form_is_parsed_with_values_and_password_flags()
    {
        using var doc = JsonDocument.Parse(Form);

        var form = ConfigTemplateService.Parse(doc.RootElement);

        Assert.Equal("quake3_server", form.Key);
        Assert.Null(form.Description);
        Assert.Equal(6, form.Fields.Count);
        Assert.Equal(["easy", "hard"], form.Fields[3].Options);
        Assert.Equal("^1RoG", form.TextOf("sv_hostname"));
        Assert.Equal("20", form.TextOf("sv_maxclients"));
        Assert.Equal("oasis\ngoldrush", form.TextOf("maps"));
        Assert.Equal(string.Empty, form.TextOf("g_password"));
        Assert.True(form.BoolOf("pvp"));
        Assert.Contains("g_password", form.PasswordSet);
    }

    [Fact]
    public void Build_converts_types_and_reports_invalid_fields()
    {
        using var doc = JsonDocument.Parse(Form);
        var fields = ConfigTemplateService.Parse(doc.RootElement).Fields;

        var (values, errors) = ConfigTemplateService.Build(fields, new Dictionary<string, TemplateInput>
        {
            ["sv_hostname"] = new("RoG; quit"),
            ["sv_maxclients"] = new("24"),
            ["g_password"] = new(string.Empty),
            ["difficulty"] = new("hard"),
            ["pvp"] = new(string.Empty, Checked: false),
            ["maps"] = new("oasis\n\n radar \n"),
        });

        Assert.Equal(["sv_hostname"], errors);
        Assert.Equal(24, values["sv_maxclients"]);
        Assert.False(values.ContainsKey("g_password")); // leer = unverändert
        Assert.Equal(false, values["pvp"]);
        Assert.Equal(new List<string> { "oasis", "radar" }, values["maps"]);
    }

    [Fact]
    public void Build_clears_password_and_rejects_out_of_range_numbers_and_bad_maps()
    {
        using var doc = JsonDocument.Parse(Form);
        var fields = ConfigTemplateService.Parse(doc.RootElement).Fields;

        var (values, errors) = ConfigTemplateService.Build(fields, new Dictionary<string, TemplateInput>
        {
            ["sv_maxclients"] = new("65"),
            ["g_password"] = new("egal", ClearPassword: true),
            ["difficulty"] = new("insane"),
            ["maps"] = new("a\nb\nc\nd"),
        });

        Assert.Equal(["sv_maxclients", "difficulty", "maps"], errors);
        Assert.Equal(string.Empty, values["g_password"]);
    }

    [Fact]
    public async Task Apply_reports_pending_on_202()
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.Accepted, """{"data":{"id":"c1"}}"""));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);

        var outcome = await new ConfigTemplateService(api.Http).ApplyAsync("et", "01F", new Dictionary<string, object?> { ["sv_maxclients"] = 20 }, ConfigAfter.Save, "Clanwar");

        Assert.Equal(ConfigSaveOutcome.Pending, outcome);
        var request = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Contains("\"values\":{\"sv_maxclients\":20}", request.Body, StringComparison.Ordinal);
    }
}
