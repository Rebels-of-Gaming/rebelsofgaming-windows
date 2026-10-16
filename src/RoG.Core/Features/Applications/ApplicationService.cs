using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Applications;

public sealed record FormOption(string Value, string Label);

/// <summary>Frage im Bewerbungsformular: <c>text</c>, <c>textarea</c>, <c>number</c>, <c>select</c>, <c>yesno</c> …</summary>
public sealed record FormQuestion(string Id, string Type, string Label, string? Help, bool Required, IReadOnlyList<FormOption> Options);

public sealed record FormRule(string Id, string Slug, string Title);

public sealed record ApplicationForm(string SquadSlug, string SquadName, bool Waitlist, int MinAge, IReadOnlyList<FormQuestion> Questions, IReadOnlyList<FormRule> Rules);

public sealed record ApplicationTimelineEntry(string Label, DateTimeOffset? At);

public sealed record ApplicationMessage(string Body, bool FromSquad, string? Author, DateTimeOffset? At);

public sealed record MyApplication(
    string Id,
    string? SquadName,
    string Status,
    string StatusLabel,
    int Step,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? TrialEndsAt,
    string? DecisionMessage,
    int? VoteParticipation,
    IReadOnlyList<(string Title, bool Done)> Tasks,
    IReadOnlyList<ApplicationTimelineEntry> Timeline,
    IReadOnlyList<ApplicationMessage> Messages)
{
    public bool IsOpen => Status is not ("accepted" or "rejected" or "withdrawn" or "expired");
}

/// <summary>Bewerbung (Guide 07): Formular, absenden, eigener Status mit Timeline/Aufgaben/Nachrichten, zurückziehen.</summary>
public sealed class ApplicationService(IRoGApiClient client, HttpClient http)
{
    public async Task<ApplicationForm> GetFormAsync(string squadSlug, CancellationToken cancellationToken = default)
    {
        var d = (await client.V1_applications_formAsync(squadSlug, cancellationToken).ConfigureAwait(false)).Data;
        return new ApplicationForm(
            d.Squad.Slug,
            d.Squad.Name,
            d.Waitlist,
            d.Min_age,
            d.Questions.Select(q => new FormQuestion(q.Id, q.Type, q.Label, ApiValues.Text(q.Help), q.Required,
                q.Options?.Select(o => new FormOption(o.Value, o.Label)).ToList() ?? [])).ToList(),
            d.Rules.Select(r => new FormRule(r.Id, r.Slug, r.Title)).ToList());
    }

    /// <summary>
    /// Bewerbung absenden. Antworten sind Text (auch Zahl, <c>yes</c>/<c>no</c>, Auswahlwert) – die Spec beschreibt sie
    /// fälschlich als Zahl (Lücke W9), deshalb wird dieser eine Aufruf ohne den generierten Client gesendet.
    /// </summary>
    public async Task SubmitAsync(
        string squadSlug,
        DateOnly birthdate,
        IReadOnlyDictionary<string, string> answers,
        IEnumerable<string> acceptedRuleIds,
        bool trialConsent,
        string? referral,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["birthdate"] = birthdate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["answers"] = answers.Where(a => !string.IsNullOrWhiteSpace(a.Value)).ToDictionary(a => a.Key, a => a.Value.Trim()),
            ["accept_rules"] = acceptedRuleIds.ToDictionary(id => id, _ => true),
            ["trial_consent"] = trialConsent,
        };
        if (ApiValues.Text(referral) is { } r)
        {
            body["referral"] = r;
        }

        using var response = await http.PostAsJsonAsync(new Uri($"squads/{Uri.EscapeDataString(squadSlug)}/applications", UriKind.Relative), body, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode(); // Fehler (RFC 9457) wirft schon die Pipeline als ApiProblemException
    }

    public async Task<IReadOnlyList<MyApplication>> MineAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.V1_me_applications_indexAsync(cancellationToken).ConfigureAwait(false);
        return response.Data.Select(a => Map(JsonSerializer.SerializeToElement(a))).ToList();
    }

    public async Task SendMessageAsync(string applicationId, string body, CancellationToken cancellationToken = default) =>
        await client.V1_me_applications_messageAsync(applicationId, new Body4 { Body = body.Trim() }, cancellationToken).ConfigureAwait(false);

    public async Task WithdrawAsync(string applicationId, CancellationToken cancellationToken = default) =>
        await client.V1_me_applications_withdrawAsync(applicationId, cancellationToken).ConfigureAwait(false);

    internal static MyApplication Map(JsonElement e)
    {
        static string? S(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        static int? I(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;
        static IEnumerable<JsonElement> A(JsonElement o, string n) => o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray() : [];

        var squad = e.TryGetProperty("squad", out var sq) ? sq : default;
        var vote = e.TryGetProperty("vote", out var v) ? v : default;
        var step = e.TryGetProperty("step", out var st) ? st.ValueKind switch
        {
            JsonValueKind.Number => st.GetInt32(),
            JsonValueKind.String when int.TryParse(st.GetString(), CultureInfo.InvariantCulture, out var n) => n,
            _ => 0,
        } : 0;

        return new MyApplication(
            S(e, "id") ?? string.Empty,
            S(squad, "name"),
            S(e, "status") ?? string.Empty,
            S(e, "status_label") ?? S(e, "status") ?? string.Empty,
            step,
            ApiValues.Time(S(e, "submitted_at")),
            ApiValues.Time(S(e, "trial_ends_at")),
            ApiValues.Text(S(e, "decision_message")),
            I(vote, "participation"),
            A(e, "tasks").Select(t => (S(t, "title") ?? string.Empty, t.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True)).ToList(),
            A(e, "timeline").Select(t => new ApplicationTimelineEntry(S(t, "label") ?? string.Empty, ApiValues.Time(S(t, "at")))).ToList(),
            A(e, "messages").Select(m => new ApplicationMessage(S(m, "body") ?? string.Empty, m.TryGetProperty("from_squad", out var f) && f.ValueKind == JsonValueKind.True, S(m, "author"), ApiValues.Time(S(m, "at")))).ToList());
    }
}
