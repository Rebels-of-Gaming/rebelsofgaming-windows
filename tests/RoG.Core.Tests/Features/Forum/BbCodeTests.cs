using RoG.Core.Content;

namespace RoG.Core.Tests.Features.Forum;

public class BbCodeTests
{
    [Fact]
    public void Wraps_the_selection()
    {
        var edit = BbCode.Wrap("Hallo Welt", 6, 4, "b");
        Assert.Equal("Hallo [b]Welt[/b]", edit.Text);
        Assert.Equal((9, 4), (edit.SelectionStart, edit.SelectionLength));
    }

    [Fact]
    public void Without_selection_the_cursor_lands_between_the_tags()
    {
        var edit = BbCode.Wrap("Text ", 5, 0, "spoiler", "Lösung");
        Assert.Equal("Text [spoiler=Lösung][/spoiler]", edit.Text);
        Assert.Equal((21, 0), (edit.SelectionStart, edit.SelectionLength));
    }

    [Fact]
    public void Out_of_range_selection_is_clamped()
    {
        var edit = BbCode.Wrap("abc", 10, 5, "i");
        Assert.Equal("abc[i][/i]", edit.Text);
    }

    [Fact]
    public void Quote_strips_brackets_from_the_name() =>
        Assert.Equal("[quote=Wa hke]Hi[/quote]\n\n", BbCode.Quote("[Wa hke]", " Hi "));
}
