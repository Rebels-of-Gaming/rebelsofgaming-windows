using System.Net;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Account;
using RoG.Core.Features.Admin;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features;

public class MediaAdminServiceTests
{
    [Fact]
    public void Pending_items_only_from_albums_i_manage()
    {
        using var managed = JsonDocument.Parse("""
            {"data":{"slug":"cw","title":"Clanwar","can_manage":true,"items":[
              {"id":"i1","kind":"image","status":"pending","caption":"Sieg","user":{"id":"u","name":"Anna"},"created_at":"2026-10-16T10:00:00Z","image":{"thumb":"https://cdn.example/t.webp","medium":"m","full":"https://cdn.example/f.webp","width":1,"height":1}},
              {"id":"i2","kind":"image","status":"approved"}]}}
            """);
        using var foreign = JsonDocument.Parse("""{"data":{"slug":"x","title":"X","can_manage":false,"items":[{"id":"i3","kind":"image","status":"pending"}]}}""");

        var pending = MediaAdminService.ParsePending(managed.RootElement).ToList();

        Assert.Equal("i1", Assert.Single(pending).Id);
        Assert.Equal("Anna", pending[0].Author);
        Assert.Equal("Clanwar", pending[0].AlbumTitle);
        Assert.Empty(MediaAdminService.ParsePending(foreign.RootElement));
    }

    [Fact]
    public void Upload_reports_are_parsed()
    {
        using var doc = JsonDocument.Parse("""
            {"data":[{"id":"r1","reason":"Urheberrecht","category":"child_safety","reporter":{"id":"u","name":"Ben"},"created_at":"2026-10-16T10:00:00Z","handled_at":null,"resolution":null,
              "upload":{"id":"up","title":null,"original_name":"bild.png","user":{"id":"o","name":"Cem"},"links":{"thumb":"https://i.example/t.webp","direct":"https://i.example/d.png"}}},
              {"id":"r2","reason":"Spam","reporter":null,"created_at":null,"handled_at":"2026-10-16T11:00:00Z","resolution":"kept","upload":null}]}
            """);

        var reports = MediaAdminService.ParseReports(doc.RootElement);

        Assert.True(reports[0].IsOpen);
        Assert.True(reports[0].IsChildSafety);
        Assert.False(reports[1].IsChildSafety);
        Assert.Equal("bild.png", reports[0].UploadTitle);
        Assert.Equal("Cem", reports[0].Owner);
        Assert.False(reports[1].IsOpen);
        Assert.Null(reports[1].Preview);
    }

    [Fact]
    public async Task Resolve_sends_resolution()
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """{"data":{}}"""));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);

        await new MediaAdminService(api.Http).ResolveAsync("r1", delete: true);

        var request = Assert.Single(stub.Requests);
        Assert.EndsWith("/v1/admin/uploads/reports/r1/resolve", request.Uri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("\"resolution\":\"deleted\"", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Web_links_respect_known_permissions()
    {
        var me = new UserProfile("u", "u", "U", null, null, null, null, "active", true, null, [],
            new Dictionary<string, IReadOnlyList<string>> { ["global"] = ["games.manage"] }, null);

        var keys = AdminWebLinks.For(me).Select(l => l.Key).ToList();

        Assert.Contains("games", keys);
        Assert.Contains("pages", keys); // ohne festes Recht – die Website prüft
        Assert.DoesNotContain("roles", keys);
        Assert.Empty(AdminWebLinks.For(null));
        Assert.Equal(AdminWebLinks.All.Count, AdminWebLinks.All.Select(l => l.Key).Distinct().Count());
    }

    [Fact]
    public void Admin_domain_comes_from_environments()
    {
        Assert.Equal("https://admin.rebelsofgaming.com/", RoGEnvironment.Get(RoGEnvironment.Production).AdminBaseUrl?.ToString());
    }
}
