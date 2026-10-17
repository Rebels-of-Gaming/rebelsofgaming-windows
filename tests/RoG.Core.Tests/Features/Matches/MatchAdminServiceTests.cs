using System.Net;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Matches;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Matches;

public class MatchAdminServiceTests
{
    private static MatchDraft Draft() => new(
        null, "01S", "clanwar", "finished", "Night Owls", new DateTimeOffset(2026, 10, 2, 20, 0, 0, TimeSpan.FromHours(2)),
        [new("supply", 2, 1)]);

    [Fact]
    public void Maps_are_parsed_from_lines_and_formatted_back()
    {
        var maps = MatchAdminService.ParseMaps("supply 2:1\r\n radar 2 - 0 \n\ngoldrush\nfrost 10:12");

        Assert.Equal(
            [new MatchMapDraft("supply", 2, 1), new MatchMapDraft("radar", 2, 0), new MatchMapDraft("goldrush", null, null), new MatchMapDraft("frost", 10, 12)],
            maps);
        Assert.Equal("supply 2:1\nradar 2:0\ngoldrush\nfrost 10:12", MatchAdminService.FormatMaps(maps));
    }

    [Fact]
    public void Links_are_parsed_with_optional_type_and_label()
    {
        var links = MatchAdminService.ParseLinks("https://www.twitch.tv/videos/1 Twitch VOD\ndemo https://files.example/d.dm_84\nhttps://example.com");

        Assert.Equal(
            [new MatchLinkDraft("vod", "Twitch VOD", "https://www.twitch.tv/videos/1"), new MatchLinkDraft("demo", null, "https://files.example/d.dm_84"), new MatchLinkDraft("other", null, "https://example.com")],
            links);
        Assert.Equal("vod https://www.twitch.tv/videos/1 Twitch VOD", MatchAdminService.FormatLinks(links.Take(1)));
    }

    [Fact]
    public void Validation_checks_opponent_maps_and_links()
    {
        Assert.Empty(MatchAdminService.Validate(Draft()));
        var bad = Draft() with
        {
            Opponent = " ",
            Maps = Enumerable.Range(0, 16).Select(i => new MatchMapDraft("m" + i, null, null)).ToList(),
            Links = [new MatchLinkDraft("vod", null, "javascript:alert(1)")],
        };

        Assert.Equal(["opponent", "maps", "links"], MatchAdminService.Validate(bad));
    }

    [Fact]
    public void Body_has_report_in_app_language_and_players_only_when_set()
    {
        var body = MatchAdminService.BuildBody(Draft() with { Report = " Stark ", OpponentTag = " " }, "en");

        Assert.Equal(new Dictionary<string, string> { ["en"] = "Stark" }, body["report"]);
        Assert.Null(body["opponent_tag"]);
        Assert.Equal("2026-10-02T20:00:00+02:00", body["played_at"]);
        Assert.False(body.ContainsKey("players"));
        Assert.Single((System.Collections.IList)MatchAdminService.BuildBody(
            Draft() with { Players = [new("u1"), new("u1", "substitute")] }, "de")["players"]!);
    }

    [Fact]
    public void Match_is_read_for_editing()
    {
        using var doc = JsonDocument.Parse("""
            {"data":{"id":"01M","slug":"x","type":"scrim","status":"finished","squad":{"id":"01S","slug":"et","name":"ET","game":"et"},
              "opponent":{"name":"Wolfpack","tag":"WP","url":null},"played_at":"2026-10-01T18:00:00Z","score":{"own":2,"opponent":0},
              "result":"win","published":true,"event_id":null,"report":"Gut",
              "maps":[{"position":2,"map":"radar","score":{"own":1,"opponent":0},"result":"win"},{"position":1,"map":"supply","score":{"own":1,"opponent":0},"result":"win"}],
              "lineup":[{"member":{"id":"u1","slug":"a","name":"A"},"slot":"starter","position":"Medic"}],
              "links":[{"type":"vod","label":"VOD","url":"https://twitch.tv/videos/1"}],
              "attachments":[{"id":"a1","name":"score.png","mime":"image/png","size":10,"image":true,"width":1,"height":1,"url":"https://cdn.example/a.png"}]}}
            """);

        var draft = MatchAdminService.Parse(doc.RootElement);

        Assert.Equal("01S", draft.SquadId);
        Assert.Equal(["supply", "radar"], draft.Maps.Select(m => m.Map));
        Assert.Null(draft.ScoreOwn); // Ergebnis kommt aus den Maps
        Assert.Equal("Medic", Assert.Single(draft.Players!).Position);
        Assert.True(Assert.Single(draft.Attachments!).Image);
    }

    [Fact]
    public async Task Save_posts_new_and_patches_existing()
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.Created, """{"data":{"id":"01M"}}"""));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);
        var service = new MatchAdminService(api.Http);

        Assert.Equal("01M", await service.SaveAsync(Draft(), "de"));
        await service.SaveAsync(Draft() with { Id = "01M" }, "de");

        Assert.Equal(HttpMethod.Post, stub.Requests[0].Method);
        Assert.Equal(HttpMethod.Patch, stub.Requests[1].Method);
        Assert.Contains("\"maps\":[{\"map\":\"supply\",\"score_own\":2,\"score_opponent\":1}]", stub.Requests[0].Body, StringComparison.Ordinal);
    }
}
