using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace RoG.Core.Content;

/// <summary>
/// Welche Inhalte die App selbst lädt. Bilder nur von den Servern der Plattform (Datenschutz: keine Anfragen an fremde
/// Server); alles andere wird als Link angezeigt und erst auf Klick im Browser geöffnet.
/// </summary>
public sealed record ContentPolicy(IReadOnlyList<string> TrustedImageHosts)
{
    /// <summary>Domains der Plattform (Website, API, CDN <c>i.rogclan.de</c>, Staging, lokal).</summary>
    public static ContentPolicy Platform { get; } = new(["rebelsofgaming.com", "rebelsofgaming.dev", "rogclan.de", "rog.test"]);

    public bool IsTrustedImage(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.Host.EndsWith(".test", StringComparison.Ordinal)))
        {
            return false;
        }

        return TrustedImageHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Zerlegt das (serverseitig bereinigte) HTML der API in ein <see cref="RichDocument"/>. Unbekannte Elemente werden
/// wie Text behandelt, Skripte/Formulare/Stile verworfen, Farben und Schriftgrößen ignoriert (Design der App gilt).
/// </summary>
public sealed partial class HtmlContentParser(ContentPolicy policy, Uri? baseUri = null)
{
    private static readonly HashSet<string> s_skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "template", "noscript", "svg", "button", "input", "select", "textarea", "form", "head", "title", "meta", "link",
    };

    private static readonly HashSet<string> s_containers = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "header", "footer", "main", "aside", "nav", "figure", "figcaption", "center", "dl", "dd", "dt", "address",
    };

    private static readonly HashSet<string> s_blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "header", "footer", "main", "aside", "nav", "figure", "figcaption", "center", "dl", "dd", "dt", "address",
        "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "blockquote", "pre", "hr", "table", "details", "img", "video", "iframe", "audio",
    };

    public RichDocument Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return RichDocument.Empty;
        }

        var document = new HtmlParser().ParseDocument("<!doctype html><html><body>" + html + "</body></html>");
        return new RichDocument(ParseBlocks(document.Body!, TextStyle.None, centered: false));
    }

    private List<ContentBlock> ParseBlocks(INode parent, TextStyle style, bool centered)
    {
        var builder = new BlockBuilder(centered);
        foreach (var node in parent.ChildNodes)
        {
            switch (node)
            {
                case IText text:
                    builder.Add(new TextInline(text.Data, style));
                    break;
                case IElement element:
                    ParseElement(element, style, centered, builder);
                    break;
            }
        }

        builder.Flush();
        return builder.Blocks;
    }

    private void ParseElement(IElement el, TextStyle style, bool centered, BlockBuilder builder)
    {
        var tag = el.LocalName;
        if (s_skip.Contains(tag))
        {
            return;
        }

        switch (tag)
        {
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                builder.Flush();
                builder.AddBlock(new HeadingBlock(tag[1] - '0', Normalize(ParseInlines(el, style))));
                return;
            case "ul" or "ol":
                builder.Flush();
                var items = el.Children.Where(c => c.LocalName == "li").Select(li => (IReadOnlyList<ContentBlock>)ParseBlocks(li, style, false)).ToList();
                if (items.Count > 0)
                {
                    builder.AddBlock(new ListBlock(tag == "ol", items));
                }

                return;
            case "blockquote":
                builder.Flush();
                var cite = el.Children.FirstOrDefault(c => c.LocalName == "cite");
                var citeText = cite is null ? null : Collapse(cite.TextContent).Trim();
                cite?.Remove();
                builder.AddBlock(new QuoteBlock(string.IsNullOrEmpty(citeText) ? null : citeText, ParseBlocks(el, style, false)));
                return;
            case "pre":
                builder.Flush();
                builder.AddBlock(new CodeBlock(el.TextContent.Trim('\n', '\r')));
                return;
            case "hr":
                builder.Flush();
                builder.AddBlock(new RuleBlock());
                return;
            case "details":
                builder.Flush();
                var summary = el.Children.FirstOrDefault(c => c.LocalName == "summary");
                var title = summary is null ? string.Empty : Collapse(summary.TextContent).Trim();
                summary?.Remove();
                builder.AddBlock(new SpoilerBlock(title, ParseBlocks(el, style, false)));
                return;
            case "table":
                builder.Flush();
                if (ParseTable(el, style) is { } table)
                {
                    builder.AddBlock(table);
                }

                return;
            case "img":
                builder.Flush();
                if (Image(el, link: null) is { } image)
                {
                    builder.AddBlock(image);
                }

                return;
            case "video" or "iframe" or "audio":
                builder.Flush();
                if (Resolve(el.GetAttribute("src") ?? el.QuerySelector("source")?.GetAttribute("src")) is { } media && IsWebLink(media))
                {
                    builder.AddBlock(new MediaLinkBlock(tag == "iframe" ? MediaKind.Embed : MediaKind.Video, media, el.GetAttribute("title")));
                }

                return;
            case "br":
                builder.Add(LineBreakInline.Instance);
                return;
            case "a" when el.Children.Length == 1 && el.Children[0].LocalName == "img" && string.IsNullOrWhiteSpace(el.TextContent):
                builder.Flush();
                if (Image(el.Children[0], Link(el)) is { } linked)
                {
                    builder.AddBlock(linked);
                }

                return;
        }

        if (s_containers.Contains(tag))
        {
            builder.Flush();
            var center = centered || tag == "center" || el.ClassList.Contains("bb-center") || el.ClassList.Contains("text-center");
            builder.AddBlocks(ParseBlocks(el, style, center));
            return;
        }

        // Inline-Element, das selbst Blöcke enthält (z. B. <a><div>…</div></a>) → als Container behandeln
        if (el.Descendants<IElement>().Any(d => s_blocks.Contains(d.LocalName)) && tag != "a")
        {
            builder.Flush();
            builder.AddBlocks(ParseBlocks(el, style | StyleOf(tag), centered));
            return;
        }

        builder.AddRange(ParseInline(el, style));
    }

    private List<ContentInline> ParseInlines(INode parent, TextStyle style)
    {
        var result = new List<ContentInline>();
        foreach (var node in parent.ChildNodes)
        {
            switch (node)
            {
                case IText text:
                    result.Add(new TextInline(text.Data, style));
                    break;
                case IElement element when !s_skip.Contains(element.LocalName):
                    result.AddRange(ParseInline(element, style));
                    break;
            }
        }

        return result;
    }

    private List<ContentInline> ParseInline(IElement el, TextStyle style)
    {
        switch (el.LocalName)
        {
            case "br":
                return [LineBreakInline.Instance];
            case "img":
                var alt = el.GetAttribute("alt");
                return string.IsNullOrWhiteSpace(alt) ? [] : [new TextInline(alt, style)];
            case "a":
                var children = ParseInlines(el, style);
                return Link(el) is { } target ? [new LinkInline(target, children)] : children;
        }

        var inner = ParseInlines(el, style | StyleOf(el.LocalName));
        if (s_blocks.Contains(el.LocalName) && inner.Count > 0)
        {
            inner.Add(LineBreakInline.Instance);
        }

        return inner;
    }

    private static TextStyle StyleOf(string tag) => tag switch
    {
        "strong" or "b" => TextStyle.Bold,
        "em" or "i" or "cite" or "var" => TextStyle.Italic,
        "u" or "ins" => TextStyle.Underline,
        "s" or "del" or "strike" => TextStyle.Strikethrough,
        "code" or "kbd" or "samp" or "tt" => TextStyle.Code,
        "small" or "sub" or "sup" => TextStyle.Small,
        _ => TextStyle.None,
    };

    private TableBlock? ParseTable(IElement table, TextStyle style)
    {
        var rows = new List<TableRow>();
        foreach (var tr in table.QuerySelectorAll("tr"))
        {
            var cells = tr.Children.Where(c => c.LocalName is "td" or "th").ToList();
            if (cells.Count == 0)
            {
                continue;
            }

            var header = tr.ParentElement?.LocalName == "thead" || cells.All(c => c.LocalName == "th");
            rows.Add(new TableRow(header, cells.Select(c => (IReadOnlyList<ContentInline>)Normalize(ParseInlines(c, style))).ToList()));
        }

        return rows.Count == 0 ? null : new TableBlock(rows);
    }

    private ContentBlock? Image(IElement img, Uri? link)
    {
        var src = Resolve(img.GetAttribute("src"));
        if (src is null || !IsWebLink(src))
        {
            return null;
        }

        var alt = img.GetAttribute("alt");
        alt = string.IsNullOrWhiteSpace(alt) ? null : alt.Trim();
        return policy.IsTrustedImage(src) ? new ImageBlock(src, alt, link) : new MediaLinkBlock(MediaKind.Image, link ?? src, alt);
    }

    private Uri? Link(IElement a)
    {
        var href = Resolve(a.GetAttribute("href"));
        return href is not null && (IsWebLink(href) || href.Scheme == Uri.UriSchemeMailto) ? href : null;
    }

    private static bool IsWebLink(Uri uri) => uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;

    private Uri? Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) && !absolute.IsFile && !value.StartsWith('/'))
        {
            return absolute;
        }

        return baseUri is not null && Uri.TryCreate(baseUri, value, out var relative) ? relative : null;
    }

    /// <summary>Leerraum wie im Browser zusammenfassen, Ränder kürzen, gleiche Stile verbinden.</summary>
    internal static List<ContentInline> Normalize(List<ContentInline> inlines)
    {
        var result = new List<ContentInline>();
        foreach (var inline in inlines)
        {
            var item = inline switch
            {
                TextInline t when (t.Style & TextStyle.Code) == 0 => t with { Text = Collapse(t.Text) },
                LinkInline l => l with { Children = Normalize(l.Children.ToList()) },
                _ => inline,
            };

            if (item is TextInline { Text.Length: 0 })
            {
                continue;
            }

            if (item is TextInline next && result.Count > 0 && result[^1] is TextInline prev && prev.Style == next.Style)
            {
                result[^1] = prev with { Text = (prev.Text + next.Text).Replace("  ", " ", StringComparison.Ordinal) };
            }
            else
            {
                result.Add(item);
            }
        }

        // Leerraum nach Zeilenumbrüchen und am Anfang/Ende entfernen
        for (var i = 0; i < result.Count; i++)
        {
            if (result[i] is TextInline t && (i == 0 || result[i - 1] is LineBreakInline))
            {
                result[i] = t with { Text = t.Text.TrimStart() };
            }

            if (result[i] is TextInline u && (i == result.Count - 1 || result[i + 1] is LineBreakInline))
            {
                result[i] = u with { Text = u.Text.TrimEnd() };
            }
        }

        result.RemoveAll(i => i is TextInline { Text.Length: 0 });
        while (result.Count > 0 && result[^1] is LineBreakInline)
        {
            result.RemoveAt(result.Count - 1);
        }

        while (result.Count > 0 && result[0] is LineBreakInline)
        {
            result.RemoveAt(0);
        }

        return result;
    }

    private static string Collapse(string text) => Whitespace().Replace(text, " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Sammelt Inline-Inhalt zu Absätzen, bis ein Block-Element kommt.</summary>
    private sealed class BlockBuilder(bool centered)
    {
        private List<ContentInline> _inlines = [];

        public List<ContentBlock> Blocks { get; } = [];

        public void Add(ContentInline inline) => _inlines.Add(inline);

        public void AddRange(IEnumerable<ContentInline> inlines) => _inlines.AddRange(inlines);

        public void AddBlock(ContentBlock block) => Blocks.Add(block);

        public void AddBlocks(IEnumerable<ContentBlock> blocks) => Blocks.AddRange(blocks);

        public void Flush()
        {
            var inlines = Normalize(_inlines);
            _inlines = [];
            if (inlines.Count > 0 && !inlines.All(i => i is LineBreakInline))
            {
                Blocks.Add(new ParagraphBlock(inlines, centered));
            }
        }
    }
}

/// <summary>Kurztext für Vorschauen (Liste, Benachrichtigung) aus HTML.</summary>
public static class HtmlText
{
    public static string ToPlain(string? html, int maxLength = 280)
    {
        var text = new HtmlContentParser(ContentPolicy.Platform).Parse(html).ToPlainText();
        if (text.Length <= maxLength)
        {
            return text;
        }

        var sb = new StringBuilder(text, 0, maxLength - 1, maxLength);
        return sb.Append('…').ToString();
    }
}
