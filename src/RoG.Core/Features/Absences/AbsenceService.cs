using System.Globalization;
using RoG.Core.Api;
using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Absences;

public sealed record Absence(string Id, DateOnly StartsOn, DateOnly EndsOn, string State, string? Reason, string? MemberName)
{
    public bool IsCurrent => State == "active";
}

/// <summary>Abwesenheit (Guide 16): eigene melden, ansehen, löschen; Clan-Übersicht (Grund nur mit Recht).</summary>
public sealed class AbsenceService(IRoGApiClient client)
{
    public async Task<IReadOnlyList<Absence>> MineAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.V1_me_absences_indexAsync(cancellationToken).ConfigureAwait(false);
        return response.Data.Select(a => Map(a.Id, a.Starts_on, a.Ends_on, a.State, a.Reason, null)).OfType<Absence>().OrderBy(a => a.StartsOn).ToList();
    }

    public async Task<IReadOnlyList<Absence>> AllAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.V1_absences_indexAsync(cancellationToken).ConfigureAwait(false);
        return response.Data.Select(a => Map(a.Id, a.Starts_on, a.Ends_on, a.State, a.Reason, a.Member?.Name)).OfType<Absence>().OrderBy(a => a.StartsOn).ToList();
    }

    /// <summary>Melden; Regeln der API: Ende nicht in der Vergangenheit, höchstens ein Jahr, keine Überschneidung (422 sonst).</summary>
    public async Task CreateAsync(DateOnly startsOn, DateOnly endsOn, string? reason, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(endsOn, startsOn);

        await client.V1_me_absences_storeAsync(new Body
        {
            Starts_on = startsOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Ends_on = endsOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Reason = ApiValues.Text(reason),
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        await client.V1_me_absences_destroyAsync(id, cancellationToken).ConfigureAwait(false);

    private static Absence? Map(string id, string starts, string ends, string state, string? reason, string? member) =>
        DateOnly.TryParseExact(starts, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var s)
        && DateOnly.TryParseExact(ends, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var e)
            ? new Absence(id, s, e, state, ApiValues.Text(reason), member)
            : null;
}
