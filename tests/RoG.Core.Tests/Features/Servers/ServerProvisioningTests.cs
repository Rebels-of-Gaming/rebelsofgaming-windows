using System.Net;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Admin;
using RoG.Core.Features.Servers;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Servers;

public class ServerProvisioningTests
{
    private const string Catalog = """
        {"data":{"locations":[{"id":1,"short":"de","long":"Falkenstein"}],
         "nodes":[{"id":1,"name":"node1","location_id":1,"fqdn":"node1.rogclan.de","memory":65536,"disk":512000,
           "allocations":[{"id":17,"ip":"203.0.113.10","ip_alias":"cs.rogclan.de","port":27016},{"id":18,"ip":"203.0.113.10","ip_alias":null,"port":27017}]}],
         "nests":[{"id":2,"name":"Source Engine","eggs":[{"id":7,"name":"Counter-Strike 2","description":null,"docker_image":"ghcr.io/cs2",
           "docker_images":{"Source":"ghcr.io/cs2"},"startup":"./srcds_run -game csgo",
           "variables":[
             {"name":"Map","description":"Start map","env_variable":"SRCDS_MAP","default_value":"de_dust2","user_viewable":true,"user_editable":true,"rules":"required|string|max:20"},
             {"name":"Max Players","description":null,"env_variable":"MAX_PLAYERS","default_value":"10","user_viewable":true,"user_editable":true,"rules":"required|integer|between:2,64"},
             {"name":"Mode","description":null,"env_variable":"MODE","default_value":"comp","user_viewable":true,"user_editable":false,"rules":"required|in:comp,casual"}]}]}],
         "expiry_presets":[1,3,7,14,30]}}
        """;

    private static ProvisionCatalog Parsed() => ProvisionCatalog.Parse(JsonDocument.Parse(Catalog).RootElement.GetProperty("data"));

    [Fact]
    public void Catalog_is_read_with_nodes_allocations_and_eggs()
    {
        var c = Parsed();

        var node = Assert.Single(c.Nodes);
        Assert.Equal("Falkenstein (de)", c.LocationOf(node));
        Assert.Equal(["cs.rogclan.de:27016", "203.0.113.10:27017"], node.Allocations.Select(a => a.Display));
        var egg = Assert.Single(c.Eggs);
        Assert.Equal("Source Engine · Counter-Strike 2", egg.Display);
        Assert.Equal(2, egg.NestId);
        Assert.Equal(3, egg.Variables.Count);
        Assert.Equal([1, 3, 7, 14, 30], c.ExpiryPresets);
    }

    [Theory]
    [InlineData("required|string|max:20", "", "Provision_Rule_required")]
    [InlineData("required|string|max:20", "de_dust2", null)]
    [InlineData("required|string|max:5", "de_dust2", "Provision_Rule_max_length|5")]
    [InlineData("required|integer|between:2,64", "abc", "Provision_Rule_numeric")]
    [InlineData("required|integer|between:2,64", "1.5", "Provision_Rule_integer")]
    [InlineData("required|integer|between:2,64", "65", "Provision_Rule_max|64")]
    [InlineData("required|integer|between:2,64", "1", "Provision_Rule_min|2")]
    [InlineData("required|integer|between:2,64", "32", null)]
    [InlineData("nullable|string", "", null)]
    [InlineData("required|in:comp,casual", "deathmatch", "Provision_Rule_in|comp, casual")]
    [InlineData("required|boolean", "yes", "Provision_Rule_boolean")]
    [InlineData("required|boolean", "1", null)]
    [InlineData("required|regex:/^[a-z]+$/", "Abc", "Provision_Rule_format")]
    [InlineData("required|regex:/^[a-z]+$/i", "Abc", null)]
    [InlineData("required|alpha_dash", "my-server_1", null)]
    [InlineData("required|alpha_dash", "my server", "Provision_Rule_format")]
    [InlineData("required|digits:4", "123", "Provision_Rule_digits|4")]
    [InlineData("nullable|url", "ftp://x", "Provision_Rule_url")]
    public void Egg_rules_follow_laravel(string rules, string value, string? expected)
    {
        var error = EggRules.Parse(rules).Validate(value);

        Assert.Equal(expected, error is null ? null : error.Argument is null ? error.Key : error.Key + "|" + error.Argument);
    }

