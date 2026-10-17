using System.Net;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.News;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.News;

public class NewsAdminServiceTests
{
    private static readonly NewsCategory[] s_categories = [new("01C", "server", "Server", "#3DDC84")];

    [Fact]
    public void Draft_is_read_with_all_translations_and_category_id()
    {
        using var doc = JsonDocument.Parse("""
            {"data":{"id":"01N","slug":"turnier","title":"Turnier","status":"scheduled","visibility":"members","pinned":true,"comments_enabled":false,
              "published_at":"2026-10-20T16:00:00Z","category":{"slug":"server","name":"Server","color":null},
              "translations":{"title":{"de":"Turnier","en":"Tournament"},"teaser":{"de":"Kurz"},"body":{"de":"**Text**"}}}}
            """);

        var draft = NewsAdminService.ParseDraft(doc.RootElement, s_categories);

        Assert.Equal("Tournament", draft.Title["en"]);
        Assert.Equal("**Text**", draft.Body["de"]);
        Assert.Equal("01C", draft.CategoryId);
        Assert.Equal("scheduled", draft.Status);
        Assert.True(draft.Pinned);
        Assert.False(draft.CommentsEnabled);
    }

    [Fact]
    public void Validation_requires_german_title_and_future_schedule()
    {
        var now = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        var draft = new NewsDraft(null, new Dictionary<string, string> { ["en"] = "Only English" }, new Dictionary<string, string>(), new Dictionary<string, string>(),
            Status: "scheduled", PublishedAt: now.AddMinutes(-5));

        Assert.Equal(["title", "published_at"], NewsAdminService.Validate(draft, now));
        Assert.Empty(NewsAdminService.Validate(draft with { Title = new Dictionary<string, string> { ["de"] = "Titel" }, PublishedAt = now.AddHours(1) }, now));
    }

    [Fact]
    public void Body_drops_empty_translations_and_sends_time_only_when_scheduled()
    {
        var draft = new NewsDraft(null, new Dictionary<string, string> { ["de"] = " Titel ", ["en"] = " ", ["xx"] = "nope" },
            new Dictionary<string, string>(), new Dictionary<string, string> { ["de"] = "Text" }, "01C", Status: "draft", PublishedAt: DateTimeOffset.UnixEpoch);

        var body = NewsAdminService.BuildBody(draft);

        Assert.Equal(new Dictionary<string, string> { ["de"] = "Titel" }, body["title"]);
        Assert.False(body.ContainsKey("published_at"));
        Assert.Equal("2026-10-20T14:00:00Z", NewsAdminService.BuildBody(draft with { Status = "scheduled", PublishedAt = new DateTimeOffset(2026, 10, 20, 16, 0, 0, TimeSpan.FromHours(2)) })["published_at"]);
    }

    [Fact]
    public async Task Save_creates_or_patches_and_returns_warnings()
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """
            {"data":{"id":"01N","slug":"turnier"},"meta":{"warnings":[{"code":"image_visibility_conflict","message":"Bild ist enger sichtbar"}]}}
            """));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);
        var service = new NewsAdminService(api.Http);
        var draft = new NewsDraft(null, new Dictionary<string, string> { ["de"] = "T" }, new Dictionary<string, string>(), new Dictionary<string, string>());

        var saved = await service.SaveAsync(draft);
        await service.SaveAsync(draft with { Id = "01N" });

        Assert.Equal("Bild ist enger sichtbar", Assert.Single(saved.Warnings));
        Assert.Equal(HttpMethod.Post, stub.Requests[0].Method);
        Assert.Equal(HttpMethod.Patch, stub.Requests[1].Method);
        Assert.EndsWith("/v1/news/01N", stub.Requests[1].Uri!.AbsolutePath, StringComparison.Ordinal);
    }
}
