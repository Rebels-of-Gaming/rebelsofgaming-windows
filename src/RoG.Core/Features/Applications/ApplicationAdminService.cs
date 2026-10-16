using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Applications;

public sealed record ApplicationSummary(string Id, string Applicant, string Squad, string Status, DateTimeOffset? SubmittedAt, int? Participation, string? VoteResult);

public sealed record ApplicationTask(string Id, string Title, bool Done);

public sealed record ApplicationNote(string Author, string Body, bool Internal, bool FromApplicant, DateTimeOffset? At);

public sealed record ApplicationVoteState(int Eligible, int Cast, int Yes, int No, int Abstain, int Participation, int QuorumPercent, bool QuorumMet, string Result, IReadOnlyList<string> Missing);

public sealed record ApplicationDetail(
    string Id,
    string Applicant,
    string? ApplicantSlug,
    string Squad,
    string Status,
    int Age,
    string? Referral,
    IReadOnlyList<(string Question, string Answer)> Answers,
    IReadOnlyList<(string Check, string Status, string Message)> BanCheck,
    IReadOnlyList<ApplicationTask> Tasks,
    IReadOnlyDictionary<string, int> ActivityMinutes,
    IReadOnlyList<ApplicationTimelineEntry> Timeline,
    IReadOnlyList<ApplicationNote> Messages,
    ApplicationVoteState? Vote,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset? VoteEndsAt,
    bool CanVote,
    string? MyVote);

/// <summary>
/// Bewerbungen für Squad-Leitung (Guide 07 „Leadership“): Liste, Detail, Entscheidungen, Erinnern, Nachrichten/Notizen,
/// Trial-Aufgaben. Liste und Detail werden selbst gelesen, weil Antworten Text sein können (Lücke W9).
/// </summary>
public sealed class ApplicationAdminService(IRoGApiClient client, HttpClient http)
{
    /// <summary>Entscheidungen wie die Website: freigeben, Trial, Vote starten, aufnehmen, verlängern, ablehnen, abbrechen.</summary>
    public static readonly IReadOnlyList<string> Decisions = ["release", "trial", "vote", "accept", "extend", "reject", "cancel"];

    public async Task<Page<ApplicationSummary>> ListAsync(string? status, string? search, string? cursor, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { "per_page=30" };
        if (!string.IsNullOrEmpty(status))
        {
            query.Add("filter%5Bstatus%5D=" + Uri.EscapeDataString(status));
        }

        if (ApiValues.Text(search) is { } s)
        {
            query.Add("filter%5Bsearch%5D=" + Uri.EscapeDataString(s));
        }

        if (cursor is not null)
        {
            query.Add("cursor=" + Uri.EscapeDataString(cursor));
        }

        using var doc = await GetJsonAsync("applications?" + string.Join('&', query), cancellationToken).ConfigureAwait(false);
        return ParseList(doc.RootElement);
    }

    public async Task<ApplicationDetail> DetailAsync(string id, CancellationToken cancellationToken = default)
    {
        using var doc = await GetJsonAsync("applications/" + Uri.EscapeDataString(id), cancellationToken).ConfigureAwait(false);
        return ParseDetail(doc.RootElement.GetProperty("data"));
    }

