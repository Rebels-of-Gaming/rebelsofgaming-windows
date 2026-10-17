using System.Text.Json;
using RoG.Core.Features.Admin;

namespace RoG.Core.Tests.Features;

public class AdminResourcesTests
{
    private static readonly AdminArea Area = new(
        "test", "T", "games.manage", "games:read", "games:write", "games",
        [
            new AdminField("name", "N", AdminFieldType.Text, Required: true, MaxLength: 10),
            new AdminField("slug", "S", AdminFieldType.Text, CreateOnly: true),
            new AdminField("description", "D", AdminFieldType.Translated, Required: true),
            new AdminField("active", "A", AdminFieldType.Bool),
            new AdminField("sort", "O", AdminFieldType.WholeNumber, Min: 0, Max: 100),
            new AdminField("color", "C", AdminFieldType.Color),
            new AdminField("game_id", "G", AdminFieldType.Reference, ReadFrom: "game"),
            new AdminField("aliases", "L", AdminFieldType.Lines),
            new AdminField("kind", "K", AdminFieldType.Choice, Choices: [new("a", "A"), new("b", "B")]),
        ],
        SubtitleFields: ["game.name", "sort"]);

    private static JsonElement Item() => JsonDocument.Parse("""
        {"id":"g1","name":"ET","slug":"et","description":{"de":"Shooter","en":"Shooter EN"},"active":true,"sort":3,
         "color":"#e0324a","game":{"id":"x1","name":"Wolfenstein"},"aliases":["et","wolf"],"kind":"a"}
        """).RootElement;

    [Fact]
    public void Reads_values_including_nested_and_translated()
    {
        var v = AdminForm.Read(Area, Item());

        Assert.Equal("ET", v.Text("name"));
        Assert.True(v.Flag("active"));
        Assert.Equal("3", v.Text("sort"));
        Assert.Equal("x1", v.Text("game_id"));
        Assert.Equal("et\nwolf", v.Text("aliases"));
        Assert.Equal("Shooter EN", v.Translations("description")["en"]);
    }

    [Fact]
    public void Update_body_contains_only_changes_and_skips_create_only()
    {
        var original = AdminForm.Read(Area, Item());
        var edited = AdminForm.Read(Area, Item());
        edited["sort"] = "7";
        edited["slug"] = "neu";
        edited["color"] = string.Empty;
        edited["description"] = new Dictionary<string, string> { ["de"] = "Shooter", ["en"] = "Shooter EN", ["fr"] = "Tir" };

        var body = AdminForm.BuildBody(Area, original, edited);

        Assert.Equal(["description", "sort", "color"], body.Select(p => p.Key).ToList()); // Reihenfolge der Felder
        Assert.Equal(7, body["sort"]!.GetValue<long>());
        Assert.Null(body["color"]);
        Assert.Equal("Tir", body["description"]!["fr"]!.GetValue<string>());
    }

    [Fact]
    public void Create_body_has_filled_fields_only()
    {
        var v = AdminForm.Read(Area, null);
        v["name"] = " CS2 ";
        v["description"] = new Dictionary<string, string> { ["de"] = "Taktik", ["en"] = " " };
        v["aliases"] = "cs\n\n go ";
        v["color"] = "#abcdef";

        var body = AdminForm.BuildBody(Area, null, v);

        Assert.Equal("CS2", body["name"]!.GetValue<string>());
        Assert.Equal("""{"de":"Taktik"}""", body["description"]!.ToJsonString());
        Assert.Equal("""["cs","go"]""", body["aliases"]!.ToJsonString());
        Assert.Equal("#ABCDEF", body["color"]!.GetValue<string>());
        Assert.False(body["active"]!.GetValue<bool>());
        Assert.False(body.ContainsKey("sort"));
    }

    [Fact]
    public void Validates_required_length_number_range_color_and_choice()
    {
        var v = AdminForm.Read(Area, null);
        v["name"] = "viel zu langer Name";
        v["sort"] = "200";
        v["color"] = "rot";
        v["kind"] = "c";

        var errors = AdminForm.Validate(Area, v, isNew: true);

        Assert.Equal(AdminFormErrors.TooLong, errors["name"]);
        Assert.Equal(AdminFormErrors.RequiredGerman, errors["description"]);
        Assert.Equal(AdminFormErrors.Range, errors["sort"]);
        Assert.Equal(AdminFormErrors.Color, errors["color"]);
        Assert.Equal(AdminFormErrors.Choice, errors["kind"]);
        Assert.Empty(AdminForm.Validate(Area, AdminForm.Read(Area, Item()), isNew: false));
    }

    [Fact]
    public void List_rows_use_locale_and_subtitles()
    {
        var root = JsonDocument.Parse("""{"data":[{"id":"1","name":{"de":"Spiel","en":"Game"},"game":{"name":"ET"},"sort":2},{"name":"ohne id"}],"meta":{"next_cursor":"c2"}}""").RootElement;

        var page = AdminResourceService.ParseList(Area, root, "en");

        var row = Assert.Single(page.Items);
        Assert.Equal("Game", row.Title);
        Assert.Equal("ET · 2", row.Subtitle);
        Assert.Equal("c2", page.NextCursor);
    }

