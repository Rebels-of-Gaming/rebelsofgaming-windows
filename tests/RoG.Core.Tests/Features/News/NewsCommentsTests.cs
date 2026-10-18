using System.Net;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.News;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.News;

public class NewsCommentsTests
{
    private const string Json = """
        {"data":[
          {"id":"c1","body":"Hallo","hidden":false,"author_blocked":false,"author":{"name":"Wahke","slug":"wahke"},"created_at":"2026-10-18T10:00:00Z"},
          {"id":"c2","body":null,"hidden":false,"author_blocked":true,"author":{"name":"Troll","slug":"troll"},"created_at":"2026-10-18T10:05:00Z"},
          {"id":"c3","body":"weg","hidden":true,"author_blocked":false,"author":null,"created_at":"2026-10-18T10:06:00Z"}
        ]}
        """;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Comments_mark_blocked_authors_and_ask_for_them_only_on_request(bool showBlocked, bool expectQuery)
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, Json));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);

        var comments = await new NewsService(api.Client, new Uri("https://rebelsofgaming.com/")).GetCommentsAsync("patch-notes", showBlocked);

        Assert.Equal(["c1", "c2"], comments.Select(c => c.Id));
        Assert.False(comments[0].AuthorBlocked);
        Assert.True(comments[1].AuthorBlocked);
        Assert.Null(comments[1].Body);
        Assert.Equal(expectQuery, Assert.Single(stub.Requests).Uri!.Query.Contains("show_blocked=true", StringComparison.Ordinal));
    }
}
