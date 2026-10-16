using System.Net;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Applications;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Applications;

public class ApplicationServiceTests
{
    [Fact]
    public async Task Answers_are_sent_as_text()
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.Created, """{"data":{"id":"01A"}}"""));
        var api = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);
        var service = new ApplicationService(api.Client, api.Http);

        await service.SubmitAsync("rog-cs2", new DateOnly(2004, 5, 17),
            new Dictionary<string, string> { ["01Q"] = "3-4", ["01R"] = "yes", ["01S"] = "  " }, ["01RULES"], true, "Nyx");

        var req = Assert.Single(stub.Requests);
        Assert.EndsWith("/v1/squads/rog-cs2/applications", req.Uri!.AbsolutePath, StringComparison.Ordinal);
        Assert.True(req.Headers.ContainsKey("Idempotency-Key"));
        using var body = JsonDocument.Parse(req.Body!);
        var root = body.RootElement;
        Assert.Equal("2004-05-17", root.GetProperty("birthdate").GetString());
        Assert.Equal("3-4", root.GetProperty("answers").GetProperty("01Q").GetString());
        Assert.False(root.GetProperty("answers").TryGetProperty("01S", out _));
        Assert.True(root.GetProperty("accept_rules").GetProperty("01RULES").GetBoolean());
        Assert.Equal("Nyx", root.GetProperty("referral").GetString());
    }

    [Fact]
    public void Status_page_is_mapped()
    {
        using var doc = JsonDocument.Parse("""
            {"id":"01A","squad":{"slug":"et","name":"ET Squad","game":"ET"},"status":"trial","status_label":"Probezeit","step":3,
             "submitted_at":"2026-10-01T10:00:00Z","trial_ends_at":"2026-10-20T10:00:00Z","decision_message":null,
             "vote":{"participation":67,"quorum_percent":50,"quorum_met":true,"ends_at":null},
             "tasks":[{"id":"t1","title":"Zwei Clanabende","done":true}],
             "timeline":[{"type":"submitted","label":"Eingereicht","at":"2026-10-01T10:00:00Z"}],
             "messages":[{"id":"m1","body":"Willkommen!","from_squad":true,"author":"Leni","at":null}]}
            """);

        var app = ApplicationService.Map(doc.RootElement);

        Assert.Equal("Probezeit", app.StatusLabel);
        Assert.Equal(3, app.Step);
        Assert.True(app.IsOpen);
        Assert.Equal(67, app.VoteParticipation);
        Assert.True(app.Tasks[0].Done);
        Assert.True(app.Messages[0].FromSquad);
    }
}

public class VoteServiceTests
{
    [Fact]
    public void Text_and_number_answers_are_read()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {"data":[{"id":"01A","status":"vote","applicant":{"id":"u","slug":"nyx","name":"Nyx"},"squad":{"id":"s","slug":"et","name":"ET"},
              "age":19,"answers":[{"question_id":"q1","label":"Wie oft?","type":"select","answer":"3-4"},{"question_id":"q2","label":"Alter Clan?","type":"number","answer":2}],
              "tasks":[{"id":"t","title":"Clanabend","done":true,"done_at":null}],
              "vote":{"round":1,"eligible":6,"cast":4,"participation":67,"quorum_percent":50,"quorum_met":true},
              "vote_ends_at":"2026-10-18T18:00:00Z","my_vote":null}]}
            """);

        var vote = Assert.Single(VoteService.Parse(doc.RootElement));

        Assert.Equal("Nyx", vote.ApplicantName);
        Assert.Equal([("Wie oft?", "3-4"), ("Alter Clan?", "2")], vote.Answers);
        Assert.True(vote.Tasks[0].Done);
        Assert.Equal(67, vote.Participation);
        Assert.Null(vote.MyVote);
    }
}

public class ApplicationAdminServiceTests
{
    [Fact]
    public void Detail_sums_activity_and_reads_text_answers()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {"id":"a1","status":"trial","applicant":{"id":"u","slug":"nyx","name":"Nyx"},"squad":{"id":"s","slug":"et","name":"ET"},
             "age":19,"referral":null,"answers":[{"question_id":"q","label":"Warum?","type":"textarea","answer":"Weil ET"}],
             "ban_check":[{"key":"discord","status":"ok","message":"keine Sperre"}],
             "tasks":[{"id":"t1","title":"Clanabend","done":false,"done_at":null}],
             "activity":[{"date":"2026-10-15","minutes":{"discord_voice":30,"teamspeak":10}},{"date":"2026-10-16","minutes":{"discord_voice":15}}],
             "timeline":[{"type":"note","label":"Notiz","public":false,"actor":"Wahke","at":null}],
             "messages":[{"id":"m","internal":true,"author":"Wahke","from_applicant":false,"body":"passt","at":null}],
             "vote":{"round":1,"eligible":6,"cast":4,"yes":3,"no":1,"abstain":0,"participation":67,"quorum_percent":50,"quorum_met":true,"result":"accept","missing":[{"id":"x","name":"Kolibri"}]},
             "my_vote":null,"can_vote":true,"trial_ends_at":"2026-10-20T18:00:00Z"}
            """);

        var detail = ApplicationAdminService.ParseDetail(doc.RootElement);

        Assert.Equal(45, detail.ActivityMinutes["discord_voice"]);
        Assert.Equal(("Warum?", "Weil ET"), Assert.Single(detail.Answers));
        Assert.StartsWith("🔒", detail.Timeline[0].Label, StringComparison.Ordinal);
        Assert.True(detail.Messages[0].Internal);
        Assert.Equal("accept", detail.Vote?.Result);
        Assert.Equal(["Kolibri"], detail.Vote?.Missing);
        Assert.True(detail.CanVote);
    }

    [Fact]
    public void List_reads_cursor_and_vote()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {"data":[{"id":"a1","status":"voting","applicant":{"name":"Nyx"},"squad":{"name":"ET"},"submitted_at":"2026-10-01T10:00:00Z",
                      "vote":{"cast":3,"eligible":6,"participation":50,"result":"pending"}},
                     {"id":"a2","status":"submitted","applicant":{"name":"Rex"},"squad":{"name":"CS2"},"submitted_at":null,"vote":null}],
             "meta":{"next_cursor":"n2"}}
            """);

        var page = ApplicationAdminService.ParseList(doc.RootElement);

        Assert.Equal(50, page.Items[0].Participation);
        Assert.Null(page.Items[1].Participation);
        Assert.Equal("n2", page.NextCursor);
    }
}