    [Fact]
    public void Single_language_text_is_partial_and_only_changes_are_sent()
    {
        var item = JsonDocument.Parse("""{"id":"1","name":"ET","description":"Shooter (en)"}""").RootElement;
        var original = AdminForm.Read(Area, item, "en");
        var map = Assert.IsType<AdminTranslations>(original["description"]);
        Assert.True(map.Partial);
        Assert.Equal(["en"], map.Keys);

        var edited = AdminForm.Read(Area, item, "en");
        edited["description"] = new AdminTranslations { ["en"] = "Shooter" };
        var body = AdminForm.BuildBody(Area, original, edited);

        Assert.Equal("""{"en":"Shooter"}""", body["description"]!.ToJsonString());
    }

    [Fact]
    public void Cleared_language_is_sent_empty_so_the_website_removes_it()
    {
        var original = AdminForm.Read(Area, Item());
        var edited = AdminForm.Read(Area, Item());
        edited["description"] = new AdminTranslations { ["de"] = "Shooter", ["en"] = " " };

        var body = AdminForm.BuildBody(Area, original, edited);

        Assert.Equal("""{"en":""}""", body["description"]!.ToJsonString());
    }

    [Fact]
    public void Dotted_keys_are_nested_and_always_send_keys_are_kept()
    {
        var area = new AdminArea("s", "T", "settings.manage", "settings:read", "settings:write", string.Empty,
            [
                new AdminField("role_id", "R", AdminFieldType.Text, AlwaysSend: true),
                new AdminField("maintenance.enabled", "M", AdminFieldType.Bool),
                new AdminField("maintenance.message", "MM", AdminFieldType.Translated),
                new AdminField("sources", "S", AdminFieldType.MultiChoice, Choices: [new("website", "W"), new("discord", "D")]),
            ],
            DetailPath: "settings/clan", UpdatePath: "settings/clan");
        var item = JsonDocument.Parse("""{"role_id":"r1","maintenance":{"enabled":false,"message":{"de":"Pause"}},"sources":["website"]}""").RootElement;
        var original = AdminForm.Read(area, item);
        var edited = AdminForm.Read(area, item);
        edited["maintenance.enabled"] = true;
        edited["sources"] = "website\ndiscord";

        var body = AdminForm.BuildBody(area, original, edited);

        Assert.True(area.IsSingleton);
        Assert.Equal("""{"role_id":"r1","maintenance":{"enabled":true},"sources":["website","discord"]}""", body.ToJsonString());
        edited["sources"] = "website\nfoo";
        Assert.Equal(AdminFormErrors.Choice, AdminForm.Validate(area, edited, isNew: false)["sources"]);
    }

    [Fact]
    public void Write_id_can_differ_from_list_id()
    {
        var area = Area with { IdField = "slug", WriteIdField = "id" };
        var record = AdminResourceService.ToRecord(area, Item(), "de");

        Assert.Equal("et", record.Id);
        Assert.Equal("g1", AdminResourceService.WriteId(area, record));
    }
}

public class AdminAreasTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    public void Every_label_of_every_area_is_translated(string language)
    {
        var path = Path.Combine(RoG.Core.Tests.Localization.ReswCompletenessTests.RepoRoot(), "src", "RoG.App", "Strings", language, "Resources.resw");
        var keys = System.Xml.Linq.XDocument.Load(path).Root!.Elements("data")
            .Where(d => !string.IsNullOrWhiteSpace(d.Element("value")?.Value))
            .Select(d => d.Attribute("name")!.Value).ToHashSet(StringComparer.Ordinal);
        var needed = AdminAreas.All.SelectMany(a => new[] { a.TitleKey, "AdminGroup_" + a.Group, a.HelpKey }
                .Concat(a.Fields.SelectMany(f => new[] { f.LabelKey, f.HelpKey }.Concat(f.Choices?.Select(c => c.LabelKey) ?? []))))
            .Concat(new[] { "squads", "members" }.Select(k => AdminAreas.Find(k)!.TitleKey))
            .OfType<string>().Distinct();

        var missing = needed.Where(k => !keys.Contains(k)).ToList();
        Assert.True(missing.Count == 0, string.Join(", ", missing));
    }

    [Fact]
    public void Areas_are_consistent()
    {
        foreach (var a in AdminAreas.All)
        {
            Assert.False(string.IsNullOrEmpty(a.Group), a.Key);
            Assert.Equal(a.Fields.Count, a.Fields.Select(f => f.Key).Distinct().Count());
            Assert.All(a.Fields.Where(f => f.Type == AdminFieldType.Reference), f => Assert.NotNull(AdminAreas.Find(f.Reference!)));
            Assert.True(a.IsSingleton ? a.DetailPath is not null && a.UpdatePath is not null : a.ListPath.Length > 0, a.Key);
            Assert.All(new[] { a.DetailPath, a.UpdatePath, a.DeletePath }.OfType<string>().Where(_ => !a.IsSingleton && !a.FullReplace),
                p => Assert.Contains("{id}", p, StringComparison.Ordinal));
        }

        Assert.Equal(AdminAreas.All.Count, AdminAreas.All.Select(a => a.Key).Distinct().Count());
    }
}
