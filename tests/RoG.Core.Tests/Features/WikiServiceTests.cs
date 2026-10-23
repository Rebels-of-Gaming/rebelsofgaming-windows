using System.Net;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Wiki;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features;

public class WikiServiceTests
{
    private static (WikiService Service, StubHttpHandler Stub) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);
        return (new WikiService(api.Client, api.Http, new Uri("https://wiki.rebelsofgaming.com/")), stub);
    }

    [Fact]
    public async Task Tree_is_flattened_with_depth()
    {
        var (service, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """
            {"data":[{"id":"a","slug":"server","title":"Server","locales":["de"],"tags":[],"updated_at":"2026-10-01T10:00:00Z",
              "children":[{"id":"b","slug":"et","title":"ET","locales":["de"],"tags":[],"updated_at":"2026-10-02T10:00:00Z","children":[]}]},
              {"id":"c","slug":"regeln","title":"Regeln","locales":["de"],"tags":[],"updated_at":"2026-10-03T10:00:00Z","children":[]}]}
            """));

        var tree = await service.TreeAsync("technik");

        Assert.Equal(["Server", "ET", "Regeln"], tree.Select(t => t.Title));
        Assert.Equal([0, 1, 0], tree.Select(t => t.Depth));
    }

    [Fact]
    public async Task Edit_conflict_carries_current_version()
    {
        var (service, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Conflict, """
            {"type":"x","title":"Conflict","status":409,"detail":"Die Seite wurde inzwischen geändert.","code":"edit_conflict",
             "current":{"revision_id":"r2","locale":"de","title":"Regeln","format":"wikitext","content":"neu","updated_at":"2026-10-14T12:05:00Z","updated_by":{"id":"u","name":"Wahke"}}}
            """, "application/problem+json"));

        var ex = await Assert.ThrowsAsync<WikiEditConflictException>(() => service.SaveAsync("p", "de", "Regeln", "mein", "wikitext", null, "r1"));

        Assert.Equal("r2", ex.CurrentRevisionId);
        Assert.Equal("neu", ex.CurrentContent);
        Assert.Equal("Wahke", ex.UpdatedBy);
        Assert.Contains("\"base_revision_id\":\"r1\"", stub.Requests[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_sends_query_the_spec_is_missing()
    {
        var (service, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """
            {"data":[{"id":"a","slug":"et","namespace":"technik","locale":"de","title":"ET-Server","snippet":"… <mark>ET</mark> …"}]}
            """));

        var hits = await service.SearchAsync(" ET Server ");

        Assert.Equal("technik", Assert.Single(hits).NamespaceSlug);
        Assert.Contains("q=ET%20Server", stub.Requests[0].Uri!.Query, StringComparison.Ordinal);
        Assert.Empty(await service.SearchAsync("x"));
    }

    [Theory]
    [InlineData("https://wiki.rebelsofgaming.com/technik/server/et-server#Ports", "technik", "et-server")]
    [InlineData("https://wiki.rebelsofgaming.com/clan/regeln", "clan", "regeln")]
    public void Wiki_links_are_recognised(string url, string ns, string slug)
    {
        var (service, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{}"));
        Assert.Equal((ns, slug), service.ParseWikiLink(new Uri(url)));
    }

    [Theory]
    [InlineData("https://wiki.rebelsofgaming.com/file/bild.png")]
    [InlineData("https://rebelsofgaming.com/de/news/x")]
    [InlineData("https://wiki.rebelsofgaming.com/technik")]
    public void Other_links_are_not_wiki_pages(string url)
    {
        var (service, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{}"));
        Assert.Null(service.ParseWikiLink(new Uri(url)));
    }

    [Fact]
    public void Line_diff_marks_changes()
    {
        var diff = LineDiff.Compare("a\nb\nc", "a\nB\nc\nd");

        Assert.Equal([(' ', "a"), ('-', "b"), ('+', "B"), (' ', "c"), ('+', "d")], diff);
    }

    [Fact]
    public async Task Offline_store_round_trip_and_rejects_bad_ids()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rog-wiki-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WikiOfflineStore(folder, new XorProtector());
            var page = new WikiArticle("01J", "et", "technik", "Technik", "de", "ET", "wikitext", null, "<p>x</p>", [], [], "r1", null, null, null);
            await store.SaveAsync(page);

            Assert.Equal("ET", (await store.LoadAsync("01J", "de"))?.Title);
            Assert.Single(await store.AllAsync("de"));
            Assert.Empty(await store.AllAsync("en"));
            Assert.DoesNotContain("<p>x</p>", File.ReadAllText(Directory.GetFiles(folder).Single()), StringComparison.Ordinal);
            await Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync("../x", "de"));
            store.Clear();
            Assert.Null(await store.LoadAsync("01J", "de"));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Fact]
    public async Task Create_page_returns_page_and_status()
    {
        var (service, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Created, """{"data":{"id":"r1","page_id":"p1","locale":"de","format":"wikitext","status":"pending"}}"""));

        var created = await service.CreatePageAsync("clan", "de", " Goldrush ", "Text", "wikitext", null, "p0");

        Assert.Equal(new WikiCreated("p1", "pending"), created);
        var request = Assert.Single(stub.Requests);
        Assert.EndsWith("/v1/wiki/namespaces/clan/pages", request.Uri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("\"title\":\"Goldrush\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"parent_id\":\"p0\"", request.Body, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreatePageAsync("clan", "de", " ", "x", "wikitext", null, null));
    }

    [Fact]
    public async Task Register_file_sends_body_and_parses_result()
    {
        var (service, stub) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.Created, """
            {"data":{"slug":"goldrush-karte.png","name":"Goldrush Karte.png","namespace":"clan","src":"https://cdn.example/x.png","url":"https://wiki.example/file/goldrush-karte.png"}}
            """));

        var file = await service.RegisterFileAsync("01U", "clan", "Goldrush Karte.png", "Übersicht");

        Assert.Equal("Goldrush Karte.png", file.Name);
        Assert.Equal("https://cdn.example/x.png", file.Source!.ToString());
        Assert.Contains("\"upload_id\":\"01U\"", Assert.Single(stub.Requests).Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public", RoG.Core.Features.Uploads.UploadVisibility.Public)]
    [InlineData("members", RoG.Core.Features.Uploads.UploadVisibility.Members)]
    [InlineData("private", RoG.Core.Features.Uploads.UploadVisibility.Private)]
    [InlineData("team", RoG.Core.Features.Uploads.UploadVisibility.Private)]
    [InlineData(null, RoG.Core.Features.Uploads.UploadVisibility.Members)]
    public void Upload_visibility_follows_namespace(string? value, RoG.Core.Features.Uploads.UploadVisibility expected) =>
        Assert.Equal(expected, WikiService.UploadVisibilityFor(value));

    [Fact]
    public void File_embed_depends_on_format()
    {
        Assert.Equal("[[Datei:Karte.png|mini]]", WikiService.FileEmbed("wikitext", "Karte.png", null));
        Assert.Equal("![Karte.png](https://cdn.example/k.png)", WikiService.FileEmbed("markdown", "Karte.png", new Uri("https://cdn.example/k.png")));
    }

    [Theory]
    [InlineData(""","last_editor":{"id":"01U","name":"Wahke"}""", "01U")]
    [InlineData(""","last_editor":null""", null)]
    [InlineData("", null)]
    public async Task Page_maps_last_editor(string editor, string? expected)
    {
        var (service, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, $$$"""
            {"data":{"id":"p1","slug":"regeln","namespace":{"id":"n","slug":"clan","name":"Clan"},"locale":"de","title":"Regeln",
             "format":"markdown","content":"x","html":"<p>x</p>","toc":[],"categories":[],"revision_id":"r1",
             "updated_at":"2026-10-18T10:00:00Z","watching":false,"url":"https://wiki.rebelsofgaming.com/clan/regeln"{{{editor}}}}}
            """));

        var page = await service.PageAsync("p1");

        Assert.Equal(expected, page.LastEditorId);
        Assert.Equal(expected is not null, page.IsLastEditedBy("01U"));
        Assert.False(page.IsLastEditedBy(null));
    }

    [Fact]
    public void Own_revision_is_recognised()
    {
        var revision = new WikiRevision("r1", "Regeln", null, "approved", "Wahke", null, "de", "01U");

        Assert.True(revision.IsBy("01U"));
        Assert.False(revision.IsBy("01X"));
        Assert.False((revision with { AuthorId = null }).IsBy("01U"));
    }

    [Theory]
    [InlineData("team", true)]
    [InlineData("squad", false)]
    [InlineData("members", false)]
    public void Team_namespaces_are_internal(string visibility, bool expected) =>
        Assert.Equal(expected, new WikiNamespace("01J", "intern", "Intern", null, visibility, false, 0).IsInternal);
}

internal sealed class XorProtector : RoG.Core.Security.IPayloadProtector
{
    public Task<byte[]> ProtectAsync(byte[] data, CancellationToken cancellationToken = default) => Task.FromResult(data.Select(b => (byte)(b ^ 0x5A)).ToArray());

    public Task<byte[]> UnprotectAsync(byte[] data, CancellationToken cancellationToken = default) => ProtectAsync(data, cancellationToken);

}
