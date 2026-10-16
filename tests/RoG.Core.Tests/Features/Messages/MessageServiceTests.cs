using RoG.Core.Content;
using RoG.Core.Features.Messages;

namespace RoG.Core.Tests.Features.Messages;

public class MessageServiceTests
{
    [Fact]
    public void Realtime_message_is_parsed()
    {
        var m = MessageService.FromRealtime("""
            {"id":"01M","conversation_id":"01C","author":{"id":"01U","slug":"wahke","name":"Wahke"},"body":"Scrim heute 20 Uhr?",
             "deleted":false,"quote":{"id":"01Q","author":"Gast","body":"Wann?"},
             "attachments":[{"id":"01A","name":"shot.png","mime":"image/png","size":1234,"image":true,"width":10,"height":10}],
             "created_at":"2026-10-16T18:00:00Z"}
            """);

        Assert.NotNull(m);
        Assert.Equal("01C", m.ConversationId);
        Assert.Equal("01U", m.AuthorId);
        Assert.Equal("Scrim heute 20 Uhr?", m.Body);
        Assert.Equal("Wann?", m.QuoteBody);
        Assert.True(Assert.Single(m.Attachments).IsImage);
    }

    [Fact]
    public void Deleted_message_has_no_body()
    {
        var m = MessageService.FromRealtime("""{"id":"01M","conversation_id":"01C","author":null,"body":"x","deleted":true,"quote":null,"attachments":[],"created_at":null}""");
        Assert.NotNull(m);
        Assert.True(m.Deleted);
        Assert.Null(m.Body);
        Assert.Equal("—", m.AuthorName);
    }

    [Theory]
    [InlineData("kein json")]
    [InlineData("""{"body":"ohne id"}""")]
    public void Broken_realtime_payload_is_ignored(string json) => Assert.Null(MessageService.FromRealtime(json));

    [Fact]
    public void Links_in_text_are_detected()
    {
        var parts = TextLinks.Split("Server: https://rogclan.de/et. Und http://x.de/a?b=1, fertig");

        Assert.Equal(["Server: ", "https://rogclan.de/et", ". Und ", "http://x.de/a?b=1", ", fertig"], parts.Select(p => p.Text));
        Assert.NotNull(parts[1].Link);
        Assert.Null(parts[2].Link);
    }

    [Fact]
    public void Text_without_links_is_one_segment() =>
        Assert.Equal("Hallo javascript:alert(1)", Assert.Single(TextLinks.Split("Hallo javascript:alert(1)")).Text);
}