    [Fact]
    public void Form_has_egg_variables_and_choices_and_builds_the_body()
    {
        var c = Parsed();
        var node = c.Nodes[0];
        var egg = c.Eggs[0];
        var area = ProvisionForm.Area(c, node, egg);

        var mode = area.Fields.Single(f => f.Key == "environment.MODE");
        Assert.Equal(AdminFieldType.Choice, mode.Type);
        Assert.Equal("Mode", mode.LabelText);
        Assert.Equal(["comp", "casual"], mode.Choices!.Select(x => x.Value));
        Assert.Equal("cs.rogclan.de:27016", area.Fields.Single(f => f.Key == "allocation_id").Choices![0].LabelText);

        var values = ProvisionForm.Defaults(egg);
        values["name"] = "CS2 Scrim";
        values["allocation_id"] = "18";
        values[ProvisionForm.Expiry] = "7";
        values["environment.SRCDS_MAP"] = "de_mirage";
        Assert.Empty(ProvisionForm.Validate(area, egg, values));

        var body = ProvisionForm.Body(area, node, egg, values);
        Assert.Equal(1, (int)body["node_id"]!);
        Assert.Equal(2, (int)body["nest_id"]!);
        Assert.Equal(7, (int)body["egg_id"]!);
        Assert.Equal(18, (int)body["allocation_id"]!);
        Assert.Equal(7, (int)body["expires_in_days"]!);
        Assert.Equal(4096, (long)body["memory"]!);
        Assert.Equal("de_mirage", (string)body["environment"]!["SRCDS_MAP"]!);
        Assert.Equal("comp", (string)body["environment"]!["MODE"]!);
        Assert.Equal("ghcr.io/cs2", (string)body["docker_image"]!);
        Assert.True((bool)body["start_on_completion"]!);
    }

    [Fact]
    public void Additional_ports_are_sent_as_ids_and_auto_count()
    {
        var c = Parsed();
        var node = c.Nodes[0];
        var egg = c.Eggs[0];
        var area = ProvisionForm.Area(c, node, egg);
        Assert.Equal(AdminFieldType.MultiChoice, area.Fields.Single(f => f.Key == "additional_allocations").Type);

        var values = ProvisionForm.Defaults(egg);
        values["name"] = "CS2 Scrim";
        values["allocation_id"] = "17";
        values["additional_allocations"] = "18";
        values["additional_auto"] = "2";
        Assert.Empty(ProvisionForm.Validate(area, egg, values));

        var body = ProvisionForm.Body(area, node, egg, values);
        Assert.Equal([18], body["additional_allocations"]!.AsArray().Select(n => (int)n!));
        Assert.Equal(2, (long)body["additional_auto"]!);

        // ohne Zusatzports gehen die Felder gar nicht erst raus
        values["additional_allocations"] = string.Empty;
        values["additional_auto"] = "0";
        var plain = ProvisionForm.Body(area, node, egg, values);
        Assert.False(plain.ContainsKey("additional_allocations"));
        Assert.False(plain.ContainsKey("additional_auto"));
    }

    [Fact]
    public void Additional_ports_reject_the_main_port_and_more_than_ten()
    {
        var c = Parsed();
        var egg = c.Eggs[0];
        var area = ProvisionForm.Area(c, c.Nodes[0], egg);
        var values = ProvisionForm.Defaults(egg);
        values["name"] = "x";
        values["allocation_id"] = "17";
        values["additional_allocations"] = "17\n18";

        Assert.Equal("Provision_Rule_AdditionalPrimary", ProvisionForm.Validate(area, egg, values)["additional_allocations"]);

        values["additional_allocations"] = "18";
        values["additional_auto"] = "10";
        Assert.Equal("Provision_Rule_AdditionalMax|10", ProvisionForm.Validate(area, egg, values)["additional_allocations"]);
    }