    public async Task DecideAsync(string id, string decision, string? message, int? days, CancellationToken cancellationToken = default)
    {
        if (!Decisions.Contains(decision))
        {
            throw new ArgumentOutOfRangeException(nameof(decision));
        }

        await client.V1_applications_decideAsync(id, decision, new Body10 { Message = ApiValues.Text(message), Days = decision == "extend" ? days : null }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemindAsync(string id, CancellationToken cancellationToken = default) =>
        await client.V1_applications_remindAsync(id, cancellationToken).ConfigureAwait(false);

    public async Task MessageAsync(string id, string body, bool isInternal, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        await client.V1_applications_messageAsync(id, new Body11 { Body = body.Trim(), Internal = isInternal }, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddTaskAsync(string id, string title, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        await client.V1_applications_tasks_storeAsync(id, new Body12 { Title = title.Trim() }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetTaskDoneAsync(string id, string taskId, bool done, CancellationToken cancellationToken = default) =>
        await client.V1_applications_tasks_updateAsync(id, taskId, new Body13 { Done = done }, cancellationToken).ConfigureAwait(false);

    public async Task DeleteTaskAsync(string id, string taskId, CancellationToken cancellationToken = default) =>
        await client.V1_applications_tasks_destroyAsync(id, taskId, cancellationToken).ConfigureAwait(false);

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string? S(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) ? p.ValueKind switch
    {
        JsonValueKind.String => p.GetString(),
        JsonValueKind.Number => p.GetRawText(),
        JsonValueKind.True => "yes",
        JsonValueKind.False => "no",
        _ => null,
    } : null;

    private static int I(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;

    private static bool B(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.True;

    private static JsonElement O(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) ? p : default;

    private static IEnumerable<JsonElement> A(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray() : [];

    internal static Page<ApplicationSummary> ParseList(JsonElement root)
    {
        var items = A(root, "data").Select(a =>
        {
            var vote = O(a, "vote");
            return new ApplicationSummary(
                S(a, "id") ?? string.Empty,
                S(O(a, "applicant"), "name") ?? "—",
                S(O(a, "squad"), "name") ?? string.Empty,
                S(a, "status") ?? string.Empty,
                ApiValues.Time(S(a, "submitted_at")),
                vote.ValueKind == JsonValueKind.Object ? I(vote, "participation") : null,
                vote.ValueKind == JsonValueKind.Object ? S(vote, "result") : null);
        }).Where(a => a.Id.Length > 0).ToList();
        return new Page<ApplicationSummary>(items, S(O(root, "meta"), "next_cursor"));
    }

    internal static ApplicationDetail ParseDetail(JsonElement d)
    {
        // Aktivität: je Tag "minutes" als Objekt {quelle: minuten} – aufsummiert je Quelle
        var activity = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var day in A(d, "activity"))
        {
            if (O(day, "minutes") is { ValueKind: JsonValueKind.Object } minutes)
            {
                foreach (var source in minutes.EnumerateObject().Where(s => s.Value.ValueKind == JsonValueKind.Number))
                {
                    activity[source.Name] = activity.GetValueOrDefault(source.Name) + source.Value.GetInt32();
                }
            }
        }

        var v = O(d, "vote");
        return new ApplicationDetail(
            S(d, "id") ?? string.Empty,
            S(O(d, "applicant"), "name") ?? "—",
            S(O(d, "applicant"), "slug"),
            S(O(d, "squad"), "name") ?? string.Empty,
            S(d, "status") ?? string.Empty,
            I(d, "age"),
            ApiValues.Text(S(d, "referral")),
            A(d, "answers").Select(a => (S(a, "label") ?? string.Empty, S(a, "answer") ?? "—")).ToList(),
            A(d, "ban_check").Select(b => (S(b, "key") ?? string.Empty, S(b, "status") ?? string.Empty, S(b, "message") ?? string.Empty)).ToList(),
            A(d, "tasks").Select(t => new ApplicationTask(S(t, "id") ?? string.Empty, S(t, "title") ?? string.Empty, B(t, "done"))).ToList(),
            activity,
            A(d, "timeline").Select(t => new ApplicationTimelineEntry((B(t, "public") ? string.Empty : "🔒 ") + (S(t, "label") ?? string.Empty), ApiValues.Time(S(t, "at")))).ToList(),
            A(d, "messages").Select(m => new ApplicationNote(S(m, "author") ?? "—", S(m, "body") ?? string.Empty, B(m, "internal"), B(m, "from_applicant"), ApiValues.Time(S(m, "at")))).ToList(),
            v.ValueKind == JsonValueKind.Object
                ? new ApplicationVoteState(I(v, "eligible"), I(v, "cast"), I(v, "yes"), I(v, "no"), I(v, "abstain"), I(v, "participation"), I(v, "quorum_percent"), B(v, "quorum_met"),
                    S(v, "result") ?? "pending",
                    A(v, "missing").Select(m => m.ValueKind == JsonValueKind.String ? m.GetString() ?? string.Empty : S(m, "name") ?? string.Empty).Where(n => n.Length > 0).ToList())
                : null,
            ApiValues.Time(S(d, "trial_ends_at")),
            ApiValues.Time(S(d, "vote_ends_at")),
            B(d, "can_vote"),
            S(d, "my_vote"));
    }
}
