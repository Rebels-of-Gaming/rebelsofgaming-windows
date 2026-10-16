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
            var store = new WikiOfflineStore(folder);
            var page = new WikiPage("01J", "et", "technik", "Technik", "de", "ET", "wikitext", null, "<p>x</p>", [], [], "r1", null, null, null);
            await store.SaveAsync(page);

            Assert.Equal("ET", (await store.LoadAsync("01J", "de"))?.Title);
            Assert.Single(store.All("de"));
            Assert.Empty(store.All("en"));
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
}
