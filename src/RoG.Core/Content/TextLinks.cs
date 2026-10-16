using System.Text.RegularExpressions;

namespace RoG.Core.Content;

/// <summary>Teil eines Fließtexts: reiner Text oder erkannter Link (nur http/https).</summary>
public sealed record TextSegment(string Text, Uri? Link);

/// <summary>Erkennt http(s)-Links in Nutzertexten (Chat, Kommentare), damit die App sie anklickbar zeigt.</summary>
public static partial class TextLinks
{
    public static IReadOnlyList<TextSegment> Split(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var result = new List<TextSegment>();
        var last = 0;
        foreach (Match m in UrlPattern().Matches(text))
        {
            // Satzzeichen am Ende gehören meist nicht zum Link („siehe https://x.de.“)
            var value = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', '\'', '"');
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                continue;
            }

            if (m.Index > last)
            {
                result.Add(new TextSegment(text[last..m.Index], null));
            }

            result.Add(new TextSegment(value, uri));
            last = m.Index + value.Length;
        }

        if (last < text.Length)
        {
            result.Add(new TextSegment(text[last..], null));
        }

        return result;
    }

    [GeneratedRegex(@"https?://[^\s<>""]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
}
