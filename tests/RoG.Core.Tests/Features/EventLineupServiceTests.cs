using System.Net;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Events;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features;

public class EventLineupServiceTests
{
    [Fact]
    public void Participants_are_grouped_by_answer()
    {
        using var doc = JsonDocument.Parse("""
            {"data":{"yes":[{"id":"u1","slug":"a","name":"Anna","absent":false,"comment":" ","answered_at":null}],
                     "maybe":[{"id":"u2","slug":"b","name":"Ben","absent":true,"comment":"evtl. später","answered_at":null}],
                     "no":[],"open":[{"id":"u3","slug":"c","name":"Cem","absent":false,"comment":null,"answered_at":null}]}}
            """);

        var p = EventLineupService.ParseParticipants(doc.RootElement);

        Assert.Equal("Anna", Assert.Single(p.Yes).Name);
        Assert.Null(p.Yes[0].Comment);
        Assert.True(p.Maybe[0].Absent);
        Assert.Empty(p.No);
        Assert.Single(p.Open);
    }

    [Fact]
    public void Lineup_is_split_into_starters_and_substitutes()
    {
        using var doc = JsonDocument.Parse("""
            {"data":{"event_id":"e","note":"Mixed","published_at":"2026-10-16T10:00:00Z",
              "players":[{"user":{"id":"u1","slug":"a","name":"Anna"},"slot":"starter","position":"Medic"},
                         {"user":{"id":"u2","slug":"b","name":"Ben"},"slot":"substitute","position":null}]},
             "meta":{"can_edit":true}}
            """);

        var lineup = EventLineupService.ParseLineup(doc.RootElement);

        Assert.True(lineup.IsPublished);
        Assert.True(lineup.CanEdit);
        Assert.Equal(["Anna"], lineup.Starters.Select(p => p.Name));
        Assert.Equal(["Ben"], lineup.Substitutes.Select(p => p.Name));
    }

    [Fact]
    public async Task Missing_lineup_is_null_and_suggestion_gets_names()
    {
        var stub = new StubHttpHandler((request, _) => request.RequestUri!.AbsolutePath.EndsWith("/lineup", StringComparison.Ordinal)
            ? StubHttpHandler.Json(HttpStatusCode.NotFound, """{"type":"about:blank","title":"Not found","status":404,"code":"not_found"}""")
            : StubHttpHandler.Json(HttpStatusCode.OK, """{"data":{"players":[{"user_id":"u1","slot":"starter","position":null},{"user_id":"u9","slot":"substitute","position":null}]}}"""));
        using var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);
        var service = new EventLineupService(api.Http);
        var participants = new EventParticipants([new("u1", "a", "Anna", false, null)], [], [], []);

        Assert.Null(await service.GetLineupAsync("e1"));
        var suggestion = await service.SuggestionAsync("e1", participants);

        Assert.Equal(["Anna", "u9"], suggestion.Select(p => p.Name));
        Assert.True(suggestion[1].IsSubstitute);
    }

    [Fact]
    public void Body_has_unique_players_with_slots()
    {
        var body = EventLineupService.Body([new("u1", "A", "starter", " "), new("u1", "A", "substitute", null), new("u2", "B", "substitute", "Sniper")], " ");

        var players = (List<Dictionary<string, object?>>)body["players"]!;
        Assert.Equal(2, players.Count);
        Assert.Null(players[0]["position"]);
        Assert.Equal("substitute", players[1]["slot"]);
        Assert.Null(body["note"]);
    }
}
