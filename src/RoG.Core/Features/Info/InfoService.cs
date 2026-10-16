using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Core.Content;

namespace RoG.Core.Features.Info;

public enum InfoKind
{
    Rules,
    Page,
    Legal,
}

/// <summary>Eintrag in der Info-Liste (Regelwerk, CMS-Seite, Rechtstext).</summary>
public sealed record InfoEntry(InfoKind Kind, string Key, string Title, string? Subtitle, bool NeedsAcceptance);

/// <summary>Abschnitt eines Dokuments (Regel mit Nummer, sonst ein einziger Abschnitt ohne Titel).</summary>
public sealed record InfoSection(string? Title, bool Important, RichDocument Content);

public sealed record InfoDocument(
    InfoEntry Entry,
    string Title,
    string? Intro,
    string? Meta,
    IReadOnlyList<InfoSection> Sections,
    Uri? WebUrl,
    bool CanAccept);

/// <summary>Regeln (inkl. Bestätigung), CMS-Seiten (Über uns, Partner …) und Rechtstexte – alles nativ gerendert.</summary>
public sealed class InfoService(IRoGApiClient client, Uri webBaseUrl)
{
    private readonly HtmlContentParser _html = new(ContentPolicy.Platform, webBaseUrl);

    /// <summary>Alle Einträge; fällt ein Bereich aus (z. B. Regeln nur für Mitglieder), fehlen nur dessen Einträge.</summary>
    public async Task<IReadOnlyList<InfoEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rulesTask = Try(() => client.V1_rules_indexAsync(cancellationToken));
        var pagesTask = Try(() => client.V1_pages_indexAsync(cancellationToken));
        var legalTask = Try(() => client.V1_legal_indexAsync(cancellationToken));
        var rules = await rulesTask.ConfigureAwait(false);
        var pages = await pagesTask.ConfigureAwait(false);
        var legal = await legalTask.ConfigureAwait(false);

        var result = new List<InfoEntry>();
        if (rules.Value is { } r)
        {
            result.AddRange(r.Data.Select(x => new InfoEntry(InfoKind.Rules, x.Slug, x.Title, ApiValues.Text(x.Intro), x.Requires_acceptance && x.Accepted == false)));
        }

        if (pages.Value is { } p)
        {
            result.AddRange(p.Data.OrderBy(x => x.Sort).Select(x => new InfoEntry(InfoKind.Page, x.Slug, x.Title, null, false)));
        }

        if (legal.Value is { } l)
        {
            result.AddRange(l.Data.Select(x => new InfoEntry(InfoKind.Legal, x.Type, x.Title, null, x.Requires_acceptance && x.Acceptable)));
        }

        if (result.Count == 0 && (rules.Error ?? pages.Error ?? legal.Error) is { } error)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
        }

        return result;
    }

    public async Task<InfoDocument> GetAsync(InfoEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        switch (entry.Kind)
        {
            case InfoKind.Rules:
                var rules = (await client.V1_rules_showAsync(entry.Key, cancellationToken).ConfigureAwait(false)).Data;
                var sections = rules.Sections
                    .OrderBy(s => s.Number)
                    .Select(s => new InfoSection($"§ {s.Number} {s.Title}", s.Important, _html.Parse(s.Html)))
                    .ToList();
                return new InfoDocument(entry, rules.Title, ApiValues.Text(rules.Intro), $"v{rules.Version}", sections,
                    null, rules.Requires_acceptance && rules.Accepted == false);

            case InfoKind.Page:
                var page = (await client.V1_pages_showAsync(entry.Key, cancellationToken).ConfigureAwait(false)).Data;
                return new InfoDocument(entry, page.Title, null, null, [new InfoSection(null, false, _html.Parse(page.Body_html))],
                    ApiValues.WebUri(page.Url), false);

            default:
                var legal = (await client.V1_legal_showAsync(entry.Key, cancellationToken).ConfigureAwait(false)).Data;
                var meta = legal.Version is { } v ? $"v{v}" + (ApiValues.Time(legal.Published_at) is { } at ? " · " + at.ToLocalTime().ToString("d", System.Globalization.CultureInfo.CurrentCulture) : string.Empty) : null;
                return new InfoDocument(entry, legal.Title, ApiValues.Text(legal.Changes), meta, [new InfoSection(null, false, _html.Parse(legal.Html))],
                    ApiValues.WebUri(legal.Url), false);
        }
    }

    /// <summary>Regelwerk bestätigen (Pflicht bei <c>requires_acceptance</c>).</summary>
    public async Task AcceptRulesAsync(string slug, CancellationToken cancellationToken = default) =>
        await client.V1_rules_acceptAsync(slug, cancellationToken).ConfigureAwait(false);

    private static async Task<(T? Value, Exception? Error)> Try<T>(Func<Task<T>> call)
        where T : class
    {
        try
        {
            return (await call().ConfigureAwait(false), null);
        }
        catch (Exception ex) when (ex is ApiProblemException or GeneratedApiException)
        {
            return (null, ex);
        }
    }
}