    [Fact]
    public void Form_reports_egg_rule_and_limit_errors()
    {
        var c = Parsed();
        var egg = c.Eggs[0];
        var area = ProvisionForm.Area(c, c.Nodes[0], egg);
        var values = ProvisionForm.Defaults(egg);
        values["name"] = "x";
        values["environment.MAX_PLAYERS"] = "100";
        values["memory"] = "64";

        var errors = ProvisionForm.Validate(area, egg, values);

        Assert.Equal("Provision_Rule_max|64", errors["environment.MAX_PLAYERS"]);
        Assert.True(errors.ContainsKey("memory"));
    }

    [Fact]
    public void Changing_egg_keeps_general_input_but_resets_egg_fields()
    {
        var c = Parsed();
        var before = ProvisionForm.Defaults(c.Eggs[0]);
        before["name"] = "Mein Server";
        before["environment.SRCDS_MAP"] = "de_inferno";
        before["memory"] = "8192";

        var after = ProvisionForm.Defaults(c.Eggs[0], before);

        Assert.Equal("Mein Server", after.Text("name"));
        Assert.Equal("8192", after.Text("memory"));
        Assert.Equal("de_dust2", after.Text("environment.SRCDS_MAP"));
    }

    private static (RoGApi Api, StubHttpHandler Stub) Create(HttpStatusCode status, string json)
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(status, json));
        return (new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub), stub);
    }

    private const string Server = """
        {"data":{"id":"01S","slug":"cs2-scrim","name":"CS2 Scrim","provisioning":{"provisioned":true,"status":"installing","provisioned_at":"2026-10-18T10:00:00Z","expires_at":"2026-10-25T10:00:00Z","deletable":true}}}
        """;

    [Fact]
    public async Task Provision_posts_and_reads_status()
    {
        var (api, stub) = Create(HttpStatusCode.Created, Server);
        using (api)
        {
            var (id, slug, p) = await new ServerProvisioningService(api.Http).ProvisionAsync(new System.Text.Json.Nodes.JsonObject { ["name"] = "CS2 Scrim" });

            var request = Assert.Single(stub.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/v1/servers/provision", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("01S", id);
            Assert.Equal("cs2-scrim", slug);
            Assert.True(p!.IsInstalling);
            Assert.True(p.Deletable);
            Assert.False(p.ExpiresSoon(new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero)));
            Assert.True(p.ExpiresSoon(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero)));
        }
    }

    [Theory]
    [InlineData(7, null, "{\"expires_in_days\":7}")]
    [InlineData(null, 3, "{\"extend_days\":3}")]
    [InlineData(null, null, "{\"expires_at\":null}")]
    public async Task Expiry_sends_exactly_one_option(int? inDays, int? extend, string expected)
    {
        var (api, stub) = Create(HttpStatusCode.OK, Server);
        using (api)
        {
            await new ServerProvisioningService(api.Http).SetExpiryAsync("cs2-scrim", inDays, extend);

            var request = Assert.Single(stub.Requests);
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.EndsWith("/v1/servers/cs2-scrim/expiry", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal(expected, request.Body);
        }
    }

    [Fact]
    public async Task Delete_confirms_with_the_server_name()
    {
        var (api, stub) = Create(HttpStatusCode.NoContent, string.Empty);
        using (api)
        {
            await new ServerProvisioningService(api.Http).DeleteAsync("cs2-scrim", "CS2 Scrim");

            var request = Assert.Single(stub.Requests);
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.EndsWith("/v1/servers/cs2-scrim/provision", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("{\"confirm\":\"CS2 Scrim\"}", request.Body);
        }

        Assert.True(ServerProvisioningService.ConfirmMatches("CS2 Scrim", "CS2 Scrim"));
        Assert.False(ServerProvisioningService.ConfirmMatches("CS2 Scrim", "cs2 scrim"));
        Assert.False(ServerProvisioningService.ConfirmMatches("CS2 Scrim", null));
    }
}
