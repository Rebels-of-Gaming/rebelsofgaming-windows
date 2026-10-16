namespace RoG.Core.Api;

/// <summary>Eine Seite einer Cursor-Pagination (<c>meta.next_cursor</c>); <see cref="NextCursor"/> <c>null</c> = Ende.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor)
{
    public bool HasMore => !string.IsNullOrEmpty(NextCursor);
}

/// <summary>Hilfen für Werte der API.</summary>
public static class ApiValues
{
    /// <summary>ISO-8601-Zeitpunkt der API → <see cref="DateTimeOffset"/>; ungültig/leer → <c>null</c>.</summary>
    public static DateTimeOffset? Time(string? value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var t) ? t : null;

    /// <summary>Absolute http(s)-Adresse oder <c>null</c> (nie andere Schemata an die Oberfläche geben).</summary>
    public static Uri? WebUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri : null;

    /// <summary>Leerer Text → <c>null</c>.</summary>
    public static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Passende Bildgröße aus den Varianten der API (Breite → URL): die kleinste, die mindestens <paramref name="width"/>
    /// breit ist, sonst die größte; ohne Varianten das Original.
    /// </summary>
    public static Uri? Image(string? original, IEnumerable<(int Width, string Url)>? variants, int width)
    {
        var list = variants?.Where(v => WebUri(v.Url) is not null).OrderBy(v => v.Width).ToList() ?? [];
        var pick = list.FirstOrDefault(v => v.Width >= width);
        if (pick.Url is null && list.Count > 0)
        {
            pick = list[^1];
        }

        return WebUri(pick.Url ?? original);
    }
}
