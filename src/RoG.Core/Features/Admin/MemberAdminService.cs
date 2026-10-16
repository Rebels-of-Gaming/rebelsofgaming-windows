using RoG.Core.Api;
using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Admin;

public sealed record AdminMemberRow(string Id, string Slug, string Name, string? Rank, string? Status, IReadOnlyList<string> Squads, DateTimeOffset? LastSeenAt);

public sealed record RoleAssignment(string AssignmentId, string RoleLabel, string? ScopeType, string? ScopeId, DateTimeOffset? ExpiresAt);

public sealed record AdminMemberDetail(
    string Id,
    string Slug,
    string Name,
    string? Rank,
    string AccountStatus,
    string? Email,
    bool? TwoFactor,
    DateTimeOffset? JoinedAt,
    DateTimeOffset? LastSeenAt,
    IReadOnlyList<(string Id, string Name)> Squads,
    IReadOnlyList<RoleAssignment> Roles);

public sealed record RoleOption(string Id, string Label, string Type);

/// <summary>
/// Mitglieder-Verwaltung (Guide 36 „Verwaltung“, Guide 39): Liste mit Filtern, Konto mit Rollen, Rolle vergeben
/// (global oder im Squad, befristet) und entziehen. Sichtbarkeit und Rechte (<c>members.view</c>, <c>roles.assign</c>) prüft die API.
/// </summary>
public sealed class MemberAdminService(IRoGApiClient client)
{
    public async Task<Page<AdminMemberRow>> ListAsync(string? search, string? status, string? cursor, CancellationToken cancellationToken = default)
    {
        var r = await client.V1_admin_members_indexAsync(cursor, 50, ApiValues.Text(search), null, null, ApiValues.Text(status), include: "squads", cancellationToken: cancellationToken).ConfigureAwait(false);
        return new Page<AdminMemberRow>(
            r.Data.Select(m => new AdminMemberRow(m.Id, m.Slug, m.Name, m.Rank?.Label, ApiValues.Text(m.Status), m.Squads?.Select(s => s.Name).ToList() ?? [], ApiValues.Time(m.Last_seen_at))).ToList(),
            r.Meta?.Next_cursor);
    }

    public async Task<AdminMemberDetail> DetailAsync(string member, CancellationToken cancellationToken = default)
    {
        var m = (await client.V1_admin_members_showAsync(member, cancellationToken).ConfigureAwait(false)).Data;
        return new AdminMemberDetail(
            m.Id,
            m.Slug,
            m.Name,
            m.Rank?.Label,
            m.Account?.Status ?? "active",
            ApiValues.Text(m.Account?.Email),
            m.Account?.Two_factor,
            ApiValues.Time(m.Joined_at),
            ApiValues.Time(m.Last_seen_at),
            m.Squads?.Select(s => (s.Id, s.Name)).ToList() ?? [],
            m.Roles?.Where(r => r.Id is not null)
                .Select(r => new RoleAssignment(r.Id!, r.Role.Label, ApiValues.Text(r.Scope_type), ApiValues.Text(r.Scope_id), ApiValues.Time(r.Expires_at)))
                .ToList() ?? []);
    }

    public async Task<IReadOnlyList<RoleOption>> RolesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<RoleOption>();
        string? cursor = null;
        do
        {
            var r = await client.V1_roles_indexAsync(cursor, 100, cancellationToken: cancellationToken).ConfigureAwait(false);
            result.AddRange(r.Data.OrderBy(x => x.Sort).Select(x => new RoleOption(x.Id, x.Label, x.Type)));
            cursor = r.Meta?.Next_cursor;
        }
        while (cursor is not null && result.Count < 500);

        return result;
    }

    /// <summary>Rolle vergeben: ohne Squad global, mit <paramref name="squadId"/> nur im Squad; optional befristet.</summary>
    public async Task AssignAsync(string member, string roleId, string? squadId, DateTimeOffset? expiresAt, CancellationToken cancellationToken = default) =>
        await client.V1_members_roles_storeAsync(member, new AssignRoleRequest
        {
            Role_id = roleId,
            Scope_type = squadId is null ? null : AssignRoleRequestScope_type.Squad,
            Scope_id = squadId,
            Expires_at = expiresAt,
        }, cancellationToken).ConfigureAwait(false);

    public async Task RevokeAsync(string member, string assignmentId, CancellationToken cancellationToken = default) =>
        await client.V1_members_roles_destroyAsync(member, assignmentId, cancellationToken).ConfigureAwait(false);
}
