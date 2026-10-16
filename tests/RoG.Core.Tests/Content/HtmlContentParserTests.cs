using RoG.Core.Content;

namespace RoG.Core.Tests.Content;

public class HtmlContentParserTests
{
    private static readonly HtmlContentParser s_parser = new(ContentPolicy.Platform, new Uri("https://rebelsofgaming.com/"));

    private static RichDocument Parse(string html) => s_parser.Parse(html);

    [Fact]
    public void Paragraphs_with_styles_and_collapsed_whitespace()
    {
        var doc = Parse("<p>Hallo   <strong>Rebels</strong>,\n  <em>willkommen</em>!</p><p></p>");

        var p = Assert.IsType<ParagraphBlock>(Assert.Single(doc.Blocks));
        Assert.Equal(
            [new TextInline("Hallo "), new TextInline("Rebels", TextStyle.Bold), new TextInline(", "), new TextInline("willkommen", TextStyle.Italic), new TextInline("!")],
            p.Inlines.Where(i => i is TextInline).Cast<TextInline>().Select(t => t).ToList(),
            new TextComparer());
        Assert.Equal("Hallo Rebels, willkommen!", doc.ToPlainText());
    }

    [Fact]
    public void Loose_text_and_line_breaks_become_one_paragraph()
    {
        var doc = Parse("Zeile 1<br>Zeile 2<br><br>");

        var p = Assert.IsType<ParagraphBlock>(Assert.Single(doc.Blocks));
        Assert.Equal(3, p.Inlines.Count);
        Assert.IsType<LineBreakInline>(p.Inlines[1]);
    }

    [Fact]
    public void Headings_lists_quotes_code_and_rules()
    {
        var doc = Parse("""
            <h2>Regeln</h2>
            <ol><li>Fair <b>spielen</b></li><li>Respekt</li></ol>
            <blockquote><cite>Wahke schrieb:</cite><p>Zitat</p></blockquote>
            <pre class="bb-code"><code>connect 1.2.3.4:27015
            </code></pre>
            <hr>
            """);

        Assert.Collection(doc.Blocks,
            b => Assert.Equal(2, Assert.IsType<HeadingBlock>(b).Level),
            b =>
            {
                var list = Assert.IsType<ListBlock>(b);
                Assert.True(list.Ordered);
                Assert.Equal(2, list.Items.Count);
            },
            b =>
            {
                var quote = Assert.IsType<QuoteBlock>(b);
                Assert.Equal("Wahke schrieb:", quote.Cite);
                Assert.Equal("Zitat", RichDocumentText(quote.Blocks));
            },
            b => Assert.Equal("connect 1.2.3.4:27015", Assert.IsType<CodeBlock>(b).Code.Trim()),
            b => Assert.IsType<RuleBlock>(b));
    }

    [Fact]
    public void Spoiler_from_bbcode_and_wiki()
    {
        var doc = Parse("""<details class="bb-spoiler"><summary>Lösung</summary><div>Geheim</div></details>""");

        var spoiler = Assert.IsType<SpoilerBlock>(Assert.Single(doc.Blocks));
        Assert.Equal("Lösung", spoiler.Title);
        Assert.Equal("Geheim", RichDocumentText(spoiler.Blocks));
    }

    [Fact]
    public void Platform_images_are_embedded_foreign_images_become_links()
    {
        var doc = Parse("""
            <p><a href="/media/big.png"><img src="https://i.rogclan.de/abc.webp" alt="Screenshot"></a></p>
            <img src="https://evil.example/tracker.gif" alt="Pixel">
            <img src="http://i.rogclan.de/insecure.png">
            """);

        var image = Assert.IsType<ImageBlock>(doc.Blocks[0]);
        Assert.Equal("https://i.rogclan.de/abc.webp", image.Source.ToString());
        Assert.Equal("https://rebelsofgaming.com/media/big.png", image.Link!.ToString());
        Assert.Equal("Screenshot", image.Alt);

        var foreign = Assert.IsType<MediaLinkBlock>(doc.Blocks[1]);
        Assert.Equal(MediaKind.Image, foreign.Kind);
        Assert.Equal("evil.example", foreign.Target.Host);

        // http-Bild auch von der Plattform nicht laden (nur https)
        Assert.IsType<MediaLinkBlock>(doc.Blocks[2]);
    }

    [Fact]
    public void Links_only_for_safe_schemes_relative_links_resolved()
    {
        var doc = Parse("""<p><a href="javascript:alert(1)">böse</a> <a href="/de/news/x">News</a> <a href="mailto:info@example.test">Mail</a> <a href="ms-settings:privacy">Einst.</a></p>""");

        var p = Assert.IsType<ParagraphBlock>(Assert.Single(doc.Blocks));
        var links = p.Inlines.OfType<LinkInline>().Select(l => l.Target.ToString()).ToList();
        Assert.Equal(["https://rebelsofgaming.com/de/news/x", "mailto:info@example.test"], links);
        Assert.Contains("böse", doc.ToPlainText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Scripts_styles_and_forms_are_dropped()
    {
        var doc = Parse("<p>Text</p><script>alert(1)</script><style>p{}</style><form><input value=x></form>");

        Assert.Equal("Text", doc.ToPlainText());
    }

    [Fact]
    public void Videos_become_media_links()
    {
        var doc = Parse("""<video src="https://i.rogclan.de/clip.mp4" controls class="bb-video"></video>""");

        var media = Assert.IsType<MediaLinkBlock>(Assert.Single(doc.Blocks));
        Assert.Equal(MediaKind.Video, media.Kind);
    }

    [Fact]
    public void Tables_with_header_row()
    {
        var doc = Parse("<table><thead><tr><th>Map</th><th>Zeit</th></tr></thead><tbody><tr><td>supply</td><td>20 min</td></tr></tbody></table>");

        var table = Assert.IsType<TableBlock>(Assert.Single(doc.Blocks));
        Assert.True(table.Rows[0].IsHeader);
        Assert.False(table.Rows[1].IsHeader);
        Assert.Equal("supply", ((TextInline)table.Rows[1].Cells[0][0]).Text);
    }

    [Fact]
    public void Centered_bbcode_block()
    {
        var doc = Parse("""<div class="bb-center">Mitte</div>""");

        Assert.True(Assert.IsType<ParagraphBlock>(Assert.Single(doc.Blocks)).Centered);
    }

    [Fact]
    public void Empty_or_null_html_is_an_empty_document()
    {
        Assert.Empty(Parse("").Blocks);
        Assert.Empty(s_parser.Parse(null).Blocks);
        Assert.Empty(Parse("   <p>  </p> <br> ").Blocks);
    }

    [Fact]
    public void Plain_text_preview_is_capped()
    {
        Assert.Equal("Hallo Welt", HtmlText.ToPlain("<p>Hallo <b>Welt</b></p>"));
        Assert.Equal("abcd…", HtmlText.ToPlain("<p>abcdefgh</p>", 5));
    }

    private static string RichDocumentText(IReadOnlyList<ContentBlock> blocks) => new RichDocument(blocks).ToPlainText();

    private sealed class TextComparer : IEqualityComparer<TextInline>
    {
        public bool Equals(TextInline? x, TextInline? y) => x?.Text == y?.Text && x?.Style == y?.Style;

        public int GetHashCode(TextInline obj) => obj.Text.GetHashCode(StringComparison.Ordinal);
    }
}
