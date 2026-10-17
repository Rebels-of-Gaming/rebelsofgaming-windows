using System.Net.Http.Json;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Features.Account;

namespace RoG.Core.Features.Squads;

/// <summary>Squad-Rolle zur Auswahl (<c>assignable</c>: darf ich grundsätzlich vergeben; den Einzelfall prüft der Server).</summary>
public sealed record SquadRole(string Id, string Name, string Label, string? Color, bool Assignable);

/// <summary>Bearbeitbare Felder eines Squads in der App-Sprache; <c>null</c> = nicht ändern.</summary>
public sealed record SquadEdit(
    string? Tagline = null,
    string? Description = null,
    string? TrainingTimes = null,
    string? ClanRequirements = null,
    string? Recruiting = null,
    string? ClanTag = null,
    string? ClanName = null,
    int? ClanLevel = null);

/// <summary>Was die angemeldete Person an einem Squad darf (Rechte im Scope <c>squad:{id}</c> + Token-Scope).</summary>
public sealed record SquadAdminRights(bool Edit, bool Members, bool Archive)
{
    public bool Any => Edit || Members || Archive;

    public static SquadAdminRights For(UserProfile? me, string squadId)
    {
        if (me is null || !me.HasScope("squads:write"))
        {
            return new(false, false, false);
        }

        var scope = "squad:" + squadId;
        return new(me.Can("squads.manage", scope), me.Can("squad.members", scope), me.Can("squads.manage"));
    }
}

/// <summary>
/// Squad-Verwaltung (Guide „Bilder“ und Changelog „Squads“): Texte und Rekrutierung ändern, Logo/Header aus der
/// Bibliothek setzen (<c>upload_id</c>), Mitglieder hinzufügen/Rolle ändern/entfernen, archivieren.
/// Übersetzbare Texte gehen nur in der App-Sprache (<c>{"de": "…"}</c>) – andere Sprachen bleiben unverändert.
/// </summary>
public sealed class SquadAdminService(HttpClient http)
{
    public static readonly IReadOnlyList<string> RecruitingValues = ["open", "waitlist", "full"];

    /// <summary>Body für <c>PATCH /v1/squads/{squad}</c> – nur geänderte Felder.</summary>
    public static Dictionary<string, object?> BuildPatch(SquadDetail current, SquadEdit edit, string locale)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(edit);
        var body = new Dictionary<string, object?>(StringComparer.Ordinal);
        void Text(string field, string? now, string? wanted)
        {
            if (wanted is not null && !string.Equals((now ?? string.Empty).Trim(), wanted.Trim(), StringComparison.Ordinal))
            {
                body[field] = new Dictionary<string, string> { [locale] = wanted.Trim() };
            }
        }

        Text("tagline", current.Summary.Tagline, edit.Tagline);
        Text("description", current.Description, edit.Description);
        Text("training_times", current.TrainingTimes, edit.TrainingTimes);
        Text("clan_requirements", current.ClanRequirements, edit.ClanRequirements);
        if (edit.Recruiting is { } r && RecruitingValues.Contains(r) && r != ValueOf(current.Summary.Recruiting))
        {
            body["recruiting_status"] = r;
        }

        if (edit.ClanTag is { } tag && tag.Trim() != (current.Summary.ClanTag ?? string.Empty))
        {
            body["clan_tag"] = ApiValues.Text(tag);
        }

        if (edit.ClanName is { } name && name.Trim() != (current.ClanName ?? string.Empty))
        {
            body["clan_name"] = ApiValues.Text(name);
        }

        if (edit.ClanLevel is { } level && level != current.ClanLevel)
        {
            body["clan_level"] = Math.Clamp(level, 0, 9999);
        }

        return body;
    }

    public static string ValueOf(Recruiting recruiting) => recruiting switch
    {
        Recruiting.Waitlist => "waitlist",
        Recruiting.Full => "full",
        Recruiting.Open => "open",
        _ => string.Empty,
    };

    /// <summary>Speichert nur Änderungen; <c>false</c>, wenn es nichts zu senden gab.</summary>
    public async Task<bool> UpdateAsync(SquadDetail current, SquadEdit edit, string locale, CancellationToken cancellationToken = default)
    {
        var body = BuildPatch(current, edit, locale);
        if (body.Count == 0)
        {
            return false;
        }

        using var response = await http.PatchAsJsonAsync(Squad(current.Summary.Slug), body, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<IReadOnlyList<SquadRole>> RolesAsync(string squad, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(Squad(squad, "roles"), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseRoles(doc.RootElement);
    }

    internal static IReadOnlyList<SquadRole> ParseRoles(JsonElement root)
    {
        static string? S(JsonElement o, string n) => o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return data.EnumerateArray()
            .Select(r => new SquadRole(
                S(r, "id") ?? string.Empty,
                S(r, "name") ?? string.Empty,
                S(r, "label") ?? S(r, "name") ?? string.Empty,
                S(r, "color"),
                r.TryGetProperty("assignable", out var a) && a.ValueKind == JsonValueKind.True))
            .Where(r => r.Id.Length > 0)
            .ToList();
    }

    /// <summary>Mitglied hinzufügen oder Squad-Rolle/Ersatz ändern.</summary>
    public async Task PutMemberAsync(string squad, string member, string roleId, bool substitute, CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync(
            Squad(squad, "members/" + Uri.EscapeDataString(member)),
            new Dictionary<string, object> { ["role_id"] = roleId, ["is_substitute"] = substitute },
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveMemberAsync(string squad, string member, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync(Squad(squad, "members/" + Uri.EscapeDataString(member)), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetArchivedAsync(string squad, bool archived, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(Squad(squad, "archive"), new Dictionary<string, bool> { ["archived"] = archived }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Logo bzw. Header aus einem öffentlichen Upload setzen (Verarbeitung im Hintergrund, <c>202</c>).</summary>
    public async Task SetImageAsync(string squad, SquadImage kind, string uploadId, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(Squad(squad, Path(kind)), new Dictionary<string, string> { ["upload_id"] = uploadId }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveImageAsync(string squad, SquadImage kind, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync(Squad(squad, Path(kind)), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static string Path(SquadImage kind) => kind == SquadImage.Logo ? "logo" : "header";

    private static Uri Squad(string squad, string? sub = null) =>
        new($"squads/{Uri.EscapeDataString(squad)}{(sub is null ? string.Empty : "/" + sub)}", UriKind.Relative);
}

public enum SquadImage
{
    Logo,
    Header,
}
