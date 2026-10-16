namespace RoG.Core.Content;

/// <summary>
/// Plattformneutrales Dokument aus dem HTML der API (News, Wiki, Seiten, Regeln, Forum-BBCode). Die App zeichnet es mit
/// eigenen Steuerelementen – keine WebView, kein Skript, keine fremden Inhalte.
/// </summary>
public sealed record RichDocument(IReadOnlyList<ContentBlock> Blocks)
{
    public static RichDocument Empty { get; } = new([]);

    /// <summary>Reiner Text (Vorschau, Sprachausgabe, Suche).</summary>
    public string ToPlainText() => string.Join("\n\n", Blocks.Select(PlainText.Of).Where(t => t.Length > 0));
}

public abstract record ContentBlock;

public sealed record ParagraphBlock(IReadOnlyList<ContentInline> Inlines, bool Centered = false) : ContentBlock;

public sealed record HeadingBlock(int Level, IReadOnlyList<ContentInline> Inlines) : ContentBlock;

public sealed record ListBlock(bool Ordered, IReadOnlyList<IReadOnlyList<ContentBlock>> Items) : ContentBlock;

public sealed record QuoteBlock(string? Cite, IReadOnlyList<ContentBlock> Blocks) : ContentBlock;

public sealed record CodeBlock(string Code) : ContentBlock;

/// <summary>Bild von den Servern der Plattform; <see cref="Link"/> = umschließender Link (z. B. Großansicht).</summary>
public sealed record ImageBlock(Uri Source, string? Alt, Uri? Link) : ContentBlock;

/// <summary>Medium, das die App nicht einbettet (Video, Bild eines fremden Servers) – als Link angezeigt.</summary>
public sealed record MediaLinkBlock(MediaKind Kind, Uri Target, string? Label) : ContentBlock;

public sealed record RuleBlock : ContentBlock;

/// <summary>Aufklappbarer Bereich (<c>&lt;details&gt;</c>, BBCode <c>[spoiler]</c>).</summary>
public sealed record SpoilerBlock(string Title, IReadOnlyList<ContentBlock> Blocks) : ContentBlock;

public sealed record TableBlock(IReadOnlyList<TableRow> Rows) : ContentBlock;

public sealed record TableRow(bool IsHeader, IReadOnlyList<IReadOnlyList<ContentInline>> Cells);

public enum MediaKind
{
    Image,
    Video,
    Embed,
}

public abstract record ContentInline;

[Flags]
public enum TextStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strikethrough = 8,
    Code = 16,
    Small = 32,
}

public sealed record TextInline(string Text, TextStyle Style = TextStyle.None) : ContentInline;

/// <summary>Link (nur http, https, mailto). Interne Links der Plattform kann die App später selbst öffnen.</summary>
public sealed record LinkInline(Uri Target, IReadOnlyList<ContentInline> Children) : ContentInline;

public sealed record LineBreakInline : ContentInline
{
    public static LineBreakInline Instance { get; } = new();
}

internal static class PlainText
{
    public static string Of(ContentBlock block) => block switch
    {
        ParagraphBlock p => Of(p.Inlines),
        HeadingBlock h => Of(h.Inlines),
        ListBlock l => string.Join("\n", l.Items.Select(i => "• " + string.Join(" ", i.Select(Of)))),
        QuoteBlock q => string.Join("\n", q.Blocks.Select(Of)),
        CodeBlock c => c.Code,
        ImageBlock i => i.Alt ?? string.Empty,
        MediaLinkBlock m => m.Label ?? m.Target.ToString(),
        SpoilerBlock s => s.Title,
        TableBlock t => string.Join("\n", t.Rows.Select(r => string.Join(" | ", r.Cells.Select(Of)))),
        _ => string.Empty,
    };

    public static string Of(IEnumerable<ContentInline> inlines) => string.Concat(inlines.Select(i => i switch
    {
        TextInline t => t.Text,
        LinkInline l => Of(l.Children),
        LineBreakInline => "\n",
        _ => string.Empty,
    })).Trim();
}
